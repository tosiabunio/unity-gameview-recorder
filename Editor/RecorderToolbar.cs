#if UNITY_6000_3_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>Record toggle on the main toolbar, showing the elapsed time while recording.</summary>
    static class RecorderToolbar
    {
        const string ElementPath = "Game View Recorder/Record";

        [InitializeOnLoadMethod]
        static void Initialize() => GameRecorder.Changed += Refresh;

        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement Create()
        {
            bool recording = GameRecorder.IsRecording;
            var icon = EditorGUIUtility.IconContent(recording ? "Record On" : "Record Off").image as Texture2D;
            string text = recording ? GameRecorder.FormatDuration(GameRecorder.Session.Seconds) : "";
            string tooltip = recording
                ? "Stop recording the Game view"
                : EditorApplication.isPlaying ? "Record the Game view to a video file" : "Record the Game view (enter Play Mode first)";

            return new MainToolbarToggle(new MainToolbarContent(text, icon, tooltip), recording, OnToggled)
            {
                enabled = recording || EditorApplication.isPlaying
            };
        }

        static void OnToggled(bool on)
        {
            if (on)
                GameRecorder.StartRecording();
            else
                GameRecorder.StopRecording();
            Refresh();
        }

        static void Refresh() => MainToolbar.Refresh(ElementPath);
    }
}
#endif
