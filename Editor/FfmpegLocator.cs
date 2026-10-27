using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Finds the ffmpeg executable: the path set in the recorder window (stored per machine, shared by
    /// all projects), else PATH (re-read from the registry on Windows, so an install made after the
    /// Editor started is found), else the usual winget, Chocolatey, Scoop and Homebrew locations.
    /// </summary>
    public static class FfmpegLocator
    {
        const string PathKey = "Airon.GameViewRecorder.FfmpegPath";

        static bool _searched;
        static string _found;
        static readonly Dictionary<string, FfmpegInfo> Infos = new();

        /// <summary>The path set by the user, or empty to search.</summary>
        public static string ConfiguredPath
        {
            get => EditorPrefs.GetString(PathKey, "");
            set
            {
                EditorPrefs.SetString(PathKey, value?.Trim() ?? "");
                Refresh();
            }
        }

        static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>The ffmpeg to use, or null when there is none.</summary>
        public static string Find()
        {
            if (!_searched)
            {
                _found = Search(ConfiguredPath, Candidates(), File.Exists);
                _searched = true;
            }
            return _found;
        }

        /// <summary>Forgets the cached search and probe results.</summary>
        public static void Refresh()
        {
            _searched = false;
            Infos.Clear();
        }

        /// <summary>Version and encoders of an ffmpeg executable, probed once and cached.</summary>
        public static FfmpegInfo Inspect(string executable)
        {
            if (!Infos.TryGetValue(executable, out var info))
                Infos[executable] = info = FfmpegInfo.Probe(executable);
            return info;
        }

        /// <summary>A configured path wins (even when missing, so a typo isn't silently replaced); else the first existing candidate.</summary>
        internal static string Search(string configured, IEnumerable<string> candidates, Func<string, bool> exists)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                return exists(configured) ? configured : null;

            foreach (var candidate in candidates)
            {
                if (exists(candidate))
                    return candidate;
            }
            return null;
        }

        internal static IEnumerable<string> Candidates()
        {
            string executable = IsWindows ? "ffmpeg.exe" : "ffmpeg";
            foreach (var folder in SearchFolders())
                yield return Path.Combine(folder, executable);
        }

        static List<string> SearchFolders()
        {
            var folders = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string folder)
            {
                if (string.IsNullOrWhiteSpace(folder))
                    return;
                folder = Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));
                if (seen.Add(folder))
                    folders.Add(folder);
            }

            foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                string path;
                try
                {
                    path = Environment.GetEnvironmentVariable("PATH", target);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (var folder in (path ?? "").Split(Path.PathSeparator))
                    Add(folder);
            }

            if (IsWindows)
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                Add(Path.Combine(local, @"Microsoft\WinGet\Links"));
                foreach (var package in SafeDirectories(Path.Combine(local, @"Microsoft\WinGet\Packages"), "*FFmpeg*"))
                foreach (var build in SafeDirectories(package, "ffmpeg*"))
                    Add(Path.Combine(build, "bin"));
                Add(@"%ProgramData%\chocolatey\bin");
                Add(@"%USERPROFILE%\scoop\shims");
                Add(@"%ProgramFiles%\ffmpeg\bin");
                Add(@"C:\ffmpeg\bin");
            }
            else
            {
                Add("/opt/homebrew/bin");
                Add("/usr/local/bin");
                Add("/usr/bin");
                Add("/snap/bin");
            }
            return folders;
        }

        static IEnumerable<string> SafeDirectories(string folder, string pattern)
        {
            try
            {
                return Directory.Exists(folder) ? Directory.GetDirectories(folder, pattern) : Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>What an ffmpeg executable reports about itself.</summary>
    public sealed class FfmpegInfo
    {
        const int ProbeTimeoutMilliseconds = 5000;

        /// <summary>First line of <c>ffmpeg -version</c>, or null when it couldn't run.</summary>
        public string Version { get; private set; }

        public string Error { get; private set; }

        readonly HashSet<string> _encoders = new();

        public bool HasEncoder(string name) => _encoders.Contains(name);

        internal static FfmpegInfo Probe(string executable)
        {
            var info = new FfmpegInfo();
            try
            {
                string version = Run(executable, "-hide_banner -version");
                info.Version = version.Split('\n')[0].Trim();
                info.ParseEncoders(Run(executable, "-hide_banner -encoders"));
            }
            catch (Exception e)
            {
                info.Error = e.Message;
            }
            return info;
        }

        /// <summary>Reads the names from <c>ffmpeg -encoders</c>: lines like " V....D libx264  description" after the "------" line.</summary>
        internal void ParseEncoders(string listing)
        {
            bool started = false;
            foreach (var raw in listing.Split('\n'))
            {
                string line = raw.Trim();
                if (!started)
                {
                    started = line.StartsWith("------", StringComparison.Ordinal);
                    continue;
                }

                var parts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    _encoders.Add(parts[1]);
            }
        }

        static string Run(string executable, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            var error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(ProbeTimeoutMilliseconds))
            {
                process.Kill();
                throw new TimeoutException($"{Path.GetFileName(executable)} {arguments} didn't finish.");
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(executable)} {arguments} failed: {error.Result.Trim()}");
            return output;
        }
    }
}
