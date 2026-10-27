using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Airon.GameViewRecorder
{
    /// <summary>An ffmpeg process reading frames from its stdin. Keeps the last lines of its log for error reports.</summary>
    sealed class FfmpegProcess : IDisposable
    {
        const int TailLines = 20;

        readonly Process _process;
        readonly Queue<string> _tail = new();

        FfmpegProcess(Process process) => _process = process;

        public Stream Input => _process.StandardInput.BaseStream;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        /// <summary>The last lines ffmpeg wrote to stderr.</summary>
        public string Tail
        {
            get { lock (_tail) return string.Join("\n", _tail); }
        }

        public static FfmpegProcess Start(string executable, string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true
                }
            };
            var ffmpeg = new FfmpegProcess(process);
            process.ErrorDataReceived += (_, e) => ffmpeg.AddLine(e.Data);
            process.Start();
            process.BeginErrorReadLine();
            return ffmpeg;
        }

        public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }

        public void Dispose() => _process.Dispose();

        void AddLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            lock (_tail)
            {
                _tail.Enqueue(line);
                while (_tail.Count > TailLines)
                    _tail.Dequeue();
            }
        }
    }
}
