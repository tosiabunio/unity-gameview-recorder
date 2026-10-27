using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// The mouse as the game sees it: the Input System's <c>Mouse.current</c> when the project has it
    /// (<c>GAMEVIEWRECORDER_INPUT_SYSTEM</c>), else the old Input Manager. Main thread.
    /// </summary>
    static class MouseInput
    {
        /// <summary>The mouse position in Screen coordinates (pixels from the bottom left). False when there is no mouse.</summary>
        internal static bool TryReadPosition(out Vector2 position)
        {
#if GAMEVIEWRECORDER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.mousePresent)
            {
                position = Input.mousePosition;
                return true;
            }
#endif
            position = default;
            return false;
        }

        /// <summary>Which of the left and right buttons went down this frame.</summary>
        internal static void ReadPresses(out bool left, out bool right)
        {
#if GAMEVIEWRECORDER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                left = mouse.leftButton.wasPressedThisFrame;
                right = mouse.rightButton.wasPressedThisFrame;
                return;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.mousePresent)
            {
                left = Input.GetMouseButtonDown(0);
                right = Input.GetMouseButtonDown(1);
                return;
            }
#endif
            left = right = false;
        }

        /// <summary>The pixel under the mouse in a screen image of this size (Screen coordinates). False outside it or without a mouse.</summary>
        internal static bool TryReadPixel(int width, int height, out int x, out int y)
        {
            x = y = 0;
            if (!TryReadPosition(out var position))
                return false;

            x = Mathf.FloorToInt(position.x);
            y = Mathf.FloorToInt(position.y);
            return x >= 0 && x < width && y >= 0 && y < height;
        }
    }
}
