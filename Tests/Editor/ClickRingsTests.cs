using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class ClickRingsTests
    {
        const int Size = 64;
        const byte Background = 100;

        [Test]
        public void Shape_GrowsAndFadesOverTheRingsTime()
        {
            Assert.IsFalse(ClickRings.Shape(-0.01, out _, out _), "not started");
            Assert.IsTrue(ClickRings.Shape(0, out float startRadius, out float startOpacity));
            Assert.AreEqual(ClickRings.StartRadius, startRadius, 1e-4);
            Assert.AreEqual(1, startOpacity, 1e-4);

            Assert.IsTrue(ClickRings.Shape(ClickRings.RingSeconds / 2, out float midRadius, out float midOpacity));
            Assert.Greater(midRadius, startRadius);
            Assert.Less(midRadius, ClickRings.EndRadius);
            Assert.AreEqual(0.5f, midOpacity, 1e-4);

            Assert.IsFalse(ClickRings.Shape(ClickRings.RingSeconds, out _, out _), "over");
        }

        [Test]
        public void Sample_GivesOneRingForTheLeftButtonAndTwoInARowForTheRight()
        {
            var left = new ClickRings();
            left.Add(1.0, 10, 20, rings: 1);
            var rings = left.Sample(1.0 + ClickRings.SecondRingDelay + 0.01);
            Assert.AreEqual(1, rings.Length);
            Assert.AreEqual(10, rings[0].X);
            Assert.AreEqual(20, rings[0].Y);

            var right = new ClickRings();
            right.Add(1.0, 10, 20, rings: 2);
            Assert.AreEqual(1, right.Sample(1.0 + ClickRings.SecondRingDelay / 2).Length, "the second ring hasn't started");
            rings = right.Sample(1.0 + ClickRings.SecondRingDelay + 0.01);
            Assert.AreEqual(2, rings.Length);
            Assert.Greater(rings[0].Radius, rings[1].Radius, "the first ring leads");
        }

        [Test]
        public void Sample_ForgetsClicksWhoseRingsAreOver()
        {
            var clicks = new ClickRings();
            clicks.Add(1.0, 0, 0, rings: 2);
            Assert.IsNotNull(clicks.Sample(1.0 + ClickRings.RingSeconds + 0.01), "the second ring is still growing");
            Assert.IsNull(clicks.Sample(1.0 + ClickRings.SecondRingDelay + ClickRings.RingSeconds + 0.01));
            Assert.IsNull(clicks.Sample(1.0), "forgotten, even for an earlier time");
        }

        [Test]
        public void Draw_PaintsAWhiteLineWithDarkerEdgesAroundTheCenter()
        {
            var frame = Frame();
            ClickRings.Draw(frame, Size, Size, centerX: 32.5f, centerRow: 32.5f, radius: 10, opacity: 1, scale: 1);

            Assert.AreEqual(255, Red(frame, 42, 32), "on the ring");
            Assert.AreEqual(255, Red(frame, 32, 22), "on the ring, above the center");
            Assert.Less(Red(frame, 44, 32), Background, "edge outside the line is darker");
            Assert.AreEqual(Background, Red(frame, 32, 32), "center untouched");
            Assert.AreEqual(Background, Red(frame, 50, 32), "outside untouched");
        }

        [Test]
        public void Draw_IsInvisibleAtZeroOpacityAndClipsAtTheEdges()
        {
            var frame = Frame();
            ClickRings.Draw(frame, Size, Size, 32.5f, 32.5f, radius: 10, opacity: 0, scale: 1);
            Assert.AreEqual(Background, Red(frame, 42, 32));

            Assert.DoesNotThrow(() => ClickRings.Draw(frame, Size, Size, 0.5f, 0.5f, radius: 18, opacity: 1, scale: 4));
            Assert.DoesNotThrow(() => ClickRings.Draw(frame, Size, Size, Size - 0.5f, Size - 0.5f, radius: 18, opacity: 1, scale: 4));
        }

        static byte[] Frame()
        {
            var frame = new byte[Size * Size * 4];
            for (int i = 0; i < frame.Length; i++)
                frame[i] = Background;
            return frame;
        }

        static byte Red(byte[] frame, int x, int row) => frame[(row * Size + x) * 4];
    }
}
