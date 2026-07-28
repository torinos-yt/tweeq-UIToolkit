using System;
using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class TweakGestureTests
    {
        const double TOLERANCE = 1e-12;
        const double MIN_SPEED = 0.01;
        const double MAX_SPEED = 100.0;

        static GestureUpdate Drag(TweakGesture gesture, double dx, double dy,
            double baseSpeed = 1.0, bool fine = false, bool fast = false, bool snap = false,
            double fastMultiplier = 10.0, double minSpeed = MIN_SPEED, double maxSpeed = MAX_SPEED)
        {
            return gesture.Update(dx, dy, baseSpeed,
                new GestureModifiers(fine, fast, snap), fastMultiplier, minSpeed, maxSpeed);
        }

        #region Horizontal

        [Test]
        public void HorizontalMotionAccumulatesFromCapture()
        {
            var gesture = new TweakGesture();
            GestureUpdate first = Drag(gesture, 2.0, 0.0, 0.5);
            GestureUpdate second = Drag(gesture, 3.0, 0.0, 0.5);

            Assert.That(first.Delta, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(first.AccumulatedDelta, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(second.Delta, Is.EqualTo(1.5).Within(TOLERANCE));
            Assert.That(second.AccumulatedDelta, Is.EqualTo(2.5).Within(TOLERANCE));
            Assert.That(gesture.Speed, Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void AccumulatedDeltaEqualsSumOfDeltas()
        {
            var gesture = new TweakGesture();
            double[] horizontal = { 3.0, -1.5, 7.0, 0.5, -4.0 };
            double[] vertical = { 0.0, 2.0, -1.0, 5.0, 0.0 };

            double sum = 0.0;
            GestureUpdate update = default(GestureUpdate);
            for (int i = 0; i < horizontal.Length; i++)
            {
                update = Drag(gesture, horizontal[i], vertical[i], 0.25);
                sum += update.Delta;
            }

            Assert.That(update.AccumulatedDelta, Is.EqualTo(sum).Within(TOLERANCE));
            Assert.That(gesture.AccumulatedDelta, Is.EqualTo(sum).Within(TOLERANCE));
        }

        [Test]
        public void AccumulatedDeltaIsNotResetPerFrame()
        {
            var gesture = new TweakGesture();
            Drag(gesture, 10.0, 0.0);
            GestureUpdate idle = Drag(gesture, 0.0, 0.0);

            Assert.That(idle.Delta, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(idle.AccumulatedDelta, Is.EqualTo(10.0).Within(TOLERANCE));
        }

        #endregion

        #region Modifiers

        [Test]
        public void FineModifierScalesDeltaByOneTenth()
        {
            var plainGesture = new TweakGesture();
            var fineGesture = new TweakGesture();

            GestureUpdate plainUpdate = Drag(plainGesture, 10.0, 0.0);
            GestureUpdate fineUpdate = Drag(fineGesture, 10.0, 0.0, fine: true);

            Assert.That(plainUpdate.Delta, Is.EqualTo(10.0).Within(TOLERANCE));
            Assert.That(fineUpdate.Delta, Is.EqualTo(plainUpdate.Delta * 0.1).Within(TOLERANCE));
        }

        [Test]
        public void FastModifierAppliesMultiplierWithLowerBoundOfOne()
        {
            var fastGesture = new TweakGesture();
            var weakGesture = new TweakGesture();

            Assert.That(Drag(fastGesture, 10.0, 0.0, fast: true).Delta, Is.EqualTo(100.0).Within(TOLERANCE));
            // Don't let fastMultiplier < 1 be used to slow things down
            Assert.That(Drag(weakGesture, 10.0, 0.0, fast: true, fastMultiplier: 0.25).Delta,
                Is.EqualTo(10.0).Within(TOLERANCE));
        }

        [Test]
        public void SnapModifierIsPassedThrough()
        {
            var gesture = new TweakGesture();
            Assert.That(Drag(gesture, 1.0, 0.0, snap: true).Snap, Is.True);
            Assert.That(Drag(gesture, 1.0, 0.0).Snap, Is.False);
        }

        #endregion

        #region Vertical sensitivity

        [Test]
        public void VerticalMotionChangesSpeedByDecayFactor()
        {
            var gesture = new TweakGesture();
            GestureUpdate update = Drag(gesture, 0.0, 20.0);

            Assert.That(update.Delta, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(update.Speed, Is.LessThan(1.0));
            Assert.That(gesture.HorizontalWeight, Is.LessThan(0.1));
            // Since the vertical component dominates, speed drops to nearly 0.98^dy
            Assert.That(update.Speed, Is.EqualTo(Math.Pow(0.98, 20.0)).Within(0.01));
        }

        [Test]
        public void UpwardMotionIncreasesSpeed()
        {
            var gesture = new TweakGesture();
            Assert.That(Drag(gesture, 0.0, -20.0).Speed, Is.GreaterThan(1.0));
        }

        [Test]
        public void HorizontalOnlyFollowUpDoesNotChangeSpeed()
        {
            var gesture = new TweakGesture();
            Drag(gesture, 0.0, 20.0);
            double afterVertical = gesture.Speed;

            for (int i = 0; i < 5; i++)
            {
                Drag(gesture, 10.0, 0.0);
                Assert.That(gesture.Speed, Is.EqualTo(afterVertical).Within(TOLERANCE));
            }

            // If horizontal movement continues, the weight returns to 1 and only value changes occur
            Assert.That(gesture.HorizontalWeight, Is.GreaterThan(0.9));
        }

        [Test]
        public void DiagonalMotionBlendsValueAndSensitivity()
        {
            var gesture = new TweakGesture();
            GestureUpdate update = Drag(gesture, 10.0, 10.0);

            Assert.That(update.Delta, Is.GreaterThan(0.0));
            Assert.That(update.Delta, Is.LessThanOrEqualTo(10.0));
            Assert.That(update.Speed, Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void SpeedIsClampedToRange()
        {
            var slow = new TweakGesture();
            Drag(slow, 0.0, 1000.0, minSpeed: 0.5, maxSpeed: 2.0);
            Assert.That(slow.Speed, Is.EqualTo(0.5).Within(TOLERANCE));

            var quick = new TweakGesture();
            Drag(quick, 0.0, -1000.0, minSpeed: 0.5, maxSpeed: 2.0);
            Assert.That(quick.Speed, Is.EqualTo(2.0).Within(TOLERANCE));
        }

        [Test]
        public void SpeedScalesDelta()
        {
            var gesture = new TweakGesture();
            Drag(gesture, 0.0, 20.0);
            double speed = gesture.Speed;

            // After a vertical drag the weight is small, so verify the result matches the expected value including that weight
            GestureUpdate update = Drag(gesture, 4.0, 0.0, 0.5);
            Assert.That(update.Delta,
                Is.EqualTo(4.0 * 0.5 * speed * gesture.HorizontalWeight).Within(TOLERANCE));
        }

        #endregion

        #region Reset

        [Test]
        public void ResetRestoresInitialState()
        {
            var gesture = new TweakGesture();
            Drag(gesture, 10.0, 30.0);
            Drag(gesture, -5.0, 12.0);

            gesture.Reset();

            Assert.That(gesture.Speed, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(gesture.AccumulatedDelta, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(gesture.HorizontalWeight, Is.EqualTo(1.0).Within(TOLERANCE));

            // If direction has returned to (1,0), a purely horizontal drag has weight 1 and becomes dx*baseSpeed
            GestureUpdate update = Drag(gesture, 2.0, 0.0, 0.5);
            Assert.That(update.Delta, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(update.AccumulatedDelta, Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void FreshGestureMatchesResetState()
        {
            var gesture = new TweakGesture();
            Assert.That(gesture.Speed, Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(gesture.AccumulatedDelta, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(gesture.HorizontalWeight, Is.EqualTo(1.0).Within(TOLERANCE));
        }

        #endregion
    }
}
