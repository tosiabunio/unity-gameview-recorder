namespace Airon.GameViewRecorder
{
    /// <summary>Everything a <see cref="RecordingSession"/> needs to know up front.</summary>
    public sealed class RecordingOptions
    {
        public string FfmpegPath;

        /// <summary>The file to write, with the extension of <see cref="Container"/>; its folder is created when missing.</summary>
        public string OutputPath;

        public int FrameRate = 60;
        public VideoPreset Preset = VideoPreset.Shareable;

        /// <summary>ffmpeg codec options for <see cref="VideoPreset.Custom"/>.</summary>
        public string CustomArguments = "";

        /// <summary>MKV or MP4. Lossless needs MKV: see <see cref="FfmpegArguments.ContainerFor"/>.</summary>
        public ContainerFormat Container = ContainerFormat.Mkv;

        public AudioInput Audio = AudioInput.Auto;

        /// <summary>
        /// "Disable Unity Audio" is set in Project Settings > Audio. Unity then logs an error for
        /// every query of its audio settings, so the session doesn't touch them.
        /// </summary>
        public bool UnityAudioDisabled;

        /// <summary>Draws a mouse cursor into the video (see <see cref="CursorOverlay"/>); the screen copy has none.</summary>
        public bool RecordCursor;

        /// <summary>Draws a growing ring where a mouse button goes down (see <see cref="ClickRings"/>).</summary>
        public bool RecordClicks;

        /// <summary>Video pixels per pixel of the drawn cursor and click rings; 0 picks it from the video height.</summary>
        public int CursorScale;
    }
}
