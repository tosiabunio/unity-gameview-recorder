using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class CursorOverlayTests
    {
        const int Size = 40;
        const byte Background = 100;

        [Test]
        public void Draw_PutsTheHotspotOnTheGivenPixelAndPointsDown()
        {
            var frame = Frame();
            CursorOverlay.Draw(frame, Size, Size, x: 5, row: 3, rowStep: 1, scale: 1);

            Assert.AreEqual(0, Red(frame, 5, 3), "hotspot is outline");
            Assert.AreEqual(Background, Red(frame, 4, 3), "nothing left of the hotspot");
            Assert.AreEqual(Background, Red(frame, 5, 2), "nothing above the hotspot");
            Assert.AreEqual(255, Red(frame, 6, 5), "fill below the hotspot");
            Assert.AreEqual(255, frame[Index(5, 3) + 3], "opaque");
            Assert.AreEqual(Background, Red(frame, 5 + CursorOverlay.ArrowWidth - 1, 3), "top right corner is transparent");
        }

        [Test]
        public void Draw_GoesUpTheStoredRowsWhenTheyAreBottomFirst()
        {
            var top = Frame();
            var bottom = Frame();
            CursorOverlay.Draw(top, Size, Size, x: 2, row: 1, rowStep: 1, scale: 1);
            CursorOverlay.Draw(bottom, Size, Size, x: 2, row: Size - 2, rowStep: -1, scale: 1);

            // The same picture, rows stored the other way round.
            for (int row = 0; row < Size; row++)
                for (int x = 0; x < Size; x++)
                    Assert.AreEqual(Red(top, x, row), Red(bottom, x, Size - 1 - row), $"pixel {x}, {row}");
        }

        [Test]
        public void Draw_ScalesEachPixelToASquare()
        {
            var frame = Frame();
            CursorOverlay.Draw(frame, Size, Size, x: 0, row: 0, rowStep: 1, scale: 2);

            Assert.AreEqual(0, Red(frame, 0, 0));
            Assert.AreEqual(0, Red(frame, 1, 1), "the hotspot pixel covers 2×2");
            Assert.AreEqual(Background, Red(frame, 2, 0), "arrow pixel (1, 0) is transparent");
            Assert.AreEqual(0, Red(frame, 2, 2), "arrow pixel (1, 1) is outline");
        }

        [Test]
        public void Draw_ClipsAtTheFrameEdges()
        {
            var frame = Frame();
            Assert.DoesNotThrow(() => CursorOverlay.Draw(frame, Size, Size, x: Size - 3, row: Size - 3, rowStep: 1, scale: 3));
            Assert.DoesNotThrow(() => CursorOverlay.Draw(frame, Size, Size, x: -5, row: -5, rowStep: 1, scale: 1));
            Assert.DoesNotThrow(() => CursorOverlay.Draw(frame, Size, Size, x: 0, row: 2, rowStep: -1, scale: 1));
            Assert.AreEqual(0, Red(frame, Size - 1, Size - 1));
        }

        [Test]
        public void AutoScale_GrowsWithTheVideoHeight()
        {
            Assert.AreEqual(1, CursorOverlay.AutoScale(360));
            Assert.AreEqual(1, CursorOverlay.AutoScale(1080));
            Assert.AreEqual(1, CursorOverlay.AutoScale(1440));
            Assert.AreEqual(2, CursorOverlay.AutoScale(2160));
            Assert.AreEqual(4, CursorOverlay.AutoScale(4320));
        }

        static byte[] Frame()
        {
            var frame = new byte[Size * Size * 4];
            for (int i = 0; i < frame.Length; i++)
                frame[i] = Background;
            return frame;
        }

        static int Index(int x, int row) => (row * Size + x) * 4;

        static byte Red(byte[] frame, int x, int row) => frame[Index(x, row)];
    }
}
