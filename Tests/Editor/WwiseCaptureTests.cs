using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class WwiseCaptureTests
    {
        [SetUp]
        public void Reset() => FakeWwiseEngine.Reset();

        [Test]
        public void TryStart_StartsCapturingTheMainOutput()
        {
            var capture = WwiseCapture.TryStart(typeof(FakeWwiseEngine), out string problem);
            Assert.IsNotNull(capture, problem);
            Assert.AreEqual(48000, capture.SampleRate);
            Assert.AreEqual(2, capture.Channels);
            Assert.AreEqual(FakeWwiseEngine.MainOutput, FakeWwiseEngine.Started);
            Assert.AreEqual(1, FakeWwiseEngine.Clears);
        }

        [Test]
        public void Read_ReturnsWhatWwiseRenderedSinceTheLastRead()
        {
            var capture = WwiseCapture.TryStart(typeof(FakeWwiseEngine), out _);
            FakeWwiseEngine.Pending.AddRange(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
            Assert.AreEqual(4, capture.Read());
            CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0.3f, 0.4f }, capture.Samples.Take(4).ToArray());
            Assert.AreEqual(0, capture.Read());
        }

        [Test]
        public void Stop_StopsTheOutputAndClearsTheBuffer()
        {
            var capture = WwiseCapture.TryStart(typeof(FakeWwiseEngine), out _);
            capture.Stop();
            Assert.AreEqual(FakeWwiseEngine.MainOutput, FakeWwiseEngine.Stopped);
            Assert.AreEqual(2, FakeWwiseEngine.Clears);
        }

        [Test]
        public void TryStart_ExplainsWhyThereIsNoCapture()
        {
            Assert.IsNull(WwiseCapture.TryStart(null, out string noWwise));
            StringAssert.Contains("isn't in this project", noWwise);

            Assert.IsNull(WwiseCapture.TryStart(typeof(WwiseWithoutCapture), out string noCapture));
            StringAssert.Contains("no device capture", noCapture);

            FakeWwiseEngine.Initialized = false;
            Assert.IsNull(WwiseCapture.TryStart(typeof(FakeWwiseEngine), out string notRunning));
            StringAssert.Contains("isn't initialized", notRunning);
            Assert.AreEqual(0UL, FakeWwiseEngine.Started);
        }

        /// <summary>Mirrors the static API of the Wwise integration's AkUnitySoundEngine.</summary>
        static class FakeWwiseEngine
        {
            public const ulong MainOutput = 6782401577888067914;
            public static readonly List<float> Pending = new();
            public static bool Initialized;
            public static ulong Started;
            public static ulong Stopped;
            public static int Clears;

            public static void Reset()
            {
                Pending.Clear();
                Initialized = true;
                Started = Stopped = 0;
                Clears = 0;
            }

            public static bool IsInitialized() => Initialized;
            public static uint GetSampleRate() => 48000;
            public static ulong GetOutputID(uint shareSet, uint device) => shareSet == 0 && device == 0 ? MainOutput : 1;
            public static ChannelConfig GetSpeakerConfiguration(ulong output) => new() { uNumChannels = 2 };
            public static void StartDeviceCapture(ulong output) => Started = output;
            public static void StopDeviceCapture(ulong output) => Stopped = output;
            public static void ClearCaptureData() => Clears++;
            public static uint UpdateCaptureSampleCount(ulong output) => (uint)Pending.Count;

            public static uint GetCaptureSamples(ulong output, float[] samples, uint size)
            {
                int count = Math.Min((int)size, Pending.Count);
                Pending.CopyTo(0, samples, 0, count);
                Pending.RemoveRange(0, count);
                return (uint)count;
            }
        }

        public class ChannelConfig
        {
            // ReSharper disable once InconsistentNaming (the Wwise name)
            public uint uNumChannels { get; set; }
        }

        static class WwiseWithoutCapture
        {
            public static bool IsInitialized() => true;
        }
    }
}
