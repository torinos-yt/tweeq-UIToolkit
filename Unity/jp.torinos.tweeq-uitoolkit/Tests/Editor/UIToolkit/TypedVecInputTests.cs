using NUnit.Framework;
using UnityEngine;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Contract for Vec2Input / Vec3Input / Vec4Input. Uses Vec3Input as the representative to
    /// cover the same items as <see cref="VecInputTests"/> (copy semantics, notification counts,
    /// range resolution), while Vec2 / Vec4 only check the dimensional differences (axis count,
    /// default labels, value round-trips).
    /// </summary>
    /// <remarks>
    /// An axis's <c>ChangeEvent&lt;float&gt;</c> isn't delivered without a panel, so axis-driven
    /// notifications are substituted by directly driving <c>OnAxesChanged</c> / <c>OnConfirmed</c>,
    /// the base class's sole entry points, from a test-only subclass (the NumberInput → base
    /// wiring itself is covered on the Play Mode side).
    /// </remarks>
    public class TypedVecInputTests
    {
        #region Probe

        // Scaffolding that reproduces notifications from an axis's NumberInput without a panel
        class ProbeVec3Input : Vec3Input
        {
            public void SimulateAxisEdit(int index, float newValue)
            {
                NumberInput axis = this.GetAxis(index);
                Assert.IsNotNull(axis, "axis index out of range");

                float previous = axis.value;

                // Even on the real path, the value is held on the NumberInput side, so write it first and notify afterward
                axis.SetValueWithoutNotify(newValue);
                this.OnAxesChanged(index, previous);
            }

            public void SimulateGestureConfirm()
            {
                this.OnConfirmed();
            }
        }

        #endregion

        #region Vec3: Value

        [Test]
        public void Vec3_Dimensions_IsThree()
        {
            Assert.AreEqual(3, new Vec3Input().Dimensions);
        }

        [Test]
        public void Vec3_ValueRoundTrips()
        {
            Vec3Input vec = new Vec3Input();
            vec.SetValueWithoutNotify(new Vector3(1f, 2f, 3f));

            Assert.AreEqual(new Vector3(1f, 2f, 3f), vec.value);
        }

        [Test]
        public void Vec3_ValueReflectsAxisValues()
        {
            Vec3Input vec = new Vec3Input();
            vec.SetValueWithoutNotify(new Vector3(1f, 2f, 3f));

            Assert.AreEqual(1f, vec.GetAxis(0).value);
            Assert.AreEqual(2f, vec.GetAxis(1).value);
            Assert.AreEqual(3f, vec.GetAxis(2).value);
        }

        [Test]
        public void Vec3_GetAxis_OutOfRangeReturnsNull()
        {
            Vec3Input vec = new Vec3Input();

            Assert.IsNull(vec.GetAxis(-1));
            Assert.IsNull(vec.GetAxis(3));
        }

        #endregion

        #region Vec3: Notifications

        [Test]
        public void Vec3_SetValueWithoutNotify_DoesNotRaiseAnything()
        {
            Vec3Input vec = new Vec3Input();
            int changed = 0;
            int confirmed = 0;
            vec.ValueChanged += _ => changed++;
            vec.Confirmed += _ => confirmed++;

            vec.SetValueWithoutNotify(new Vector3(1f, 2f, 3f));

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Vec3_ValueSetter_RaisesValueChangedOnce()
        {
            Vec3Input vec = new Vec3Input();
            int changed = 0;
            Vector3 received = Vector3.zero;
            vec.ValueChanged += v =>
            {
                changed++;
                received = v;
            };

            vec.value = new Vector3(4f, 5f, 6f);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), received);
        }

        [Test]
        public void Vec3_ValueSetter_SameValueIsSilent()
        {
            Vec3Input vec = new Vec3Input();
            vec.SetValueWithoutNotify(new Vector3(1f, 2f, 3f));

            int changed = 0;
            vec.ValueChanged += _ => changed++;

            vec.value = new Vector3(1f, 2f, 3f);

            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Vec3_AxisEdit_RaisesValueChangedOncePerEdit()
        {
            ProbeVec3Input vec = new ProbeVec3Input();
            vec.SetValueWithoutNotify(new Vector3(1f, 2f, 3f));

            int changed = 0;
            Vector3 received = Vector3.zero;
            vec.ValueChanged += v =>
            {
                changed++;
                received = v;
            };

            vec.SimulateAxisEdit(1, 9f);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(new Vector3(1f, 9f, 3f), received);
        }

        [Test]
        public void Vec3_AxisEdit_DoesNotRaiseConfirmed()
        {
            ProbeVec3Input vec = new ProbeVec3Input();
            int confirmed = 0;
            vec.Confirmed += _ => confirmed++;

            vec.SimulateAxisEdit(0, 1f);
            vec.SimulateAxisEdit(0, 2f);

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Vec3_Gesture_RaisesConfirmedOnceWithFinalValue()
        {
            ProbeVec3Input vec = new ProbeVec3Input();
            vec.SetValueWithoutNotify(new Vector3(1f, 2f, 3f));

            int confirmed = 0;
            Vector3 received = Vector3.zero;
            vec.Confirmed += v =>
            {
                confirmed++;
                received = v;
            };

            // 1 gesture = moving 1 axis over a few frames and then releasing
            vec.SimulateAxisEdit(2, 4f);
            vec.SimulateAxisEdit(2, 5f);
            vec.SimulateGestureConfirm();

            Assert.AreEqual(1, confirmed);
            Assert.AreEqual(new Vector3(1f, 2f, 5f), received);
        }

        #endregion

        #region Vec3: Inherited properties

        [Test]
        public void Vec3_AxisLabels_DefaultToXyz()
        {
            Vec3Input vec = new Vec3Input();

            Assert.AreEqual("X", vec.GetAxis(0).LeftLabel);
            Assert.AreEqual("Y", vec.GetAxis(1).LeftLabel);
            Assert.AreEqual("Z", vec.GetAxis(2).LeftLabel);
        }

        [Test]
        public void Vec3_AxisLabels_CustomOverridesDefaults()
        {
            Vec3Input vec = new Vec3Input { AxisLabels = new[] { "R", "G", "B" } };

            Assert.AreEqual("R", vec.GetAxis(0).LeftLabel);
            Assert.AreEqual("G", vec.GetAxis(1).LeftLabel);
            Assert.AreEqual("B", vec.GetAxis(2).LeftLabel);
        }

        [Test]
        public void Vec3_Ranges_ScalarIsBroadcastAndArrayIsPerAxis()
        {
            Vec3Input vec = new Vec3Input
            {
                Min = new[] { -1.0 },
                Max = new[] { 1.0, 2.0, 3.0 },
                Step = new[] { 0.5 },
            };

            Assert.AreEqual(-1.0, vec.GetAxis(2).Min);
            Assert.AreEqual(2.0, vec.GetAxis(1).Max);
            Assert.AreEqual(0.5, vec.GetAxis(0).Step);
        }

        [Test]
        public void Vec3_Theme_PropagatesToAxes()
        {
            Vec3Input vec = new Vec3Input();
            TweeqTheme light = TweeqTheme.Light();

            vec.Theme = light;

            Assert.AreSame(light, vec.GetAxis(0).Theme);
            Assert.AreSame(light, vec.GetAxis(2).Theme);
        }

        #endregion

        #region Vec2 / Vec4: Dimensional differences

        [Test]
        public void Vec2_HasTwoAxesLabelledXy()
        {
            Vec2Input vec = new Vec2Input();

            Assert.AreEqual(2, vec.Dimensions);
            Assert.AreEqual("X", vec.GetAxis(0).LeftLabel);
            Assert.AreEqual("Y", vec.GetAxis(1).LeftLabel);
            Assert.IsNull(vec.GetAxis(2));
        }

        [Test]
        public void Vec2_ValueRoundTripsAndNotifies()
        {
            Vec2Input vec = new Vec2Input();
            int changed = 0;
            Vector2 received = Vector2.zero;
            vec.ValueChanged += v =>
            {
                changed++;
                received = v;
            };

            vec.value = new Vector2(7f, 8f);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(new Vector2(7f, 8f), received);
            Assert.AreEqual(new Vector2(7f, 8f), vec.value);
        }

        [Test]
        public void Vec4_HasFourAxesLabelledXyzw()
        {
            Vec4Input vec = new Vec4Input();

            Assert.AreEqual(4, vec.Dimensions);
            Assert.AreEqual("X", vec.GetAxis(0).LeftLabel);
            Assert.AreEqual("Y", vec.GetAxis(1).LeftLabel);
            Assert.AreEqual("Z", vec.GetAxis(2).LeftLabel);
            Assert.AreEqual("W", vec.GetAxis(3).LeftLabel);
        }

        [Test]
        public void Vec4_ValueRoundTripsAndNotifies()
        {
            Vec4Input vec = new Vec4Input();
            int changed = 0;
            Vector4 received = Vector4.zero;
            vec.ValueChanged += v =>
            {
                changed++;
                received = v;
            };

            vec.value = new Vector4(1f, 2f, 3f, 4f);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(new Vector4(1f, 2f, 3f, 4f), received);
            Assert.AreEqual(new Vector4(1f, 2f, 3f, 4f), vec.value);
        }

        [Test]
        public void Vec4_SetValueWithoutNotify_WritesEveryAxis()
        {
            Vec4Input vec = new Vec4Input();

            vec.SetValueWithoutNotify(new Vector4(1f, 2f, 3f, 4f));

            Assert.AreEqual(4f, vec.GetAxis(3).value);
        }

        #endregion

        #region disabled / invalid

        [Test]
        public void Vec2_DisabledPropagatesToBothAxes()
        {
            Vec2Input vec = new Vec2Input();

            vec.Disabled = true;

            Assert.IsTrue(vec.GetAxis(0).Disabled);
            Assert.IsTrue(vec.GetAxis(1).Disabled);
        }

        [Test]
        public void Vec2_InvalidPropagatesToBothAxes()
        {
            Vec2Input vec = new Vec2Input();

            vec.Invalid = true;

            Assert.IsTrue(vec.GetAxis(0).Invalid);
            Assert.IsTrue(vec.GetAxis(1).Invalid);
        }

        [Test]
        public void Vec3_DisabledDoesNotBlockTheProgrammaticValue()
        {
            // An assignment from outside isn't "manipulation," so it passes through (same treatment as an axis's NumberInput)
            Vec3Input vec = new Vec3Input { Disabled = true };

            vec.value = new Vector3(1f, 2f, 3f);

            Assert.AreEqual(new Vector3(1f, 2f, 3f), vec.value);
        }

        [Test]
        public void Vec3_DisabledIsIndependentFromInvalid()
        {
            Vec3Input vec = new Vec3Input { Invalid = true };

            Assert.IsFalse(vec.Disabled);
            Assert.IsFalse(vec.GetAxis(0).Disabled);
            Assert.IsTrue(vec.GetAxis(0).Invalid);
        }

        #endregion
    }
}
