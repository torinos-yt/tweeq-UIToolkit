using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// RotaryInput のうち panel 非依存の部分（Disabled のゲート・ドラッグセッションの巻き戻し・
    /// UXML 属性の適用）を検証する。
    ///
    /// ドラッグは panel 非依存の命令的 API（BeginRotaryDrag / UpdateRotaryDrag /
    /// EndRotaryDrag / CancelRotaryDrag）で駆動する。以下は panel と描画が要るので Play Mode 側の担当:
    /// - ポインタ位置由来の絶対／相対モード判定とスナップリング
    /// - ノブの 1.8 倍スケールとフォーカスリング
    /// - オーバーレイ（弧・多回転サークル・角度ラベル）の描画
    /// </summary>
    public class RotaryInputTests
    {
        const float EPSILON = 1e-4f;
        const float DISABLED_OPACITY = 0.4f;

        // ドラッグ中はカーソルを隠すので、途中で失敗しても Editor に隠れたまま残さない
        [TearDown]
        public void RestoreCursor()
        {
            UnityEngine.Cursor.visible = true;
        }

        #region ドラッグセッション

        [Test]
        public void Drag_AccumulatesTheDelta()
        {
            RotaryInput input = new RotaryInput();

            input.BeginRotaryDrag();
            input.UpdateRotaryDrag(30.0);
            input.UpdateRotaryDrag(15.0);
            input.EndRotaryDrag();

            Assert.AreEqual(45f, input.value, EPSILON);
            Assert.IsFalse(input.Dragging);
        }

        [Test]
        public void Drag_ConfirmsOnceOnEnd()
        {
            RotaryInput input = new RotaryInput();
            int confirmed = 0;
            float confirmedValue = 0f;
            input.Confirmed += value =>
            {
                confirmed++;
                confirmedValue = value;
            };

            input.BeginRotaryDrag();
            input.UpdateRotaryDrag(10.0);
            input.EndRotaryDrag();
            input.EndRotaryDrag();

            Assert.AreEqual(1, confirmed);
            Assert.AreEqual(10f, confirmedValue, EPSILON);
        }

        [Test]
        public void Cancel_RestoresTheStartValueWithoutConfirming()
        {
            RotaryInput input = new RotaryInput();
            input.SetValueWithoutNotify(90f);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginRotaryDrag();
            input.UpdateRotaryDrag(30.0);
            input.CancelRotaryDrag();

            Assert.AreEqual(0, confirmed);
            Assert.IsFalse(input.Dragging);
            Assert.AreEqual(90f, input.value, EPSILON);
        }

        [Test]
        public void Drag_UpdateWithoutBeginIsIgnored()
        {
            RotaryInput input = new RotaryInput();

            input.UpdateRotaryDrag(30.0);

            Assert.AreEqual(0f, input.value, EPSILON);
        }

        #endregion

        #region Disabled

        [Test]
        public void Disabled_DefaultsToFalse()
        {
            RotaryInput input = new RotaryInput();

            Assert.IsFalse(input.Disabled);
        }

        [Test]
        public void Disabled_BlocksTheDragSession()
        {
            RotaryInput input = new RotaryInput();
            input.Disabled = true;

            input.BeginRotaryDrag();
            input.UpdateRotaryDrag(45.0);
            input.EndRotaryDrag();

            Assert.IsFalse(input.Dragging);
            Assert.AreEqual(0f, input.value, EPSILON);
        }

        [Test]
        public void Disabled_WhileDraggingRollsBackToTheStartValue()
        {
            RotaryInput input = new RotaryInput();
            input.SetValueWithoutNotify(20f);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginRotaryDrag();
            input.UpdateRotaryDrag(60.0);
            Assert.AreEqual(80f, input.value, EPSILON);

            input.Disabled = true;

            Assert.IsFalse(input.Dragging);
            Assert.AreEqual(20f, input.value, EPSILON);
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Disabled_WhileDraggingRestoresTheCursor()
        {
            RotaryInput input = new RotaryInput();

            input.BeginRotaryDrag();
            Assert.IsFalse(UnityEngine.Cursor.visible);

            input.Disabled = true;

            Assert.IsTrue(UnityEngine.Cursor.visible);
        }

        [Test]
        public void Disabled_BlocksPickingAndFocusAndDims()
        {
            RotaryInput input = new RotaryInput();

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
            RotaryInput input = new RotaryInput();
            input.Disabled = true;

            input.value = 123f;

            Assert.AreEqual(123f, input.value, EPSILON);
        }

        #endregion

        #region UXML

        /// <summary>
        /// <c>[UxmlElement]</c> が生成する UxmlSerializedData 経由で属性が実体へ届くことを見る。
        /// UXML 文字列からの Instantiate は VisualTreeAsset のインポート（＝Assets への書き込み）が
        /// 要るため、パッケージのテストからは生成データを直接叩いて代用する。
        /// </summary>
        [Test]
        public void Uxml_SerializedDataAppliesAttributes()
        {
            Type dataType = typeof(RotaryInput).GetNestedType(
                "UxmlSerializedData", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(dataType, "[UxmlElement] の UxmlSerializedData が生成されていない");

            UxmlSerializedData data = (UxmlSerializedData)Activator.CreateInstance(dataType);
            OverrideAttribute(dataType, data, "Snap", 30.0);
            OverrideAttribute(dataType, data, "AngleOffset", 90.0);
            OverrideAttribute(dataType, data, "Disabled", true);

            object instance = data.CreateInstance();
            Assert.IsInstanceOf<RotaryInput>(instance);

            data.Deserialize(instance);

            RotaryInput rotary = (RotaryInput)instance;
            Assert.AreEqual(30.0, rotary.Snap, EPSILON);
            Assert.AreEqual(90.0, rotary.AngleOffset, EPSILON);
            Assert.IsTrue(rotary.Disabled);
        }

        // 生成コードは「UXML に書かれた属性か」をフラグで判定してから実体へ書くので、
        // 値と同時に上書き済みフラグも立てる（フラグ名に依存しないよう非ゼロ値を拾う）
        static void OverrideAttribute(Type dataType, object data, string name, object value)
        {
            const BindingFlags lookup = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo field = dataType.GetField(name, lookup);
            Assert.IsNotNull(field, $"UxmlSerializedData に属性フィールド {name} が無い");
            field.SetValue(data, value);

            FieldInfo flags = dataType.GetField(name + "_UxmlAttributeFlags", lookup);
            Assert.IsNotNull(flags, $"UxmlSerializedData に {name} のフラグフィールドが無い");
            flags.SetValue(data, FirstNonZero(flags.FieldType));
        }

        static object FirstNonZero(Type enumType)
        {
            foreach (object candidate in Enum.GetValues(enumType))
            {
                if (Convert.ToInt64(candidate) != 0L)
                {
                    return candidate;
                }
            }

            Assert.Fail($"{enumType.Name} に非ゼロの値が無い");
            return null;
        }

        #endregion
    }
}
