using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Writes raw frames to ffmpeg on its own thread. Each frame goes to its slot of the
    /// constant-frame-rate video (<see cref="FrameTimeline"/>); slots without a frame repeat the
    /// previous one. Frame buffers are pooled, and at most <see cref="Capacity"/> of them are in
    /// flight (in GPU readback or queued), so a slow encoder costs dropped frames, never memory or a
    /// stalled game.
    /// </summary>
    sealed class VideoWriter
    {
        struct Item
        {
            public long Slot;

            // Null marks the end; Slot is then the video's length in slots.
            public byte[] Frame;
        }

        readonly Stream _output;
        readonly int _frameBytes;
        readonly BlockingCollection<Item> _queue = new();
        readonly ConcurrentBag<byte[]> _pool = new();
        readonly Thread _thread;
        byte[] _previous;
        byte[] _black;
        long _nextSlot;
        int _inFlight;
        long _written;
        long _repeated;
        volatile Exception _error;

        public VideoWriter(Stream output, int frameBytes, int capacity)
        {
            _output = output;
            _frameBytes = frameBytes;
            Capacity = Math.Max(1, capacity);
            _thread = new Thread(Run) { IsBackground = true, Name = "GameViewRecorder video" };
            _thread.Start();
        }

        public int Capacity { get; }

        /// <summary>Frames written to ffmpeg, repeats included.</summary>
        public long Written => Interlocked.Read(ref _written);

        /// <summary>Slots filled with a repeat of the previous frame.</summary>
        public long Repeated => Interlocked.Read(ref _repeated);

        /// <summary>Why writing stopped early (usually: ffmpeg exited).</summary>
        public Exception Error => _error;

        /// <summary>A buffer for one frame, or null when <see cref="Capacity"/> frames are already in flight.</summary>
        public byte[] Rent()
        {
            if (Interlocked.Increment(ref _inFlight) > Capacity)
            {
                Interlocked.Decrement(ref _inFlight);
                return null;
            }
            return _pool.TryTake(out var buffer) ? buffer : new byte[_frameBytes];
        }

        /// <summary>Gives back a rented buffer that won't be written.</summary>
        public void Return(byte[] buffer)
        {
            _pool.Add(buffer);
            Interlocked.Decrement(ref _inFlight);
        }

        /// <summary>Queues a rented buffer holding the frame for <paramref name="slot"/>.</summary>
        public void Write(long slot, byte[] frame)
        {
            if (!TryAdd(new Item { Slot = slot, Frame = frame }))
                Return(frame);
        }

        /// <summary>
        /// Ends the video at <paramref name="slotCount"/> slots (repeating the last frame up to there)
        /// and closes ffmpeg's input.
        /// </summary>
        public void Complete(long slotCount)
        {
            TryAdd(new Item { Slot = slotCount });
            Abort();
        }

        /// <summary>Stops taking frames without finishing the video. The thread ends once ffmpeg is gone.</summary>
        public void Abort() => _queue.CompleteAdding();

        public bool Join(int milliseconds) => _thread.Join(milliseconds);

        bool TryAdd(Item item)
        {
            try
            {
                return _queue.TryAdd(item);
            }
            catch (InvalidOperationException)
            {
                // Completed or aborted meanwhile.
                return false;
            }
        }

        void Run()
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    if (item.Frame == null)
                    {
                        FillTo(item.Slot);
                        break;
                    }

                    Interlocked.Decrement(ref _inFlight);
                    if (item.Slot < _nextSlot)
                    {
                        _pool.Add(item.Frame);
                        continue;
                    }

                    FillTo(item.Slot);
                    _output.Write(item.Frame, 0, _frameBytes);
                    Interlocked.Increment(ref _written);
                    _nextSlot = item.Slot + 1;

                    if (_previous != null)
                        _pool.Add(_previous);
                    _previous = item.Frame;
                }

                // End of input: ffmpeg finishes the video once its stdin closes.
                _output.Flush();
                _output.Close();
            }
            catch (Exception e)
            {
                _error = e;
                try
                {
                    _output.Close();
                }
                catch (Exception)
                {
                    // The pipe is already broken.
                }
            }
        }

        void FillTo(long slot)
        {
            var frame = _previous ?? (_black ??= new byte[_frameBytes]);
            for (; _nextSlot < slot; _nextSlot++)
            {
                _output.Write(frame, 0, _frameBytes);
                Interlocked.Increment(ref _written);
                Interlocked.Increment(ref _repeated);
            }
        }
    }
}
