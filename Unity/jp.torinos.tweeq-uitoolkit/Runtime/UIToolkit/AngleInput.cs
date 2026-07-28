using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Composite angle input (equivalent to the Vue InputAngle). Places a <see cref="RotaryInput"/> on the left and a
    /// degree-display <see cref="NumberInput"/> on the right, syncing the value bidirectionally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When width is insufficient, collapse the number field and show only the knob, same as the Vue version
    /// (threshold is <c>theme.inputHeight * 4</c>).
    /// </para>
    /// <para>
    /// Notifications are consolidated into a single stream. Regardless of which child is operated, ValueChanged
    /// fires once per update and Confirmed fires once per gesture.
    /// </para>
    /// <para>
    /// The Vue version separates the two with a gap-control (9px), but here they are fused via
    /// <see cref="InputGroup"/>. On the Unity side, having the connected rounded corners convey "one value edited
    /// through two mouths" communicates the intent better than placing them apart.
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class AngleInput : VisualElement, INotifyValueChanged<float>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Vue: showNumber = width > theme.inputHeight * 4
        const float SHOW_NUMBER_WIDTH_FACTOR = 4f;

        const string DEGREE_SUFFIX = "°";

        #endregion

        #region Fields

        readonly InputGroup _group;
        readonly RotaryInput _rotary;
        readonly NumberInput _number;

        float _value;
        bool _disabled;
        bool _invalid;
        TweeqTheme _theme = TweeqTheme.Dark();

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // Vue's useElementSize returns 0 until the first measurement, so default to the "collapsed" state to match
        bool _showNumber;

        // Guard to avoid mistaking notifications during write-back to children for our own input
        bool _syncing;

        #endregion

        #region Public API

        /// <summary>Fires every time the value changes.</summary>
        public event Action<float> ValueChanged;

        /// <summary>Fires once per gesture on drag commit, Enter, or blur.</summary>
        public event Action<float> Confirmed;

        /// <summary>Current angle (in degrees).</summary>
        [UxmlAttribute]
        public float value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                float previous = _value;
                SetValueWithoutNotify(value);
                Notify(previous, _value);
            }
        }

        /// <summary>The left-side knob. Use this when you need to touch overlay settings etc. individually.</summary>
        public RotaryInput Rotary => _rotary;

        /// <summary>The right-side number field. Use this when you need to touch Bar, SnapStep, etc. individually.</summary>
        public NumberInput Number => _number;

        /// <summary>Whether the number field is shown. Result of the width check.</summary>
        public bool ShowsNumber => _showNumber;

        /// <summary>Color theme. Propagated to children as-is.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;
                _rotary.Theme = _theme;
                _number.Theme = _theme;
                ApplyRotarySize();
                ApplyBoxFusion();
            }
        }

        /// <summary>Snap angle (in degrees). Default 45. A knob-only concept, so it is not distributed to the number field.</summary>
        [UxmlAttribute]
        public double Snap
        {
            get => _rotary.Snap;
            set => _rotary.Snap = value;
        }

        /// <summary>Indicator angle offset (in degrees).</summary>
        [UxmlAttribute]
        public double AngleOffset
        {
            get => _rotary.AngleOffset;
            set => _rotary.AngleOffset = value;
        }

        /// <summary>
        /// Quantization width. Distributed to both the knob and the number field.
        /// Applying it to only one side would let the knob's raw angle flow straight into the field, leaving the granularity mismatched.
        /// </summary>
        [UxmlAttribute]
        public double Step
        {
            get => _number.Step;
            set
            {
                _rotary.Step = value;
                _number.Step = value;
            }
        }

        /// <summary>
        /// Lower bound of the number field. The knob is specced to retain multi-turn state, so it is not clamped
        /// (the Vue InputAngle also has no min/max, and the knob side passes values through untouched).
        /// </summary>
        [UxmlAttribute]
        public double Min
        {
            get => _number.Min;
            set => _number.Min = value;
        }

        /// <summary>Upper bound of the number field. Interpreted the same as <see cref="Min"/>.</summary>
        [UxmlAttribute]
        public double Max
        {
            get => _number.Max;
            set => _number.Max = value;
        }

        /// <summary>Digits shown in the number field at rest.</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _number.Precision;
            set => _number.Precision = value;
        }

        /// <summary>Disabled state. Distributed to both the knob and the number field.</summary>
        [UxmlAttribute]
        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value)
                {
                    return;
                }

                _disabled = value;
                _rotary.Disabled = _disabled;
                _number.Disabled = _disabled;
            }
        }

        /// <summary>Invalid-value display. Distributed only to the number field (the knob has no invalid representation).</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get => _invalid;
            set
            {
                if (_invalid == value)
                {
                    return;
                }

                _invalid = value;
                _number.Invalid = _invalid;
            }
        }

        /// <summary>Position within a horizontal group. Decomposed and redistributed to the two inner parts.</summary>
        public TweeqBoxPosition InlinePosition
        {
            get => _inlinePosition;
            set
            {
                if (_inlinePosition == value)
                {
                    return;
                }

                _inlinePosition = value;
                ApplyBoxFusion();
            }
        }

        /// <summary>Position within a vertical group. Since the two parts are laid out side by side, distribute the value to both as-is.</summary>
        public TweeqBoxPosition BlockPosition
        {
            get => _blockPosition;
            set
            {
                if (_blockPosition == value)
                {
                    return;
                }

                _blockPosition = value;
                ApplyBoxFusion();
            }
        }

        /// <summary>
        /// Given a layout width, redetermine whether the number field should be shown.
        /// Normally called automatically from GeometryChangedEvent, but this entry point is left open so it can
        /// also be driven from environments where layout does not run (EditMode tests, not-yet-attached).
        /// </summary>
        public void PerformResize(float width)
        {
            if (float.IsNaN(width))
            {
                return;
            }

            float threshold = (_theme != null ? _theme.InputHeight : 0f) * SHOW_NUMBER_WIDTH_FACTOR;
            SetShowNumber(width > threshold);
        }

        /// <summary>
        /// Reproduces a value change on the knob side. Since RotaryInput's ChangeEvent is not dispatched without a
        /// panel, this entry point is left open for external drivers and tests.
        /// </summary>
        public void PerformRotaryEdit(float newValue)
        {
            // While Disabled, real operations don't reach it, so block this entry point too (keep behavior consistent with the real path)
            if (_disabled)
            {
                return;
            }

            // The child holds the value even on the real path, so write to the child first, then flow it to the aggregate
            _rotary.SetValueWithoutNotify(newValue);
            Adopt(newValue, _number);
        }

        /// <summary>Reproduces a value change on the number field side. Same purpose as <see cref="PerformRotaryEdit"/>.</summary>
        public void PerformNumberEdit(float newValue)
        {
            if (_disabled)
            {
                return;
            }

            _number.SetValueWithoutNotify(newValue);
            Adopt(newValue, _rotary);
        }

        /// <summary>
        /// Fires gesture confirmation. Since a child's Confirmed only occurs from operations on the panel, this
        /// entry point is left open for external drivers and tests.
        /// </summary>
        public void PerformConfirm()
        {
            if (_disabled)
            {
                return;
            }

            OnChildConfirmed(_value);
        }

        /// <summary>Sets the value without firing a ChangeEvent.</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = newValue;

            _syncing = true;
            _rotary.SetValueWithoutNotify(newValue);
            _number.SetValueWithoutNotify(newValue);
            _syncing = false;
        }

        #endregion

        #region Construction

        public AngleInput()
        {
            this.AddToClassList("tweeq-angle-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };

            _rotary = new RotaryInput
            {
                name = "tweeq-angle-rotary",
                Theme = _theme,
            };

            // InputGroup.ApplyStretch assigns basis 0 to children with no flexBasis specified.
            // Since basis wins over width, without an explicit value the 24px knob would collapse to zero width
            _rotary.style.flexGrow = 0f;
            _rotary.style.flexShrink = 0f;

            _number = new NumberInput
            {
                name = "tweeq-angle-number",
                Theme = _theme,
                Suffix = DEGREE_SUFFIX,
            };
            _number.style.flexGrow = 1f;
            _number.style.flexBasis = 0f;

            // A child's value change only ever surfaces via ChangeEvent (neither has a ValueChanged)
            _rotary.RegisterValueChangedCallback(OnRotaryChanged);
            _number.RegisterValueChangedCallback(OnNumberChanged);
            _rotary.Confirmed += OnChildConfirmed;
            _number.Confirmed += OnChildConfirmed;

            // Paint order in UI Toolkit is hierarchy order, and the knob scales to 1.8x on
            // hover/drag — added last so it paints over the number field (the original solves
            // this with z-index: 2, which UI Toolkit does not have). RowReverse keeps the
            // visual order knob-left, field-right.
            _group.Direction = FlexDirection.RowReverse;
            _group.Add(_number);
            _group.Add(_rotary);
            this.hierarchy.Add(_group);

            // InputGroup still counts a collapsed number field as one box, so we redistribute the rounded corners ourselves.
            // Since this overrides after the group assigns positions, run it every time layout is finalized
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            ApplyRotarySize();
            ApplyNumberVisibility();
            ApplyBoxFusion();
        }

        #endregion

        #region Internals

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt != null)
            {
                PerformResize(evt.newRect.width);
            }

            // InputGroup redistributes gap and rounded corners on every attach, so the override for the collapsed
            // number field must be reapplied every time layout is finalized
            // (each setter is a no-op for the same value, so it's fine to call them every time)
            ApplyNumberVisibility();
            ApplyBoxFusion();
        }

        void ApplyRotarySize()
        {
            float size = _theme != null ? _theme.InputHeight : 0f;
            _rotary.style.width = size;
            _rotary.style.height = size;
            _rotary.style.flexBasis = size;
        }

        void SetShowNumber(bool show)
        {
            if (_showNumber == show)
            {
                return;
            }

            _showNumber = show;
            ApplyNumberVisibility();
            ApplyBoxFusion();
        }

        void ApplyNumberVisibility()
        {
            _number.style.display = _showNumber ? DisplayStyle.Flex : DisplayStyle.None;

            // InputGroup's gap is distributed to "all but the last", so remove the margin ourselves on the collapsed side
            float gap = _showNumber && _theme != null ? _theme.GapGroup : 0f;
            _rotary.style.marginRight = gap;
        }

        // Decompose the position received from outside into the two boxes [knob][number field].
        // The knob is circular and has no corners to round (a no-op on the RotaryInput side), but it's still needed for the standalone-display check
        void ApplyBoxFusion()
        {
            bool roundStart = _inlinePosition == TweeqBoxPosition.None
                || _inlinePosition == TweeqBoxPosition.Start;
            bool roundEnd = _inlinePosition == TweeqBoxPosition.None
                || _inlinePosition == TweeqBoxPosition.End;

            if (_showNumber)
            {
                _rotary.InlinePosition = roundStart
                    ? TweeqBoxPosition.Start
                    : TweeqBoxPosition.Middle;
                _number.InlinePosition = roundEnd
                    ? TweeqBoxPosition.End
                    : TweeqBoxPosition.Middle;
            }
            else
            {
                _rotary.InlinePosition = _inlinePosition;
            }

            _rotary.BlockPosition = _blockPosition;
            _number.BlockPosition = _blockPosition;
        }

        void OnRotaryChanged(ChangeEvent<float> evt)
        {
            if (evt == null)
            {
                return;
            }

            Adopt(evt.newValue, _number);
        }

        void OnNumberChanged(ChangeEvent<float> evt)
        {
            if (evt == null)
            {
                return;
            }

            Adopt(evt.newValue, _rotary);
        }

        // Don't write back to the source of the change. The child that's mid-drag holds a raw accumulated value, and
        // if SetValueWithoutNotify overwrote it, the gesture would break
        void Adopt(float next, INotifyValueChanged<float> other)
        {
            if (_syncing || _value == next)
            {
                return;
            }

            float previous = _value;
            _value = next;

            _syncing = true;
            other.SetValueWithoutNotify(next);
            _syncing = false;

            Notify(previous, next);
        }

        void OnChildConfirmed(float childValue)
        {
            if (_syncing)
            {
                return;
            }

            Confirmed?.Invoke(_value);
        }

        void Notify(float previous, float current)
        {
            if (this.panel != null)
            {
                using (ChangeEvent<float> changeEvent = ChangeEvent<float>.GetPooled(previous, current))
                {
                    changeEvent.target = this;
                    this.SendEvent(changeEvent);
                }
            }

            ValueChanged?.Invoke(current);
        }

        #endregion
    }
}
