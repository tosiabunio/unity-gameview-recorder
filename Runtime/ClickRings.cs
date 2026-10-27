using System.Collections.Generic;
using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Mouse clicks drawn into recorded frames: a ring grows from where a button went down and fades
    /// out, one ring for the left button and two in a row for the right. Presses are collected every
    /// Update, so a frame the video skips still shows its click, and timed on the recording clock, so
    /// the rings grow smoothly in the video whatever the game's frame rate. Main thread.
    /// </summary>
    sealed class ClickRings
    {
        internal const double RingSeconds = 0.4;
        internal const double SecondRingDelay = 0.12;

        // In cursor pixels, multiplied by the cursor scale when drawn.
        internal const float StartRadius = 3;
        internal const float EndRadius = 18;
        const float HalfWidth = 1;
        const float EdgeWidth = 1;
        const float EdgeOpacity = 0.6f;

        /// <summary>A ring at one moment: centered on a pixel of the screen image (Screen coordinates).</summary>
        internal struct Ring
        {
            public int X;
            public int Y;
            public float Radius;
            public float Opacity;
        }

        struct Click
        {
            public double Time;
            public int X;
            public int Y;
            public int Rings;
        }

        readonly List<Click> _clicks = new();
        readonly List<Ring> _rings = new();

        /// <summary>Notes the buttons that went down this frame, unless the cursor is locked or the mouse is outside the image.</summary>
        internal void Poll(double time, int width, int height)
        {
            MouseInput.ReadPresses(out bool left, out bool right);
            if (!left && !right)
                return;
            if (Cursor.lockState == CursorLockMode.Locked || !MouseInput.TryReadPixel(width, height, out int x, out int y))
                return;

            Add(time, x, y, right ? 2 : 1);
        }

        internal void Add(double time, int x, int y, int rings) => _clicks.Add(new Click { Time = time, X = x, Y = y, Rings = rings });

        /// <summary>The rings to draw at this time, oldest first; null when there are none. Forgets clicks whose rings are over.</summary>
        internal Ring[] Sample(double time)
        {
            _clicks.RemoveAll(click => time - click.Time >= (click.Rings - 1) * SecondRingDelay + RingSeconds);
            if (_clicks.Count == 0)
                return null;

            _rings.Clear();
            foreach (var click in _clicks)
            {
                for (int ring = 0; ring < click.Rings; ring++)
                {
                    if (Shape(time - click.Time - ring * SecondRingDelay, out float radius, out float opacity))
                        _rings.Add(new Ring { X = click.X, Y = click.Y, Radius = radius, Opacity = opacity });
                }
            }
            return _rings.Count == 0 ? null : _rings.ToArray();
        }

        /// <summary>Radius (cursor pixels) and opacity of a ring this many seconds after it started; false before it starts and once it's over.</summary>
        internal static bool Shape(double age, out float radius, out float opacity)
        {
            radius = opacity = 0;
            if (age < 0 || age >= RingSeconds)
                return false;

            float t = (float)(age / RingSeconds);
            float eased = 1 - (1 - t) * (1 - t);
            radius = StartRadius + (EndRadius - StartRadius) * eased;
            opacity = 1 - t;
            return true;
        }

        /// <summary>
        /// Blends a ring into an RGBA32 frame: a white line with darker edges, anti-aliased. The center is in
        /// frame pixels (column, stored row; pixel centers at .5), the radius in cursor pixels, enlarged
        /// <paramref name="scale"/> times like the line. What falls outside the frame is left out.
        /// </summary>
        internal static void Draw(byte[] frame, int width, int height, float centerX, float centerRow, float radius, float opacity, int scale)
        {
            float middle = radius * scale;
            float halfWidth = HalfWidth * scale;
            float outerHalfWidth = halfWidth + EdgeWidth * scale;
            float reach = middle + outerHalfWidth + 1;

            int left = Mathf.Max(0, Mathf.FloorToInt(centerX - reach));
            int right = Mathf.Min(width - 1, Mathf.CeilToInt(centerX + reach));
            int top = Mathf.Max(0, Mathf.FloorToInt(centerRow - reach));
            int bottom = Mathf.Min(height - 1, Mathf.CeilToInt(centerRow + reach));

            for (int row = top; row <= bottom; row++)
            {
                float dy = row + 0.5f - centerRow;
                for (int x = left; x <= right; x++)
                {
                    float dx = x + 0.5f - centerX;
                    float distance = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - middle);
                    float outer = Mathf.Clamp01(outerHalfWidth + 0.5f - distance);
                    if (outer <= 0)
                        continue;

                    float inner = Mathf.Clamp01(halfWidth + 0.5f - distance);
                    float dark = opacity * EdgeOpacity * (outer - inner);
                    float light = opacity * inner;
                    int index = (row * width + x) * 4;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        float value = frame[index + channel] * (1 - dark);
                        value = value * (1 - light) + 255 * light;
                        frame[index + channel] = (byte)(value + 0.5f);
                    }
                }
            }
        }
    }
}
