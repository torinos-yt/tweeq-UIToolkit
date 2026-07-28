using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A composite 2D position input (M6 wave-2 spec §C).
    /// Places a <see cref="TranslateInput"/> on the left and a <see cref="Vec2Input"/> on the right,
    /// keeping their values synchronized bidirectionally.
    /// </summary>
    /// <remarks>
    /// Notifications are consolidated into a single stream. Regardless of which child is operated,
    /// ValueChanged fires once per update and Confirmed fires only once per gesture.
    /// </remarks>
    [UxmlElement]
    public partial class PositionInput : VisualElement, INotifyValueChanged<Vector2>, ITweeqThemed
    {
        #region Fields

        readonly InputGroup _group;
        readonly TranslateInput _translate;
        readonly Vec2Input _field;

        Vector2 _value;
        bool _disabled;
        bool _invalid;
        TweeqTheme _theme = TweeqTheme.Dark();

        // A guard so notifications received while writing back to children aren't mistaken for our own input
        bool _syncing;

        #endregion

        #region Public API

        /// <summary>Fires every time the value changes.</summary>
        public event Action<Vector2> ValueChanged;

        /// <summary>Fires only once per gesture, on drag confirm, Enter, or blur.</summary>
        public event Action<Vector2> Confirmed;

        /// <summary>The current value.</summary>
        [UxmlAttribute]
        public Vector2 value
        {
            get => _value;
            set
            {
                if (_value.Equals(value))
                {
                    return;
                }

                Vector2 previous = _value;
                SetValueWithoutNotify(value);
                Notify(previous, _value);
            }
        }

        /// <summary>The drag scrubber on the left. Use this to tweak things like overlay settings individually.</summary>
        public TranslateInput Translate => _translate;

        /// <summary>The numeric tuple on the right. Use this to tweak per-axis Precision and the like.</summary>
        public Vec2Input Field => _field;

        /// <summary>Color theme. Propagated to children as-is.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;
                _translate.Theme = _theme;
                _field.Theme = _theme;
                ApplyBoxFusion();
            }
        }

        /// <summary>Lower bound. Applies to both the scrubber and the numeric field.</summary>
        [UxmlAttribute]
        public Vector2 Min
        {
            get => _translate.Min;
            set
            {
                _translate.Min = value;
                _field.Min = new double[] { value.x, value.y };
            }
        }

        /// <summary>Upper bound. Applies to both the scrubber and the numeric field.</summary>
        [UxmlAttribute]
        public Vector2 Max
        {
            get => _translate.Max;
            set
            {
                _translate.Max = value;
                _field.Max = new double[] { value.x, value.y };
            }
        }

        /// <summary>Quantization step for the numeric field (shared by both axes). Doesn't affect the scrubber side, which is px 1:1.</summary>
        [UxmlAttribute]
        public double Step
        {
            get
            {
                NumberInput axis = _field.GetAxis(0);
                return axis != null ? axis.Step : 0.0;
            }

            set => _field.Step = new[] { value };
        }

        /// <summary>Display digits for the numeric field at rest (shared by both axes).</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _field.Precision;
            set => _field.Precision = value;
        }

        /// <summary>Disabled state. Distributed to both the scrubber and the numeric field.</summary>
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
                _translate.Disabled = _disabled;
                _field.Disabled = _disabled;
            }
        }

        /// <summary>Invalid-value display. Distributed only to the numeric field (the original has no invalid representation for the scrubber either).</summary>
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
                _field.Invalid = _invalid;
            }
        }

        /// <summary>
        /// Fires the numeric field's gesture confirmation. Because NumberInput can only confirm via
        /// keyboard/pointer operations on the panel, this is exposed for external drivers and tests.
        /// </summary>
        public void PerformFieldConfirm()
        {
            if (_disabled)
            {
                return;
            }

            OnChildConfirmed(_value);
        }

        /// <summary>Sets the value without firing a ChangeEvent.</summary>
        public void SetValueWithoutNotify(Vector2 newValue)
        {
            _value = newValue;

            _syncing = true;
            _translate.SetValueWithoutNotify(newValue);
            _field.SetValueWithoutNotify(newValue);
            _syncing = false;
        }

        #endregion

        #region Construction

        public PositionInput()
        {
            this.AddToClassList("tweeq-position-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };

            _translate = new TranslateInput
            {
                name = "tweeq-position-translate",
                Theme = _theme,

                // The original InputPosition always calls this with a label
                ShowOverlayLabel = true,
            };

            // The scrubber is fixed at 24px. Keep it out of InputGroup's equal-split distribution
            _translate.style.flexGrow = 0f;
            _translate.style.flexShrink = 0f;

            _field = new Vec2Input
            {
                name = "tweeq-position-field",
                Theme = _theme,
            };

            _translate.ValueChanged += OnTranslateChanged;
            _translate.Confirmed += OnChildConfirmed;
            _field.ValueChanged += OnFieldChanged;
            _field.Confirmed += OnChildConfirmed;

            _group.Add(_translate);
            _group.Add(_field);
            this.hierarchy.Add(_group);

            // Vec2Input isn't an ITweeqInputBox, so InputGroup can't assign corner rounding to its ends.
            // Reapplied on every layout resolution to override after the group redistributes positions
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            ApplyBoxFusion();
        }

        #endregion

        #region Internals

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyBoxFusion();
        }

        // Makes [Translate][X][Y] look like a single connected group. Each setter is a no-op for the same value, so it's fine to call every time
        void ApplyBoxFusion()
        {
            _translate.InlinePosition = TweeqBoxPosition.Start;

            NumberInput x = _field.GetAxis(0);
            if (x != null)
            {
                x.InlinePosition = TweeqBoxPosition.Middle;
            }

            NumberInput y = _field.GetAxis(1);
            if (y != null)
            {
                y.InlinePosition = TweeqBoxPosition.End;
            }
        }

        void OnTranslateChanged(Vector2 next)
        {
            Adopt(next, _field);
        }

        void OnFieldChanged(Vector2 next)
        {
            Adopt(next, _translate);
        }

        void Adopt(Vector2 next, INotifyValueChanged<Vector2> other)
        {
            if (_syncing || _value.Equals(next))
            {
                return;
            }

            Vector2 previous = _value;
            _value = next;

            _syncing = true;
            other.SetValueWithoutNotify(next);
            _syncing = false;

            Notify(previous, next);
        }

        void OnChildConfirmed(Vector2 childValue)
        {
            if (_syncing)
            {
                return;
            }

            Confirmed?.Invoke(_value);
        }

        void Notify(Vector2 previous, Vector2 current)
        {
            if (this.panel != null)
            {
                using (ChangeEvent<Vector2> changeEvent = ChangeEvent<Vector2>.GetPooled(previous, current))
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
