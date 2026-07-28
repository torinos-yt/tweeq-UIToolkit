using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    /// <summary>
    /// SizeLogic's contract (the SizeLogic item under m6-wave2-spec.md's "test contract").
    /// Covers the other axis following a single-axis change, ratios involving 0, and the automatic-release decision when both axes change at once.
    /// </summary>
    public class SizeLogicTests
    {
        const double EPSILON = 1e-9;

        static void AssertResult(double x, double y, bool keepRatio, SizeApplyResult actual)
        {
            Assert.AreEqual(x, actual.X, EPSILON, "X");
            Assert.AreEqual(y, actual.Y, EPSILON, "Y");
            Assert.AreEqual(keepRatio, actual.KeepRatio, "KeepRatio");
        }

        #region Ratio following

        [Test]
        public void Apply_LockedXChangeScalesY()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 50.0, true);

            AssertResult(200.0, 100.0, true, result);
        }

        [Test]
        public void Apply_LockedYChangeScalesX()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 100.0, 25.0, true);

            AssertResult(50.0, 25.0, true, result);
        }

        [Test]
        public void Apply_UnlockedPassesBothAxesThrough()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 50.0, false);

            AssertResult(200.0, 50.0, false, result);
        }

        [Test]
        public void Apply_BaselineIsUsedInsteadOfPreviousValue()
        {
            // Even mid-gesture (where previous has already followed along), the multiplier is counted from the start value
            SizeApplyResult result = SizeLogic.Apply(200.0, 100.0, 300.0, 100.0, 100.0, 50.0, true);

            AssertResult(300.0, 150.0, true, result);
        }

        [Test]
        public void Apply_NoChangeKeepsValue()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 100.0, 50.0, true);

            AssertResult(100.0, 50.0, true, result);
        }

        #endregion

        #region Ratios involving 0

        [Test]
        public void Apply_ZeroBaselineOnTheDrivingAxisPassesThrough()
        {
            // No multiplier can be formed from 0. The other axis is left unchanged and passed through
            SizeApplyResult result = SizeLogic.Apply(0.0, 50.0, 10.0, 50.0, true);

            AssertResult(10.0, 50.0, true, result);
        }

        [Test]
        public void Apply_ZeroOnTheFollowingAxisStaysZero()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 0.0, 200.0, 0.0, true);

            AssertResult(200.0, 0.0, true, result);
        }

        [Test]
        public void Apply_BothZeroPassesThrough()
        {
            SizeApplyResult result = SizeLogic.Apply(0.0, 0.0, 10.0, 0.0, true);

            AssertResult(10.0, 0.0, true, result);
        }

        #endregion

        #region Automatic release

        [Test]
        public void Apply_BothAxesChangedWithNewRatioReleasesTheLock()
        {
            // 2:1 -> 1:1. Since this is input where the user came to break the ratio, the lock is released
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 200.0, true);

            AssertResult(200.0, 200.0, false, result);
        }

        [Test]
        public void Apply_BothAxesChangedWithSameRatioKeepsTheLock()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 100.0, true);

            AssertResult(200.0, 100.0, true, result);
        }

        [Test]
        public void Apply_SingleAxisChangeNeverReleasesTheLock()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 999.0, 50.0, true);

            Assert.IsTrue(result.KeepRatio);
        }

        [Test]
        public void Apply_BothAxesChangedWhileUnlockedStaysUnlocked()
        {
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 1.0, 2.0, false);

            AssertResult(1.0, 2.0, false, result);
        }

        [Test]
        public void Apply_RatioComparisonIsToleratedForRoundingNoise()
        {
            // 200/100.0000001 is treated as 2. Releasing here would cause the lock to come off by itself during a drag
            SizeApplyResult result = SizeLogic.Apply(100.0, 50.0, 200.0, 100.0000001, true);

            Assert.IsTrue(result.KeepRatio);
        }

        [Test]
        public void Apply_DegenerateRatioOnBothSidesDoesNotReleaseTheLock()
        {
            // If both axes get values while height stays 0, the ratio is +/-Infinity on both sides. Equal values are treated as "unchanged"
            SizeApplyResult result = SizeLogic.Apply(100.0, 0.0, 200.0, 0.0, 100.0, 0.0, true);

            Assert.IsTrue(result.KeepRatio);
        }

        [Test]
        public void Apply_LeavingADegenerateRatioReleasesTheLock()
        {
            // Going from height 0 (ratio Infinity) to a real size is itself a ratio change
            SizeApplyResult result = SizeLogic.Apply(100.0, 0.0, 200.0, 5.0, true);

            AssertResult(200.0, 5.0, false, result);
        }

        #endregion
    }
}
