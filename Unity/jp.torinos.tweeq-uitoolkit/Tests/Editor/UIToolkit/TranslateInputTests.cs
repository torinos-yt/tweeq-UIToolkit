using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// TranslateInput の契約（m6-wave2-spec.md「テスト契約」の TranslateInput 項目）。
    ///
    /// ドラッグセッションは panel 非依存の命令的 API（BeginTranslateDrag / UpdateTranslateDrag /
    /// EndTranslateDrag / CancelTranslateDrag）として持たせてあるので、感度・軸ロック・クランプ・
    /// 通知回数はここで完結する。以下は panel と描画が要るので Play Mode 側の担当:
    /// - ポインタ押下でセッションが始まりカーソルが消えること
    /// - オーバーレイのドットグリッドと gridScale の補間
    /// - X / Y キーの押下追従とフォーカスリング
    /// </summary>
    public class TranslateInputTests
    {
        const float EPSILON = 1e-4f;

        static TranslateInput Create(Vector2 initial)
        {
            TranslateInput input = new TranslateInput();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        static void AssertVector(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, EPSILON, "x");
            Assert.AreEqual(expected.y, actual.y, EPSILON, "y");
        }

        // ドラッグ中はカーソルを隠すので、途中で失敗しても Editor に隠れたまま残さない
        [TearDown]
        public void RestoreCursor()
        {
            UnityEngine.Cursor.visible = true;
        }

        #region 感度

        [Test]
        public void Drag_DefaultSpeedIsOneToOne()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(10f, 4f));
            input.EndTranslateDrag();

            // Y は「上ドラッグ = +Y」の Unity 合わせ逸脱（m6-wave2-spec.md）。下 4px は −4
            AssertVector(new Vector2(10f, -4f), input.value);
        }

        [Test]
        public void Drag_ShiftMultipliesByFive()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.SetTweakModifiers(true, false);
            input.UpdateTranslateDrag(new Vector2(10f, 4f));
            input.EndTranslateDrag();

            Assert.AreEqual(5f, input.Speed);
            AssertVector(new Vector2(50f, -20f), input.value);
        }

        [Test]
        public void Drag_AltDividesByTen()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.SetTweakModifiers(false, true);
            input.UpdateTranslateDrag(new Vector2(10f, 4f));
            input.EndTranslateDrag();

            Assert.AreEqual(0.1f, input.Speed);
            AssertVector(new Vector2(1f, -0.4f), input.value);
        }

        [Test]
        public void Drag_ShiftWinsOverAlt()
        {
            TranslateInput input = Create(Vector2.zero);
            input.SetTweakModifiers(true, true);

            Assert.AreEqual(5f, input.Speed);
        }

        [Test]
        public void Drag_SpeedChangeAppliesFromTheNextMoveOnly()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(10f, 0f));
            input.SetTweakModifiers(true, false);
            input.UpdateTranslateDrag(new Vector2(10f, 0f));
            input.EndTranslateDrag();

            // 既に積んだぶんは遡って掛け直さない（Vue の累積方式）
            AssertVector(new Vector2(60f, 0f), input.value);
        }

        [Test]
        public void GridScale_FollowsTheModifierKeys()
        {
            TranslateInput input = Create(Vector2.zero);

            Assert.AreEqual(2f, input.GridScaleTarget);

            input.SetTweakModifiers(true, false);
            Assert.AreEqual(0.5f, input.GridScaleTarget);

            input.SetTweakModifiers(false, true);
            Assert.AreEqual(4f, input.GridScaleTarget);
        }

        #endregion

        #region 軸ロック

        [Test]
        public void Drag_XLockDropsTheVerticalComponent()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.SetAxisLocks(true, false);
            input.UpdateTranslateDrag(new Vector2(10f, 4f));
            input.EndTranslateDrag();

            AssertVector(new Vector2(10f, 0f), input.value);
        }

        [Test]
        public void Drag_YLockDropsTheHorizontalComponent()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.SetAxisLocks(false, true);
            input.UpdateTranslateDrag(new Vector2(10f, 4f));
            input.EndTranslateDrag();

            AssertVector(new Vector2(0f, -4f), input.value);
        }

        [Test]
        public void Drag_BothLocksFreezeTheValue()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.SetAxisLocks(true, true);
            input.UpdateTranslateDrag(new Vector2(10f, 4f));
            input.EndTranslateDrag();

            AssertVector(Vector2.zero, input.value);
        }

        [Test]
        public void Drag_LockIsOnlyActiveWhileHeld()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.SetAxisLocks(true, false);
            input.UpdateTranslateDrag(new Vector2(10f, 4f));
            input.SetAxisLocks(false, false);
            input.UpdateTranslateDrag(new Vector2(0f, 4f));
            input.EndTranslateDrag();

            AssertVector(new Vector2(10f, -4f), input.value);
        }

        #endregion

        #region クランプ

        [Test]
        public void Clamp_VectorRangeStopsBothAxes()
        {
            TranslateInput input = Create(Vector2.zero);
            input.Min = new Vector2(-1f, -2f);
            input.Max = new Vector2(3f, 4f);

            input.BeginTranslateDrag();

            // 下 100px = −Y 方向なので min.y=−2 側でクランプ
            input.UpdateTranslateDrag(new Vector2(100f, 100f));
            AssertVector(new Vector2(3f, -2f), input.value);

            input.UpdateTranslateDrag(new Vector2(-100f, -100f));
            AssertVector(new Vector2(-1f, 4f), input.value);

            input.EndTranslateDrag();
        }

        [Test]
        public void Clamp_ScalarRangeAppliesToBothAxes()
        {
            TranslateInput input = Create(Vector2.zero);
            input.SetMin(0f);
            input.SetMax(5f);

            input.BeginTranslateDrag();

            // 上 100px = +Y 方向なので両軸とも max=5 側でクランプ
            input.UpdateTranslateDrag(new Vector2(100f, -100f));
            input.EndTranslateDrag();

            AssertVector(new Vector2(5f, 5f), input.value);
        }

        [Test]
        public void Clamp_ValueRecoversAsSoonAsTheDragComesBack()
        {
            // 「開始値 + 総移動量」方式だと端で押し続けたぶんの遅れが出る。Vue の累積方式では出ない
            TranslateInput input = Create(Vector2.zero);
            input.SetMax(5f);

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(100f, 0f));
            input.UpdateTranslateDrag(new Vector2(-1f, 0f));
            input.EndTranslateDrag();

            AssertVector(new Vector2(4f, 0f), input.value);
        }

        [Test]
        public void Clamp_DefaultRangeIsUnbounded()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(9999f, -9999f));
            input.EndTranslateDrag();

            AssertVector(new Vector2(9999f, 9999f), input.value);
        }

        #endregion

        #region 通知

        [Test]
        public void Drag_RaisesValueChangedPerMoveAndConfirmsOnce()
        {
            TranslateInput input = Create(Vector2.zero);
            List<Vector2> changed = new List<Vector2>();
            int confirmed = 0;
            Vector2 confirmedValue = Vector2.zero;
            input.ValueChanged += value => changed.Add(value);
            input.Confirmed += value =>
            {
                confirmed++;
                confirmedValue = value;
            };

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(1f, 0f));
            input.UpdateTranslateDrag(new Vector2(1f, 0f));
            input.UpdateTranslateDrag(new Vector2(1f, 0f));
            input.EndTranslateDrag();

            Assert.AreEqual(3, changed.Count);
            Assert.AreEqual(1, confirmed);
            AssertVector(new Vector2(3f, 0f), confirmedValue);
        }

        [Test]
        public void Drag_MoveWithoutValueChangeIsSilent()
        {
            TranslateInput input = Create(Vector2.zero);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(Vector2.zero);
            input.EndTranslateDrag();

            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Drag_UpdateWithoutBeginIsIgnored()
        {
            TranslateInput input = Create(Vector2.zero);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.UpdateTranslateDrag(new Vector2(10f, 10f));

            Assert.AreEqual(0, changed);
            AssertVector(Vector2.zero, input.value);
        }

        [Test]
        public void Drag_EndWithoutBeginDoesNotConfirm()
        {
            TranslateInput input = Create(Vector2.zero);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.EndTranslateDrag();

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Drag_EndTwiceConfirmsOnlyOnce()
        {
            TranslateInput input = Create(Vector2.zero);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(1f, 1f));
            input.EndTranslateDrag();
            input.EndTranslateDrag();

            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Drag_BeginTwiceKeepsASingleSession()
        {
            TranslateInput input = Create(new Vector2(2f, 2f));

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(5f, 0f));

            // 二重開始で開始値を取り直すと Escape の戻り先がずれる
            input.BeginTranslateDrag();
            input.CancelTranslateDrag();

            AssertVector(new Vector2(2f, 2f), input.value);
        }

        #endregion

        #region Escape 復元

        [Test]
        public void Cancel_RestoresTheStartValueWithoutConfirming()
        {
            TranslateInput input = Create(new Vector2(5f, 5f));
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(10f, 10f));
            input.CancelTranslateDrag();

            Assert.AreEqual(0, confirmed);
            Assert.IsFalse(input.Dragging);
            AssertVector(new Vector2(5f, 5f), input.value);
        }

        [Test]
        public void Cancel_NotifiesTheRollback()
        {
            // ドラッグ中に通知した値を巻き戻すので、戻したことも通知しないと外側が置いていかれる
            TranslateInput input = Create(Vector2.zero);
            List<Vector2> changed = new List<Vector2>();
            input.ValueChanged += value => changed.Add(value);

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(10f, 0f));
            input.CancelTranslateDrag();

            Assert.AreEqual(2, changed.Count);
            AssertVector(Vector2.zero, changed[1]);
        }

        [Test]
        public void Cancel_WithoutBeginIsIgnored()
        {
            TranslateInput input = Create(new Vector2(3f, 3f));
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.CancelTranslateDrag();

            Assert.AreEqual(0, changed);
            AssertVector(new Vector2(3f, 3f), input.value);
        }

        #endregion

        #region 値

        [Test]
        public void Value_SetterNotifiesOnce()
        {
            TranslateInput input = Create(Vector2.zero);
            List<Vector2> changed = new List<Vector2>();
            input.ValueChanged += value => changed.Add(value);

            input.value = new Vector2(1f, 2f);

            Assert.AreEqual(1, changed.Count);
            AssertVector(new Vector2(1f, 2f), changed[0]);
        }

        [Test]
        public void Value_SameValueIsSilent()
        {
            TranslateInput input = Create(new Vector2(1f, 2f));
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = new Vector2(1f, 2f);

            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Value_SetValueWithoutNotifyIsSilent()
        {
            TranslateInput input = Create(Vector2.zero);
            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.SetValueWithoutNotify(new Vector2(7f, 8f));

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, confirmed);
            AssertVector(new Vector2(7f, 8f), input.value);
        }

        #endregion

        #region グループ融合

        [Test]
        public void InlinePosition_StartFlattensTheTrailingCorners()
        {
            TranslateInput input = new TranslateInput();

            input.InlinePosition = TweeqBoxPosition.Start;

            Assert.AreEqual(0f, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomRightRadius));
            Assert.Greater(Radius(input.style.borderTopLeftRadius), 0f);
        }

        static float Radius(StyleLength length)
        {
            return length.value.value;
        }

        #endregion

        #region Disabled

        const float DISABLED_OPACITY = 0.4f;

        [Test]
        public void Disabled_DefaultsToFalse()
        {
            Assert.IsFalse(new TranslateInput().Disabled);
        }

        [Test]
        public void Disabled_BlocksTheDragSession()
        {
            TranslateInput input = Create(Vector2.zero);
            input.Disabled = true;

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(10f, 10f));
            input.EndTranslateDrag();

            Assert.IsFalse(input.Dragging);
            AssertVector(Vector2.zero, input.value);
        }

        [Test]
        public void Disabled_WhileDraggingRollsBackToTheStartValue()
        {
            TranslateInput input = Create(new Vector2(3f, 4f));
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginTranslateDrag();
            input.UpdateTranslateDrag(new Vector2(10f, 0f));
            AssertVector(new Vector2(13f, 4f), input.value);

            input.Disabled = true;

            Assert.IsFalse(input.Dragging);
            AssertVector(new Vector2(3f, 4f), input.value);
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Disabled_WhileDraggingRestoresTheCursor()
        {
            TranslateInput input = Create(Vector2.zero);

            input.BeginTranslateDrag();
            Assert.IsFalse(UnityEngine.Cursor.visible);

            input.Disabled = true;

            Assert.IsTrue(UnityEngine.Cursor.visible);
        }

        [Test]
        public void Disabled_BlocksPickingAndFocusAndDims()
        {
            TranslateInput input = Create(Vector2.zero);

            input.Disabled = true;

            Assert.AreEqual(PickingMode.Ignore, input.pickingMode);
            Assert.IsFalse(input.focusable);
            Assert.AreEqual(DISABLED_OPACITY, input.style.opacity.value, EPSILON);

            input.Disabled = false;

            Assert.AreEqual(PickingMode.Position, input.pickingMode);
            Assert.IsTrue(input.focusable);
            Assert.AreEqual(1f, input.style.opacity.value, EPSILON);
        }

        [Test]
        public void Disabled_DoesNotBlockTheProgrammaticValue()
        {
            // 外部からの代入は「操作」ではないので通す（NumberInput と同じ扱い）
            TranslateInput input = Create(Vector2.zero);
            input.Disabled = true;

            input.value = new Vector2(1f, 2f);

            AssertVector(new Vector2(1f, 2f), input.value);
        }

        #endregion
    }
}
