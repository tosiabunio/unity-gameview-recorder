using NUnit.Framework;

namespace Airon.GameViewRecorder.Tests
{
    public class AudioAlignerTests
    {
        const int Rate = 1000;
        const double Tolerance = 0.1;

        [Test]
        public void FirstChunk_IsPaddedToItsTime()
        {
            var aligner = new AudioAligner(Rate, Tolerance);
            aligner.Place(0.05, 20, out long silence, out int skip);
            Assert.AreEqual(50, silence);
            Assert.AreEqual(0, skip);
            Assert.AreEqual(70, aligner.Written);
            Assert.AreEqual(0, aligner.Corrections);
        }

        [Test]
        public void SteadyStreamWithJitter_IsNotTouched()
        {
            var aligner = new AudioAligner(Rate, Tolerance);
            aligner.Place(0, 20, out _, out _);
            double[] arrivals = { 0.03, 0.035, 0.07, 0.081, 0.09, 0.13 };
            foreach (double seconds in arrivals)
            {
                aligner.Place(seconds, 20, out long silence, out int skip);
                Assert.AreEqual(0, silence);
                Assert.AreEqual(0, skip);
            }
            Assert.AreEqual(140, aligner.Written);
            Assert.AreEqual(0, aligner.Corrections);
        }

        [Test]
        public void StreamFallingBehind_IsPaddedBackToTheClock()
        {
            var aligner = new AudioAligner(Rate, Tolerance);
            aligner.Place(0, 20, out _, out _);
            aligner.Place(0.5, 20, out long silence, out int skip);
            Assert.AreEqual(480, silence);
            Assert.AreEqual(0, skip);
            Assert.AreEqual(520, aligner.Written);
            Assert.AreEqual(1, aligner.Corrections);
        }

        [Test]
        public void StreamRunningAhead_IsTrimmed()
        {
            var aligner = new AudioAligner(Rate, Tolerance);
            aligner.Place(0, 300, out _, out _);
            aligner.Place(0.15, 100, out long silence, out int skip);
            Assert.AreEqual(0, silence);
            Assert.AreEqual(100, skip);
            Assert.AreEqual(300, aligner.Written);
            Assert.AreEqual(1, aligner.Corrections);
        }

        [Test]
        public void CatchUp_PadsOnlyBeyondTheTolerance()
        {
            var aligner = new AudioAligner(Rate, Tolerance);
            Assert.AreEqual(0, aligner.CatchUp(0.08));
            Assert.AreEqual(250, aligner.CatchUp(0.25));
            Assert.AreEqual(250, aligner.Written);
        }

        [Test]
        public void Finish_PadsToTheEndButNeverRemoves()
        {
            var aligner = new AudioAligner(Rate, Tolerance);
            aligner.Place(0, 100, out _, out _);
            Assert.AreEqual(900, aligner.Finish(1.0));
            Assert.AreEqual(0, aligner.Finish(0.5));
            Assert.AreEqual(1000, aligner.Written);
        }
    }
}
