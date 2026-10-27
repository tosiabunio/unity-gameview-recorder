namespace Airon.GameViewRecorder
{
    /// <summary>The kind of file a recording is written to. See <see cref="FfmpegArguments"/> for the exact options.</summary>
    public enum ContainerFormat
    {
        /// <summary>Matroska (.mkv). Keeps what was written if ffmpeg is stopped before it finishes the file.</summary>
        Mkv,

        /// <summary>MP4 (.mp4), with its index at the front. Plays everywhere, browsers included; readable only once ffmpeg has finished it.</summary>
        Mp4
    }
}
