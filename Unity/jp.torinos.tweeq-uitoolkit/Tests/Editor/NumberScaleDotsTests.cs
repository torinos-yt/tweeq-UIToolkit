using System;
using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    /// <summary>
    /// Geometry of NumberInput's scale dots (a faithful reproduction of the original's display). Rather than the
    /// rendering itself, this only exercises the pure functions that determine the dots' center coordinates.
    /// </summary>
    public class NumberScaleDotsTests
    {
        const double TOLERANCE = 1e-9;

        // Same "discard bands that are too faint" threshold as on the NumberInput side
        const double MIN_OPACITY = 0.01;

        #region ScaleDotPrecision

        [Test]
        public void PrecisionIsTheOffsetWhenSpeedIsOne()
        {
            Assert.That(NumberLogic.ScaleDotPrecision(1.0, 0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(NumberLogic.ScaleDotPrecision(1.0, 1), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(NumberLogic.ScaleDotPrecision(1.0, 2), Is.EqualTo(2.0).Within(TOLERANCE));
        }

        [Test]
        public void PrecisionShiftsByOneDigitPerDecadeOfSpeedAndWrapsAtThree()
        {
            // When sensitivity becomes 1/100, the band advances by 2
            Assert.That(NumberLogic.ScaleDotPrecision(0.01, 0), Is.EqualTo(2.0).Within(TOLERANCE));

            // A band past 3 wraps back around to 0 (the three keep cycling)
            Assert.That(NumberLogic.ScaleDotPrecision(0.01, 1), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(NumberLogic.ScaleDotPrecision(0.01, 2), Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void PrecisionIsNaNForUnusableSpeeds()
        {
            Assert.IsNaN(NumberLogic.ScaleDotPrecision(0.0, 0));
            Assert.IsNaN(NumberLogic.ScaleDotPrecision(-1.0, 0));
            Assert.IsNaN(NumberLogic.ScaleDotPrecision(double.NaN, 0));
        }

        #endregion

        #region ScaleDotOpacity

        [Test]
        public void OpacityFadesInBetweenOneAndTwoDigits()
        {
            Assert.That(NumberLogic.ScaleDotOpacity(0.0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(NumberLogic.ScaleDotOpacity(1.0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(NumberLogic.ScaleDotOpacity(2.0), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(NumberLogic.ScaleDotOpacity(3.0), Is.EqualTo(1.0).Within(TOLERANCE));

            // smoothstep(1,2,1.5) = 0.5, so ^0.5
            Assert.That(
                NumberLogic.ScaleDotOpacity(1.5), Is.EqualTo(Math.Sqrt(0.5)).Within(TOLERANCE));
        }

        #endregion

        #region ScaleDotPhase

        [Test]
        public void PhaseFollowsTheHandleWhenTheBarIsVisible()
        {
            double phase = NumberLogic.ScaleDotPhase(true, 25.0, 0.0, 100.0, 200.0, 0.5);

            Assert.That(phase, Is.EqualTo(50.0).Within(TOLERANCE));
        }

        [Test]
        public void PhaseIsClampedToTheBarWhenTheValueIsOutOfRange()
        {
            Assert.That(
                NumberLogic.ScaleDotPhase(true, 500.0, 0.0, 100.0, 200.0, 0.5),
                Is.EqualTo(200.0).Within(TOLERANCE));
            Assert.That(
                NumberLogic.ScaleDotPhase(true, -500.0, 0.0, 100.0, 200.0, 0.5),
                Is.EqualTo(0.0).Within(TOLERANCE));
        }

        [Test]
        public void PhasePutsZeroAtTheCentreWhenThereIsNoBar()
        {
            // width/2 - value/valuePerPixel
            Assert.That(
                NumberLogic.ScaleDotPhase(false, 0.0, 0.0, 0.0, 200.0, 0.5),
                Is.EqualTo(100.0).Within(TOLERANCE));
            Assert.That(
                NumberLogic.ScaleDotPhase(false, 30.0, 0.0, 0.0, 200.0, 0.5),
                Is.EqualTo(40.0).Within(TOLERANCE));
        }

        [Test]
        public void PhaseIsNaNWithoutAUsableSensitivity()
        {
            Assert.IsNaN(NumberLogic.ScaleDotPhase(false, 1.0, 0.0, 0.0, 200.0, 0.0));
            Assert.IsNaN(
                NumberLogic.ScaleDotPhase(false, 1.0, 0.0, 0.0, 200.0, double.PositiveInfinity));
        }

        #endregion

        #region TryBuildScaleDotLayer

        [Test]
        public void LayerGapIsTenToThePrecision()
        {
            NumberLogic.ScaleDotLayer layer;
            bool built = NumberLogic.TryBuildScaleDotLayer(
                0.01, 0, 0.0, 400.0, MIN_OPACITY, out layer);

            Assert.IsTrue(built);
            Assert.That(layer.Gap, Is.EqualTo(100.0).Within(TOLERANCE));
            Assert.That(layer.Opacity, Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void LayerIsAlignedToThePhase()
        {
            NumberLogic.ScaleDotLayer layer;
            bool built = NumberLogic.TryBuildScaleDotLayer(
                0.01, 0, 250.0, 400.0, MIN_OPACITY, out layer);

            Assert.IsTrue(built);
            Assert.That(layer.FirstX, Is.EqualTo(50.0).Within(TOLERANCE));
            Assert.That(layer.Count, Is.EqualTo(4));

            Assert.That(layer.DotX(0), Is.EqualTo(50.0).Within(TOLERANCE));
            Assert.That(layer.DotX(1), Is.EqualTo(150.0).Within(TOLERANCE));

            // A dot always lands exactly on the phase itself (i.e. the dot row is aligned to the value)
            Assert.That(layer.DotX(2), Is.EqualTo(250.0).Within(TOLERANCE));
            Assert.That(layer.DotX(3), Is.EqualTo(350.0).Within(TOLERANCE));
        }

        [Test]
        public void LayerWrapsANegativePhaseIntoTheField()
        {
            NumberLogic.ScaleDotLayer layer;
            bool built = NumberLogic.TryBuildScaleDotLayer(
                0.01, 0, -30.0, 400.0, MIN_OPACITY, out layer);

            Assert.IsTrue(built);
            Assert.That(layer.FirstX, Is.EqualTo(70.0).Within(TOLERANCE));
            Assert.That(layer.Count, Is.EqualTo(4));
            Assert.That(layer.DotX(3), Is.EqualTo(370.0).Within(TOLERANCE));
        }

        [Test]
        public void TooFaintLayersAreDropped()
        {
            // At speed=1, bands 0/1 have precision 0/1 = opacity 0. Only band 2 remains
            NumberLogic.ScaleDotLayer layer;

            Assert.IsFalse(
                NumberLogic.TryBuildScaleDotLayer(1.0, 0, 0.0, 400.0, MIN_OPACITY, out layer));
            Assert.IsFalse(
                NumberLogic.TryBuildScaleDotLayer(1.0, 1, 0.0, 400.0, MIN_OPACITY, out layer));
            Assert.IsTrue(
                NumberLogic.TryBuildScaleDotLayer(1.0, 2, 0.0, 400.0, MIN_OPACITY, out layer));
            Assert.That(layer.Gap, Is.EqualTo(100.0).Within(TOLERANCE));
        }

        [Test]
        public void TwoBandsCrossFadeWhileTheSensitivityMoves()
        {
            // Once sensitivity reaches the midpoint between 1 and 0.1, the finer band appears faintly and overlaps the coarser one
            double speed = Math.Pow(10.0, -0.5);

            NumberLogic.ScaleDotLayer fine;
            NumberLogic.ScaleDotLayer coarse;

            Assert.IsTrue(
                NumberLogic.TryBuildScaleDotLayer(speed, 1, 0.0, 400.0, MIN_OPACITY, out fine));
            Assert.IsTrue(
                NumberLogic.TryBuildScaleDotLayer(speed, 2, 0.0, 400.0, MIN_OPACITY, out coarse));

            Assert.That(fine.Gap, Is.EqualTo(Math.Pow(10.0, 1.5)).Within(1e-6));
            Assert.That(coarse.Gap, Is.EqualTo(Math.Pow(10.0, 2.5)).Within(1e-6));
            Assert.Less(fine.Opacity, coarse.Opacity);
        }

        [Test]
        public void UnusableInputsProduceNoLayer()
        {
            NumberLogic.ScaleDotLayer layer;

            Assert.IsFalse(
                NumberLogic.TryBuildScaleDotLayer(0.01, 0, 0.0, 0.0, MIN_OPACITY, out layer));
            Assert.IsFalse(
                NumberLogic.TryBuildScaleDotLayer(0.01, 0, double.NaN, 400.0, MIN_OPACITY, out layer));
            Assert.IsFalse(
                NumberLogic.TryBuildScaleDotLayer(0.0, 0, 0.0, 400.0, MIN_OPACITY, out layer));
        }

        [Test]
        public void LayerCountNeverExceedsTheSafetyCap()
        {
            // With a threshold of 0, even a 1px-spacing band passes, so this checks that it caps at the upper bound
            NumberLogic.ScaleDotLayer layer;
            bool built = NumberLogic.TryBuildScaleDotLayer(1.0, 0, 0.0, 100000.0, 0.0, out layer);

            Assert.IsTrue(built);
            Assert.That(layer.Gap, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(layer.Count, Is.EqualTo(NumberLogic.SCALE_DOT_MAX_PER_LAYER));
        }

        #endregion

        #region ShowScaleDots

        [Test]
        public void DotsAreHiddenOnlyWhenSteppedAndClampedOnBothSides()
        {
            Assert.IsFalse(NumberLogic.ShowScaleDots(1.0, true, true, 0.0, 100.0));
        }

        [Test]
        public void DotsStayVisibleWithoutAStep()
        {
            Assert.IsTrue(NumberLogic.ShowScaleDots(0.0, true, true, 0.0, 100.0));
        }

        [Test]
        public void DotsStayVisibleWhenOnlyOneSideIsClamped()
        {
            Assert.IsTrue(NumberLogic.ShowScaleDots(1.0, true, false, 0.0, 100.0));
            Assert.IsTrue(NumberLogic.ShowScaleDots(1.0, false, true, 0.0, 100.0));
        }

        [Test]
        public void DotsStayVisibleWithoutAFiniteRange()
        {
            Assert.IsTrue(
                NumberLogic.ShowScaleDots(
                    1.0, true, true, double.NegativeInfinity, double.PositiveInfinity));
            Assert.IsTrue(
                NumberLogic.ShowScaleDots(1.0, true, true, 0.0, double.PositiveInfinity));
        }

        #endregion
    }
}
