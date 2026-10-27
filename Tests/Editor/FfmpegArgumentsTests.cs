using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class FfmpegArgumentsTests
    {
        static FfmpegInput Input(bool audio = true, bool flip = true) => new()
        {
            Width = 1920,
            Height = 1080,
            FrameRate = 60,
            FlipVertically = flip,
            AudioPort = audio ? 51234 : 0,
            AudioSampleRate = 48000,
            AudioChannels = 2
        };

        [Test]
        public void Shareable_ReadsRawFramesAndAudioAndEncodesH264()
        {
            string line = FfmpegArguments.Build(Input(), VideoPreset.Shareable, "", ContainerFormat.Mkv, @"D:\Videos\out.mkv");
            StringAssert.Contains("-f rawvideo -pix_fmt rgba -video_size 1920x1080 -framerate 60 -i pipe:0", line);
            StringAssert.Contains("-f f32le -ar 48000 -ac 2 -probesize 32 -analyzeduration 0 -i tcp://127.0.0.1:51234", line);
            StringAssert.Contains("-map 0:v -map 1:a", line);
            StringAssert.Contains("-vf vflip,pad=", line);
            StringAssert.Contains("format=yuv420p", line);
            StringAssert.Contains("-c:v libx264", line);
            StringAssert.Contains("-c:a aac", line);
            StringAssert.EndsWith(@"-f matroska D:\Videos\out.mkv", line);
        }

        [Test]
        public void WithoutAudio_HasNoAudioInputOrCodec()
        {
            string line = FfmpegArguments.Build(Input(audio: false), VideoPreset.Shareable, "", ContainerFormat.Mkv, "out.mkv");
            StringAssert.DoesNotContain("tcp://", line);
            StringAssert.DoesNotContain("1:a", line);
            StringAssert.DoesNotContain("-c:a", line);
        }

        [Test]
        public void Lossless_KeepsRgbAndNeedsNoPadding()
        {
            string line = FfmpegArguments.Build(Input(flip: false), VideoPreset.Lossless, "", ContainerFormat.Mkv, "out.mkv");
            StringAssert.Contains("-c:v ffv1", line);
            StringAssert.Contains("-pix_fmt bgr0", line);
            StringAssert.Contains("-c:a flac", line);
            StringAssert.DoesNotContain("-vf", line);
        }

        [Test]
        public void Custom_AppendsTheUserOptionsBeforeTheOutput()
        {
            string line = FfmpegArguments.Build(Input(), VideoPreset.Custom, "  -c:v libx264rgb -crf 0 ", ContainerFormat.Mkv, "out.mkv");
            StringAssert.Contains("-vf vflip -c:v libx264rgb -crf 0 -f matroska out.mkv", line);
            StringAssert.DoesNotContain("libx264 ", line);
        }

        [Test]
        public void Mp4_MovesTheIndexToTheFront()
        {
            string line = FfmpegArguments.Build(Input(), VideoPreset.Shareable, "", ContainerFormat.Mp4, "out.mp4");
            StringAssert.Contains("-c:v libx264", line);
            StringAssert.Contains("-c:a aac", line);
            StringAssert.EndsWith("-movflags +faststart -f mp4 out.mp4", line);
            StringAssert.DoesNotContain("matroska", line);
        }

        [Test]
        public void Mp4_CustomOptionsComeAfterTheFormatOptions()
        {
            string line = FfmpegArguments.Build(Input(), VideoPreset.Custom, "-c:v libx264 -movflags +frag_keyframe", ContainerFormat.Mp4, "out.mp4");
            StringAssert.EndsWith("-vf vflip -movflags +faststart -c:v libx264 -movflags +frag_keyframe -f mp4 out.mp4", line);
        }

        [Test]
        public void ContainerFor_KeepsLosslessInMkv()
        {
            Assert.AreEqual(ContainerFormat.Mp4, FfmpegArguments.ContainerFor(VideoPreset.Shareable, ContainerFormat.Mp4));
            Assert.AreEqual(ContainerFormat.Mp4, FfmpegArguments.ContainerFor(VideoPreset.Custom, ContainerFormat.Mp4));
            Assert.AreEqual(ContainerFormat.Mkv, FfmpegArguments.ContainerFor(VideoPreset.Lossless, ContainerFormat.Mp4));
            Assert.AreEqual(ContainerFormat.Mkv, FfmpegArguments.ContainerFor(VideoPreset.Hardware, ContainerFormat.Mkv));
            Assert.AreEqual(".mkv", FfmpegArguments.Extension(ContainerFormat.Mkv));
            Assert.AreEqual(".mp4", FfmpegArguments.Extension(ContainerFormat.Mp4));
        }

        [Test]
        public void OutputPathWithSpaces_IsQuoted()
        {
            string line = FfmpegArguments.Build(Input(), VideoPreset.Shareable, "", ContainerFormat.Mkv, @"C:\My Videos\Cop 2026.mkv");
            StringAssert.EndsWith("-f matroska \"C:\\My Videos\\Cop 2026.mkv\"", line);
        }

        [TestCase("plain", "plain")]
        [TestCase("two words", "\"two words\"")]
        [TestCase("", "\"\"")]
        [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [TestCase(@"C:\dir\", "C:\\dir\\")]
        [TestCase(@"C:\my dir\", "\"C:\\my dir\\\\\"")]
        public void Quote_FollowsWindowsCommandLineRules(string argument, string expected)
        {
            Assert.AreEqual(expected, FfmpegArguments.Quote(argument));
        }
    }
}
