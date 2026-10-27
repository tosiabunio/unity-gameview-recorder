using System;
using System.IO;
using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class EditorPartsTests
    {
        [Test]
        public void BuildFileName_ReplacesTokensAndInvalidCharacters()
        {
            var time = new DateTime(2026, 10, 11, 9, 5, 3);
            Assert.AreEqual("Cop From Hel 2026-10-11 09-05-03.mkv", RecorderSettings.BuildFileName("{product} {date}", "Cop From Hel", "posterunek", time, ".mkv"));
            Assert.AreEqual("a_b posterunek.mkv", RecorderSettings.BuildFileName("a/b {scene}", "x", "posterunek", time, ".mkv"));
            Assert.AreEqual("P 2026-10-11 09-05-03.mp4", RecorderSettings.BuildFileName("", "P", "s", time, ".mp4"));
        }

        [Test]
        public void UniquePath_NumbersExistingNames()
        {
            string path = Path.Combine("rec", "clip.mkv");
            Assert.AreEqual(path, RecorderSettings.UniquePath(path, _ => false));
            Assert.AreEqual(Path.Combine("rec", "clip (3).mkv"),
                RecorderSettings.UniquePath(path, p => p == path || p.EndsWith("clip (2).mkv")));
        }

        [Test]
        public void ResolveFolder_IsRelativeToTheProject()
        {
            string project = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Project"));
            Assert.AreEqual(Path.Combine(project, "Recordings"), RecorderSettings.ResolveFolder("Recordings", project));
            Assert.AreEqual(Path.Combine(project, "Recordings"), RecorderSettings.ResolveFolder("  ", project));
            string absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Clips"));
            Assert.AreEqual(absolute, RecorderSettings.ResolveFolder(absolute, project));
        }

        [Test]
        public void Search_PrefersTheConfiguredPathEvenWhenMissing()
        {
            var candidates = new[] { "a/ffmpeg", "b/ffmpeg" };
            Assert.AreEqual("b/ffmpeg", FfmpegLocator.Search("", candidates, p => p == "b/ffmpeg"));
            Assert.AreEqual("my/ffmpeg", FfmpegLocator.Search("my/ffmpeg", candidates, _ => true));
            Assert.IsNull(FfmpegLocator.Search("typo/ffmpeg", candidates, p => p != "typo/ffmpeg"));
        }

        [Test]
        public void ParseEncoders_ReadsNamesAfterTheHeader()
        {
            var info = new FfmpegInfo();
            info.ParseEncoders("Encoders:\n V..... = Video\n ------\n V....D libx264              libx264 H.264\n A....D aac                  AAC (Advanced Audio Coding)\n");
            Assert.IsTrue(info.HasEncoder("libx264"));
            Assert.IsTrue(info.HasEncoder("aac"));
            Assert.IsFalse(info.HasEncoder("Video"));
        }
    }
}
