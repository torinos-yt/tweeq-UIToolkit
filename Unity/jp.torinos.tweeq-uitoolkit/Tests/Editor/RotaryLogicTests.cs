using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class RotaryLogicTests
    {
        const double TOLERANCE = 1e-12;

        #region Accumulation

        [Test]
        public void LocalAccumulatesRawDelta()
        {
            var result = RotaryLogic.GetDragValue(10.0, 5.0, 45.0, true);
            Assert.That(result.local, Is.EqualTo(15.0).Within(TOLERANCE));
            Assert.That(result.output, Is.EqualTo(0.0).Within(TOLERANCE));
        }

        [Test]
        public void LocalIsNeverFedBackFromSnappedOutput()
        {
            double local = 0.0;
            double output = 0.0;
            for (int i = 0; i < 3; i++)
            {
                var result = RotaryLogic.GetDragValue(local, 10.0, 45.0, true);
                local = result.local;
                output = result.output;
            }

            Assert.That(local, Is.EqualTo(30.0).Within(TOLERANCE));
            Assert.That(output, Is.EqualTo(45.0).Within(TOLERANCE));
        }

        [Test]
        public void UnsnappedOutputFollowsLocalExactly()
        {
            var result = RotaryLogic.GetDragValue(0.0, 50.0, 45.0, false);
            Assert.That(result.local, Is.EqualTo(50.0).Within(TOLERANCE));
            Assert.That(result.output, Is.EqualTo(50.0).Within(TOLERANCE));
        }

        #endregion

        #region Snapping

        [Test]
        public void OutputSnapsToNearestMultiple()
        {
            Assert.That(RotaryLogic.GetDragValue(0.0, 50.0, 45.0, true).output,
                Is.EqualTo(45.0).Within(TOLERANCE));
            Assert.That(RotaryLogic.GetDragValue(0.0, 70.0, 45.0, true).output,
                Is.EqualTo(90.0).Within(TOLERANCE));
            Assert.That(RotaryLogic.GetDragValue(0.0, -50.0, 45.0, true).output,
                Is.EqualTo(-45.0).Within(TOLERANCE));
        }

        [Test]
        public void InvalidSnapPassesValueThrough()
        {
            var zeroSnap = RotaryLogic.GetDragValue(0.0, 50.0, 0.0, true);
            Assert.That(zeroSnap.output, Is.EqualTo(50.0).Within(TOLERANCE));

            var infiniteSnap = RotaryLogic.GetDragValue(0.0, 50.0, double.PositiveInfinity, true);
            Assert.That(infiniteSnap.output, Is.EqualTo(50.0).Within(TOLERANCE));

            var nanSnap = RotaryLogic.GetDragValue(0.0, 50.0, double.NaN, true);
            Assert.That(nanSnap.output, Is.EqualTo(50.0).Within(TOLERANCE));
        }

        [Test]
        public void SnappedOutputNeverReturnsNegativeZero()
        {
            var result = RotaryLogic.GetDragValue(0.0, -5.0, 45.0, true);
            Assert.That(result.local, Is.EqualTo(-5.0).Within(TOLERANCE));
            Assert.That(result.output, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(double.IsNegative(result.output), Is.False);
        }

        #endregion
    }
}
