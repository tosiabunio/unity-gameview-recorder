using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Streams the game's mixed audio to ffmpeg as raw 32-bit float samples. ffmpeg's stdin carries
    /// the video, so the audio gets its own channel: a loopback TCP connection ffmpeg opens to
    /// <see cref="Port"/>. Chunks arrive stamped with the recording clock (from Unity's audio thread,
    /// or polled from Wwise on the main thread); the writer thread lines them up with it
    /// (<see cref="AudioAligner"/>) and pads silence while no audio arrives.
    /// </summary>
    sealed class AudioWriter
    {
        struct Chunk
        {
            public double Seconds;

            // Null marks the end of the recording, at Seconds.
            public float[] Samples;
            public int Count;
        }

        const int IdleWaitMilliseconds = 50;

        readonly RecordingClock _clock;
        readonly int _channels;
        readonly AudioAligner _aligner;
        readonly TcpListener _listener;
        readonly BlockingCollection<Chunk> _queue = new();
        readonly ConcurrentBag<float[]> _pool = new();
        readonly Thread _thread;
        readonly byte[] _silence = new byte[16 * 1024];
        byte[] _bytes = Array.Empty<byte>();
        volatile bool _open = true;
        volatile Exception _error;

        public AudioWriter(RecordingClock clock, int sampleRate, int channels, double toleranceSeconds)
        {
            _clock = clock;
            _channels = channels;
            _aligner = new AudioAligner(sampleRate, toleranceSeconds);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Run) { IsBackground = true, Name = "GameViewRecorder audio" };
            _thread.Start();
        }

        public int Port { get; }

        /// <summary>Times the audio was padded or trimmed to stay in sync with the video.</summary>
        public int Corrections => _aligner.Corrections;

        /// <summary>Why streaming stopped early (usually: ffmpeg exited).</summary>
        public Exception Error => _error;

        /// <summary>Takes a chunk that is being mixed right now (OnAudioFilterRead, on the audio thread).</summary>
        public void Push(float[] data, int channels) => Push(data, data.Length, channels, _clock.Seconds);

        /// <summary>
        /// Takes the first <paramref name="count"/> interleaved samples of <paramref name="data"/>,
        /// which start at clock time <paramref name="seconds"/>. Any thread.
        /// </summary>
        public void Push(float[] data, int count, int channels, double seconds)
        {
            if (!_open || channels <= 0 || !_clock.IsRunning)
                return;

            int frames = Math.Min(count, data.Length) / channels;
            if (frames == 0)
                return;
            int length = frames * _channels;
            if (!_pool.TryTake(out var samples) || samples.Length < length)
                samples = new float[length];

            if (channels == _channels)
                Array.Copy(data, samples, length);
            else
                Remap(data, channels, samples, _channels, frames);

            try
            {
                _queue.TryAdd(new Chunk { Seconds = seconds, Samples = samples, Count = length });
            }
            catch (InvalidOperationException)
            {
                // Completed meanwhile.
            }
        }

        /// <summary>Ends the audio at <paramref name="seconds"/>, padding silence up to there, and closes the connection.</summary>
        public void Complete(double seconds)
        {
            _open = false;
            try
            {
                _queue.TryAdd(new Chunk { Seconds = seconds });
                _queue.CompleteAdding();
            }
            catch (InvalidOperationException)
            {
                // Already aborted.
            }
        }

        /// <summary>Stops without finishing, also when ffmpeg never connected.</summary>
        public void Abort()
        {
            _open = false;
            _queue.CompleteAdding();
            _listener.Stop();
        }

        public bool Join(int milliseconds) => _thread.Join(milliseconds);

        /// <summary>Copies the channels both layouts share; extra target channels stay silent.</summary>
        internal static void Remap(float[] source, int sourceChannels, float[] target, int targetChannels, int frames)
        {
            int shared = Math.Min(sourceChannels, targetChannels);
            for (int frame = 0; frame < frames; frame++)
            {
                int from = frame * sourceChannels;
                int to = frame * targetChannels;
                for (int channel = 0; channel < targetChannels; channel++)
                    target[to + channel] = channel < shared ? source[from + channel] : 0f;
            }
        }

        void Run()
        {
            TcpClient client = null;
            try
            {
                client = _listener.AcceptTcpClient();
                _listener.Stop();
                client.NoDelay = true;
                var stream = client.GetStream();

                while (true)
                {
                    if (!_queue.TryTake(out var chunk, IdleWaitMilliseconds))
                    {
                        if (_queue.IsCompleted)
                            break;
                        WriteSilence(stream, _aligner.CatchUp(_clock.Seconds));
                        continue;
                    }

                    if (chunk.Samples == null)
                    {
                        WriteSilence(stream, _aligner.Finish(chunk.Seconds));
                        break;
                    }

                    _aligner.Place(chunk.Seconds, chunk.Count / _channels, out long silence, out int skip);
                    WriteSilence(stream, silence);
                    WriteSamples(stream, chunk.Samples, skip * _channels, chunk.Count - skip * _channels);
                    _pool.Add(chunk.Samples);
                }

                stream.Flush();
                client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception e)
            {
                // Abort() before ffmpeg connected stops the listener; that is not an error.
                if (_open || client != null)
                    _error = e;
            }
            finally
            {
                client?.Close();
                _listener.Stop();
            }
        }

        void WriteSamples(NetworkStream stream, float[] samples, int offset, int count)
        {
            if (count <= 0)
                return;

            int bytes = count * sizeof(float);
            if (_bytes.Length < bytes)
                _bytes = new byte[bytes];
            Buffer.BlockCopy(samples, offset * sizeof(float), _bytes, 0, bytes);
            stream.Write(_bytes, 0, bytes);
        }

        void WriteSilence(NetworkStream stream, long frames)
        {
            long bytes = frames * _channels * sizeof(float);
            while (bytes > 0)
            {
                int size = (int)Math.Min(bytes, _silence.Length);
                stream.Write(_silence, 0, size);
                bytes -= size;
            }
        }
    }
}
