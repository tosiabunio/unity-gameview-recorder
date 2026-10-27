using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Editor entry point: starts and stops recordings of the Game view with the user's
    /// <see cref="RecorderSettings"/>, and ties them to the Editor. Leaving Play Mode, a script reload
    /// or quitting stops and finishes the recording; the Pause button pauses it; "Record on Play"
    /// starts one with Play Mode. Results go to the Console with a [GameViewRecorder] prefix.
    /// </summary>
    [InitializeOnLoad]
    public static class GameRecorder
    {
        const string LogPrefix = "[GameViewRecorder]";
        const int ShutdownWaitMilliseconds = 10_000;

        static RecordingState _reportedState;
        static double _nextTick;

        /// <summary>The current recording, or the last one.</summary>
        public static RecordingSession Session { get; private set; }

        public static bool IsRecording => Session is { IsActive: true };

        /// <summary>The recording started, stopped or finished, or (while recording) another second passed.</summary>
        public static event Action Changed;

        static GameRecorder()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged += OnPauseStateChanged;
            EditorApplication.update += Poll;
            EditorApplication.quitting += StopAndWait;
            AssemblyReloadEvents.beforeAssemblyReload += StopAndWait;
        }

        public static bool StartRecording()
        {
            if (IsRecording)
                return true;

            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning($"{LogPrefix} Enter Play Mode to record the Game view.");
                return false;
            }
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Debug.LogError($"{LogPrefix} This graphics API ({SystemInfo.graphicsDeviceType}) has no async GPU readback; recording isn't supported.");
                return false;
            }

            string ffmpeg = FfmpegLocator.Find();
            if (ffmpeg == null)
            {
                Debug.LogError($"{LogPrefix} ffmpeg not found. Install it (Windows: winget install Gyan.FFmpeg) "
                               + "or set its path in Tools > Game View Recorder > Open Window.");
                return false;
            }

            var settings = RecorderSettings.instance;
            Session = RecordingSession.Start(new RecordingOptions
            {
                FfmpegPath = ffmpeg,
                OutputPath = settings.NextOutputPath(),
                FrameRate = settings.FrameRate,
                Preset = settings.Preset,
                CustomArguments = settings.CustomArguments,
                Container = settings.EffectiveContainer,
                Audio = settings.Audio,
                UnityAudioDisabled = IsUnityAudioDisabled(),
                RecordCursor = settings.RecordCursor,
                RecordClicks = settings.RecordClicks,
                CursorScale = settings.CursorScale
            });
            if (EditorApplication.isPaused)
                Session.Pause();
            FocusGameView();

            _reportedState = Session.State;
            Changed?.Invoke();
            return true;
        }

        public static void StopRecording()
        {
            if (!IsRecording)
                return;

            Session.Stop();
            Changed?.Invoke();
        }

        public static void ToggleRecording()
        {
            if (IsRecording)
                StopRecording();
            else
                StartRecording();
        }

        /// <summary>
        /// Gives the Game view its focus back after the Start button or the toolbar took it, so the game
        /// keeps its keyboard input and Wwise (which suspends on focus loss by default) keeps playing.
        /// </summary>
        static void FocusGameView()
        {
            var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null)
                EditorWindow.FocusWindowIfItsOpen(gameView);
        }

        /// <summary>"Disable Unity Audio" in Project Settings > Audio, read without touching AudioSettings (which logs errors then).</summary>
        public static bool IsUnityAudioDisabled()
        {
            var managers = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (managers.Length == 0)
                return false;
            var disabled = new SerializedObject(managers[0]).FindProperty("m_DisableAudio");
            return disabled is { boolValue: true };
        }

        public static string FormatDuration(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
        }

        public static string FormatSize(long bytes) => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            _ => $"{bytes / 1024.0:0} KB"
        };

        /// <summary>Size of the recording's file so far, or 0.</summary>
        public static long FileSize(RecordingSession session)
        {
            try
            {
                var file = new FileInfo(session.OutputPath);
                return file.Exists ? file.Length : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && RecorderSettings.instance.RecordOnPlay)
                StartRecording();
            else if (change == PlayModeStateChange.ExitingPlayMode)
                StopRecording();
            Changed?.Invoke();
        }

        static void OnPauseStateChanged(PauseState state)
        {
            if (!IsRecording)
                return;

            if (state == PauseState.Paused)
                Session.Pause();
            else
                Session.Resume();
        }

        static void StopAndWait()
        {
            if (Session == null)
                return;

            StopRecording();
            if (!Session.Wait(ShutdownWaitMilliseconds))
                Session.Abort($"ffmpeg didn't finish within {ShutdownWaitMilliseconds / 1000} s before the Editor reloaded or quit. "
                              + RecordingSession.CutShortNote(Session.Options.Container));
            Report();
        }

        static void Poll()
        {
            if (Session == null)
                return;

            if (Session.State != _reportedState)
            {
                Report();
                Changed?.Invoke();
            }
            else if (IsRecording && EditorApplication.timeSinceStartup >= _nextTick)
            {
                _nextTick = EditorApplication.timeSinceStartup + 1;
                Changed?.Invoke();
            }
        }

        static void Report()
        {
            var state = Session.State;
            if (state == _reportedState)
                return;
            _reportedState = state;

            if (state == RecordingState.Finished)
            {
                string audio = Session.AudioSampleRate > 0
                    ? $", {Session.RecordedAudio} audio {Session.AudioSampleRate / 1000.0:0.#} kHz × {Session.AudioChannels}"
                    : ", no audio";
                Debug.Log($"{LogPrefix} Saved {Session.OutputPath} ({Session.Width}×{Session.Height}, {Session.Options.FrameRate} fps{audio}, "
                          + $"{FormatDuration(Session.Seconds)}, {FormatSize(FileSize(Session))}; {Session.Repeated} repeated and "
                          + $"{Session.Dropped} dropped frames, {Session.AudioCorrections} audio corrections).");
            }
            else if (state == RecordingState.Failed)
            {
                Debug.LogError($"{LogPrefix} Recording failed: {Session.Error}");
            }
        }
    }
}
