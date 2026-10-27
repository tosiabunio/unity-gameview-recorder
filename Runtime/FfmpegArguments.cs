using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Airon.GameViewRecorder
{
    /// <summary>What ffmpeg receives: raw RGBA frames on stdin and, optionally, raw float audio over TCP.</summary>
    public struct FfmpegInput
    {
        public int Width;
        public int Height;
        public int FrameRate;

        /// <summary>Frames arrive bottom row first (OpenGL).</summary>
        public bool FlipVertically;

        /// <summary>Loopback TCP port ffmpeg reads the audio from; 0 records no audio.</summary>
        public int AudioPort;
        public int AudioSampleRate;
        public int AudioChannels;
    }

    /// <summary>Builds the ffmpeg command line for a recording.</summary>
    public static class FfmpegArguments
    {
        // 4:2:0 needs even sizes; the conversion uses (and the file is tagged with) BT.709, which players assume for video.
        const string Yuv420Filters = "pad=ceil(iw/2)*2:ceil(ih/2)*2,scale=out_color_matrix=bt709:out_range=tv,format=yuv420p";
        static readonly string[] Bt709Tags = { "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv" };

        public static string Describe(VideoPreset preset) => preset switch
        {
            VideoPreset.Shareable => "H.264 4:2:0 (libx264, CRF 18) + AAC. Plays everywhere.",
            VideoPreset.Lossless => "FFV1 RGB + FLAC. Exact pixels for editing or archiving; larger files, plays in VLC/mpv.",
            VideoPreset.Hardware => "H.264 4:2:0 on an NVIDIA GPU (h264_nvenc) + AAC. Almost no CPU cost at 1080p and above.",
            _ => "Your own ffmpeg codec options, e.g. \"-c:v libx264 -crf 0 -c:a flac\". Don't add -vf; the recorder sets it."
        };

        public static string Describe(ContainerFormat container) => container switch
        {
            ContainerFormat.Mp4 => "Plays everywhere, browsers included. Readable only once ffmpeg has finished the file.",
            _ => "Keeps what was written if ffmpeg is stopped before it finishes the file. Not every player or site takes MKV."
        };

        /// <summary>The file extension, with its dot.</summary>
        public static string Extension(ContainerFormat container) => container == ContainerFormat.Mp4 ? ".mp4" : ".mkv";

        /// <summary>
        /// The format a recording with <paramref name="preset"/> gets when <paramref name="requested"/> is
        /// asked for: Lossless stays in MKV, since few players read FFV1 from an MP4.
        /// </summary>
        public static ContainerFormat ContainerFor(VideoPreset preset, ContainerFormat requested) =>
            preset == VideoPreset.Lossless ? ContainerFormat.Mkv : requested;

        /// <summary>
        /// The full argument string. <paramref name="customArguments"/> replace the preset's codec
        /// options when <paramref name="preset"/> is <see cref="VideoPreset.Custom"/>; they come after the
        /// format's own options, so they can override them.
        /// </summary>
        public static string Build(FfmpegInput input, VideoPreset preset, string customArguments, ContainerFormat container, string outputPath)
        {
            var args = new List<string>
            {
                "-hide_banner", "-nostats", "-loglevel", "warning", "-y",
                "-f", "rawvideo", "-pix_fmt", "rgba",
                "-video_size", $"{input.Width}x{input.Height}",
                "-framerate", Number(input.FrameRate),
                "-i", "pipe:0"
            };

            bool audio = input.AudioPort > 0;
            if (audio)
            {
                // Tiny probe: ffmpeg opens its inputs one after another, and a default probe would
                // block on 5 s of audio while the video queue on stdin overflows.
                args.AddRange(new[]
                {
                    "-f", "f32le", "-ar", Number(input.AudioSampleRate), "-ac", Number(input.AudioChannels),
                    "-probesize", "32", "-analyzeduration", "0",
                    "-i", $"tcp://127.0.0.1:{Number(input.AudioPort)}"
                });
            }

            args.AddRange(new[] { "-map", "0:v" });
            if (audio)
                args.AddRange(new[] { "-map", "1:a" });

            var filters = new List<string>();
            if (input.FlipVertically)
                filters.Add("vflip");

            string custom = null;
            switch (preset)
            {
                case VideoPreset.Shareable:
                    filters.Add(Yuv420Filters);
                    args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "18" });
                    args.AddRange(Bt709Tags);
                    if (audio)
                        args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
                    break;
                case VideoPreset.Lossless:
                    args.AddRange(new[] { "-c:v", "ffv1", "-level", "3", "-g", "1", "-slices", "16", "-slicecrc", "1", "-pix_fmt", "bgr0" });
                    if (audio)
                        args.AddRange(new[] { "-c:a", "flac" });
                    break;
                case VideoPreset.Hardware:
                    filters.Add(Yuv420Filters);
                    args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", "p5", "-tune", "hq", "-rc", "vbr", "-cq", "19", "-b:v", "0" });
                    args.AddRange(Bt709Tags);
                    if (audio)
                        args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
                    break;
                default:
                    custom = customArguments?.Trim();
                    break;
            }

            if (filters.Count > 0)
                args.AddRange(new[] { "-vf", string.Join(",", filters) });

            // A second pass at the end moves the index to the front, so players start before the whole file is in.
            if (container == ContainerFormat.Mp4)
                args.AddRange(new[] { "-movflags", "+faststart" });

            var line = new StringBuilder(Join(args));
            if (!string.IsNullOrEmpty(custom))
                line.Append(' ').Append(custom);
            line.Append(" -f ").Append(container == ContainerFormat.Mp4 ? "mp4" : "matroska").Append(' ').Append(Quote(outputPath));
            return line.ToString();
        }

        public static string Join(IEnumerable<string> args)
        {
            var line = new StringBuilder();
            foreach (var arg in args)
            {
                if (line.Length > 0)
                    line.Append(' ');
                line.Append(Quote(arg));
            }
            return line.ToString();
        }

        /// <summary>Quotes one argument the way the Windows C runtime (and Mono on other platforms) splits it back.</summary>
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return arg;

            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                // Backslashes are literal unless they precede a quote.
                quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2).Append('"');
            return quoted.ToString();
        }

        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
