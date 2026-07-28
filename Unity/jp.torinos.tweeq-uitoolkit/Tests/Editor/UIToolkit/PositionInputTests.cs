using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// PositionInput の契約（m6-wave2-spec.md §C「PositionInput（合成）」）。
    /// スクラバーと数値欄の双方向同期・通知の一本化・角丸の融合を見る。
    /// 実際のポインタ操作とレイアウト、および NumberInput → Vec2Input → 自身という確定の
    /// 配線そのものは Play Mode 側の担当（ここでは PerformFieldConfirm で口の側だけ確かめる）。
    /// </summary>
    public class PositionInputTests
    {
        const float EPSILON = 1e-4f;

        static PositionInput Create(Vector2 initial)
        {
            PositionInput input = new PositionInput();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        static void AssertVector(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, EPSILON, "x");
            Assert.AreEqual(expected.y, actual.y, EPSILON, "y");
        }

        [TearDown]
        public void RestoreCursor()
        {
            Cursor.visible = true;
        }

        #region 同期

        [Test]
        public void SetValueWithoutNotify_WritesBothChildren()
        {
            PositionInput input = Create(new Vector2(3f, 4f));

            AssertVector(new Vector2(3f, 4f), input.Translate.value);
            AssertVector(new Vector2(3f, 4f), input.Field.value);
        }

        [Test]
        public void TranslateDrag_UpdatesTheFieldAndNotifiesOnce()
        {
            PositionInput input = Create(Vector2.zero);
            List<Vector2> changed = new List<Vector2>();
            input.ValueChanged += value => changed.Add(value);

            input.Translate.BeginTranslateDrag();
            input.Translate.UpdateTranslateDrag(new Vector2(10f, 5f));
            input.Translate.EndTranslateDrag();

            Assert.AreEqual(1, changed.Count);

            // Y は「上ドラッグ = +Y」の Unity 合わせ逸脱（m6-wave2-spec.md）。数値欄も同じ値
            AssertVector(new Vector2(10f, -5f), input.value);
            AssertVector(new Vector2(10f, -5f), input.Field.value);
        }

        [Test]
        public void FieldEdit_UpdatesTheScrubber()
        {
            PositionInput input = Create(Vector2.zero);

            input.Field.value = new Vector2(2f, -6f);

            AssertVector(new Vector2(2f, -6f), input.value);
            AssertVector(new Vector2(2f, -6f), input.Translate.value);
        }

        [Test]
        public void ValueSetter_NotifiesOnceAndWritesBothChildren()
        {
            PositionInput input = Create(Vector2.zero);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = new Vector2(1f, 2f);

            Assert.AreEqual(1, changed);
            AssertVector(new Vector2(1f, 2f), input.Translate.value);
            AssertVector(new Vector2(1f, 2f), input.Field.value);
        }

        [Test]
        public void SetValueWithoutNotify_IsSilent()
        {
            PositionInput input = Create(Vector2.zero);
            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.SetValueWithoutNotify(new Vector2(9f, 9f));

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, confirmed);
        }

        #endregion

        #region 確定

        [Test]
        public void Confirm_TranslateGestureRaisesConfirmedOnce()
        {
            PositionInput input = Create(Vector2.zero);
            int confirmed = 0;
            Vector2 received = Vector2.zero;
            input.Confirmed += value =>
            {
                confirmed++;
                received = value;
            };

            input.Translate.BeginTranslateDrag();
            input.Translate.UpdateTranslateDrag(new Vector2(1f, 1f));
            input.Translate.UpdateTranslateDrag(new Vector2(1f, 1f));
            input.Translate.EndTranslateDrag();

            Assert.AreEqual(1, confirmed);
            AssertVector(new Vector2(2f, -2f), received);
        }

        [Test]
        public void Confirm_FieldGestureRaisesConfirmedOnce()
        {
            PositionInput input = Create(Vector2.zero);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.Field.value = new Vector2(5f, 0f);
            input.PerformFieldConfirm();

            Assert.AreEqual(1, confirmed);
        }

        #endregion

        #region レンジ

        [Test]
        public void Range_PropagatesToBothChildren()
        {
            PositionInput input = Create(Vector2.zero);

            input.Min = new Vector2(-1f, -2f);
            input.Max = new Vector2(3f, 4f);

            AssertVector(new Vector2(-1f, -2f), input.Translate.Min);
            AssertVector(new Vector2(3f, 4f), input.Translate.Max);
            Assert.AreEqual(-2.0, input.Field.GetAxis(1).Min);
            Assert.AreEqual(3.0, input.Field.GetAxis(0).Max);
        }

        [Test]
        public void Step_AppliesToBothAxesOfTheField()
        {
            PositionInput input = Create(Vector2.zero);

            input.Step = 0.25;

            Assert.AreEqual(0.25, input.Field.GetAxis(0).Step);
            Assert.AreEqual(0.25, input.Field.GetAxis(1).Step);
            Assert.AreEqual(0.25, input.Step);
        }

        #endregion

        #region グループ融合

        [Test]
        public void BoxFusion_JoinsScrubberAndBothAxes()
        {
            PositionInput input = Create(Vector2.zero);

            Assert.AreEqual(TweeqBoxPosition.Start, input.Translate.InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.Middle, input.Field.GetAxis(0).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.End, input.Field.GetAxis(1).InlinePosition);
        }

        #endregion

        #region disabled / invalid

        [Test]
        public void Disabled_PropagatesToScrubberAndField()
        {
            PositionInput input = Create(Vector2.zero);

            Assert.IsFalse(input.Disabled);

            input.Disabled = true;

            Assert.IsTrue(input.Translate.Disabled);
            Assert.IsTrue(input.Field.Disabled);
            Assert.IsTrue(input.Field.GetAxis(0).Disabled);
            Assert.IsTrue(input.Field.GetAxis(1).Disabled);

            input.Disabled = false;

            Assert.IsFalse(input.Translate.Disabled);
            Assert.IsFalse(input.Field.Disabled);
        }

        [Test]
        public void Invalid_GoesToTheFieldOnly()
        {
            // スクラバーには Vue にも invalid 表現が無いので、数値側だけに配る
            PositionInput input = Create(Vector2.zero);

            input.Invalid = true;

            Assert.IsTrue(input.Field.Invalid);
            Assert.IsTrue(input.Field.GetAxis(0).Invalid);
            Assert.IsFalse(input.Translate.Disabled);
        }

        [Test]
        public void Disabled_BlocksTheScrubberDrag()
        {
            PositionInput input = Create(Vector2.zero);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.Disabled = true;
            input.Translate.BeginTranslateDrag();
            input.Translate.UpdateTranslateDrag(new Vector2(10f, 10f));
            input.Translate.EndTranslateDrag();

            Assert.AreEqual(0, changed);
            AssertVector(Vector2.zero, input.value);
        }

        [Test]
        public void Disabled_BlocksPerformFieldConfirm()
        {
            PositionInput input = Create(Vector2.zero);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.Disabled = true;
            input.PerformFieldConfirm();

            Assert.AreEqual(0, confirmed);
        }

        #endregion
    }
}
