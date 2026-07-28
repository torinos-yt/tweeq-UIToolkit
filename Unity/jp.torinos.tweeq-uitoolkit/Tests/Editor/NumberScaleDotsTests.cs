using System;
using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    /// <summary>
    /// NumberInput のスケールドット（本家忠実表示）の幾何。描画そのものではなく、
    /// 点の中心座標を決める純関数だけを見る。
    /// </summary>
    public class NumberScaleDotsTests
    {
        const double TOLERANCE = 1e-9;

        // NumberInput 側と同じ「薄すぎる帯は捨てる」閾値
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
            // 感度が 1/100 になると帯が 2 つぶん送られる
            Assert.That(NumberLogic.ScaleDotPrecision(0.01, 0), Is.EqualTo(2.0).Within(TOLERANCE));

            // 3 を超えた帯は 0 側へ巻き戻る（3 本が循環し続ける）
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

            // smoothstep(1,2,1.5) = 0.5 なので ^0.5
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

            // 位相そのものにも必ず点が乗る（＝点列が値に整列している）
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
            // speed=1 の帯 0/1 は precision 0/1 ＝ opacity 0。帯 2 だけが残る
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
            // 感度が 1 と 0.1 の中間まで来ると、細かい帯が薄く現れて粗い帯と重なる
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
            // 閾値 0 なら間隔 1px の帯も通るので、上限で頭打ちになることを見る
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
