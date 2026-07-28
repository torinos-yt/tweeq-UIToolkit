using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class NumberValidatorTests
    {
        const double TOLERANCE = 1e-9;

        #region Clamp

        [Test]
        public void ClampsToValidRangeAndReportsIt()
        {
            NumberValidation high = NumberValidator.Validate(2.0, 0.0, 1.0, 0.0, 10.0, false);
            NumberValidation low = NumberValidator.Validate(-5.0, 0.0, 1.0, 0.0, 10.0, false);

            Assert.That(high.Value, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(high.Clamped, Is.True);
            Assert.That(low.Value, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(low.Clamped, Is.True);
        }

        [Test]
        public void InRangeValuesAreNotFlaggedAsClamped()
        {
            NumberValidation result = NumberValidator.Validate(0.5, 0.0, 1.0, 0.0, 10.0, false);

            Assert.That(result.Value, Is.EqualTo(0.5).Within(TOLERANCE));
            Assert.That(result.Clamped, Is.False);
            Assert.That(result.Quantized, Is.False);
        }

        [Test]
        public void InfiniteBoundsDisableClamping()
        {
            NumberValidation result = NumberValidator.Validate(
                1e9, double.NegativeInfinity, double.PositiveInfinity, 0.0, 10.0, false);

            Assert.That(result.Value, Is.EqualTo(1e9).Within(TOLERANCE));
            Assert.That(result.Clamped, Is.False);
        }

        #endregion

        #region Quantize

        [Test]
        public void QuantizesToStepFromOriginZero()
        {
            NumberValidation result = NumberValidator.Validate(0.26, 0.0, 1.0, 0.1, 10.0, false);

            Assert.That(result.Value, Is.EqualTo(0.3).Within(TOLERANCE));
            Assert.That(result.Quantized, Is.True);
            Assert.That(result.Clamped, Is.False);
        }

        [Test]
        public void ExactMultiplesAreNotFlaggedAsQuantized()
        {
            // 0.3/0.1 doesn't divide evenly in binary and leaves a residue, but that isn't "quantized"
            NumberValidation result = NumberValidator.Validate(0.3, 0.0, 1.0, 0.1, 10.0, false);

            Assert.That(result.Value, Is.EqualTo(0.3).Within(TOLERANCE));
            Assert.That(result.Quantized, Is.False);
        }

        [Test]
        public void ZeroStepLeavesValueUntouched()
        {
            NumberValidation result = NumberValidator.Validate(0.26, 0.0, 1.0, 0.0, 10.0, false);

            Assert.That(result.Value, Is.EqualTo(0.26).Within(TOLERANCE));
            Assert.That(result.Quantized, Is.False);
        }

        [Test]
        public void QuantizationRoundsHalvesAwayFromZero()
        {
            Assert.That(
                NumberValidator.Validate(0.25, 0.0, 1.0, 0.1, 10.0, false).Value,
                Is.EqualTo(0.3).Within(TOLERANCE));
            Assert.That(
                NumberValidator.Validate(-0.25, -1.0, 1.0, 0.1, 10.0, false).Value,
                Is.EqualTo(-0.3).Within(TOLERANCE));
        }

        #endregion

        #region Order

        [Test]
        public void ClampHappensBeforeQuantization()
        {
            // Since the upper bound 1.0 isn't a multiple of 0.3, it gets quantized after clamping and falls back to 0.9
            NumberValidation result = NumberValidator.Validate(2.0, 0.0, 1.0, 0.3, 10.0, false);

            Assert.That(result.Value, Is.EqualTo(0.9).Within(TOLERANCE));
            Assert.That(result.Clamped, Is.True);
            Assert.That(result.Quantized, Is.True);
        }

        [Test]
        public void SnapIsAppliedAfterStep()
        {
            // In step->snap order: 7 -> 6 -> 10. In the reverse order it would be 7 -> 10 -> 9
            NumberValidation result = NumberValidator.Validate(7.0, -100.0, 100.0, 3.0, 10.0, true);

            Assert.That(result.Value, Is.EqualTo(10.0).Within(TOLERANCE));
            Assert.That(result.Quantized, Is.True);
        }

        [Test]
        public void SnapOnlyAppliesWhenEnabled()
        {
            NumberValidation off = NumberValidator.Validate(7.0, -100.0, 100.0, 0.0, 10.0, false);
            NumberValidation on = NumberValidator.Validate(7.0, -100.0, 100.0, 0.0, 10.0, true);

            Assert.That(off.Value, Is.EqualTo(7.0).Within(TOLERANCE));
            Assert.That(off.Quantized, Is.False);
            Assert.That(on.Value, Is.EqualTo(10.0).Within(TOLERANCE));
            Assert.That(on.Quantized, Is.True);
        }

        #endregion

        #region Edge cases

        [Test]
        public void NonFiniteValuesArePreservedForHostPolicy()
        {
            NumberValidation nan = NumberValidator.Validate(double.NaN, 0.0, 1.0, 0.1, 10.0, true);
            NumberValidation infinite = NumberValidator.Validate(
                double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity,
                0.1, 10.0, true);

            Assert.That(nan.Value, Is.NaN);
            Assert.That(nan.Clamped, Is.False);
            Assert.That(nan.Quantized, Is.False);
            Assert.That(double.IsPositiveInfinity(infinite.Value), Is.True);
        }

        [Test]
        public void NegativeZeroIsNormalized()
        {
            NumberValidation result = NumberValidator.Validate(-0.04, -1.0, 1.0, 0.1, 10.0, false);

            Assert.That(result.Value, Is.EqualTo(0.0));
            Assert.That(double.IsNegative(result.Value), Is.False);
        }

        #endregion
    }
}
