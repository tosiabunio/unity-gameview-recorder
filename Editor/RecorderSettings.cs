using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Recorder settings of this user in this project, kept in UserSettings/ (not version-controlled).
    /// The ffmpeg path is per machine instead: see <see cref="FfmpegLocator"/>.
    /// </summary>
    [FilePath("UserSettings/GameViewRecorder.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class RecorderSettings : ScriptableSingleton<RecorderSettings>
    {
        public const string DefaultFileName = "{product} {date}";

        public int FrameRate = 60;
        public VideoPreset Preset = VideoPreset.Shareable;
        public string CustomArguments = "-c:v libx264rgb -preset veryfast -crf 0 -c:a flac";

        /// <summary>The format asked for; recordings get <see cref="EffectiveContainer"/>.</summary>
        public ContainerFormat Container = ContainerFormat.Mkv;

        public AudioInput Audio = AudioInput.Auto;

        /// <summary>Draw a cursor into the video at the mouse position.</summary>
        public bool RecordCursor;

        /// <summary>Draw a growing ring where a mouse button goes down.</summary>
        public bool RecordClicks;

        /// <summary>Video pixels per pixel of the cursor and click rings; 0 = Auto (by the video height).</summary>
        public int CursorScale;

        /// <summary>Absolute, or relative to the project folder.</summary>
        public string OutputFolder = "Recordings";

        /// <summary>Tokens: {product}, {scene}, {date}. The format's extension (.mkv, .mp4) is added.</summary>
        public string FileName = DefaultFileName;

        /// <summary>Start recording whenever Play Mode starts.</summary>
        public bool RecordOnPlay;

        public void Save() => Save(true);

        /// <summary><see cref="Container"/>, except that Lossless always records to MKV.</summary>
        public ContainerFormat EffectiveContainer => FfmpegArguments.ContainerFor(Preset, Container);

        public string OutputFolderPath => ResolveFolder(OutputFolder, ProjectFolder);

        static string ProjectFolder => Path.GetDirectoryName(Application.dataPath);

        /// <summary>A path for a new recording that doesn't overwrite an existing file.</summary>
        public string NextOutputPath()
        {
            string name = BuildFileName(FileName, Application.productName, SceneManager.GetActiveScene().name, DateTime.Now,
                FfmpegArguments.Extension(EffectiveContainer));
            return UniquePath(Path.Combine(OutputFolderPath, name), File.Exists);
        }

        internal static string ResolveFolder(string folder, string projectFolder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                folder = "Recordings";
            return Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(projectFolder, folder));
        }

        internal static string BuildFileName(string pattern, string product, string scene, DateTime time, string extension)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                pattern = DefaultFileName;

            string name = pattern
                .Replace("{product}", product)
                .Replace("{scene}", scene)
                .Replace("{date}", time.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            name = name.Trim();
            return (name.Length > 0 ? name : "Recording") + extension;
        }

        internal static string UniquePath(string path, Func<string, bool> exists)
        {
            if (!exists(path))
                return path;

            string folder = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int number = 2; ; number++)
            {
                string candidate = Path.Combine(folder, $"{name} ({number}){extension}");
                if (!exists(candidate))
                    return candidate;
            }
        }
    }
}
