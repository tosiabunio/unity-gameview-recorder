using System.Diagnostics;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Real-time clock of a recording, shared by the video and audio paths. It stands still while
    /// the recording is paused (the Editor's Pause button), so paused time never reaches the file.
    /// Safe to use from any thread.
    /// </summary>
    public sealed class RecordingClock
    {
        readonly Stopwatch _watch = new();
        readonly object _gate = new();
        bool _started;
        bool _paused;

        /// <summary>Recorded time: real time since <see cref="Start"/>, minus the paused stretches.</summary>
        public double Seconds
        {
            get { lock (_gate) return _watch.Elapsed.TotalSeconds; }
        }

        /// <summary>True between <see cref="Start"/> and the end of the recording, except while paused.</summary>
        public bool IsRunning
        {
            get { lock (_gate) return _watch.IsRunning; }
        }

        public void Start()
        {
            lock (_gate)
            {
                _started = true;
                _watch.Reset();
                if (!_paused)
                    _watch.Start();
            }
        }

        public void Pause()
        {
            lock (_gate)
            {
                _paused = true;
                _watch.Stop();
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                _paused = false;
                if (_started)
                    _watch.Start();
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _started = false;
                _watch.Stop();
            }
        }
    }
}
