using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class FrameTimelineTests
    {
        [Test]
        public void SlotAt_FloorsTimeTimesRate()
        {
            var timeline = new FrameTimeline(60);
            Assert.AreEqual(0, timeline.SlotAt(0));
            Assert.AreEqual(0, timeline.SlotAt(0.016));
            Assert.AreEqual(1, timeline.SlotAt(1.0 / 60));
            Assert.AreEqual(59, timeline.SlotAt(0.999));
            Assert.AreEqual(0, timeline.SlotAt(-1));
        }

        [Test]
        public void SlotAt_ExactBoundaryIsNotRoundedDown()
        {
            // 0.1 * 30 is 2.9999999999999996 in doubles.
            Assert.AreEqual(3, new FrameTimeline(30).SlotAt(0.1));
        }

        [Test]
        public void TryClaim_SkipsFramesInAnAlreadyFilledSlot()
        {
            var timeline = new FrameTimeline(30);
            Assert.IsTrue(timeline.TryClaim(0.001, out long first));
            Assert.IsFalse(timeline.TryClaim(0.010, out _));
            Assert.IsTrue(timeline.TryClaim(0.034, out long second));
            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
            Assert.AreEqual(2, timeline.Next);
        }

        [Test]
        public void TryClaim_JumpsOverSlotsTheGameMissed()
        {
            var timeline = new FrameTimeline(60);
            timeline.TryClaim(0, out _);
            Assert.IsTrue(timeline.TryClaim(0.1, out long slot));
            Assert.AreEqual(6, slot);
        }

        [Test]
        public void SlotCount_RoundsTheDuration()
        {
            var timeline = new FrameTimeline(60);
            Assert.AreEqual(600, timeline.SlotCount(10.004));
            Assert.AreEqual(601, timeline.SlotCount(10.01));
            Assert.AreEqual(0, timeline.SlotCount(0));
        }
    }
}
