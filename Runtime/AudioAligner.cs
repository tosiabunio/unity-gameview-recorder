using System;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Keeps a live audio stream on the recording clock. Audio arrives in chunks, each stamped with
    /// the clock time it was mixed at. When the written stream falls behind the clock by more than
    /// the tolerance (no AudioListener for a while, a hiccup, the sound card's clock drifting from the
    /// system clock) the aligner pads silence; when it runs ahead, it drops samples. Jitter below the
    /// tolerance is left alone, so a steady stream is never touched. Positions are in sample frames
    /// (one sample per channel). Not thread-safe; used by the audio writer thread only.
    /// </summary>
    public sealed class AudioAligner
    {
        readonly int _sampleRate;
        readonly long _tolerance;

        public AudioAligner(int sampleRate, double toleranceSeconds)
        {
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            _sampleRate = sampleRate;
            _tolerance = Math.Max(0, (long)Math.Round(toleranceSeconds * sampleRate));
        }

        /// <summary>Sample frames written so far, audio and padding.</summary>
        public long Written { get; private set; }

        /// <summary>Times the stream was padded or trimmed back onto the clock after it started.</summary>
        public int Corrections { get; private set; }

        /// <summary>
        /// Places a chunk of <paramref name="frames"/> sample frames that arrived at
        /// <paramref name="seconds"/>: <paramref name="silence"/> frames of silence go before it and
        /// its first <paramref name="skip"/> frames are dropped. The caller writes the rest.
        /// </summary>
        public void Place(double seconds, int frames, out long silence, out int skip)
        {
            long expected = ToFrames(seconds);
            long drift = Written - expected;
            silence = 0;
            skip = 0;

            if (Written == 0)
            {
                // The first chunk lines up with the start of the recording.
                silence = Math.Max(0, expected);
            }
            else if (drift < -_tolerance)
            {
                silence = -drift;
                Corrections++;
            }
            else if (drift > _tolerance)
            {
                skip = (int)Math.Min(frames, drift);
                Corrections++;
            }

            Written += silence + frames - skip;
        }

        /// <summary>
        /// Silence to write when no audio has arrived for a while (no AudioListener): whatever the
        /// stream is behind <paramref name="seconds"/>, once that exceeds the tolerance.
        /// </summary>
        public long CatchUp(double seconds)
        {
            long behind = ToFrames(seconds) - Written;
            if (behind <= _tolerance)
                return 0;

            Written += behind;
            return behind;
        }

        /// <summary>Silence that brings the stream to <paramref name="seconds"/> at the end of the recording.</summary>
        public long Finish(double seconds)
        {
            long behind = ToFrames(seconds) - Written;
            if (behind <= 0)
                return 0;

            Written += behind;
            return behind;
        }

        long ToFrames(double seconds) => seconds <= 0 ? 0 : (long)Math.Round(seconds * _sampleRate);
    }
}
