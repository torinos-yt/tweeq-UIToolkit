using System;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A 2D size input with a ratio lock (M6 wave-2 spec §C).
    /// Fuses a chain toggle onto the right end of a <see cref="Vec2Input"/>; while locked, changing one
    /// axis makes the other axis follow.
    /// </summary>
    /// <remarks>
    /// The baseline for following is "the value at the start of editing" (the original's valueOnEdit).
    /// Using the previous value as the baseline would let the multiplier accumulate during a drag and
    /// drift the ratio off. The starting point is retaken at the start of each gesture, which closes with Confirmed.
    /// </remarks>
    [UxmlElement]
    public partial class SizeInput : VisualElement, INotifyValueChanged<Vector2>, ITweeqThemed
    {
        #region Constants

        static readonly string[] DEFAULT_AXIS_LABELS = { "W", "H" };

        // Chain icon (a simplified version of the reference implementation's link-icon painter). Anchored to the center of the 24px box
        const float LINK_LOOP_OFFSET = 4.5f;
        const float LINK_LOOP_RADIUS = 3.2f;
        const float LINK_STROKE_WIDTH = 1.25f;
        const float LINK_BAR_WIDTH = 1f;

        #endregion

        #region Fields

        readonly InputGroup _group;
        readonly Vec2Input _field;
        readonly ButtonToggleInput _chain;
        readonly VisualElement _chainIcon;

        Vector2 _value;
        Vector2 _baseline;
        bool _hasBaseline;
        bool _keepRatio = true;
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

        /// <summary>Fires when the ratio lock toggles (including automatic release).</summary>
        public event Action<bool> KeepRatioChanged;

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

        /// <summary>Ratio lock. Defaults to on (the original's keepRatio = ref(true)).</summary>
        [UxmlAttribute]
        public bool KeepRatio
        {
            get => _keepRatio;
            set
            {
                if (_keepRatio == value)
                {
                    return;
                }

                _keepRatio = value;
                _chain.SetValueWithoutNotify(value);

                // The value at the moment the lock state changes becomes the baseline for the next following
                _hasBaseline = false;
                RefreshChain();
                KeepRatioChanged?.Invoke(value);
            }
        }

        /// <summary>The numeric tuple itself. Use this to tweak per-axis Precision and the like.</summary>
        public Vec2Input Field => _field;

        /// <summary>The chain toggle itself.</summary>
        public ButtonToggleInput Chain => _chain;

        /// <summary>Color theme. Propagated to children as-is.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;
                _field.Theme = _theme;
                _chain.Theme = _theme;
                ApplyChainSize();
                ApplyBoxFusion();
                RefreshChain();
            }
        }

        /// <summary>Lower bound per axis. null = unrestricted / length 1 = shared across all axes / length 2 = per axis.</summary>
        [UxmlAttribute]
        public double[] Min
        {
            get => _field.Min;
            set => _field.Min = value;
        }

        /// <summary>Upper bound per axis.</summary>
        [UxmlAttribute]
        public double[] Max
        {
            get => _field.Max;
            set => _field.Max = value;
        }

        /// <summary>Quantization step per axis.</summary>
        [UxmlAttribute]
        public double[] Step
        {
            get => _field.Step;
            set => _field.Step = value;
        }

        /// <summary>Axis labels. Defaults to W / H.</summary>
        [UxmlAttribute]
        public string[] AxisLabels
        {
            get => _field.AxisLabels;
            set => _field.AxisLabels = value;
        }

        /// <summary>Display digits for the numeric field at rest (shared by both axes).</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _field.Precision;
            set => _field.Precision = value;
        }

        /// <summary>Disabled state. Distributed to both the numeric field and the chain toggle.</summary>
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
                _field.Disabled = _disabled;
                _chain.Disabled = _disabled;
            }
        }

        /// <summary>Invalid-value display. Distributed only to the numeric field (the chain toggle has no invalid representation).</summary>
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
        /// The ratio-following baseline is also cut here.
        /// </summary>
        public void PerformFieldConfirm()
        {
            if (_disabled)
            {
                return;
            }

            OnFieldConfirmed(_value);
        }

        /// <summary>Sets the value without firing a ChangeEvent. The editing baseline is also retaken here.</summary>
        public void SetValueWithoutNotify(Vector2 newValue)
        {
            _value = newValue;

            _syncing = true;
            _field.SetValueWithoutNotify(newValue);
            _syncing = false;

            // An externally driven set falls outside an editing session, so the next edit uses the new value as its baseline
            _hasBaseline = false;
        }

        #endregion

        #region Construction

        public SizeInput()
        {
            this.AddToClassList("tweeq-size-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };

            _field = new Vec2Input
            {
                name = "tweeq-size-field",
                Theme = _theme,
                AxisLabels = DEFAULT_AXIS_LABELS,
            };

            _chain = new ButtonToggleInput
            {
                name = "tweeq-size-chain",
                Theme = _theme,
            };
            _chain.SetValueWithoutNotify(_keepRatio);

            // The chain is fixed at 24px. Keep it out of InputGroup's equal-split distribution
            _chain.style.flexGrow = 0f;
            _chain.style.flexShrink = 0f;

            _chainIcon = new VisualElement
            {
                name = "tweeq-size-chain-icon",
                pickingMode = PickingMode.Ignore,
            };
            _chainIcon.style.position = Position.Absolute;
            _chainIcon.style.left = 0f;
            _chainIcon.style.top = 0f;
            _chainIcon.style.right = 0f;
            _chainIcon.style.bottom = 0f;
            _chainIcon.generateVisualContent += OnGenerateChainIcon;
            _chain.hierarchy.Add(_chainIcon);

            _field.ValueChanged += OnFieldChanged;
            _field.Confirmed += OnFieldConfirmed;
            _chain.Confirmed += OnChainConfirmed;

            _group.Add(_field);
            _group.Add(_chain);
            this.hierarchy.Add(_group);

            // Vec2Input isn't an ITweeqInputBox, so InputGroup can't assign corner rounding to its ends.
            // Reapplied on every layout resolution to override after the group redistributes positions
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            ApplyChainSize();
            ApplyBoxFusion();
            RefreshChain();
        }

        #endregion

        #region Internals

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyBoxFusion();
        }

        void ApplyChainSize()
        {
            float size = _theme != null ? _theme.InputHeight : 24f;
            _chain.style.width = size;
            _chain.style.flexBasis = size;
        }

        // Makes [W][H][chain] look like a single connected group. Each setter is a no-op for the same value, so it's fine to call every time
        void ApplyBoxFusion()
        {
            NumberInput x = _field.GetAxis(0);
            if (x != null)
            {
                x.InlinePosition = TweeqBoxPosition.Start;
            }

            NumberInput y = _field.GetAxis(1);
            if (y != null)
            {
                y.InlinePosition = TweeqBoxPosition.Middle;
            }

            _chain.InlinePosition = TweeqBoxPosition.End;
        }

        void OnFieldChanged(Vector2 next)
        {
            if (_syncing)
            {
                return;
            }

            if (!_hasBaseline)
            {
                // This gesture's baseline is "the value before it started moving". Held fixed until the next Confirmed
                _baseline = _value;
                _hasBaseline = true;
            }

            SizeApplyResult result = SizeLogic.Apply(
                _value.x,
                _value.y,
                next.x,
                next.y,
                _baseline.x,
                _baseline.y,
                _keepRatio);

            // Automatic release. Goes through the setter, so the chain's appearance and notification stay in sync here too
            this.KeepRatio = result.KeepRatio;

            Vector2 applied = new Vector2((float)result.X, (float)result.Y);
            if (_value.Equals(applied))
            {
                // Even if the input was cancelled out by ratio-following, realign the field's display to the result
                WriteField(applied);
                return;
            }

            Vector2 previous = _value;
            _value = applied;
            WriteField(applied);
            Notify(previous, applied);
        }

        void WriteField(Vector2 applied)
        {
            if (_field.value.Equals(applied))
            {
                return;
            }

            _syncing = true;
            _field.SetValueWithoutNotify(applied);
            _syncing = false;
        }

        void OnFieldConfirmed(Vector2 fieldValue)
        {
            if (_syncing)
            {
                return;
            }

            // The next gesture uses the new value as its baseline
            _hasBaseline = false;
            Confirmed?.Invoke(_value);
        }

        void OnChainConfirmed(bool next)
        {
            // The toggle side has already flipped its own value. The setter is a no-op for the same value, so this doesn't double-flip
            this.KeepRatio = next;
        }

        void RefreshChain()
        {
            _chainIcon.MarkDirtyRepaint();
        }

        void OnGenerateChainIcon(MeshGenerationContext context)
        {
            if (context == null || _theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Vector2 center = _chainIcon.contentRect.center;

            // The fill color is painted by the toggle side, so choose a color that reads well on top of it
            painter.strokeColor = _keepRatio
                ? TweeqTheme.ContrastText(_theme.Accent)
                : _theme.TextSubtle;
            painter.lineWidth = LINK_STROKE_WIDTH;
            painter.lineCap = LineCap.Butt;

            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 loop = new Vector2(center.x + side * LINK_LOOP_OFFSET, center.y);
                painter.BeginPath();
                painter.Arc(
                    loop,
                    LINK_LOOP_RADIUS,
                    new Angle(0f, AngleUnit.Degree),
                    new Angle(360f, AngleUnit.Degree));
                painter.ClosePath();
                painter.Stroke();
            }

            if (!_keepRatio)
            {
                return;
            }

            // Bridge the two loops only while connected (when broken, the gap itself reads as the "disconnected" symbol)
            painter.lineWidth = LINK_BAR_WIDTH;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - LINK_LOOP_OFFSET, center.y));
            painter.LineTo(new Vector2(center.x + LINK_LOOP_OFFSET, center.y));
            painter.Stroke();
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
