using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class WriterTests
    {
        const int Timeout = 5000;

        [Test]
        public void VideoWriter_RepeatsFramesIntoEmptySlotsAndPadsTheEnd()
        {
            var output = new MemoryStream();
            var writer = new VideoWriter(output, frameBytes: 1, capacity: 8);

            Write(writer, 0, 10);
            Write(writer, 3, 20);
            writer.Complete(6);
            Assert.IsTrue(writer.Join(Timeout));

            CollectionAssert.AreEqual(new byte[] { 10, 10, 10, 20, 20, 20 }, output.ToArray());
            Assert.AreEqual(6, writer.Written);
            Assert.AreEqual(4, writer.Repeated);
            Assert.IsNull(writer.Error);
        }

        [Test]
        public void VideoWriter_StartsWithBlackWhenTheFirstFrameIsLate()
        {
            var output = new MemoryStream();
            var writer = new VideoWriter(output, frameBytes: 1, capacity: 8);

            Write(writer, 2, 7);
            writer.Complete(3);
            Assert.IsTrue(writer.Join(Timeout));

            CollectionAssert.AreEqual(new byte[] { 0, 0, 7 }, output.ToArray());
        }

        [Test]
        public void VideoWriter_RentRefusesBeyondCapacity()
        {
            var writer = new VideoWriter(new MemoryStream(), frameBytes: 4, capacity: 2);
            var first = writer.Rent();
            Assert.IsNotNull(first);
            Assert.IsNotNull(writer.Rent());
            Assert.IsNull(writer.Rent());

            writer.Return(first);
            Assert.IsNotNull(writer.Rent());
            writer.Abort();
            Assert.IsTrue(writer.Join(Timeout));
        }

        [Test]
        public void AudioWriter_StreamsAudioAndPadsSilenceToTheEnd()
        {
            const int rate = 1000;
            var clock = new RecordingClock();
            clock.Start();
            var writer = new AudioWriter(clock, rate, channels: 2, toleranceSeconds: 10);

            using var client = new TcpClient("127.0.0.1", writer.Port);
            writer.Push(Enumerable.Repeat(0.5f, 20).ToArray(), 2);
            writer.Push(new float[] { 1, 2, 3, 4, 5, 6 }, 3);
            writer.Complete(1.0);

            var received = ReadAll(client.GetStream());
            Assert.IsTrue(writer.Join(Timeout));
            Assert.IsNull(writer.Error);

            var samples = new float[received.Length / sizeof(float)];
            System.Buffer.BlockCopy(received, 0, samples, 0, received.Length);
            Assert.AreEqual(rate * 2, samples.Length, "one second of stereo");
            CollectionAssert.Contains(samples, 0.5f);

            // The 3-channel chunk is remapped to stereo: the third channel is dropped.
            int remapped = System.Array.IndexOf(samples, 1f);
            Assert.GreaterOrEqual(remapped, 0);
            CollectionAssert.AreEqual(new float[] { 1, 2, 4, 5 }, samples.Skip(remapped).Take(4).ToArray());
        }

        [Test]
        public void AudioWriter_PlacesTimestampedChunksAtTheirTime()
        {
            const int rate = 100;
            var clock = new RecordingClock();
            clock.Start();
            var writer = new AudioWriter(clock, rate, channels: 1, toleranceSeconds: 10);

            using var client = new TcpClient("127.0.0.1", writer.Port);
            // Only the first 3 of the 5 samples count, and they start half a second in.
            writer.Push(new float[] { 1, 2, 3, 9, 9 }, 3, 1, 0.5);
            writer.Complete(1.0);

            var received = ReadAll(client.GetStream());
            Assert.IsTrue(writer.Join(Timeout));
            var samples = new float[received.Length / sizeof(float)];
            System.Buffer.BlockCopy(received, 0, samples, 0, received.Length);

            Assert.AreEqual(rate, samples.Length);
            CollectionAssert.AreEqual(new float[50], samples.Take(50).ToArray());
            CollectionAssert.AreEqual(new float[] { 1, 2, 3, 0 }, samples.Skip(50).Take(4).ToArray());
        }

        [Test]
        public void AudioWriter_AbortBeforeConnectingEndsQuietly()
        {
            var clock = new RecordingClock();
            var writer = new AudioWriter(clock, 48000, 2, 0.15);
            writer.Abort();
            Assert.IsTrue(writer.Join(Timeout));
            Assert.IsNull(writer.Error);
        }

        static void Write(VideoWriter writer, long slot, byte value)
        {
            var frame = writer.Rent();
            frame[0] = value;
            writer.Write(slot, frame);
        }

        static byte[] ReadAll(Stream stream)
        {
            stream.ReadTimeout = Timeout;
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
