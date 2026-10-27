using System;
using System.IO;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Airon.GameViewRecorder
{
    /// <summary>Tools > Game View Recorder > Open Window: start/stop, live stats, settings and ffmpeg setup.</summary>
    public sealed class RecorderWindow : EditorWindow
    {
        const double RepaintInterval = 0.25;
        const string ToggleShortcutId = "Main Menu/Tools/Game View Recorder/Start or Stop Recording";
        static readonly int[] FrameRates = { 24, 25, 30, 50, 60, 120 };
        static readonly GUIContent[] FrameRateLabels = Array.ConvertAll(FrameRates, rate => new GUIContent($"{rate} fps"));
        static readonly int[] Containers = { (int)ContainerFormat.Mkv, (int)ContainerFormat.Mp4 };
        static readonly GUIContent[] ContainerLabels = { new("MKV"), new("MP4") };
        static readonly int[] CursorScales = { 0, 1, 2, 3, 4 };
        static readonly GUIContent[] CursorScaleLabels = Array.ConvertAll(CursorScales, scale => new GUIContent(scale == 0 ? "Auto" : $"{scale}×"));

        Vector2 _scroll;
        double _nextRepaint;
        GUIStyle _wrappedLabel;
        bool _unityAudioDisabled;
        bool _hasWwise;

        public static void Open() => GetWindow<RecorderWindow>();

        void OnEnable()
        {
            titleContent = new GUIContent("Game View Recorder", EditorGUIUtility.IconContent("Record Off").image);
            minSize = new Vector2(380, 300);
            GameRecorder.Changed += Repaint;
            DetectAudio();
        }

        void OnFocus() => DetectAudio();

        void DetectAudio()
        {
            _unityAudioDisabled = GameRecorder.IsUnityAudioDisabled();
            _hasWwise = WwiseCapture.FindEngine() != null;
        }

        void OnDisable() => GameRecorder.Changed -= Repaint;

        void Update()
        {
            if (GameRecorder.IsRecording && EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                _nextRepaint = EditorApplication.timeSinceStartup + RepaintInterval;
                Repaint();
            }
        }

        void OnGUI()
        {
            _wrappedLabel ??= new GUIStyle(EditorStyles.label) { wordWrap = true };
            EditorGUIUtility.labelWidth = 100;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawRecording();
            EditorGUILayout.Space(8);
            DrawSettings();
            EditorGUILayout.Space(8);
            DrawFfmpeg();
            EditorGUILayout.EndScrollView();
        }

        void DrawRecording()
        {
            var session = GameRecorder.Session;
            bool recording = GameRecorder.IsRecording;

            using (new EditorGUI.DisabledScope(!recording && !EditorApplication.isPlaying))
            {
                var icon = EditorGUIUtility.IconContent(recording ? "Record On" : "Record Off").image;
                string shortcut = ShortcutLabel();
                var label = new GUIContent(recording ? " Stop Recording" : " Start Recording", icon,
                    string.IsNullOrEmpty(shortcut) ? null : $"Shortcut: {shortcut}");
                if (GUILayout.Button(label, GUILayout.Height(30)))
                    GameRecorder.ToggleRecording();
            }

            if (recording)
            {
                DrawLiveStats(session);
                return;
            }

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to record. \"Record on Play\" below starts a recording with Play Mode.", MessageType.None);
            else
            {
                PlayModeWindow.GetRenderingResolution(out uint width, out uint height);
                EditorGUILayout.LabelField("Game view", $"{width}×{height}");
            }

            string shortcutHint = ShortcutLabel();
            if (!string.IsNullOrEmpty(shortcutHint))
                EditorGUILayout.LabelField("Shortcut", $"{shortcutHint}  (Edit > Shortcuts > Main Menu)", EditorStyles.miniLabel);

            if (session != null)
                DrawLastRecording(session);
        }

        void DrawLiveStats(RecordingSession session)
        {
            if (session.State == RecordingState.Starting)
            {
                EditorGUILayout.LabelField("Starting…");
                return;
            }

            EditorGUILayout.LabelField("Recording", $"{GameRecorder.FormatDuration(session.Seconds)}{(EditorApplication.isPaused ? "  (paused)" : "")}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Video", $"{session.Width}×{session.Height}, {session.Options.FrameRate} fps, {session.Options.Preset}{(session.Options.RecordCursor ? ", cursor" : "")}{(session.Options.RecordClicks ? ", clicks" : "")}");
            EditorGUILayout.LabelField("Frames", $"{session.Frames} ({session.Repeated} repeated, {session.Dropped} dropped)");
            EditorGUILayout.LabelField("Audio", session.AudioSampleRate > 0
                ? $"{session.RecordedAudio}, {session.AudioSampleRate / 1000.0:0.#} kHz × {session.AudioChannels} ({session.AudioCorrections} corrections)"
                : "none");
            EditorGUILayout.LabelField("File", $"{Path.GetFileName(session.OutputPath)}  ({GameRecorder.FormatSize(GameRecorder.FileSize(session))})");
        }

        void DrawLastRecording(RecordingSession session)
        {
            EditorGUILayout.Space(4);
            switch (session.State)
            {
                case RecordingState.Finalizing:
                    EditorGUILayout.LabelField("Finishing", Path.GetFileName(session.OutputPath));
                    return;
                case RecordingState.Failed:
                    EditorGUILayout.HelpBox(session.Error, MessageType.Error);
                    break;
                case RecordingState.Finished:
                    EditorGUILayout.LabelField("Last recording",
                        $"{GameRecorder.FormatDuration(session.Seconds)}, {GameRecorder.FormatSize(GameRecorder.FileSize(session))}");
                    break;
            }

            if (!File.Exists(session.OutputPath))
                return;

            EditorGUILayout.LabelField(session.OutputPath, EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show in Explorer"))
                    EditorUtility.RevealInFinder(session.OutputPath);
                if (GUILayout.Button("Play"))
                    EditorUtility.OpenWithDefaultApp(session.OutputPath);
            }
        }

        void DrawSettings()
        {
            var settings = RecorderSettings.instance;
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(GameRecorder.IsRecording))
            {
                EditorGUI.BeginChangeCheck();

                settings.FrameRate = EditorGUILayout.IntPopup(new GUIContent("Frame rate"), settings.FrameRate, FrameRateLabels, FrameRates);
                settings.Preset = (VideoPreset)EditorGUILayout.EnumPopup("Encoding", settings.Preset);
                EditorGUILayout.LabelField(" ", FfmpegArguments.Describe(settings.Preset), _wrappedLabel);
                if (settings.Preset == VideoPreset.Custom)
                    settings.CustomArguments = EditorGUILayout.TextField("ffmpeg options", settings.CustomArguments);

                DrawContainer(settings);

                settings.Audio = (AudioInput)EditorGUILayout.EnumPopup("Audio", settings.Audio);
                EditorGUILayout.LabelField(" ", DescribeAudio(settings.Audio), _wrappedLabel);

                settings.RecordCursor = EditorGUILayout.Toggle(new GUIContent("Cursor",
                    "Draw an arrow cursor into the video at the mouse position. The OS cursor isn't part of the Game view image."), settings.RecordCursor);
                settings.RecordClicks = EditorGUILayout.Toggle(new GUIContent("Clicks",
                    "Draw a growing ring where a mouse button goes down: one for the left button, two for the right."), settings.RecordClicks);
                if (settings.RecordCursor || settings.RecordClicks)
                {
                    settings.CursorScale = EditorGUILayout.IntPopup(new GUIContent("Cursor size",
                        "Video pixels per cursor pixel, for the arrow and the click rings. Auto: 1× up to 1080p and 1440p, 2× at 4K."),
                        settings.CursorScale, CursorScaleLabels, CursorScales);
                    EditorGUILayout.LabelField(" ", DescribePointer(settings), _wrappedLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    settings.OutputFolder = EditorGUILayout.TextField(new GUIContent("Output folder", "Absolute, or relative to the project folder."), settings.OutputFolder);
                    if (GUILayout.Button("…", GUILayout.Width(24)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Recordings folder", settings.OutputFolderPath, "");
                        if (!string.IsNullOrEmpty(picked))
                            settings.OutputFolder = picked;
                    }
                    if (GUILayout.Button("Open", GUILayout.Width(48)))
                    {
                        Directory.CreateDirectory(settings.OutputFolderPath);
                        EditorUtility.OpenWithDefaultApp(settings.OutputFolderPath);
                    }
                }

                settings.FileName = EditorGUILayout.TextField(new GUIContent("File name", "Tokens: {product}, {scene}, {date}."), settings.FileName);
                string preview = RecorderSettings.BuildFileName(settings.FileName, Application.productName, SceneManager.GetActiveScene().name, DateTime.Now,
                    FfmpegArguments.Extension(settings.EffectiveContainer));
                EditorGUILayout.LabelField(" ", preview, EditorStyles.miniLabel);

                settings.RecordOnPlay = EditorGUILayout.Toggle("Record on Play", settings.RecordOnPlay);

                if (EditorGUI.EndChangeCheck())
                    settings.Save();
            }
        }

        void DrawContainer(RecorderSettings settings)
        {
            // Shows what the recording will get; the choice is kept for the other encodings.
            bool mkvOnly = FfmpegArguments.ContainerFor(settings.Preset, ContainerFormat.Mp4) != ContainerFormat.Mp4;
            using (new EditorGUI.DisabledScope(mkvOnly))
            {
                var picked = (ContainerFormat)EditorGUILayout.IntPopup(new GUIContent("Format", "The kind of file the recording is written to."),
                    (int)settings.EffectiveContainer, ContainerLabels, Containers);
                if (!mkvOnly)
                    settings.Container = picked;
            }

            string description = mkvOnly
                ? "Lossless always records to MKV: few players read FFV1 from an MP4."
                : FfmpegArguments.Describe(settings.Container);
            if (settings.Preset == VideoPreset.Custom && settings.Container == ContainerFormat.Mp4)
                description += " Your options must use codecs MP4 can hold.";
            EditorGUILayout.LabelField(" ", description, _wrappedLabel);
        }

        void DrawFfmpeg()
        {
            EditorGUILayout.LabelField("ffmpeg", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                string configured = FfmpegLocator.ConfiguredPath;
                string edited = EditorGUILayout.DelayedTextField(new GUIContent("Path", "Leave empty to search PATH and the usual install folders. Stored per machine."), configured);
                if (edited != configured)
                    FfmpegLocator.ConfiguredPath = edited;
                if (GUILayout.Button("…", GUILayout.Width(24)))
                {
                    string picked = EditorUtility.OpenFilePanel("ffmpeg executable", "", Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                    if (!string.IsNullOrEmpty(picked))
                        FfmpegLocator.ConfiguredPath = picked;
                }
                if (GUILayout.Button(new GUIContent("Detect", "Search again, e.g. after installing ffmpeg."), GUILayout.Width(48)))
                    FfmpegLocator.Refresh();
            }

            string found = FfmpegLocator.Find();
            if (found == null)
            {
                EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(FfmpegLocator.ConfiguredPath)
                    ? "ffmpeg not found. Install it, then press Detect:\n  Windows: winget install Gyan.FFmpeg\n  macOS: brew install ffmpeg\nor set the path above."
                    : "There is no ffmpeg at the path above.", MessageType.Warning);
                return;
            }

            var info = FfmpegLocator.Inspect(found);
            EditorGUILayout.LabelField(found, EditorStyles.miniLabel);
            if (info.Error != null)
            {
                EditorGUILayout.HelpBox($"ffmpeg doesn't run: {info.Error}", MessageType.Error);
                return;
            }
            EditorGUILayout.LabelField(info.Version, EditorStyles.miniLabel);

            var settings = RecorderSettings.instance;
            string encoder = settings.Preset switch
            {
                VideoPreset.Shareable => "libx264",
                VideoPreset.Lossless => "ffv1",
                VideoPreset.Hardware => "h264_nvenc",
                _ => null
            };
            if (encoder != null && !info.HasEncoder(encoder))
                EditorGUILayout.HelpBox($"This ffmpeg has no {encoder} encoder; pick another encoding or another ffmpeg build.", MessageType.Warning);
        }

        string DescribeAudio(AudioInput input)
        {
            bool unityDisabled = _unityAudioDisabled;
            bool hasWwise = _hasWwise;
            string wwiseNote = "Wwise goes silent while the Game view isn't focused, unless Render During Focus Loss is on in its settings.";
            return input switch
            {
                AudioInput.Off => "No sound.",
                AudioInput.Unity => unityDisabled
                    ? "Unity audio is disabled in Project Settings > Audio: the recording will have no sound."
                    : "What the active AudioListener hears.",
                AudioInput.Wwise => hasWwise ? $"The Wwise main output. {wwiseNote}" : "This project has no Wwise: the recording will have no sound.",
                _ => hasWwise
                    ? $"Wwise while its sound engine runs{(unityDisabled ? "" : ", else Unity audio")}. {wwiseNote}"
                    : unityDisabled
                        ? "Unity audio is disabled and there is no Wwise: the recording will have no sound."
                        : "What the active AudioListener hears (Unity audio)."
            };
        }

        static string DescribePointer(RecorderSettings settings)
        {
            const string cursor = "A standard arrow, hidden while the game hides the cursor. It doesn't take the shape of a custom or text cursor.";
            const string clicks = "Clicks: a ring for the left button, two for the right, also while the cursor is hidden (not when it's locked).";
            return settings.RecordCursor && settings.RecordClicks ? $"{cursor} {clicks}"
                : settings.RecordCursor ? cursor
                : clicks;
        }

        static string ShortcutLabel()
        {
            try
            {
                var binding = ShortcutManager.instance.GetShortcutBinding(ToggleShortcutId);
                return binding.Equals(ShortcutBinding.empty) ? "" : binding.ToString();
            }
            catch (ArgumentException)
            {
                return "";
            }
        }
    }
}
