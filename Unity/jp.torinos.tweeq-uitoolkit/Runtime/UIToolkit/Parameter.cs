using UnityEngine;
using UnityEngine.UIElements;

// The class already has a string Label property, so reference the Label type under an alias
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A single "label | input" row (spec §3).
    /// The label width is not decided by itself; it is distributed by the ancestor <see cref="ParameterGrid"/>.
    /// </summary>
    [UxmlElement]
    public partial class Parameter : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>USS class for the label element.</summary>
        public const string LABEL_USS_CLASS_NAME = "tweeq-parameter__label";

        /// <summary>USS class for the input container.</summary>
        public const string INPUT_USS_CLASS_NAME = "tweeq-parameter__input";

        const float LABEL_FONT_SIZE = 12f;

        // MeasureTextSize can return a tight value, so add 1px of margin to prevent clipping
        const float MEASURE_PAD = 1f;

        // Writing back with a difference under 0.5px causes GeometryChangedEvent to keep bouncing back and forth
        const float WIDTH_EPSILON = 0.5f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();
        readonly UILabel _label;
        readonly VisualElement _input;
        string _hint = string.Empty;

        // The width last distributed by the Grid. resolvedStyle isn't updated until the next layout pass,
        // so the "write only when it changed" check is kept here instead
        float _appliedLabelWidth = float.NaN;

        #endregion

        #region Public API

        /// <summary>The label string. Changing it recalculates the Grid's label column width.</summary>
        [UxmlAttribute("label")]
        public string Label
        {
            get => _label.text;
            set
            {
                string text = value ?? string.Empty;
                if (_label.text == text)
                {
                    return;
                }

                _label.text = text;
                ParameterGrid.Find(this)?.RequestRefresh();
            }
        }

        /// <summary>
        /// Tooltip text. Until the Tooltip infrastructure is in place, this just holds the value (spec §3).
        /// Also forwarded to UI Toolkit's standard tooltip.
        /// </summary>
        public string Hint
        {
            get => _hint;
            set
            {
                _hint = value ?? string.Empty;
                this.tooltip = _hint;
            }
        }

        /// <summary>The destination to Add() input controls to.</summary>
        public VisualElement InputContainer => _input;

        /// <summary>
        /// Makes UXML children and plain Add() calls land in the input column (internal construction goes
        /// through hierarchy.Add, so this is safe). Guarded against null since this can be called during the
        /// constructor before _input is created
        /// </summary>
        public override VisualElement contentContainer => _input ?? this;

        /// <summary>Color theme. Normally distributed by the ParameterGrid.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // Don't bail out even for the same instance, so children added after the theme is set still receive it.
                // This setter is the only entry point for redistribution (fix for a gap in the M7 propagation contract)
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                TweeqThemeDistribution.Distribute(_input, _theme);
            }
        }

        /// <summary>Redistributes the gap (gapRelated) inside the input container. Call after adding children.</summary>
        public void RefreshInputGaps()
        {
            TweeqGap.Apply(_input, _theme.RelatedGap, FlexDirection.Row);
        }

        #endregion

        #region Construction

        public Parameter()
        {
            this.AddToClassList("tweeq-parameter");
            this.style.flexDirection = FlexDirection.Row;

            // align-items:start from the original .TqParameterGrid (rows are top-aligned)
            this.style.alignItems = Align.FlexStart;

            _label = new UILabel(string.Empty);
            _label.AddToClassList(LABEL_USS_CLASS_NAME);

            // height = line-height = InputHeight. UI Toolkit has no line-height, so
            // substitute "fixed height + vertical center alignment" instead
            _label.style.unityTextAlign = TextAnchor.MiddleLeft;
            _label.style.whiteSpace = WhiteSpace.NoWrap;
            _label.style.flexShrink = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;
            _label.style.marginLeft = 0f;
            _label.style.paddingLeft = 0f;
            _label.style.paddingRight = 0f;
            _label.style.width = ParameterGrid.MIN_LABEL_WIDTH;
            this.hierarchy.Add(_label);

            _input = new VisualElement();
            _input.AddToClassList(INPUT_USS_CLASS_NAME);
            _input.style.flexGrow = 1f;
            _input.style.flexShrink = 1f;
            _input.style.flexDirection = FlexDirection.Row;
            _input.style.alignItems = Align.Center;

            // min-width:0 from the original .input. Without this the value column won't shrink and the input overflows
            _input.style.minWidth = 0f;
            _input.RegisterCallback<GeometryChangedEvent>(OnInputGeometryChanged);
            this.hierarchy.Add(_input);

            ApplyStaticStyles();

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        public Parameter(string label)
            : this()
        {
            this.Label = label;
        }

        void ApplyStaticStyles()
        {
            _label.style.height = _theme.InputHeight;
            _label.style.fontSize = _theme != null ? _theme.FontSizeLabel : LABEL_FONT_SIZE;
            _label.style.color = _theme.TextMuted;
            TweeqFonts.Apply(_label, _theme.FontUi);

            // Gap between the label column and the value column (original grid-gap = gapControl)
            _label.style.marginRight = _theme.GapControl;

            RefreshInputGaps();
        }

        #endregion

        #region Grid interop

        /// <summary>The label's desired width (measured text size). 0 if there is no text.</summary>
        internal float MeasureLabelWidth()
        {
            string text = _label.text;
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            Vector2 size = _label.MeasureTextSize(
                text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);

            if (float.IsNaN(size.x) || size.x <= 0f)
            {
                // Font not yet resolved (e.g. outside a panel). This row does not request a width
                return 0f;
            }

            return size.x + MEASURE_PAD;
        }

        /// <summary>Applies the shared label width decided by the Grid. Writes nothing if unchanged.</summary>
        internal void ApplyLabelWidth(float width)
        {
            if (!float.IsNaN(_appliedLabelWidth)
                && Mathf.Abs(_appliedLabelWidth - width) <= WIDTH_EPSILON)
            {
                return;
            }

            _appliedLabelWidth = width;
            _label.style.width = width;
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ParameterGrid.Find(this)?.RequestRefresh();
        }

        void OnInputGeometryChanged(GeometryChangedEvent evt)
        {
            // UI Toolkit has no event that notifies of child additions, so
            // redistribute gaps whenever the layout moves.
            // This event bubbles, so changes inside the input field are ignored
            if (evt == null || !ReferenceEquals(evt.target, _input))
            {
                return;
            }

            RefreshInputGaps();
            StretchInputChildren();
        }

        // UXML children come in without flexGrow specified, which collapses input widgets down to their
        // intrinsic width (e.g. 24px). Apply the same safeguard as InputGroup.ApplyStretch: "honor an
        // explicit value; otherwise stretch"
        void StretchInputChildren()
        {
            if (_input == null)
            {
                return;
            }

            for (int i = 0; i < _input.childCount; i++)
            {
                VisualElement child = _input[i];
                if (child != null && child.style.flexGrow.keyword == StyleKeyword.Null)
                {
                    child.style.flexGrow = 1f;
                }
            }
        }

        #endregion
    }
}
