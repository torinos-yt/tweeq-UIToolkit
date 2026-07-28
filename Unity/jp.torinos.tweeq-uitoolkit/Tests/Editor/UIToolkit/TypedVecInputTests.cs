using NUnit.Framework;
using UnityEngine;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Vec2Input / Vec3Input / Vec4Input の契約。Vec3Input を代表として
    /// <see cref="VecInputTests"/> と同等の項目（コピー意味論・通知回数・レンジ解決）を見て、
    /// Vec2 / Vec4 は次元差分（軸数・既定ラベル・値の往復）だけ確かめる。
    /// </summary>
    /// <remarks>
    /// 軸の <c>ChangeEvent&lt;float&gt;</c> は panel が無いと配送されないので、軸由来の通知は
    /// 基底の唯一の入口である <c>OnAxesChanged</c> / <c>OnConfirmed</c> をテスト用の派生から
    /// 直接叩いて代用する（NumberInput → 基底の配線そのものは Play Mode 側の担当）。
    /// </remarks>
    public class TypedVecInputTests
    {
        #region Probe

        // 軸 NumberInput からの通知を panel 無しで再現する足場
        class ProbeVec3Input : Vec3Input
        {
            public void SimulateAxisEdit(int index, float newValue)
            {
                NumberInput axis = this.GetAxis(index);
                Assert.IsNotNull(axis, "軸番号が範囲外");

                float previous = axis.value;

                // 実際の経路でも値を持っているのは NumberInput 側なので、先に書いてから通知する
                axis.SetValueWithoutNotify(newValue);
                this.OnAxesChanged(index, previous);
            }

            public void SimulateGestureConfirm()
            {
                this.OnConfirmed();
            }
        }

        #endregion

        #region Vec3: 値

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

        #region Vec3: 通知

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

            // 1 ジェスチャ = 1 軸を何フレームか動かして離す
            vec.SimulateAxisEdit(2, 4f);
            vec.SimulateAxisEdit(2, 5f);
            vec.SimulateGestureConfirm();

            Assert.AreEqual(1, confirmed);
            Assert.AreEqual(new Vector3(1f, 2f, 5f), received);
        }

        #endregion

        #region Vec3: 継承したプロパティ

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

        #region Vec2 / Vec4: 次元差分

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
            // 外部からの代入は「操作」ではないので通す（軸の NumberInput と同じ扱い）
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
