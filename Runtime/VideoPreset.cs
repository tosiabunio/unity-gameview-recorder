namespace Airon.GameViewRecorder
{
    /// <summary>How ffmpeg encodes a recording. See <see cref="FfmpegArguments"/> for the exact options.</summary>
    public enum VideoPreset
    {
        /// <summary>H.264 (libx264) 4:2:0, CRF 18, AAC audio. Plays everywhere, good for sharing.</summary>
        Shareable,

        /// <summary>FFV1 RGB, FLAC audio. Exact pixels for editing or archiving; larger files.</summary>
        Lossless,

        /// <summary>H.264 on an NVIDIA GPU (h264_nvenc), AAC audio. Almost no CPU cost at high resolutions.</summary>
        Hardware,

        /// <summary>Codec options typed in by the user.</summary>
        Custom
    }
}
