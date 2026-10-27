using System;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Airon.GameViewRecorder
{
    public enum RecordingState
    {
        /// <summary>Waiting for the first frame, which fixes the video size.</summary>
        Starting,
        Recording,

        /// <summary>Stopped; ffmpeg is finishing the file.</summary>
        Finalizing,
        Finished,
        Failed
    }

    /// <summary>
    /// One recording of the Game view, with audio, to an MKV or MP4 file encoded by ffmpeg. Needs Play Mode.
    /// At the end of each frame the whole screen image (what the Game view shows at its resolution,
    /// UI and letterbox bars included) is copied on the GPU and read back asynchronously, then piped
    /// to ffmpeg as raw RGBA, with a drawn cursor and click rings if asked for (<see cref="CursorOverlay"/>,
    /// <see cref="ClickRings"/>; the OS cursor isn't in the screen image). The video has a constant frame
    /// rate in real time: frames are repeated or skipped to follow the clock (<see cref="FrameTimeline"/>). The audio (the AudioListener's mix,
    /// or Wwise's main output, see <see cref="AudioInput"/>) follows the same clock
    /// (<see cref="AudioWriter"/>). Main-thread API; the state may change on its own (ffmpeg exits,
    /// finishing completes), so poll <see cref="State"/>.
    /// </summary>
    public sealed class RecordingSession
    {
        const string LogPrefix = "[GameViewRecorder]";

        // Frames in flight are capped by memory as well as by one second's worth.
        const long MaxBufferedBytes = 512L * 1024 * 1024;
        const int MinBufferedFrames = 3;
        const float ListenerCheckInterval = 1f;
        const int FinishTimeoutMilliseconds = 60_000;

        // Audio arrives in blocks (a DSP buffer, a Wwise render pass); only drift beyond this is corrected.
        const double MinAudioTolerance = 0.15;

        readonly RecordingOptions _options;
        readonly RecordingClock _clock = new();
        GameObject _host;
        AudioTap _tap;
        RenderTexture _capture;
        FrameTimeline _timeline;
        FfmpegProcess _ffmpeg;
        VideoWriter _video;
        AudioWriter _audio;
        WwiseCapture _wwise;
        bool _discardWwise;
        Thread _finisher;
        bool _accepting;
        bool _sizeWarned;
        bool _topRowFirst;
        int _cursorScale;
        ClickRings _clicks;
        long _dropped;
        float _nextListenerCheck;
        volatile RecordingState _state;
        volatile string _failure;

        RecordingSession(RecordingOptions options) => _options = options;

        public RecordingState State => _state;

        /// <summary>Starting or recording; <see cref="Stop"/> has not been called and nothing failed.</summary>
        public bool IsActive => _state is RecordingState.Starting or RecordingState.Recording;

        public RecordingOptions Options => _options;

        public string OutputPath => _options.OutputPath;

        /// <summary>Why the recording failed, with ffmpeg's last log lines; null otherwise.</summary>
        public string Error
        {
            get
            {
                string failure = _failure;
                if (failure == null)
                    return null;
                string tail = _ffmpeg?.Tail;
                return string.IsNullOrEmpty(tail) ? failure : $"{failure}\nffmpeg:\n{tail}";
            }
        }

        /// <summary>Video size, fixed by the Game view at the first frame.</summary>
        public int Width { get; private set; }

        public int Height { get; private set; }

        /// <summary>Where the sound comes from (<see cref="AudioInput.Unity"/> or <see cref="AudioInput.Wwise"/>), or Off.</summary>
        public AudioInput RecordedAudio { get; private set; } = AudioInput.Off;

        /// <summary>0 when recording without audio.</summary>
        public int AudioSampleRate { get; private set; }

        public int AudioChannels { get; private set; }

        /// <summary>Recorded time, paused stretches excluded.</summary>
        public double Seconds => _clock.Seconds;

        /// <summary>Frames written to ffmpeg, repeats included.</summary>
        public long Frames => _video?.Written ?? 0;

        /// <summary>Video frames that repeat the previous one because the game rendered no new frame in time.</summary>
        public long Repeated => _video?.Repeated ?? 0;

        /// <summary>Frames not captured because the encoder was behind; they show as repeats.</summary>
        public long Dropped => Interlocked.Read(ref _dropped);

        /// <summary>Times the audio was padded or trimmed to stay in sync with the video.</summary>
        public int AudioCorrections => _audio?.Corrections ?? 0;

        public static RecordingSession Start(RecordingOptions options)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("Recording the Game view needs Play Mode.");
            if (options.FrameRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), options.FrameRate, "Frame rate must be positive.");

            var session = new RecordingSession(options);
            session._host = new GameObject("GameViewRecorder") { hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector };
            Object.DontDestroyOnLoad(session._host);
            session._host.AddComponent<FrameGrabber>().Session = session;
            return session;
        }

        /// <summary>Stops the clock; nothing is recorded until <see cref="Resume"/>.</summary>
        public void Pause() => _clock.Pause();

        public void Resume()
        {
            _clock.Resume();
            // Wwise keeps rendering while the Editor is paused; that audio belongs to no video.
            _discardWwise = true;
        }

        /// <summary>Ends the recording. ffmpeg finishes the file in the background (<see cref="RecordingState.Finalizing"/>).</summary>
        public void Stop()
        {
            if (_state == RecordingState.Starting)
            {
                Cleanup();
                _failure = "Stopped before the first frame was captured.";
                _state = RecordingState.Failed;
                return;
            }
            if (_state != RecordingState.Recording)
                return;

            PollWwise();
            double seconds = _clock.Seconds;
            _clock.Stop();
            _state = RecordingState.Finalizing;
            Cleanup();
            _accepting = false;
            _video.Complete(_timeline.SlotCount(seconds));
            _audio?.Complete(seconds);
            _finisher = new Thread(Finish) { IsBackground = true, Name = "GameViewRecorder finish" };
            _finisher.Start();
        }

        /// <summary>Waits for ffmpeg to finish the file after <see cref="Stop"/>. False on timeout.</summary>
        public bool Wait(int milliseconds) => _finisher == null || _finisher.Join(milliseconds);

        /// <summary>Kills ffmpeg without waiting; an MKV keeps what was written so far, an MP4 is unreadable.</summary>
        public void Abort(string reason)
        {
            if (_state is RecordingState.Finished or RecordingState.Failed)
                return;
            Fail(reason);
        }

        internal void OnEndOfFrame()
        {
            if (_state == RecordingState.Starting && !Begin())
                return;
            if (_state != RecordingState.Recording || !_clock.IsRunning)
                return;
            if (!_timeline.TryClaim(_clock.Seconds, out long slot))
                return;

            var frame = _video.Rent();
            if (frame == null)
            {
                Interlocked.Increment(ref _dropped);
                return;
            }

            // Inside the frame loop Screen.width/height are the Game view's rendering resolution.
            int width = Screen.width;
            int height = Screen.height;
            PrepareCapture(width, height);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_capture);
            // Read with the frame they're drawn into, so the cursor, the clicks and the game stay in step.
            Vector2Int? cursor = _options.RecordCursor && CursorOverlay.TrySample(width, height, out int cursorX, out int cursorY)
                ? new Vector2Int(cursorX, cursorY)
                : null;
            var rings = _clicks?.Sample(_clock.Seconds);
            AsyncGPUReadback.Request(_capture, 0, TextureFormat.RGBA32, request => OnReadback(request, slot, frame, width, height, cursor, rings));
        }

        internal void OnUpdate()
        {
            if (_state != RecordingState.Recording)
                return;

            if (_ffmpeg.HasExited)
            {
                Fail($"ffmpeg exited during the recording (code {_ffmpeg.ExitCode}).");
                return;
            }

            var error = _video.Error ?? _audio?.Error;
            if (error != null)
            {
                Fail($"Writing to ffmpeg failed: {error.Message}");
                return;
            }

            PollWwise();
            if (_clicks != null && _clock.IsRunning)
                _clicks.Poll(_clock.Seconds, Screen.width, Screen.height);
            if (Time.realtimeSinceStartup >= _nextListenerCheck)
            {
                _nextListenerCheck = Time.realtimeSinceStartup + ListenerCheckInterval;
                AttachAudioTap();
            }
        }

        bool Begin()
        {
            Width = Screen.width;
            Height = Screen.height;
            try
            {
                StartAudio();

                string folder = Path.GetDirectoryName(_options.OutputPath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                // The screen copy reads back top row first where textures start at the top (D3D, Metal, Vulkan)
                // and bottom row first on OpenGL.
                _topRowFirst = SystemInfo.graphicsUVStartsAtTop;
                var input = new FfmpegInput
                {
                    Width = Width,
                    Height = Height,
                    FrameRate = _options.FrameRate,
                    FlipVertically = !_topRowFirst,
                    AudioPort = _audio?.Port ?? 0,
                    AudioSampleRate = AudioSampleRate,
                    AudioChannels = AudioChannels
                };
                string arguments = FfmpegArguments.Build(input, _options.Preset, _options.CustomArguments, _options.Container, _options.OutputPath);
                _ffmpeg = FfmpegProcess.Start(_options.FfmpegPath, arguments);

                int frameBytes = Width * Height * 4;
                long byMemory = Math.Max(MinBufferedFrames, MaxBufferedBytes / frameBytes);
                _video = new VideoWriter(_ffmpeg.Input, frameBytes, (int)Math.Min(byMemory, Math.Max(MinBufferedFrames, _options.FrameRate)));
            }
            catch (Exception e)
            {
                Fail($"Couldn't start ffmpeg ({_options.FfmpegPath}): {e.Message}");
                return false;
            }

            _timeline = new FrameTimeline(_options.FrameRate);
            _cursorScale = _options.CursorScale > 0 ? _options.CursorScale : CursorOverlay.AutoScale(Height);
            _clicks = _options.RecordClicks ? new ClickRings() : null;
            _accepting = true;
            _state = RecordingState.Recording;
            _clock.Start();
            AttachAudioTap();
            return true;
        }

        void StartAudio()
        {
            var input = _options.Audio;
            if (input == AudioInput.Off)
                return;

            string wwiseProblem = null;
            if (input != AudioInput.Unity)
            {
                _wwise = WwiseCapture.TryStart(WwiseCapture.FindEngine(), out wwiseProblem);
                if (_wwise != null)
                {
                    OpenAudio(AudioInput.Wwise, _wwise.SampleRate, _wwise.Channels, MinAudioTolerance);
                    return;
                }
                if (input == AudioInput.Wwise)
                {
                    Debug.LogWarning($"{LogPrefix} {wwiseProblem} Recording without sound.");
                    return;
                }
            }

            if (_options.UnityAudioDisabled)
            {
                string reason = input == AudioInput.Unity ? "" : $" {wwiseProblem}";
                Debug.LogWarning($"{LogPrefix} Unity audio is disabled (Project Settings > Audio).{reason} Recording without sound.");
                return;
            }

            int rate = AudioSettings.outputSampleRate;
            if (rate <= 0)
            {
                Debug.LogWarning($"{LogPrefix} Unity audio reports no sample rate; recording without sound.");
                return;
            }

            // Audio callbacks can arrive in bursts of a whole DSP queue.
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            OpenAudio(AudioInput.Unity, rate, ChannelCount(AudioSettings.speakerMode),
                Math.Max(MinAudioTolerance, 2.0 * bufferLength * bufferCount / rate));
        }

        void OpenAudio(AudioInput input, int sampleRate, int channels, double tolerance)
        {
            RecordedAudio = input;
            AudioSampleRate = sampleRate;
            AudioChannels = channels;
            _audio = new AudioWriter(_clock, sampleRate, channels, tolerance);
        }

        /// <summary>Hands what Wwise rendered since the last frame to the audio writer. Main thread.</summary>
        void PollWwise()
        {
            if (_wwise == null)
                return;

            int count = _wwise.Read();
            if (_discardWwise)
            {
                _discardWwise = false;
                return;
            }
            if (count == 0)
                return;

            // The samples were rendered over the time they last, ending about now.
            int frames = count / _wwise.Channels;
            _audio.Push(_wwise.Samples, count, _wwise.Channels, _clock.Seconds - (double)frames / _wwise.SampleRate);
        }

        static int ChannelCount(AudioSpeakerMode mode) => mode switch
        {
            AudioSpeakerMode.Mono => 1,
            AudioSpeakerMode.Quad => 4,
            AudioSpeakerMode.Surround => 5,
            AudioSpeakerMode.Mode5point1 => 6,
            AudioSpeakerMode.Mode7point1 => 8,
            _ => 2
        };

        void PrepareCapture(int width, int height)
        {
            if (_capture != null && _capture.width == width && _capture.height == height)
                return;

            if (_capture != null)
            {
                // Pending readbacks still read the old texture.
                AsyncGPUReadback.WaitAllRequests();
                ReleaseCapture();
            }

            if ((width != Width || height != Height) && !_sizeWarned)
            {
                _sizeWarned = true;
                Debug.LogWarning($"{LogPrefix} The Game view changed to {width}×{height} during the recording. Frames are centered in the "
                                 + $"original {Width}×{Height}, cropped or with black bars. Set a fixed Game view resolution to avoid this.");
            }

            // Default read/write matches the back buffer (sRGB in Linear projects), so the bytes are copied as displayed.
            _capture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = "GameViewRecorder capture",
                hideFlags = HideFlags.HideAndDontSave
            };
            _capture.Create();
        }

        void OnReadback(AsyncGPUReadbackRequest request, long slot, byte[] frame, int width, int height, Vector2Int? cursor, ClickRings.Ring[] rings)
        {
            if (!_accepting || request.hasError)
            {
                if (_accepting)
                    Interlocked.Increment(ref _dropped);
                _video.Return(frame);
                return;
            }

            Place(request.GetData<byte>(), width, height, frame);
            if (rings != null || cursor != null)
                DrawPointer(cursor, rings, width, height, frame);
            _video.Write(slot, frame);
        }

        /// <summary>
        /// Draws the click rings, then the cursor over them, at pixels of the captured image (Screen
        /// coordinates), wherever <see cref="Place"/> put those pixels.
        /// </summary>
        void DrawPointer(Vector2Int? cursor, ClickRings.Ring[] rings, int width, int height, byte[] frame)
        {
            int offsetX = PlaceOffset(Width, width);
            int offsetRow = PlaceOffset(Height, height);
            if (rings != null)
            {
                foreach (var ring in rings)
                {
                    ClickRings.Draw(frame, Width, Height, ring.X + offsetX + 0.5f, StoredRow(ring.Y, height) + offsetRow + 0.5f,
                        ring.Radius, ring.Opacity, _cursorScale);
                }
            }
            if (cursor is { } position)
            {
                CursorOverlay.Draw(frame, Width, Height, position.x + offsetX, StoredRow(position.y, height) + offsetRow,
                    _topRowFirst ? 1 : -1, _cursorScale);
            }
        }

        /// <summary>Where row <paramref name="y"/> of the screen image (from the bottom) is in the read-back data.</summary>
        int StoredRow(int y, int height) => _topRowFirst ? height - 1 - y : y;

        /// <summary>How far <see cref="Place"/> moves the image's pixels along one axis (columns, or stored rows).</summary>
        static int PlaceOffset(int frameSize, int imageSize)
        {
            int copy = Math.Min(frameSize, imageSize);
            return (frameSize - copy) / 2 - (imageSize - copy) / 2;
        }

        /// <summary>Copies a captured image into a frame of the recording's size, centered and unscaled.</summary>
        void Place(NativeArray<byte> image, int width, int height, byte[] frame)
        {
            if (width == Width && height == Height)
            {
                image.CopyTo(frame);
                return;
            }

            Array.Clear(frame, 0, frame.Length);
            int copyWidth = Math.Min(width, Width);
            int copyHeight = Math.Min(height, Height);
            int sourceX = (width - copyWidth) / 2;
            int sourceY = (height - copyHeight) / 2;
            int targetX = (Width - copyWidth) / 2;
            int targetY = (Height - copyHeight) / 2;
            for (int row = 0; row < copyHeight; row++)
            {
                NativeArray<byte>.Copy(image, ((sourceY + row) * width + sourceX) * 4,
                    frame, ((targetY + row) * Width + targetX) * 4, copyWidth * 4);
            }
        }

        void AttachAudioTap()
        {
            if (RecordedAudio != AudioInput.Unity)
                return;

            AudioListener listener = null;
            // The overload with a sort mode is obsolete in newer versions; the one without it is documented from 6000.5.
#if UNITY_6000_5_OR_NEWER
            var candidates = Object.FindObjectsByType<AudioListener>();
#else
            var candidates = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#endif
            foreach (var candidate in candidates)
            {
                if (candidate.isActiveAndEnabled)
                {
                    listener = candidate;
                    break;
                }
            }

            if (_tap != null && listener != null && _tap.gameObject == listener.gameObject)
                return;

            DetachAudioTap();
            if (listener == null)
                return;

            _tap = listener.gameObject.AddComponent<AudioTap>();
            _tap.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            _tap.Writer = _audio;
        }

        void DetachAudioTap()
        {
            if (_tap != null)
            {
                _tap.Writer = null;
                Destroy(_tap);
            }
            _tap = null;
        }

        /// <summary>Removes everything the session put in the scene. Main thread.</summary>
        void Cleanup()
        {
            DetachAudioTap();
            _wwise?.Stop();
            _wwise = null;
            if (_host != null)
                Destroy(_host);
            _host = null;

            // Readbacks still pending are delivered (and given back) before the texture goes.
            AsyncGPUReadback.WaitAllRequests();
            ReleaseCapture();
        }

        void ReleaseCapture()
        {
            if (_capture == null)
                return;
            _capture.Release();
            Destroy(_capture);
            _capture = null;
        }

        static void Destroy(Object target)
        {
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        void Fail(string message)
        {
            _accepting = false;
            _clock.Stop();
            Cleanup();
            _audio?.Abort();
            _video?.Abort();
            _ffmpeg?.Kill();
            _failure = message;
            _state = RecordingState.Failed;
        }

        /// <summary>Background thread: waits for the writers and ffmpeg after <see cref="Stop"/>.</summary>
        void Finish()
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(FinishTimeoutMilliseconds);
            bool done = _video.Join(Remaining(deadline))
                        && (_audio == null || _audio.Join(Remaining(deadline)))
                        && _ffmpeg.WaitForExit(Remaining(deadline));
            if (!done)
            {
                _ffmpeg.Kill();
                _audio?.Abort();
                _video.Abort();
                _failure = $"ffmpeg didn't finish within {FinishTimeoutMilliseconds / 1000} s and was stopped. {CutShortNote(_options.Container)}";
                _state = RecordingState.Failed;
                return;
            }

            var error = _video.Error ?? _audio?.Error;
            if (_ffmpeg.ExitCode != 0)
                _failure = $"ffmpeg exited with code {_ffmpeg.ExitCode}.";
            else if (error != null)
                _failure = $"Writing to ffmpeg failed: {error.Message}";
            _state = _failure == null ? RecordingState.Finished : RecordingState.Failed;
        }

        /// <summary>What is left of a file whose ffmpeg was stopped before it finished.</summary>
        internal static string CutShortNote(ContainerFormat container) => container == ContainerFormat.Mp4
            ? "The MP4 is unreadable."
            : "The file may be cut short.";

        static int Remaining(DateTime deadline) => Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
    }
}
