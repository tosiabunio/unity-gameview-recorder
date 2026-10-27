using System;
using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// The mouse cursor drawn into recorded frames. The OS draws its cursor over the Game view after Unity
    /// has rendered, so the screen copy never contains it. This draws a standard arrow instead, at the
    /// mouse position Unity reports, whenever the game shows the cursor.
    /// </summary>
    static class CursorOverlay
    {
        // 'X' outline, '.' fill, ' ' transparent. Top row first; the hotspot is the top-left pixel.
        static readonly string[] Arrow =
        {
            "X           ",
            "XX          ",
            "X.X         ",
            "X..X        ",
            "X...X       ",
            "X....X      ",
            "X.....X     ",
            "X......X    ",
            "X.......X   ",
            "X........X  ",
            "X.........X ",
            "X..........X",
            "X......XXXXX",
            "X...X..X    ",
            "X..X X..X   ",
            "X.X  X..X   ",
            "XX    X..X  ",
            "      X..X  ",
            "       X..X ",
            "       X..X ",
            "        XX  "
        };

        internal static int ArrowWidth => Arrow[0].Length;

        internal static int ArrowHeight => Arrow.Length;

        /// <summary>The arrow's size for a video this many rows high: 1× up to 1619 rows, then one step per 1080 rows (2× at 4K).</summary>
        internal static int AutoScale(int videoHeight) => Math.Max(1, (videoHeight + 540) / 1080);

        /// <summary>
        /// The pixel under the mouse in a screen image of this size, counted from the bottom left like
        /// Screen coordinates. False when the game hides the cursor, the mouse is outside the image, or
        /// there is no mouse. Main thread.
        /// </summary>
        internal static bool TrySample(int width, int height, out int x, out int y)
        {
            x = y = 0;
            // A locked cursor is invisible whatever Cursor.visible says.
            return Cursor.visible && Cursor.lockState != CursorLockMode.Locked && MouseInput.TryReadPixel(width, height, out x, out y);
        }

        /// <summary>
        /// Draws the arrow into an RGBA32 frame, <paramref name="scale"/> frame pixels per arrow pixel, with
        /// its hotspot at column <paramref name="x"/> of stored row <paramref name="row"/>. Rows are stored top
        /// first when <paramref name="rowStep"/> is 1 and bottom first when it is -1; the arrow points down
        /// the image either way. What falls outside the frame is left out.
        /// </summary>
        internal static void Draw(byte[] frame, int width, int height, int x, int row, int rowStep, int scale)
        {
            for (int arrowY = 0; arrowY < Arrow.Length; arrowY++)
            {
                string line = Arrow[arrowY];
                for (int arrowX = 0; arrowX < line.Length; arrowX++)
                {
                    char pixel = line[arrowX];
                    if (pixel == ' ')
                        continue;

                    byte value = pixel == 'X' ? (byte)0 : (byte)255;
                    for (int dy = 0; dy < scale; dy++)
                    {
                        int targetRow = row + (arrowY * scale + dy) * rowStep;
                        if (targetRow < 0 || targetRow >= height)
                            continue;

                        for (int dx = 0; dx < scale; dx++)
                        {
                            int targetX = x + arrowX * scale + dx;
                            if (targetX < 0 || targetX >= width)
                                continue;

                            int index = (targetRow * width + targetX) * 4;
                            frame[index] = value;
                            frame[index + 1] = value;
                            frame[index + 2] = value;
                            frame[index + 3] = 255;
                        }
                    }
                }
            }
        }
    }
}
