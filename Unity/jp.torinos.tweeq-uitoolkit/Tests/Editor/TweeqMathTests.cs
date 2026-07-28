using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class TweeqMathTests
    {
        const double TOLERANCE = 1e-12;

        #region Lerp / Smoothstep

        [Test]
        public void LerpInterpolatesEndpoints()
        {
            Assert.That(TweeqMath.Lerp(1.0, 3.0, 0.0), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(TweeqMath.Lerp(1.0, 3.0, 1.0), Is.EqualTo(3.0).Within(TOLERANCE));
            Assert.That(TweeqMath.Lerp(1.0, 3.0, 0.5), Is.EqualTo(2.0).Within(TOLERANCE));
        }

        [Test]
        public void SmoothstepIsClampedAndSymmetric()
        {
            Assert.That(TweeqMath.Smoothstep(0.4, 0.6, 0.0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(TweeqMath.Smoothstep(0.4, 0.6, 1.0), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(TweeqMath.Smoothstep(0.4, 0.6, 0.5), Is.EqualTo(0.5).Within(TOLERANCE));
            Assert.That(TweeqMath.Smoothstep(0.4, 0.6, 0.45), Is.LessThan(0.5));
            Assert.That(TweeqMath.Smoothstep(0.4, 0.6, 0.55), Is.GreaterThan(0.5));
        }

        #endregion

        #region UnsignedMod / SignedAngleBetween

        [Test]
        public void UnsignedModAlwaysReturnsNonNegativeForPositiveModulo()
        {
            Assert.That(TweeqMath.UnsignedMod(370.0, 360.0), Is.EqualTo(10.0).Within(TOLERANCE));
            Assert.That(TweeqMath.UnsignedMod(-10.0, 360.0), Is.EqualTo(350.0).Within(TOLERANCE));
            Assert.That(TweeqMath.UnsignedMod(-370.0, 360.0), Is.EqualTo(350.0).Within(TOLERANCE));
            Assert.That(TweeqMath.UnsignedMod(0.0, 360.0), Is.EqualTo(0.0).Within(TOLERANCE));
        }

        [Test]
        public void SignedAngleBetweenTakesShortestPath()
        {
            Assert.That(TweeqMath.SignedAngleBetween(10.0, 350.0), Is.EqualTo(20.0).Within(TOLERANCE));
            Assert.That(TweeqMath.SignedAngleBetween(350.0, 10.0), Is.EqualTo(-20.0).Within(TOLERANCE));
            Assert.That(TweeqMath.SignedAngleBetween(5.0, 0.0), Is.EqualTo(5.0).Within(TOLERANCE));
            Assert.That(TweeqMath.SignedAngleBetween(0.0, 5.0), Is.EqualTo(-5.0).Within(TOLERANCE));
        }

        [Test]
        public void SignedAngleBetweenIsConsistentAtHalfTurn()
        {
            double forward = TweeqMath.SignedAngleBetween(180.0, 0.0);
            double backward = TweeqMath.SignedAngleBetween(0.0, 180.0);
            Assert.That(System.Math.Abs(forward), Is.EqualTo(180.0).Within(TOLERANCE));
            Assert.That(System.Math.Abs(backward), Is.EqualTo(180.0).Within(TOLERANCE));
            Assert.That(forward, Is.EqualTo(backward).Within(TOLERANCE));
        }

        [Test]
        public void SignedAngleBetweenHandlesMultipleTurns()
        {
            Assert.That(TweeqMath.SignedAngleBetween(730.0, 10.0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(TweeqMath.SignedAngleBetween(-350.0, 0.0), Is.EqualTo(10.0).Within(TOLERANCE));
            Assert.That(TweeqMath.SignedAngleBetween(0.0, -350.0), Is.EqualTo(-10.0).Within(TOLERANCE));
        }

        #endregion

        #region Quantize

        [Test]
        public void QuantizeIsRelativeToOrigin()
        {
            // A case that would become 0.3 if it were not relative to origin
            Assert.That(TweeqMath.Quantize(0.26, 0.1, 0.05), Is.EqualTo(0.25).Within(TOLERANCE));
            Assert.That(TweeqMath.Quantize(0.26, 0.1), Is.EqualTo(0.3).Within(TOLERANCE));
        }

        [Test]
        public void QuantizeSnapsToNearestStep()
        {
            Assert.That(TweeqMath.Quantize(7.0, 5.0), Is.EqualTo(5.0).Within(TOLERANCE));
            Assert.That(TweeqMath.Quantize(8.0, 5.0), Is.EqualTo(10.0).Within(TOLERANCE));
            Assert.That(TweeqMath.Quantize(-7.0, 5.0), Is.EqualTo(-5.0).Within(TOLERANCE));
        }

        [Test]
        public void QuantizePassesThroughInvalidArguments()
        {
            Assert.That(TweeqMath.Quantize(0.26, 0.0), Is.EqualTo(0.26).Within(TOLERANCE));
            Assert.That(TweeqMath.Quantize(0.26, -1.0), Is.EqualTo(0.26).Within(TOLERANCE));
            Assert.That(TweeqMath.Quantize(0.26, double.NaN), Is.EqualTo(0.26).Within(TOLERANCE));
            Assert.That(TweeqMath.Quantize(0.26, double.PositiveInfinity), Is.EqualTo(0.26).Within(TOLERANCE));
            Assert.That(TweeqMath.Quantize(0.26, 0.1, double.NaN), Is.EqualTo(0.26).Within(TOLERANCE));
            Assert.That(double.IsNaN(TweeqMath.Quantize(double.NaN, 0.1)), Is.True);
            Assert.That(double.IsInfinity(TweeqMath.Quantize(double.PositiveInfinity, 0.1)), Is.True);
        }

        [Test]
        public void QuantizeNormalizesNegativeZero()
        {
            // Created at runtime to avoid platform differences where the -0.0 literal becomes +0 via constant folding
            double negativeZero = 0.0 * -1.0;
            Assume.That(double.IsNegative(negativeZero), Is.True);

            double result = TweeqMath.Quantize(-0.04, 0.1, negativeZero);
            Assert.That(result, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(double.IsNegative(result), Is.False);
        }

        #endregion

        #region PrecisionOf

        [Test]
        public void PrecisionOfCountsDecimalDigits()
        {
            Assert.That(TweeqMath.PrecisionOf(0.1), Is.EqualTo(1));
            Assert.That(TweeqMath.PrecisionOf(0.01), Is.EqualTo(2));
            Assert.That(TweeqMath.PrecisionOf(0.001), Is.EqualTo(3));
            Assert.That(TweeqMath.PrecisionOf(0.5), Is.EqualTo(1));
        }

        [Test]
        public void PrecisionOfIsZeroForIntegerSteps()
        {
            Assert.That(TweeqMath.PrecisionOf(1.0), Is.EqualTo(0));
            Assert.That(TweeqMath.PrecisionOf(20.0), Is.EqualTo(0));
            Assert.That(TweeqMath.PrecisionOf(0.0), Is.EqualTo(0));
            Assert.That(TweeqMath.PrecisionOf(double.NaN), Is.EqualTo(0));
            Assert.That(TweeqMath.PrecisionOf(double.PositiveInfinity), Is.EqualTo(0));
        }

        #endregion
    }
}
