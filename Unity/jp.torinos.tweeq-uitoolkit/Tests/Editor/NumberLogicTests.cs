using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class NumberLogicTests
    {
        const double TOLERANCE = 1e-12;

        #region PrecisionOfDisplay

        [Test]
        public void PrecisionOfDisplayCountsDigitsAfterDot()
        {
            Assert.That(NumberLogic.PrecisionOfDisplay("1.2345"), Is.EqualTo(4));
            Assert.That(NumberLogic.PrecisionOfDisplay("100"), Is.EqualTo(0));
            // Trailing zeroes are also counted as digits (because we want the digit count exactly as displayed)
            Assert.That(NumberLogic.PrecisionOfDisplay("1.200"), Is.EqualTo(3));
            Assert.That(NumberLogic.PrecisionOfDisplay("-0.5"), Is.EqualTo(1));
        }

        [Test]
        public void PrecisionOfDisplayReturnsZeroForNonNumericTails()
        {
            Assert.That(NumberLogic.PrecisionOfDisplay(""), Is.EqualTo(0));
            Assert.That(NumberLogic.PrecisionOfDisplay(null), Is.EqualTo(0));
            Assert.That(NumberLogic.PrecisionOfDisplay("1."), Is.EqualTo(0));
            Assert.That(NumberLogic.PrecisionOfDisplay("1.2e3"), Is.EqualTo(0));
        }

        #endregion

        #region GetDisplayPrecision

        [Test]
        public void StepPrecisionWinsOverDisplayAndSlider()
        {
            int precision = NumberLogic.GetDisplayPrecision(
                0.01, "1.2345", 0.0, 1.0, 100.0, true, false, 1.0, 4);

            Assert.That(precision, Is.EqualTo(2));
        }

        [Test]
        public void StepPrecisionWinsWhileTweakingToo()
        {
            int precision = NumberLogic.GetDisplayPrecision(
                0.01, "1.2345", 0.0, 1.0, 100.0, true, true, 0.0001, 4);

            Assert.That(precision, Is.EqualTo(2));
        }

        [Test]
        public void IdlePrecisionIsCappedByPrecisionLimit()
        {
            // display=4 digits, slider=precisionOf(1/100)=2 → max=4, but capped at limit=2
            Assert.That(
                NumberLogic.GetDisplayPrecision(0.0, "1.2345", 0.0, 1.0, 100.0, true, false, 1.0, 2),
                Is.EqualTo(2));
            Assert.That(
                NumberLogic.GetDisplayPrecision(0.0, "1.2345", 0.0, 1.0, 100.0, true, false, 1.0, 6),
                Is.EqualTo(4));
        }

        [Test]
        public void TweakingPrecisionTakesMaxOfDisplaySliderAndSpeed()
        {
            // display=1, slider=precisionOf(0.01)=2, speed=0.001 → precisionOf=3
            int precision = NumberLogic.GetDisplayPrecision(
                0.0, "1.2", 0.0, 1.0, 100.0, true, true, 0.001, 4);

            Assert.That(precision, Is.EqualTo(3));
            // While dragging, exceeding limit is fine (sensitivity feedback takes priority)
            Assert.That(
                NumberLogic.GetDisplayPrecision(0.0, "1.2", 0.0, 1.0, 100.0, true, true, 1e-6, 2),
                Is.EqualTo(6));
        }

        [Test]
        public void SliderPrecisionIsZeroWithoutBar()
        {
            Assert.That(
                NumberLogic.GetDisplayPrecision(0.0, "1", 0.0, 1.0, 100.0, false, false, 1.0, 4),
                Is.EqualTo(0));
            Assert.That(
                NumberLogic.GetDisplayPrecision(0.0, "1", 0.0, 1.0, 0.0, true, false, 1.0, 4),
                Is.EqualTo(0));
            Assert.That(
                NumberLogic.GetDisplayPrecision(
                    0.0, "1", double.NegativeInfinity, double.PositiveInfinity, 100.0, true, false, 1.0, 4),
                Is.EqualTo(0));
        }

        #endregion

        #region Format

        [Test]
        public void IdleFormatTrimsTrailingZeroesAndDot()
        {
            Assert.That(NumberLogic.Format(1.25, 4, false), Is.EqualTo("1.25"));
            Assert.That(NumberLogic.Format(100.0, 3, false), Is.EqualTo("100"));
            Assert.That(NumberLogic.Format(0.1 + 0.2, 4, false), Is.EqualTo("0.3"));
            Assert.That(NumberLogic.Format(2.0, 0, false), Is.EqualTo("2"));
        }

        [Test]
        public void TweakingFormatKeepsTrailingZeroes()
        {
            Assert.That(NumberLogic.Format(1.25, 4, true), Is.EqualTo("1.2500"));
            Assert.That(NumberLogic.Format(100.0, 3, true), Is.EqualTo("100.000"));
            Assert.That(NumberLogic.Format(2.0, 0, true), Is.EqualTo("2"));
        }

        [Test]
        public void IdleFormatNormalizesNegativeZero()
        {
            Assert.That(NumberLogic.Format(-0.0, 4, false), Is.EqualTo("0"));
            Assert.That(NumberLogic.Format(-0.0, 0, false), Is.EqualTo("0"));
            // A tiny negative value that rounds away should also not become "-0"
            Assert.That(NumberLogic.Format(-0.0000001, 4, false), Is.EqualTo("0"));
            Assert.That(NumberLogic.Format(-0.0000001, 4, false), Does.Not.StartWith("-"));
        }

        [Test]
        public void FormatUsesInvariantDecimalSeparator()
        {
            Assert.That(NumberLogic.Format(1234.5, 2, false), Is.EqualTo("1234.5"));
            Assert.That(NumberLogic.Format(1234.5, 2, true), Is.EqualTo("1234.50"));
        }

        [Test]
        public void FormatKeepsNonFiniteValuesReadable()
        {
            Assert.That(NumberLogic.Format(double.NaN, 4, false), Is.EqualTo("NaN"));
            Assert.That(NumberLogic.Format(double.PositiveInfinity, 4, false), Is.Not.Empty);
        }

        #endregion

        #region Speed

        [Test]
        public void BaseSpeedMapsRangeToWidthWhenBarVisible()
        {
            Assert.That(NumberLogic.BaseSpeed(true, 0.0, 100.0, 200.0, 0.0),
                Is.EqualTo(0.5).Within(TOLERANCE));
        }

        [Test]
        public void BaseSpeedUsesFixedPixelsPerStepWithoutBar()
        {
            Assert.That(NumberLogic.BaseSpeed(false, 0.0, 100.0, 200.0, 0.1),
                Is.EqualTo(0.1 / NumberLogic.PX_PER_STEP).Within(TOLERANCE));
            Assert.That(NumberLogic.BaseSpeed(false, 0.0, 100.0, 200.0, 0.0),
                Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void BaseSpeedFallsBackWhenRangeIsUnusable()
        {
            // With width=0 or an infinite range the bar formula diverges, so it falls back to the step side
            Assert.That(NumberLogic.BaseSpeed(true, 0.0, 100.0, 0.0, 0.2),
                Is.EqualTo(0.01).Within(TOLERANCE));
            Assert.That(
                NumberLogic.BaseSpeed(true, double.NegativeInfinity, double.PositiveInfinity, 200.0, 0.0),
                Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void MinSpeedFollowsPixelsPerStepWhenBarAndStepExist()
        {
            // stepCount=1000, pxPerStep=0.1 → precisionOf(0.1)=1
            Assert.That(NumberLogic.MinSpeed(true, 0.0, 1000.0, 100.0, 1.0, 4),
                Is.EqualTo(0.1).Within(TOLERANCE));
            // pxPerStep=2 → precisionOf=0
            Assert.That(NumberLogic.MinSpeed(true, 0.0, 100.0, 200.0, 1.0, 4),
                Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void MinSpeedFallsBackToPrecisionLimit()
        {
            Assert.That(NumberLogic.MinSpeed(false, 0.0, 100.0, 200.0, 1.0, 4),
                Is.EqualTo(1e-4).Within(TOLERANCE));
            Assert.That(NumberLogic.MinSpeed(true, 0.0, 100.0, 200.0, 0.0, 3),
                Is.EqualTo(1e-3).Within(TOLERANCE));
            Assert.That(NumberLogic.MinSpeed(true, 0.0, 100.0, 0.0, 1.0, 2),
                Is.EqualTo(1e-2).Within(TOLERANCE));
        }

        [Test]
        public void MinSpeedIsAlwaysFinite()
        {
            // If min==max then stepCount=0 and pxPerStep diverges. Must not return NaN
            double degenerate = NumberLogic.MinSpeed(true, 5.0, 5.0, 200.0, 1.0, 4);
            Assert.That(degenerate, Is.EqualTo(1e-4).Within(TOLERANCE));
            // A negative precisionLimit must not produce a huge lower bound like 10^3
            Assert.That(NumberLogic.MinSpeed(false, 0.0, 100.0, 200.0, 0.0, -3), Is.EqualTo(1.0));
        }

        [Test]
        public void MaxSpeedDependsOnBarVisibility()
        {
            Assert.That(NumberLogic.MaxSpeed(true), Is.EqualTo(1.0));
            Assert.That(NumberLogic.MaxSpeed(false), Is.EqualTo(1000.0));
        }

        #endregion

        #region ArrowIncrement

        [Test]
        public void SteppedArrowMovesByStep()
        {
            Assert.That(
                NumberLogic.ArrowIncrement(1.0, 1, 2.0, 10.0, false, false,
                    double.NegativeInfinity, double.PositiveInfinity),
                Is.EqualTo(3.0).Within(TOLERANCE));
            Assert.That(
                NumberLogic.ArrowIncrement(1.0, -1, 2.0, 10.0, false, false,
                    double.NegativeInfinity, double.PositiveInfinity),
                Is.EqualTo(-1.0).Within(TOLERANCE));
        }

        [Test]
        public void SteppedArrowIgnoresFineAndScalesWithFast()
        {
            double fine = NumberLogic.ArrowIncrement(0.0, 1, 0.25, 10.0, false, true,
                double.NegativeInfinity, double.PositiveInfinity);
            double fast = NumberLogic.ArrowIncrement(0.0, 1, 0.25, 10.0, true, false,
                double.NegativeInfinity, double.PositiveInfinity);
            double both = NumberLogic.ArrowIncrement(0.0, 1, 0.25, 10.0, true, true,
                double.NegativeInfinity, double.PositiveInfinity);

            Assert.That(fine, Is.EqualTo(0.25).Within(TOLERANCE));
            Assert.That(fast, Is.EqualTo(2.5).Within(TOLERANCE));
            // Alt+Shift gives a multiplier of 0.1*10=1 → max(1, 1), so it reverts to the plain step
            Assert.That(both, Is.EqualTo(0.25).Within(TOLERANCE));
        }

        [Test]
        public void UnsteppedArrowUsesModifierMultipliers()
        {
            double plain = NumberLogic.ArrowIncrement(0.0, 1, 0.0, 10.0, false, false,
                double.NegativeInfinity, double.PositiveInfinity);
            double fast = NumberLogic.ArrowIncrement(0.0, 1, 0.0, 10.0, true, false,
                double.NegativeInfinity, double.PositiveInfinity);
            double fine = NumberLogic.ArrowIncrement(0.0, 1, 0.0, 10.0, false, true,
                double.NegativeInfinity, double.PositiveInfinity);
            double both = NumberLogic.ArrowIncrement(0.0, 1, 0.0, 10.0, true, true,
                double.NegativeInfinity, double.PositiveInfinity);

            Assert.That(plain, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(fast, Is.EqualTo(10.0).Within(TOLERANCE));
            Assert.That(fine, Is.EqualTo(0.1).Within(TOLERANCE));
            Assert.That(both, Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void UnsteppedArrowGetsExtraTenthForNarrowRange()
        {
            Assert.That(
                NumberLogic.ArrowIncrement(0.5, 1, 0.0, 10.0, false, false, 0.0, 1.0),
                Is.EqualTo(0.6).Within(TOLERANCE));
            Assert.That(
                NumberLogic.ArrowIncrement(0.5, 1, 0.0, 10.0, false, true, 0.0, 1.0),
                Is.EqualTo(0.51).Within(TOLERANCE));
            // If the range is wider than 1, the normal step of 1 applies
            Assert.That(
                NumberLogic.ArrowIncrement(0.5, 1, 0.0, 10.0, false, false, 0.0, 2.0),
                Is.EqualTo(1.5).Within(TOLERANCE));
        }

        [Test]
        public void ArrowResultIsClampedToValidRange()
        {
            Assert.That(
                NumberLogic.ArrowIncrement(0.95, 1, 0.0, 10.0, false, false, 0.0, 1.0),
                Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(
                NumberLogic.ArrowIncrement(1.0, -1, 2.0, 10.0, false, false, 0.0, 10.0),
                Is.EqualTo(0.0).Within(TOLERANCE));
        }

        [Test]
        public void ArrowKeepsNonFiniteInputUntouched()
        {
            Assert.That(
                NumberLogic.ArrowIncrement(double.NaN, 1, 1.0, 10.0, false, false, 0.0, 1.0),
                Is.NaN);
        }

        #endregion

        #region Scrub integration

        [Test]
        public void UnrangedScrubOfTwentyPixelsAdvancesOneStep()
        {
            var gesture = new TweakGesture();
            double baseSpeed = NumberLogic.BaseSpeed(false, -100.0, 100.0, 100.0, 0.1);
            double minSpeed = NumberLogic.MinSpeed(false, -100.0, 100.0, 100.0, 0.1, 4);
            double maxSpeed = NumberLogic.MaxSpeed(false);

            GestureUpdate update = gesture.Update(
                NumberLogic.PX_PER_STEP, 0.0, baseSpeed,
                new GestureModifiers(false, false, false), 10.0, minSpeed, maxSpeed);

            Assert.That(1.0 + update.AccumulatedDelta, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(update.Speed, Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void StationaryScrubSamplesStayFinite()
        {
            var gesture = new TweakGesture();
            double baseSpeed = NumberLogic.BaseSpeed(false, -100.0, 100.0, 100.0, 0.0);

            // Normalizing the direction vector with a zero-length delta can produce 0/0. Check that the guard holds
            for (int i = 0; i < 100; i++)
            {
                GestureUpdate update = gesture.Update(
                    0.0, 0.0, baseSpeed,
                    new GestureModifiers(false, false, false), 10.0, 1e-4, 1000.0);

                Assert.That(update.Delta, Is.EqualTo(0.0).Within(TOLERANCE));
                Assert.That(double.IsNaN(update.AccumulatedDelta), Is.False);
                Assert.That(double.IsNaN(update.Speed), Is.False);
                Assert.That(double.IsNaN(gesture.HorizontalWeight), Is.False);
            }

            Assert.That(gesture.Speed, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(gesture.HorizontalWeight, Is.EqualTo(1.0).Within(TOLERANCE));
        }

        #endregion
    }
}
