using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// SizeInput の契約（m6-wave2-spec.md §C「SizeInput（比率ロック付き Vec2）」）。
    /// 比率追従の基準点・自動解除・鎖トグルとの連動を見る。
    /// 比率計算そのものは <c>SizeLogicTests</c>、描画とポインタ操作は Play Mode 側の担当。
    /// </summary>
    public class SizeInputTests
    {
        const float EPSILON = 1e-3f;

        static SizeInput Create(Vector2 initial)
        {
            SizeInput input = new SizeInput();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        static void AssertVector(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, EPSILON, "x");
            Assert.AreEqual(expected.y, actual.y, EPSILON, "y");
        }

        #region 既定

        [Test]
        public void KeepRatio_DefaultsToOn()
        {
            SizeInput input = Create(new Vector2(100f, 50f));

            Assert.IsTrue(input.KeepRatio);
            Assert.IsTrue(input.Chain.value);
        }

        [Test]
        public void AxisLabels_DefaultToWidthAndHeight()
        {
            SizeInput input = Create(Vector2.one);

            Assert.AreEqual("W", input.Field.GetAxis(0).LeftLabel);
            Assert.AreEqual("H", input.Field.GetAxis(1).LeftLabel);
        }

        #endregion

        #region 比率追従

        [Test]
        public void Locked_WidthChangeScalesHeight()
        {
            SizeInput input = Create(new Vector2(100f, 50f));

            input.Field.value = new Vector2(200f, 50f);

            AssertVector(new Vector2(200f, 100f), input.value);
            AssertVector(new Vector2(200f, 100f), input.Field.value);
        }

        [Test]
        public void Locked_HeightChangeScalesWidth()
        {
            SizeInput input = Create(new Vector2(100f, 50f));

            input.Field.value = new Vector2(100f, 25f);

            AssertVector(new Vector2(50f, 25f), input.value);
        }

        [Test]
        public void Locked_BaselineIsHeldForTheWholeGesture()
        {
            // 1 ジェスチャの間は開始値が基準。直前値を基準にすると倍率が積み上がって比率がずれる
            SizeInput input = Create(new Vector2(100f, 50f));

            input.Field.value = new Vector2(200f, 100f);
            input.Field.value = new Vector2(300f, 150f);

            AssertVector(new Vector2(300f, 150f), input.value);
            Assert.IsTrue(input.KeepRatio);
        }

        [Test]
        public void Locked_BaselineIsRetakenAfterConfirm()
        {
            SizeInput input = Create(new Vector2(100f, 50f));

            input.Field.value = new Vector2(200f, 50f);
            input.PerformFieldConfirm();

            // 200x100 が新しい基準。ここから幅を倍にすれば高さも倍になる
            input.Field.value = new Vector2(400f, 100f);

            AssertVector(new Vector2(400f, 200f), input.value);
        }

        [Test]
        public void Unlocked_PassesBothAxesThrough()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            input.KeepRatio = false;

            input.Field.value = new Vector2(200f, 50f);

            AssertVector(new Vector2(200f, 50f), input.value);
        }

        [Test]
        public void Locked_ZeroBaselinePassesThrough()
        {
            SizeInput input = Create(new Vector2(0f, 50f));

            input.Field.value = new Vector2(10f, 50f);

            AssertVector(new Vector2(10f, 50f), input.value);
            Assert.IsTrue(input.KeepRatio);
        }

        #endregion

        #region 自動解除

        [Test]
        public void Locked_BothAxesChangedWithNewRatioReleasesTheLock()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            List<bool> keepRatioEvents = new List<bool>();
            input.KeepRatioChanged += value => keepRatioEvents.Add(value);

            input.Field.value = new Vector2(200f, 200f);

            Assert.IsFalse(input.KeepRatio);
            Assert.IsFalse(input.Chain.value);
            Assert.AreEqual(1, keepRatioEvents.Count);
            Assert.IsFalse(keepRatioEvents[0]);
            AssertVector(new Vector2(200f, 200f), input.value);
        }

        [Test]
        public void Locked_BothAxesChangedWithSameRatioKeepsTheLock()
        {
            SizeInput input = Create(new Vector2(100f, 50f));

            input.Field.value = new Vector2(200f, 100f);

            Assert.IsTrue(input.KeepRatio);
        }

        #endregion

        #region 鎖トグル

        [Test]
        public void Chain_ClickTogglesKeepRatio()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            List<bool> keepRatioEvents = new List<bool>();
            input.KeepRatioChanged += value => keepRatioEvents.Add(value);

            input.Chain.PerformClick();

            Assert.IsFalse(input.KeepRatio);
            Assert.AreEqual(1, keepRatioEvents.Count);

            input.Chain.PerformClick();

            Assert.IsTrue(input.KeepRatio);
            Assert.AreEqual(2, keepRatioEvents.Count);
        }

        [Test]
        public void Chain_ClickDoesNotChangeTheValue()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.Chain.PerformClick();

            Assert.AreEqual(0, changed);
            AssertVector(new Vector2(100f, 50f), input.value);
        }

        [Test]
        public void KeepRatio_SetterSyncsTheChain()
        {
            SizeInput input = Create(new Vector2(100f, 50f));

            input.KeepRatio = false;

            Assert.IsFalse(input.Chain.value);
        }

        [Test]
        public void Chain_RelockRetakesTheBaseline()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            input.KeepRatio = false;

            input.Field.value = new Vector2(300f, 50f);
            input.KeepRatio = true;

            // 300x50 が新しい基準。幅を倍にすれば高さも倍になる
            input.Field.value = new Vector2(600f, 50f);

            AssertVector(new Vector2(600f, 100f), input.value);
        }

        #endregion

        #region 通知

        [Test]
        public void Value_SetterNotifiesOnce()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            List<Vector2> changed = new List<Vector2>();
            input.ValueChanged += value => changed.Add(value);

            input.value = new Vector2(10f, 20f);

            Assert.AreEqual(1, changed.Count);
            AssertVector(new Vector2(10f, 20f), changed[0]);
            AssertVector(new Vector2(10f, 20f), input.Field.value);
        }

        [Test]
        public void Value_SetterIsNotFilteredByKeepRatio()
        {
            // プログラムからの代入は「ユーザーの片軸編集」ではないので比率追従に通さない
            SizeInput input = Create(new Vector2(100f, 50f));

            input.value = new Vector2(300f, 50f);

            AssertVector(new Vector2(300f, 50f), input.value);
        }

        [Test]
        public void SetValueWithoutNotify_IsSilent()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.SetValueWithoutNotify(new Vector2(1f, 2f));

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, confirmed);
            AssertVector(new Vector2(1f, 2f), input.value);
        }

        [Test]
        public void Edit_RaisesValueChangedPerEditAndConfirmsOnce()
        {
            SizeInput input = Create(new Vector2(100f, 50f));
            int changed = 0;
            int confirmed = 0;
            Vector2 confirmedValue = Vector2.zero;
            input.ValueChanged += _ => changed++;
            input.Confirmed += value =>
            {
                confirmed++;
                confirmedValue = value;
            };

            input.Field.value = new Vector2(200f, 50f);
            input.Field.value = new Vector2(300f, 100f);
            input.PerformFieldConfirm();

            Assert.AreEqual(2, changed);
            Assert.AreEqual(1, confirmed);
            AssertVector(new Vector2(300f, 150f), confirmedValue);
        }

        #endregion

        #region グループ融合

        [Test]
        public void BoxFusion_JoinsBothAxesAndTheChain()
        {
            SizeInput input = Create(Vector2.one);

            Assert.AreEqual(TweeqBoxPosition.Start, input.Field.GetAxis(0).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.Middle, input.Field.GetAxis(1).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.End, input.Chain.InlinePosition);
        }

        #endregion

        #region disabled / invalid

        [Test]
        public void Disabled_PropagatesToFieldAndChain()
        {
            SizeInput input = Create(Vector2.one);

            Assert.IsFalse(input.Disabled);

            input.Disabled = true;

            Assert.IsTrue(input.Field.Disabled);
            Assert.IsTrue(input.Field.GetAxis(0).Disabled);
            Assert.IsTrue(input.Field.GetAxis(1).Disabled);
            Assert.IsTrue(input.Chain.Disabled);

            input.Disabled = false;

            Assert.IsFalse(input.Field.Disabled);
            Assert.IsFalse(input.Chain.Disabled);
        }

        [Test]
        public void Invalid_GoesToTheFieldOnly()
        {
            SizeInput input = Create(Vector2.one);

            input.Invalid = true;

            Assert.IsTrue(input.Field.Invalid);
            Assert.IsTrue(input.Field.GetAxis(1).Invalid);
            Assert.IsFalse(input.Chain.Disabled);
        }

        [Test]
        public void Disabled_BlocksTheChainToggleClick()
        {
            SizeInput input = Create(Vector2.one);
            int keepRatioChanged = 0;
            input.KeepRatioChanged += _ => keepRatioChanged++;

            input.Disabled = true;
            input.Chain.PerformClick();

            Assert.AreEqual(0, keepRatioChanged);
            Assert.IsTrue(input.KeepRatio);
        }

        [Test]
        public void Disabled_BlocksPerformFieldConfirm()
        {
            SizeInput input = Create(Vector2.one);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.Disabled = true;
            input.PerformFieldConfirm();

            Assert.AreEqual(0, confirmed);
        }

        #endregion
    }
}
