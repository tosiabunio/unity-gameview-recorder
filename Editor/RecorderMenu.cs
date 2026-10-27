using UnityEditor;

namespace Airon.GameViewRecorder
{
    static class RecorderMenu
    {
        const string Root = "Tools/Game View Recorder/";

        // A menu item rather than a Shortcut Manager shortcut: only menu shortcuts fire while the Game
        // view has focus in Play Mode. Rebind it under Main Menu in Edit > Shortcuts.
        // Ctrl+F7: Ctrl+F9 is Multiplayer Play Mode's "Focus Player One", and F9 alone is Fullscreen
        // Editor's Game View and the Profiler's record toggle.
        const string ToggleItem = Root + "Start or Stop Recording %F7";

        [MenuItem(Root + "Open Window", priority = 0)]
        static void OpenWindow() => RecorderWindow.Open();

        [MenuItem(ToggleItem, priority = 20)]
        static void ToggleRecording() => GameRecorder.ToggleRecording();

        [MenuItem(ToggleItem, true)]
        static bool CanToggleRecording() => EditorApplication.isPlaying;
    }
}
