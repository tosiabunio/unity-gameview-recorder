using System;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Maps the moments frames are captured to the frames ("slots") of a constant-frame-rate video.
    /// Slot n covers [n / rate, (n + 1) / rate). The first frame rendered in a slot fills it and later
    /// ones are skipped, so a game running faster than the video is not captured needlessly. Slots
    /// that get no frame repeat the previous one (<see cref="VideoWriter"/>), so the video always runs
    /// in real time.
    /// </summary>
    public sealed class FrameTimeline
    {
        // Guards floor() against 2.9999999 for a moment that is exactly on a slot boundary.
        const double Epsilon = 1e-9;

        readonly double _rate;
        long _next;

        public FrameTimeline(double rate)
        {
            if (rate <= 0)
                throw new ArgumentOutOfRangeException(nameof(rate), rate, "Frame rate must be positive.");
            _rate = rate;
        }

        /// <summary>First slot not claimed yet.</summary>
        public long Next => _next;

        public long SlotAt(double seconds) => seconds <= 0 ? 0 : (long)Math.Floor(seconds * _rate + Epsilon);

        /// <summary>Number of slots in a video <paramref name="seconds"/> long.</summary>
        public long SlotCount(double seconds) => seconds <= 0 ? 0 : (long)Math.Round(seconds * _rate);

        /// <summary>
        /// Claims the slot of a frame captured at <paramref name="seconds"/>. False when that slot
        /// already has a frame.
        /// </summary>
        public bool TryClaim(double seconds, out long slot)
        {
            slot = SlotAt(seconds);
            if (slot < _next)
                return false;

            _next = slot + 1;
            return true;
        }
    }
}
