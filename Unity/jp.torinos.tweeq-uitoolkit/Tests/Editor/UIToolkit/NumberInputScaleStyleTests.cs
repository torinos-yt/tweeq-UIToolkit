using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// NumberInput's scale display style. Covers the default being dots that faithfully match the Vue original,
    /// and the dots' display gate. The dot coordinates themselves are handled by NumberScaleDotsTests (pure functions).
    /// </summary>
    public class NumberInputScaleStyleTests
    {
        const BindingFlags LOOKUP =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        #region Default value

        [Test]
        public void ScaleStyleDefaultsToDots()
        {
            NumberInput input = new NumberInput();

            Assert.AreEqual(NumberScaleStyle.Dots, input.ScaleStyle);
        }

        [Test]
        public void ScaleStyleRoundTrips()
        {
            NumberInput input = new NumberInput();

            input.ScaleStyle = NumberScaleStyle.Values;
            Assert.AreEqual(NumberScaleStyle.Values, input.ScaleStyle);

            input.ScaleStyle = NumberScaleStyle.Dots;
            Assert.AreEqual(NumberScaleStyle.Dots, input.ScaleStyle);
        }

        #endregion

        #region Display gate

        [Test]
        public void DotsAreVisibleOnAPlainField()
        {
            NumberInput input = new NumberInput();

            Assert.IsTrue(ScaleDotsVisible(input));
        }

        [Test]
        public void DotsAreHiddenOnASteppedAndFullyClampedField()
        {
            NumberInput input = new NumberInput
            {
                Min = 0.0,
                Max = 100.0,
                Step = 1.0,
                ClampMin = true,
                ClampMax = true,
            };

            Assert.IsFalse(ScaleDotsVisible(input));
        }

        [Test]
        public void DotsStayVisibleWhenOnlyOneSideIsClamped()
        {
            NumberInput input = new NumberInput
            {
                Min = 0.0,
                Max = 100.0,
                Step = 1.0,
                ClampMin = true,
                ClampMax = false,
            };

            Assert.IsTrue(ScaleDotsVisible(input));
        }

        [Test]
        public void DotsStayVisibleWithoutAStep()
        {
            NumberInput input = new NumberInput
            {
                Min = 0.0,
                Max = 100.0,
                ClampMin = true,
                ClampMax = true,
            };

            Assert.IsTrue(ScaleDotsVisible(input));
        }

        [Test]
        public void DotsStayVisibleWithoutAFiniteRange()
        {
            NumberInput input = new NumberInput
            {
                Step = 1.0,
                ClampMin = true,
                ClampMax = true,
            };

            Assert.IsTrue(ScaleDotsVisible(input));
        }

        [Test]
        public void ValuesStyleNeverPaintsDots()
        {
            NumberInput input = new NumberInput { ScaleStyle = NumberScaleStyle.Values };

            Assert.IsFalse(ScaleDotsVisible(input));
        }

        #endregion

        #region UXML

        /// <summary>
        /// Same technique as RotaryInputTests. Instantiating from a UXML string requires importing
        /// a VisualTreeAsset, so this instead drives the generated UxmlSerializedData directly.
        /// </summary>
        [Test]
        public void Uxml_SerializedDataAppliesScaleStyle()
        {
            Type dataType = typeof(NumberInput).GetNestedType(
                "UxmlSerializedData", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(dataType, "[UxmlElement] の UxmlSerializedData が生成されていない");

            UxmlSerializedData data = (UxmlSerializedData)Activator.CreateInstance(dataType);
            OverrideAttribute(dataType, data, "ScaleStyle", NumberScaleStyle.Values);

            object instance = data.CreateInstance();
            Assert.IsInstanceOf<NumberInput>(instance);

            data.Deserialize(instance);

            Assert.AreEqual(NumberScaleStyle.Values, ((NumberInput)instance).ScaleStyle);
        }

        /// <summary>With no attribute specified in UXML, it stays at the default (dots).</summary>
        [Test]
        public void Uxml_KeepsDotsWhenTheAttributeIsAbsent()
        {
            Type dataType = typeof(NumberInput).GetNestedType(
                "UxmlSerializedData", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(dataType);

            UxmlSerializedData data = (UxmlSerializedData)Activator.CreateInstance(dataType);
            object instance = data.CreateInstance();
            data.Deserialize(instance);

            Assert.AreEqual(NumberScaleStyle.Dots, ((NumberInput)instance).ScaleStyle);
        }

        #endregion

        #region Helpers

        // The gate is not exposed publicly since it's internal state used only for rendering
        static bool ScaleDotsVisible(NumberInput input)
        {
            PropertyInfo property = typeof(NumberInput).GetProperty("ScaleDotsVisible", LOOKUP);
            Assert.IsNotNull(property, "ScaleDotsVisible が見つからない");

            return (bool)property.GetValue(input);
        }

        // Generated code first checks a flag for "was this attribute written in UXML" before writing the actual value
        static void OverrideAttribute(Type dataType, object data, string name, object value)
        {
            FieldInfo field = dataType.GetField(name, LOOKUP);
            Assert.IsNotNull(field, $"UxmlSerializedData に属性フィールド {name} が無い");
            field.SetValue(data, value);

            FieldInfo flags = dataType.GetField(name + "_UxmlAttributeFlags", LOOKUP);
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
