using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// VecInput のうち panel 非依存の部分（値のコピー意味論・軸ごとの min/max/step 解決・
    /// 長さガード）を検証する。ドラッグ由来の ValueChanged / Confirmed は panel が要るので対象外。
    /// </summary>
    public class VecInputTests
    {
        [Test]
        public void Dimensions_AreClampedToTwoThroughFour()
        {
            Assert.AreEqual(2, new VecInput(0).Dimensions);
            Assert.AreEqual(2, new VecInput(1).Dimensions);
            Assert.AreEqual(3, new VecInput(3).Dimensions);
            Assert.AreEqual(4, new VecInput(9).Dimensions);
        }

        [Test]
        public void Value_GetReturnsCopy()
        {
            VecInput vec = new VecInput(3);
            vec.SetValueWithoutNotify(new[] { 1f, 2f, 3f });

            float[] snapshot = vec.Value;
            snapshot[0] = 99f;

            Assert.AreEqual(1f, vec.Value[0]);
        }

        [Test]
        public void Value_SetCopiesInput()
        {
            VecInput vec = new VecInput(2);
            float[] source = { 4f, 5f };
            vec.SetValueWithoutNotify(source);

            source[1] = 99f;

            Assert.AreEqual(5f, vec.Value[1]);
        }

        [Test]
        public void SetValueWithoutNotify_WrongLengthIsIgnored()
        {
            VecInput vec = new VecInput(3);
            vec.SetValueWithoutNotify(new[] { 1f, 2f, 3f });

            vec.SetValueWithoutNotify(new[] { 7f, 8f });

            Assert.AreEqual(new[] { 1f, 2f, 3f }, vec.Value);
        }

        [Test]
        public void SetValueWithoutNotify_NullIsIgnored()
        {
            VecInput vec = new VecInput(2);
            vec.SetValueWithoutNotify(new[] { 1f, 2f });

            vec.SetValueWithoutNotify(null);

            Assert.AreEqual(new[] { 1f, 2f }, vec.Value);
        }

        [Test]
        public void SetValueWithoutNotify_DoesNotRaiseValueChanged()
        {
            VecInput vec = new VecInput(2);
            int calls = 0;
            vec.ValueChanged += _ => calls++;

            vec.SetValueWithoutNotify(new[] { 1f, 2f });

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Min_ScalarIsBroadcastToEveryAxis()
        {
            VecInput vec = new VecInput(3) { Min = new[] { -1.0 } };

            for (int i = 0; i < vec.Dimensions; i++)
            {
                Assert.AreEqual(-1.0, vec.GetAxis(i).Min);
            }
        }

        [Test]
        public void Min_PerAxisArrayIsAppliedInOrder()
        {
            VecInput vec = new VecInput(3) { Min = new[] { -1.0, -2.0, -3.0 } };

            Assert.AreEqual(-1.0, vec.GetAxis(0).Min);
            Assert.AreEqual(-2.0, vec.GetAxis(1).Min);
            Assert.AreEqual(-3.0, vec.GetAxis(2).Min);
        }

        [Test]
        public void Ranges_NullMeansUnbounded()
        {
            VecInput vec = new VecInput(2)
            {
                Min = new[] { -1.0 },
                Max = new[] { 1.0 },
                Step = new[] { 0.5 },
            };

            vec.Min = null;
            vec.Max = null;
            vec.Step = null;

            Assert.AreEqual(double.NegativeInfinity, vec.GetAxis(0).Min);
            Assert.AreEqual(double.PositiveInfinity, vec.GetAxis(0).Max);
            Assert.AreEqual(0.0, vec.GetAxis(0).Step);
        }

        [Test]
        public void Ranges_ShortArrayFallsBackForMissingAxes()
        {
            VecInput vec = new VecInput(4) { Max = new[] { 1.0, 2.0 } };

            Assert.AreEqual(1.0, vec.GetAxis(0).Max);
            Assert.AreEqual(2.0, vec.GetAxis(1).Max);
            Assert.AreEqual(double.PositiveInfinity, vec.GetAxis(2).Max);
            Assert.AreEqual(double.PositiveInfinity, vec.GetAxis(3).Max);
        }

        [Test]
        public void Min_GetReturnsCopy()
        {
            VecInput vec = new VecInput(2) { Min = new[] { -1.0, -2.0 } };

            double[] snapshot = vec.Min;
            snapshot[0] = 99.0;

            Assert.AreEqual(-1.0, vec.Min[0]);
        }

        [Test]
        public void AxisLabels_DefaultToXyzw()
        {
            VecInput vec = new VecInput(4);

            Assert.AreEqual("X", vec.GetAxis(0).LeftLabel);
            Assert.AreEqual("Y", vec.GetAxis(1).LeftLabel);
            Assert.AreEqual("Z", vec.GetAxis(2).LeftLabel);
            Assert.AreEqual("W", vec.GetAxis(3).LeftLabel);
        }

        [Test]
        public void AxisLabels_CustomOverridesDefaults()
        {
            VecInput vec = new VecInput(3) { AxisLabels = new[] { "R", "G", "B" } };

            Assert.AreEqual("R", vec.GetAxis(0).LeftLabel);
            Assert.AreEqual("G", vec.GetAxis(1).LeftLabel);
            Assert.AreEqual("B", vec.GetAxis(2).LeftLabel);
        }

        [Test]
        public void AxisLabels_NullRestoresDefaults()
        {
            VecInput vec = new VecInput(2) { AxisLabels = new[] { "U", "V" } };
            vec.AxisLabels = null;

            Assert.AreEqual("X", vec.GetAxis(0).LeftLabel);
            Assert.AreEqual("Y", vec.GetAxis(1).LeftLabel);
        }

        [Test]
        public void GetAxis_OutOfRangeReturnsNull()
        {
            VecInput vec = new VecInput(2);

            Assert.IsNull(vec.GetAxis(-1));
            Assert.IsNull(vec.GetAxis(2));
        }

        [Test]
        public void Axes_AreGroupedWithStartMiddleEndPositions()
        {
            VecInput vec = new VecInput(3);

            Assert.AreEqual(TweeqBoxPosition.Start, vec.GetAxis(0).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.Middle, vec.GetAxis(1).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.End, vec.GetAxis(2).InlinePosition);
        }

        [Test]
        public void Theme_PropagatesToAxes()
        {
            VecInput vec = new VecInput(2);
            TweeqTheme light = TweeqTheme.Light();

            vec.Theme = light;

            Assert.AreSame(light, vec.GetAxis(0).Theme);
            Assert.AreSame(light, vec.GetAxis(1).Theme);
        }

        [Test]
        public void Theme_NullFallsBackToDark()
        {
            VecInput vec = new VecInput(2);
            vec.Theme = null;

            Assert.IsNotNull(vec.Theme);
            Assert.AreEqual(ColorMode.Dark, vec.Theme.Mode);
        }

        [Test]
        public void Precision_PropagatesToAxes()
        {
            VecInput vec = new VecInput(3) { Precision = 2 };

            for (int i = 0; i < vec.Dimensions; i++)
            {
                Assert.AreEqual(2, vec.GetAxis(i).Precision);
            }
        }

        [Test]
        public void Disabled_PropagatesToAxes()
        {
            VecInput vec = new VecInput(3);

            Assert.IsFalse(vec.Disabled);
            Assert.IsFalse(vec.GetAxis(0).Disabled);

            vec.Disabled = true;

            for (int i = 0; i < vec.Dimensions; i++)
            {
                Assert.IsTrue(vec.GetAxis(i).Disabled, $"axis {i}");
            }

            vec.Disabled = false;

            for (int i = 0; i < vec.Dimensions; i++)
            {
                Assert.IsFalse(vec.GetAxis(i).Disabled, $"axis {i}");
            }
        }

        [Test]
        public void Invalid_PropagatesToAxes()
        {
            VecInput vec = new VecInput(4);

            Assert.IsFalse(vec.Invalid);

            vec.Invalid = true;

            for (int i = 0; i < vec.Dimensions; i++)
            {
                Assert.IsTrue(vec.GetAxis(i).Invalid, $"axis {i}");
            }
        }

        [Test]
        public void Disabled_BlocksAxisPicking()
        {
            // 軸側の視覚・遮断は NumberInput の実装に任せる契約なので、伝播の結果だけを見る
            VecInput vec = new VecInput(2);

            vec.Disabled = true;

            Assert.AreEqual(PickingMode.Ignore, vec.GetAxis(0).pickingMode);
            Assert.AreEqual(PickingMode.Ignore, vec.GetAxis(1).pickingMode);
        }

        [Test]
        public void DefaultConstructor_UsesTwoAxesForUxml()
        {
            // UXML / UI Builder は引数なしで生成するので、既定は最小軸数
            Assert.AreEqual(2, new VecInput().Dimensions);
        }
    }
}
