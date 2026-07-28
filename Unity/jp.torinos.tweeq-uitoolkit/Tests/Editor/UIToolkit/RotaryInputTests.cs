using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies the panel-independent parts of RotaryInput (the Disabled gate, drag session
    /// rollback, and UXML attribute application).
    ///
    /// Drags are driven via the panel-independent imperative API (BeginRotaryDrag /
    /// UpdateRotaryDrag / EndRotaryDrag / CancelRotaryDrag). The following require a panel and
    /// rendering, so they are the responsibility of the Play Mode side:
    /// - Absolute/relative mode determination and snap ring behavior derived from pointer position
    /// - The knob's 1.8x scale and focus ring
    /// - Rendering of the overlay (arc, multi-rotation circle, angle label)
    /// </summary>
    public class RotaryInputTests
    {
        const float EPSILON = 1e-4f;
        const float DISABLED_OPACITY = 0.4f;

        // The cursor is hidden during a drag, so restore it even if a test fails partway through,
        // to avoid leaving it hidden in the Editor
        [TearDown]
        public void RestoreCursor()
        {
            UnityEngine.Cursor.visible = true;
        }

        #region Drag session

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
            // An external assignment is not an "operation", so it passes through (same handling as NumberInput)
            RotaryInput input = new RotaryInput();
            input.Disabled = true;

            input.value = 123f;

            Assert.AreEqual(123f, input.value, EPSILON);
        }

        #endregion

        #region UXML

        /// <summary>
        /// Verifies that attributes reach the instance via the UxmlSerializedData generated by
        /// <c>[UxmlElement]</c>.
        /// Instantiating from a UXML string requires importing a VisualTreeAsset (i.e. writing to
        /// Assets), so the package tests instead poke the generated data directly as a substitute.
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

        // The generated code checks a flag for "was this attribute written in the UXML" before
        // writing to the instance, so also set the overridden flag along with the value (pick a
        // non-zero value so this doesn't depend on the flag's name)
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
