using System;
using System.Collections.Generic;
using System.Globalization;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>How the scale shown during a scrub is drawn.</summary>
    public enum NumberScaleStyle
    {
        /// <summary>The same row of sensitivity dots as the original tweeq. Default.</summary>
        Dots,

        /// <summary>Shows the "value reached if dragged this far" as a number at each tick's position (fork extension).</summary>
        Values,
    }

    /// <summary>
    /// A number input field. Handles text editing, a range bar, and horizontal-drag scrubbing all in one field.
    /// Internal computation is double; the external API is float, matching UI Toolkit's convention.
    /// </summary>
    [UxmlElement]
    public partial class NumberInput
        : VisualElement, INotifyValueChanged<float>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // Enters a scrub via long-press even without reaching the movement threshold (spec §1).
        const long HOLD_DRAG_DELAY_MS = 500;

        // The handle's grab zone. Same width as Vue's .handle:before (-inputHeight/2 on both left/right).
        const float GRAB_ZONE_WIDTH = 24f;

        // The tick grid isn't drawn once its spacing falls below this (spec §5).
        const float MIN_TICK_GAP = 10f;

        // The outermost 1px on each side isn't drawn (equivalent to Vue's mask).
        const float TICK_EDGE_MARGIN = 1f;
        const int MAX_TICKS = 512;

        const float HANDLE_WIDTH_IDLE = 1f;
        const float HANDLE_WIDTH_ACTIVE = 3f;
        const float HANDLE_OPACITY_IDLE = 0.3f;

        const float ARROW_SIZE = 4f;
        const float ARROW_OPACITY_IDLE = 0.3f;

        // The "ideal screen spacing" (px) for each train. The actual spacing gets rounded to somewhere
        // between 1/sqrt(10) and sqrt(10) times this by D-2 rev2's dv quantization.
        const double SCALE_IDEAL_GAP_MIN = 1.0;
        const double SCALE_IDEAL_GAP_MAX = 1000.0;

        // Once the spacing tightens to 10px, ticks would number in the hundreds, but that band doesn't
        // need to be shown since smoothstep(1,2,log10(screenGap)) is 0 there. Anything at or below the threshold is discarded outright.
        // Guaranteeing an actual spacing > 10px also keeps the scan count per train within width/10.
        const float SCALE_MIN_OPACITY = 0.01f;
        const int SCALE_TRAIN_COUNT = 3;
        const double SCALE_PRECISION_CYCLE = 3.0;

        // The phase is value/(baseSpeed*speed) px, so for huge values the index would overflow an int.
        // An overflowing band is off-screen anyway, so that train is discarded outright.
        const double MAX_SCALE_TICK_INDEX = 1e9;

        // feedback-fixes-01.md C-1: ticks are the "value reached" number itself, not a dot.
        const float SCALE_LABEL_FONT_SIZE = 9f;

        // Width is fixed and centered on x (so the center can be aligned without measuring the text width).
        const float SCALE_LABEL_WIDTH = 48f;
        const float SCALE_LABEL_HEIGHT = 11f;

        // The minimum spacing before labels overlap each other. Below this, thin out to every 2nd or every 4th.
        const float SCALE_LABEL_MIN_GAP = 32f;
        const int SCALE_LABEL_MAX_STRIDE = 4;

        // C-1: since all 3 trains become numbers, the pool is "one train's worth x number of trains."
        // As long as the thinning-out is in effect, only around 10 per train are actually used.
        const int SCALE_LABEL_PER_TRAIN_MAX = 16;
        const int SCALE_LABEL_POOL_MAX = SCALE_TRAIN_COUNT * SCALE_LABEL_PER_TRAIN_MAX;

        // Dot diameter. The original's .overlay .scale stroke-width: calc(4px + var(--offset-weight) * -1px).
        // Gets thinner the more you lean into a vertical drag (sensitivity adjustment).
        const float SCALE_DOT_DIAMETER_BASE = 4f;
        const float SCALE_DOT_DIAMETER_WEIGHT = 1f;

        // A circle with 0 diameter can't produce vertices, so a lower bound is enforced.
        const float SCALE_DOT_MIN_RADIUS = 0.5f;

        // Tolerance for discarding a fine train's label when a coarse train lands on the same value at the same x (C-1).
        const double SCALE_LABEL_DEDUPE_EPSILON = 1e-6;

        // Relative tolerance used for the reachability check on the side where Clamp is active (D-2 rev2). Multiplied by dv to become an absolute value.
        // Slack so that a tick exactly at the boundary (v=min / v=max) doesn't disappear due to floating-point error.
        const double SCALE_TICK_RANGE_EPSILON = 1e-6;

        // The top/bottom strips of the scrub zone (spec §5: max((24 - 1em) / 2, 4px)).
        const float STRIP_MIN_HEIGHT = 4f;
        const float FALLBACK_FONT_SIZE = 12f;

        // The grip's hint icon. Guide width assumes an 18px icon drawn at scale 0.8.
        const float ICON_SIZE = 18f;
        const float ICON_SCALE = 0.8f;
        const float GRIP_HINT_OPACITY = 0.5f;
        const float GRIP_HINT_HEAD = 3f;

        const float TEXT_PADDING = 4f;

        // The axis label (spec §5-4). Kept narrower than the 24px grab zone so it doesn't look like it's blocking the whole grip.
        const float LEFT_LABEL_WIDTH = 18f;
        const float LEFT_LABEL_FONT_SIZE = 11f;
        const int LEFT_LABEL_MAX_LENGTH = 2;

        // TextField's inner element (touched to remove the background/border and center-align it).
        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

        #region Fields

        float _value;

        // The raw value while scrubbing/editing. Quantization and snapping are only applied on the output side, never left in here.
        double _local;

        // The most recently composed display string. Also feeds displayPrecision's input (equivalent to Vue's display ref).
        string _display = string.Empty;

        // Memo for ComposeDisplayText. Key = (value's bit pattern, digit count, whether scrubbing).
        string _formatCache;
        double _formatCacheSource;
        int _formatCachePrecision;
        bool _formatCacheTweaking;

        double _min = double.NegativeInfinity;
        double _max = double.PositiveInfinity;
        double _step;
        double _snapStep = 10.0;
        bool _bar = true;
        double _barOrigin;
        bool _clampMin = true;
        bool _clampMax = true;
        int _precision = 4;
        string _prefix = string.Empty;
        string _suffix = string.Empty;
        bool _disabled;
        bool _invalid;
        string _leftLabelText = string.Empty;
        NumberScaleStyle _scaleStyle = NumberScaleStyle.Dots;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        TweeqTheme _theme = TweeqTheme.Dark();

        VisualElement _barFill;
        VisualElement _backLayer;
        TweeqFocusRing _focusRing;
        VisualElement _displayOverlay;
        VisualElement _scaleLabelLayer;
        Label _prefixLabel;
        Label _valueLabel;
        Label _suffixLabel;
        Label _leftLabel;
        TextField _textField;
        VisualElement _textInput;
        TextElement _textElement;

        // Reached-value labels reuse a pool instead of being created every frame (feedback-fixes-01.md A-4 / C-1).
        readonly List<ScaleLabelSlot> _scaleLabels = new List<ScaleLabelSlot>();

        readonly ScaleTrain[] _scaleTrains = new ScaleTrain[SCALE_TRAIN_COUNT];

        // An index of trains sorted by descending gap (= descending opacity). Used for the dedup scan order (C-1).
        readonly int[] _scaleOrder = new int[SCALE_TRAIN_COUNT];

        readonly TweakGesture _gesture = new TweakGesture();

        int _pointerId = PointerId.invalidPointerId;
        bool _pointerDown;
        bool _scrubbing;
        bool _grabbedHandle;
        bool _startedEditing;
        float _dragThreshold = MOUSE_DRAG_THRESHOLD;
        Vector2 _pressPosition;
        Vector2 _previousPosition;
        Vector2 _pointerPosition;
        float _valueOnDragStart;
        float _valueAtFocus;
        IVisualElementScheduledItem _holdItem;

        bool _shiftHeld;
        bool _altHeld;
        bool _snapKeyHeld;

        bool _hovered;
        bool _editing;

        // feedback-fixes-01.md C-2: remembers whether the most recent focus originated from a pointer (same approach as CheckboxInput).
        // If it originated from Tab, enter edit mode; if from a click/drag, leave it to PointerUp's judgment as before.
        bool _focusFromPointer;

        // Set only while text parsing is failing. Cleared once a valid input comes in next.
        bool _parseFailed;

        #endregion

        #region Public API

        /// <summary>Fires on drag end, Enter, or blur. Does not fire on arrow keys.</summary>
        public event Action<float> Confirmed;

        /// <summary>The validated output value.</summary>
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
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>The bar's lower bound. Default -infinity (no bar).</summary>
        [UxmlAttribute]
        public double Min
        {
            get => _min;
            set
            {
                _min = value;
                Refresh();
            }
        }

        /// <summary>The bar's upper bound. Default +infinity (no bar).</summary>
        [UxmlAttribute]
        public double Max
        {
            get => _max;
            set
            {
                _max = value;
                Refresh();
            }
        }

        /// <summary>The quantization step for the committed value, and the increment for arrow keys. Disabled at 0.</summary>
        [UxmlAttribute]
        public double Step
        {
            get => _step;
            set
            {
                _step = value;
                Refresh();
            }
        }

        /// <summary>The Q-snap interval. Doubles as Shift's acceleration multiplier. Default 10.</summary>
        public double SnapStep
        {
            get => _snapStep;
            set
            {
                _snapStep = value;
                Refresh();
            }
        }

        /// <summary>Whether to show the bar. Default true.</summary>
        // The UXML name is bar-visible. Since it's a boolean, "bar" alone wouldn't read as a visibility toggle.
        [UxmlAttribute("bar-visible")]
        public bool Bar
        {
            get => _bar;
            set
            {
                _bar = value;
                Refresh();
            }
        }

        /// <summary>The bar fill's origin point. Default 0.</summary>
        public double BarOrigin
        {
            get => _barOrigin;
            set
            {
                _barOrigin = value;
                Refresh();
            }
        }

        /// <summary>Whether to clamp the value to Min. If false, it can go outside the bar's display range.</summary>
        [UxmlAttribute]
        public bool ClampMin
        {
            get => _clampMin;
            set
            {
                _clampMin = value;
                Refresh();
            }
        }

        /// <summary>Whether to clamp the value to Max.</summary>
        [UxmlAttribute]
        public bool ClampMax
        {
            get => _clampMax;
            set
            {
                _clampMax = value;
                Refresh();
            }
        }

        /// <summary>The maximum decimal digits shown while idle. Default 4.</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _precision;
            set
            {
                _precision = value;
                Refresh();
            }
        }

        /// <summary>String prepended in the unfocused overlay.</summary>
        [UxmlAttribute]
        public string Prefix
        {
            get => _prefix;
            set
            {
                _prefix = value ?? string.Empty;
                Refresh();
            }
        }

        /// <summary>String appended in the unfocused overlay.</summary>
        [UxmlAttribute]
        public string Suffix
        {
            get => _suffix;
            set
            {
                _suffix = value ?? string.Empty;
                Refresh();
            }
        }

        /// <summary>Non-interactive state.</summary>
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
                if (_disabled)
                {
                    // If a drag is still alive at the moment of disabling, there would be no way left to release it.
                    CancelScrub(false);
                    SetEditing(false);
                }

                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>
        /// The scale display shown during a scrub. Defaults to the row of dots faithful to the original; numeric labels are opt-in.
        /// </summary>
        [UxmlAttribute("scale-style")]
        public NumberScaleStyle ScaleStyle
        {
            get => _scaleStyle;
            set
            {
                if (_scaleStyle == value)
                {
                    return;
                }

                _scaleStyle = value;
                Refresh();
            }
        }

        /// <summary>Externally supplied invalid-value display.</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get => _invalid;
            set
            {
                _invalid = value;
                Refresh();
            }
        }

        /// <summary>The color theme. Falls back to Dark() when null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                Refresh();
            }
        }

        /// <summary>
        /// A 1-2 character label always shown at the left edge (e.g. an axis name; spec §5-4).
        /// Equivalent to Vue's leftIcon, so the grip's hint icon is suppressed, but the grab zone itself remains.
        /// </summary>
        public string LeftLabel
        {
            get => _leftLabelText;
            set
            {
                string text = value ?? string.Empty;
                if (text.Length > LEFT_LABEL_MAX_LENGTH)
                {
                    text = text.Substring(0, LEFT_LABEL_MAX_LENGTH);
                }

                if (_leftLabelText == text)
                {
                    return;
                }

                _leftLabelText = text;
                ApplyLeftLabelLayout();
                Refresh();
            }
        }

        /// <summary>Position within a horizontal group. Setting this collapses the corner radius according to the spec §1 table.</summary>
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
                ApplyCornerRadius();
            }
        }

        /// <summary>Position within a vertical group.</summary>
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
                ApplyCornerRadius();
            }
        }

        /// <summary>Sets the value without firing ChangeEvent. The raw value is also synced.</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = newValue;

            // An external set happens outside any drag/edit session, so the accumulator is kept in sync too.
            _local = newValue;
            _parseFailed = false;
            SyncDisplayText(true);
            Refresh();
        }

        #endregion

        #region Construction

        public NumberInput()
        {
            this.AddToClassList("tweeq-number-input");

            // The root itself is made focusable too, in order to receive Q / Shift / Escape during a non-editing drag.
            // Focusing here doesn't enter text-edit mode (spec §6's "DOM focus doesn't move").
            this.focusable = true;
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;

            BuildChildren();
            ApplyStaticStyles();
            ApplyInteractivity();

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);

            // Registered with TrickleDown to intercept arrows / Enter / Escape before TextField does.
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);

            // Arrow keys also fire a NavigationMoveEvent separately from KeyDown, and that one moves
            // focus (feedback-fixes-01.md A-5). With TrickleDown, this can suppress it here first
            // even when the target is the TextElement inside TextField.
            this.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);

            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        void BuildChildren()
        {
            // Only the bar is made a real element. Painter2D can't transition colors, but
            // spec §5 requires a 0.15s transition on the background color, so this achieves it via an inline transition.
            _barFill = new VisualElement
            {
                name = "tweeq-number-bar",
                pickingMode = PickingMode.Ignore,
            };
            _barFill.style.position = Position.Absolute;
            _barFill.style.top = 0f;
            _barFill.style.bottom = 0f;
            _barFill.style.left = Length.Percent(0f);
            _barFill.style.right = Length.Percent(0f);
            this.hierarchy.Add(_barFill);

            _backLayer = new VisualElement
            {
                name = "tweeq-number-back",
                pickingMode = PickingMode.Ignore,
            };
            _backLayer.style.position = Position.Absolute;
            _backLayer.style.left = 0f;
            _backLayer.style.top = 0f;
            _backLayer.style.right = 0f;
            _backLayer.style.bottom = 0f;
            _backLayer.generateVisualContent += OnGenerateBackContent;
            this.hierarchy.Add(_backLayer);

            // Inserted directly above the dots and directly below the value text (the add order becomes the draw order as-is).
            _scaleLabelLayer = new VisualElement
            {
                name = "tweeq-number-scale-labels",
                pickingMode = PickingMode.Ignore,
            };
            _scaleLabelLayer.style.position = Position.Absolute;
            _scaleLabelLayer.style.left = 0f;
            _scaleLabelLayer.style.top = 0f;
            _scaleLabelLayer.style.right = 0f;
            _scaleLabelLayer.style.bottom = 0f;
            _scaleLabelLayer.style.overflow = Overflow.Hidden;
            _scaleLabelLayer.style.display = DisplayStyle.None;
            this.hierarchy.Add(_scaleLabelLayer);

            _textField = new TextField
            {
                name = "tweeq-number-text",

                // Every character needs to reflect into the value (spec §3, "digit/. input updates the value live").
                // isDelayed = true would delay ChangeEvent until Enter/blur, so this is fixed to false,
                // and confirming on Enter is handled by our own KeyDown instead.
                isDelayed = false,
                multiline = false,
            };
            _textField.style.position = Position.Absolute;
            _textField.style.left = 0f;
            _textField.style.top = 0f;
            _textField.style.right = 0f;
            _textField.style.bottom = 0f;
            _textField.style.marginLeft = 0f;
            _textField.style.marginRight = 0f;
            _textField.style.marginTop = 0f;
            _textField.style.marginBottom = 0f;
            _textField.style.display = DisplayStyle.None;
            _textField.pickingMode = PickingMode.Ignore;
            _textField.RegisterValueChangedCallback(OnTextChanged);
            this.hierarchy.Add(_textField);

            _textInput = _textField.Q(TEXT_INPUT_NAME);

            // The character actually gets drawn by the TextElement inside unity-text-input.
            // Vertical squashing (A-6) persists even if only the input side is fixed, so the same setting is applied here too.
            _textElement = _textInput != null ? _textInput.Q<TextElement>() : null;

            _displayOverlay = new VisualElement
            {
                name = "tweeq-number-display",
                pickingMode = PickingMode.Ignore,
            };
            _displayOverlay.style.position = Position.Absolute;
            _displayOverlay.style.left = 0f;
            _displayOverlay.style.top = 0f;
            _displayOverlay.style.right = 0f;
            _displayOverlay.style.bottom = 0f;
            _displayOverlay.style.flexDirection = FlexDirection.Row;
            _displayOverlay.style.alignItems = Align.Center;
            _displayOverlay.style.justifyContent = Justify.Center;
            _displayOverlay.style.overflow = Overflow.Hidden;

            _prefixLabel = CreateOverlayLabel();
            _valueLabel = CreateOverlayLabel();
            _suffixLabel = CreateOverlayLabel();
            _displayOverlay.Add(_prefixLabel);
            _displayOverlay.Add(_valueLabel);
            _displayOverlay.Add(_suffixLabel);
            this.hierarchy.Add(_displayOverlay);

            // Needs to stay visible during editing too, so it's placed on its own independent layer instead of the overlay.
            _leftLabel = new Label(string.Empty) { name = "tweeq-number-left-label" };
            _leftLabel.pickingMode = PickingMode.Ignore;
            _leftLabel.style.position = Position.Absolute;
            _leftLabel.style.left = 0f;
            _leftLabel.style.top = 0f;
            _leftLabel.style.bottom = 0f;
            _leftLabel.style.width = LEFT_LABEL_WIDTH;
            _leftLabel.style.marginLeft = 0f;
            _leftLabel.style.marginRight = 0f;
            _leftLabel.style.marginTop = 0f;
            _leftLabel.style.marginBottom = 0f;
            _leftLabel.style.paddingLeft = 0f;
            _leftLabel.style.paddingRight = 0f;
            _leftLabel.style.fontSize = LEFT_LABEL_FONT_SIZE;
            _leftLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _leftLabel.style.display = DisplayStyle.None;
            this.hierarchy.Add(_leftLabel);

            // The focus ring is drawn using the element's border. Adding a border on the root side would
            // shift the absolutely-positioned children (the bar and handle) 1px inward, so it's split into a separate layer.
            _focusRing = TweeqFocusRing.Attach(this);
            _focusRing.name = "tweeq-number-focus-ring";
        }

        static Label CreateOverlayLabel()
        {
            Label label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        void ApplyStaticStyles()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            ApplyCornerRadius();
            TweeqInputBoxStyles.SetBorderColor(this, _theme.Border);

            // Spec §5: background only, 0.15s / cubic-bezier(0.4,0,0.2,1).
            // UI Toolkit has no identical curve, so EaseInOutCubic is used as an approximation (same judgment as RotaryInput).
            TweeqInputBoxStyles.ApplyBackgroundTransition(this, _theme);

            if (_barFill != null)
            {
                TweeqInputBoxStyles.ApplyBackgroundTransition(_barFill, _theme);
            }

            ApplyLeftLabelLayout();

            // Normalization of height, padding, and caret color was moved into the shared public helper (EXT-03-A).
            TweeqInputBoxStyles.ApplyTextField(_textField, _theme);

            // Number fields are center-aligned. Only the alignment and left/right padding are widget-specific, so they're added here.
            if (_textInput != null)
            {
                _textInput.style.paddingLeft = TEXT_PADDING;
                _textInput.style.paddingRight = TEXT_PADDING;
                _textInput.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            if (_textElement != null)
            {
                _textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            if (_textField != null)
            {
                _textField.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            ApplyFonts();
        }

        // Only where digits are laid out gets FontNumeric (per m7-wave2-spec.md's mapping).
        // Prefix / Suffix are unit words, so they stay on the UI font.
        // This is only called when the theme is applied, so it's never hit during a scrub.
        void ApplyFonts()
        {
            if (_theme == null)
            {
                return;
            }

            FontDefinition numeric = _theme.FontNumeric;

            TweeqFonts.Apply(_valueLabel, numeric);
            TweeqFonts.Apply(_textField, numeric);

            // TextField's contents form a hierarchy that explicitly sets its own fontSize,
            // so rather than relying on inheritance alone, the same setting is pushed down to input / TextElement too.
            TweeqFonts.Apply(_textInput, numeric);
            TweeqFonts.Apply(_textElement, numeric);

            for (int i = 0; i < _scaleLabels.Count; i++)
            {
                TweeqFonts.Apply(_scaleLabels[i].Element, numeric);
            }
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;

            if (_textField != null)
            {
                _textField.SetEnabled(!_disabled);
            }
        }

        void ApplyCornerRadius()
        {
            TweeqInputBoxStyles.ApplyCornerRadius(this, _theme, _inlinePosition, _blockPosition);

            // The focus ring is a separate layer, so the same corner radius is reapplied to it.
            if (_focusRing != null)
            {
                _focusRing.Apply(_theme, _inlinePosition, _blockPosition);
            }
        }

        // Offsets the text and display overlay from the left by however much space the axis label takes up.
        void ApplyLeftLabelLayout()
        {
            bool hasLabel = HasLeftLabel;
            float inset = hasLabel ? LEFT_LABEL_WIDTH : 0f;

            if (_leftLabel != null)
            {
                _leftLabel.text = _leftLabelText;
                _leftLabel.style.display = hasLabel ? DisplayStyle.Flex : DisplayStyle.None;

                if (_theme != null)
                {
                    _leftLabel.style.color = _theme.TextMuted;
                }
            }

            if (_displayOverlay != null)
            {
                _displayOverlay.style.left = inset;
            }

            if (_textField != null)
            {
                _textField.style.left = inset;
            }
        }

        #endregion

        #region Derived state

        float Width
        {
            get
            {
                Rect rect = this.contentRect;
                return float.IsNaN(rect.width) ? 0f : rect.width;
            }
        }

        float Height
        {
            get
            {
                Rect rect = this.contentRect;
                return float.IsNaN(rect.height) ? 0f : rect.height;
            }
        }

        bool BarVisible
        {
            get
            {
                return _bar
                    && TweeqMath.IsFinite(_min)
                    && TweeqMath.IsFinite(_max)
                    && _max > _min
                    && Width > 0f;
            }
        }

        double ValidMin => _clampMin ? _min : double.NegativeInfinity;

        double ValidMax => _clampMax ? _max : double.PositiveInfinity;

        // Whether the value is inside [min, max]. There's no handle outside that range, so how the grab zone is built changes.
        bool InsideRange => _min <= _value && _value <= _max;

        // feedback-fixes-01.md A-1: always shown during a scrub.
        // The old (Vue-faithful) behavior hid it when step && clampMin && clampMax && a full range were all set,
        // but with a bar, D-2 rev2's handle anchoring turns it into a "value ruler" laid over bar coordinates,
        // so it's meaningful to show even for a fully-ranged field with a step.
        bool ShowTweakScale => true;

        // Dots keep the original's showTweakScale gate as-is (unlike numeric labels, visualizing continuous
        // sensitivity has no meaning for a field whose resting positions are discrete).
        bool ScaleDotsVisible =>
            _scaleStyle == NumberScaleStyle.Dots
            && NumberLogic.ShowScaleDots(_step, _clampMin, _clampMax, _min, _max);

        // Sensitivity from modifier keys. Same formula as TweakGesture's internal keySpeed (used to compute the displayed digit count).
        double KeySpeed => (_altHeld ? 0.1 : 1.0) * (_shiftHeld ? Math.Max(_snapStep, 1.0) : 1.0);

        double CurrentSpeed => KeySpeed * _gesture.Speed;

        // feedback-fixes-01.md D-1: reverts A-2 (which unified bar and no-bar to step/20) and restores the
        // bar case to Vue's original "bar width = range." With speed=1, the handle sticking to the mouse
        // 1:1 is more natural for bar manipulation, and it also meshes with D-2 rev2, which aligns the ticks
        // to bar coordinates at speed=1. The no-range case stays as A-2 (step/20 or 1).
        double ScrubBaseSpeed => NumberLogic.BaseSpeed(BarVisible, _min, _max, Width, _step);

        // feedback-fixes-01.md D-1 addendum: Vue's ranged minSpeed (derived from step's pixel density)
        // pins to 1 for a bar like Opacity where "1 step is 1px or more," which kills the sensitivity
        // adjustment from a vertical drag. The starting 1:1 (maxSpeed=1) is kept, but only the lower bound
        // is allowed to drop as far as 10^-precision, same as the no-range case (an intentional deviation).
        double ScrubMinSpeed =>
            NumberLogic.MinSpeed(false, _min, _max, Width, _step, _precision);

        double ScrubMaxSpeed => NumberLogic.MaxSpeed(BarVisible);

        // "How many values 1 screen px represents" during a scrub. The shared denominator for tick phase, value step, and reached value (D-2 rev2).
        double ScrubValuePerPixel => ScrubBaseSpeed * CurrentSpeed;

        bool ShowInvalid => (_invalid || _parseFailed) && !_scrubbing;

        bool HasLeftLabel => !string.IsNullOrEmpty(_leftLabelText);

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _pointerDown || _disabled)
            {
                return;
            }

            // C-2: set before this.Focus() and TextField's caret placement.
            // Only cleared on FocusOut (i.e. it means "the current focus began from a pointer").
            _focusFromPointer = true;

            Vector2 position = LocalPosition(evt);

            // While editing, only the area above the scrub zone is a drag start point. Elsewhere,
            // it neither captures nor stops the event, leaving it to TextField's caret handling (spec §1).
            if (_editing && !IsInScrubZone(position))
            {
                return;
            }

            _pointerDown = true;
            _scrubbing = false;
            _pointerId = evt.pointerId;
            _dragThreshold = evt.pointerType == UnityEngine.UIElements.PointerType.mouse
                ? MOUSE_DRAG_THRESHOLD
                : TOUCH_DRAG_THRESHOLD;
            _pressPosition = position;
            _previousPosition = position;
            _pointerPosition = position;
            _startedEditing = _editing;
            _valueOnDragStart = _value;
            _grabbedHandle = BarVisible && InsideRange && IsInGrabZone(position.x);
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            if (!_editing)
            {
                // Focuses the root in order to receive Q / Shift / Escape.
                // This doesn't enter text-edit mode, so the display stays on the overlay.
                this.Focus();
            }

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);

                // Enters a scrub via long-press even without reaching the movement threshold.
                _holdItem = this.schedule.Execute(OnHoldElapsed).StartingIn(HOLD_DRAG_DELAY_MS);
            }

            evt.StopPropagation();
            Refresh();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            Vector2 position = LocalPosition(evt);
            _pointerPosition = position;

            if (!_pointerDown)
            {
                if (_hovered)
                {
                    // The handle's thickening follows the pointer position, so it's redrawn every time while hovered.
                    _backLayer?.MarkDirtyRepaint();
                }

                return;
            }

            if (evt.pointerId != _pointerId)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            if (!_scrubbing)
            {
                if (Vector2.Distance(position, _pressPosition) < _dragThreshold)
                {
                    return;
                }

                BeginScrub(position);
                evt.StopPropagation();
                return;
            }

            Vector2 delta = position - _previousPosition;
            _previousPosition = position;
            ApplyScrubDelta(delta.x, delta.y);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            bool startedEditing = _startedEditing;
            int pointerId = _pointerId;

            ResetDragState();
            ReleasePointerSafely(pointerId);

            if (wasScrubbing)
            {
                EndScrub(startedEditing);
            }
            else if (!startedEditing)
            {
                // Released below the threshold means a click. This is where text-edit mode is first entered (spec §1).
                BeginEditing();
            }

            evt.StopPropagation();
            Refresh();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_pointerDown && !_scrubbing)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            bool startedEditing = _startedEditing;
            ResetDragState();

            if (wasScrubbing)
            {
                EndScrub(startedEditing);
            }

            Refresh();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;

            if (evt != null)
            {
                _pointerPosition = LocalPosition(evt);
            }

            Refresh();
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // A width change also changes both baseSpeed and the tick spacing.
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            ResetDragState();
        }

        void OnHoldElapsed()
        {
            if (!_pointerDown || _scrubbing || _disabled)
            {
                return;
            }

            BeginScrub(_pointerPosition);
        }

        #endregion

        #region Scrub session

        void BeginScrub(Vector2 position)
        {
            _scrubbing = true;
            _previousPosition = position;
            _valueOnDragStart = _value;
            _local = _value;
            _gesture.Reset();
            StopHoldTimer();

            // Jumps immediately to the pressed position only when unfocused, inside the range, and not on the handle (spec §1).
            // Fit folds t into [0,1] before lerping, so the result is always within [min,max].
            // There's no need to apply D-3's clamp separately.
            if (!_startedEditing && BarVisible && InsideRange && !_grabbedHandle)
            {
                _local = Fit(_pressPosition.x, 0.0, Width, _min, _max);
            }

            ApplyOutput();
            Refresh();
        }

        void ApplyScrubDelta(double dx, double dy)
        {
            GestureModifiers modifiers = new GestureModifiers(_altHeld, _shiftHeld, _snapKeyHeld);

            // feedback-fixes-01.md D-1: with a bar it's (max-min)/width; without a range it's step/20.
            // The width is read every frame on the Scrub*Speed side (to track layout changes).
            double baseSpeed = ScrubBaseSpeed;
            double minSpeed = ScrubMinSpeed;
            double maxSpeed = ScrubMaxSpeed;

            GestureUpdate update = _gesture.Update(
                dx, dy, baseSpeed, modifiers, _snapStep, minSpeed, maxSpeed);

            _local += update.Delta;

            // feedback-fixes-01.md D-3: on the side where Clamp is active, the raw value itself is folded
            // during a drag regardless of whether there's a bar (Vue leaves local unclamped, but seeing
            // an out-of-range number is a recipe for mistakes). The side where Clamp is inactive is left
            // as-is, so it can overshoot and the out-of-range arrow shows as-is.
            if (_clampMin && TweeqMath.IsFinite(_min))
            {
                _local = Math.Max(_local, _min);
            }

            if (_clampMax && TweeqMath.IsFinite(_max))
            {
                _local = Math.Min(_local, _max);
            }

            ApplyOutput();
        }

        void EndScrub(bool startedEditing)
        {
            _local = _value;
            SyncDisplayText(true);
            Confirmed?.Invoke(_value);

            if (startedEditing)
            {
                // Returns to text-edit mode. Re-selects the text so retyping directly replaces it.
                ScheduleSelectAll();
            }
        }

        void CancelScrub(bool notify)
        {
            if (!_pointerDown && !_scrubbing)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            float restored = _valueOnDragStart;
            int pointerId = _pointerId;

            ResetDragState();
            ReleasePointerSafely(pointerId);

            if (!wasScrubbing)
            {
                return;
            }

            // The value notified during the drag is being rolled back, so notify this too.
            _local = restored;
            if (notify)
            {
                this.value = restored;
            }
            else
            {
                SetValueWithoutNotify(restored);
            }

            SyncDisplayText(true);
            Refresh();
        }

        void ResetDragState()
        {
            _pointerDown = false;
            _scrubbing = false;
            _grabbedHandle = false;
            _pointerId = PointerId.invalidPointerId;
            StopHoldTimer();
        }

        void StopHoldTimer()
        {
            if (_holdItem == null)
            {
                return;
            }

            _holdItem.Pause();
            _holdItem = null;
        }

        void ReleasePointerSafely(int pointerId)
        {
            if (this.panel == null || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }
        }

        #endregion

        #region Value

        // Runs the raw value through clamp -> step -> snap and reflects the result into the output (spec §2: every frame, not just on commit).
        void ApplyOutput()
        {
            NumberValidation result = NumberValidator.Validate(
                _local, ValidMin, ValidMax, _step, _snapStep, _snapKeyHeld && _scrubbing);

            float next = (float)result.Value;
            if (next == _value)
            {
                Refresh();
                return;
            }

            float previous = _value;
            _value = next;
            Refresh();
            NotifyValueChanged(previous, next);
        }

        void NotifyValueChanged(float previous, float current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<float> changeEvent = ChangeEvent<float>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Text editing

        void BeginEditing()
        {
            if (_disabled || _editing)
            {
                return;
            }

            _valueAtFocus = _value;
            SetEditing(true);
            SyncDisplayText(true);

            if (_textField != null)
            {
                _textField.Focus();
                ScheduleSelectAll();
            }
        }

        void SetEditing(bool editing)
        {
            if (_editing == editing)
            {
                return;
            }

            _editing = editing;

            if (_textField != null)
            {
                // Focus() doesn't take effect while display:none, so the display is always switched first.
                _textField.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
                _textField.pickingMode = editing ? PickingMode.Position : PickingMode.Ignore;
            }

            if (_displayOverlay != null)
            {
                _displayOverlay.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (editing)
            {
                _valueAtFocus = _value;
            }
            else
            {
                _parseFailed = false;
            }

            Refresh();
        }

        void ScheduleSelectAll()
        {
            if (_textField == null || this.panel == null)
            {
                return;
            }

            // The selection range gets overwritten unless this waits until the frame after focus is settled (equivalent to Vue's nextTick).
            this.schedule.Execute(() =>
            {
                if (_textField != null && _editing)
                {
                    _textField.SelectAll();
                }
            }).StartingIn(0);
        }

        void OnTextChanged(ChangeEvent<string> evt)
        {
            if (evt == null || !_editing || _scrubbing)
            {
                return;
            }

            _display = evt.newValue ?? string.Empty;
            ParseDisplay(_display);
            ApplyOutput();
        }

        // Expression-input mode is out of scope (spec §7-2). Only plain numeric parsing is performed.
        void ParseDisplay(string text)
        {
            if (double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed)
                && TweeqMath.IsFinite(parsed))
            {
                _local = parsed;
                _parseFailed = false;
                return;
            }

            // On parse failure, the value is left as-is and shown as invalid until a valid input comes in next.
            _parseFailed = true;
        }

        // Confirms on Enter / blur. Rebuilds the display from the output value and fires Confirmed.
        void Commit()
        {
            if (_editing && _textField != null)
            {
                // Should already be reflected on every keystroke, but Enter/blur always forces a commit in case anything slipped through.
                ParseDisplay(_textField.value);
                ApplyOutput();
            }

            _local = _value;
            _parseFailed = false;
            SyncDisplayText(true);
            Confirmed?.Invoke(_value);
        }

        void RestoreEditing()
        {
            _local = _valueAtFocus;
            _parseFailed = false;
            this.value = _valueAtFocus;
            SyncDisplayText(true);
            ScheduleSelectAll();
        }

        void OnFocusIn(FocusInEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (IsTextTarget(evt.target))
            {
                SetEditing(true);
                SyncDisplayText(true);
                return;
            }

            // feedback-fixes-01.md C-2: when focus reaches the root via Tab, enter the same editing state as a click.
            if (!ReferenceEquals(evt.target, this))
            {
                return;
            }

            ScheduleEnterEditingFromFocus();
        }

        // Whether it originated from a pointer isn't settled until "after this frame's PointerDown finishes
        // processing" (so this doesn't depend on which comes first, the panel's focus move or our own handler).
        // schedule runs only after all of this frame's event processing is done, so the check happens there (C-2).
        void ScheduleEnterEditingFromFocus()
        {
            if (this.panel == null || _disabled || _editing)
            {
                return;
            }

            this.schedule.Execute(() =>
            {
                if (_focusFromPointer || _pointerDown || _scrubbing || _editing || _disabled)
                {
                    return;
                }

                // If Tab was pressed again within this one tick, don't steal focus back.
                if (this.focusController == null || !ReferenceEquals(this.focusController.focusedElement, this))
                {
                    return;
                }

                // Same entry point as the click path (OnPointerUp). SelectAll is also scheduled from within this.
                BeginEditing();
            }).StartingIn(0);
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // C-2: once focus leaves, the pointer-origin flag is also cleared.
            // The next FocusIn is re-evaluated as "the start of a new focus session."
            _focusFromPointer = false;

            if (!IsTextTarget(evt.target))
            {
                // If the root's own focus was lost, only release the modifier keys' held state.
                _snapKeyHeld = false;
                _shiftHeld = false;
                _altHeld = false;
                Refresh();
                return;
            }

            _snapKeyHeld = false;
            _shiftHeld = false;
            _altHeld = false;
            Commit();
            SetEditing(false);
        }

        bool IsTextTarget(IEventHandler target)
        {
            if (_textField == null)
            {
                return false;
            }

            VisualElement element = target as VisualElement;
            return element != null && _textField.Contains(element);
        }

        #endregion

        #region Keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            switch (evt.keyCode)
            {
                case KeyCode.Q:
                    _snapKeyHeld = true;

                    if (_scrubbing)
                    {
                        // Toggling snap reflects into the output immediately (the raw value isn't touched).
                        ApplyOutput();
                        evt.StopPropagation();
                    }

                    break;

                case KeyCode.UpArrow:
                    Increment(1);
                    evt.StopPropagation();
                    break;

                case KeyCode.DownArrow:
                    Increment(-1);
                    evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Commit();
                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    if (_pointerDown || _scrubbing)
                    {
                        CancelScrub(true);
                        evt.StopPropagation();
                    }
                    else if (_editing)
                    {
                        RestoreEditing();
                        evt.StopPropagation();
                    }

                    break;
            }

            if (_scrubbing)
            {
                Refresh();
            }
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            if (evt.keyCode == KeyCode.Q)
            {
                _snapKeyHeld = false;

                if (_scrubbing)
                {
                    ApplyOutput();
                    evt.StopPropagation();
                }
            }

            if (_scrubbing)
            {
                Refresh();
            }
        }

        // feedback-fixes-01.md A-5: Up/Down should only change the value. UI Toolkit also fires
        // NavigationMoveEvent for arrow keys, so stopping propagation on KeyDown alone still lets focus move.
        // Next/Previous (Tab) is let through in order to preserve spec §3's "Tab -> blur -> confirm."
        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            bool blocked;
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                    blocked = true;
                    break;

                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
                    // During editing, Left/Right are handled by TextField as caret movement.
                    // When not editing, focus is only on the root, so this stops it here rather than letting it through.
                    blocked = !_editing;
                    break;

                default:
                    blocked = false;
                    break;
            }

            if (!blocked)
            {
                return;
            }

            evt.StopPropagation();

            // In Unity 6, this is what actually stops "the focus move itself" (PreventDefault is deprecated).
            this.focusController?.IgnoreEvent(evt);
        }

        // Spec §3. Confirmed does not fire.
        void Increment(int direction)
        {
            if (_disabled)
            {
                return;
            }

            _local = NumberLogic.ArrowIncrement(
                _local, direction, _step, _snapStep, _shiftHeld, _altHeld, ValidMin, ValidMax);

            ApplyOutput();
            _local = _value;
            SyncDisplayText(true);
        }

        #endregion

        #region Display

        // When force=false, the text being edited isn't disturbed (same condition as Vue's watcher).
        void SyncDisplayText(bool force)
        {
            if (_editing && !_scrubbing && !force)
            {
                return;
            }

            string text = ComposeDisplayText();
            _display = text;

            if (_valueLabel != null)
            {
                _valueLabel.text = text;
            }

            if (_textField != null && _textField.value != text)
            {
                _textField.SetValueWithoutNotify(text);
            }
        }

        string ComposeDisplayText()
        {
            int precision = NumberLogic.GetDisplayPrecision(
                _step, _display ?? string.Empty, _min, _max, Width,
                BarVisible, _scrubbing, CurrentSpeed, _precision);

            // Only the display during a drag uses the raw value (trailing zeros preserved). The digit count itself becomes sensitivity feedback.
            double source = _scrubbing ? _local : _value;

            // Format is a pure function: the same input always gives the same result. Refresh still runs
            // even on frames where the pointer isn't moving, so as long as the key matches, string generation is skipped entirely.
            // _display also gets rewritten by text input, so the cache is kept in a separate field.
            if (_formatCache != null
                && _formatCachePrecision == precision
                && _formatCacheTweaking == _scrubbing
                && TweeqFormat.SameValueBits(_formatCacheSource, source))
            {
                return _formatCache;
            }

            _formatCache = TweeqFormat.Format(source, precision, _scrubbing);
            _formatCacheSource = source;
            _formatCachePrecision = precision;
            _formatCacheTweaking = _scrubbing;
            return _formatCache;
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            SyncDisplayText(false);
            UpdateBackground();
            UpdateBar();
            UpdateOverlayLabels();
            UpdateTextColor();

            // Labels are elements, so they're placed here rather than in a draw callback (generateVisualContent).
            // Mixing layout-touching work into repaint would create a re-layout loop.
            UpdateScaleLabels();

            if (_focusRing != null)
            {
                _focusRing.Visible = _editing && !_disabled;
            }

            _backLayer?.MarkDirtyRepaint();
        }

        void UpdateBackground()
        {
            TweeqInputBoxStyles.ApplyDisabledChrome(this, _theme, _disabled);

            if (_disabled)
            {
                return;
            }

            this.style.backgroundColor = TweeqInputBoxStyles.ResolveBackground(_theme, _hovered);
        }

        void UpdateBar()
        {
            if (_barFill == null)
            {
                return;
            }

            if (!BarVisible)
            {
                // Hidden while keeping the layout intact (spec §5).
                _barFill.style.visibility = Visibility.Hidden;
                return;
            }

            _barFill.style.visibility = Visibility.Visible;

            // The fill is determined by the validated output value (the raw value is not used).
            double originT = Clamp01(Invlerp(_min, _max, _barOrigin));
            double valueT = Clamp01(Invlerp(_min, _max, _value));
            double left = Math.Min(originT, valueT);
            double right = 1.0 - Math.Max(originT, valueT);

            _barFill.style.left = Length.Percent((float)(left * 100.0));
            _barFill.style.right = Length.Percent((float)(right * 100.0));

            Color fill = _disabled
                ? _theme.Input
                : _hovered ? _theme.AccentSoftHover : _theme.AccentSoft;
            _barFill.style.backgroundColor = fill;
        }

        void UpdateOverlayLabels()
        {
            if (_prefixLabel == null || _valueLabel == null || _suffixLabel == null)
            {
                return;
            }

            _prefixLabel.text = _prefix;
            _suffixLabel.text = _suffix;
            _prefixLabel.style.display = string.IsNullOrEmpty(_prefix)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            _suffixLabel.style.display = string.IsNullOrEmpty(_suffix)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            _prefixLabel.style.color = _theme.TextMuted;
            _suffixLabel.style.color = _theme.TextMuted;
            _valueLabel.style.color = ShowInvalid ? _theme.Error : _theme.Text;
        }

        void UpdateTextColor()
        {
            Color color = ShowInvalid ? _theme.Error : _theme.Text;

            if (_textField != null)
            {
                _textField.style.color = color;
            }

            if (_textInput != null)
            {
                _textInput.style.color = color;
            }
        }

        #endregion

        #region Hit testing

        // A 24px width centered on the handle. Near the edges it's shifted so the full 24px still fits within the field (Vue's zoneStyle).
        bool IsInGrabZone(float x)
        {
            float width = Width;
            if (!BarVisible || width < GRAB_ZONE_WIDTH)
            {
                return false;
            }

            float t = (float)Clamp01(Invlerp(_min, _max, _value));
            float left = Mathf.Clamp(
                (width - 1f) * t - GRAB_ZONE_WIDTH * 0.5f,
                0f,
                width - GRAB_ZONE_WIDTH);
            return x >= left && x <= left + GRAB_ZONE_WIDTH;
        }

        // The top/bottom strips (the center is left open for text selection).
        bool IsInStrip(float y)
        {
            float height = Height;
            if (height <= 0f)
            {
                return false;
            }

            float stripHeight = Mathf.Max((height - FontSize()) * 0.5f, STRIP_MIN_HEIGHT);
            return y <= stripHeight || y >= height - stripHeight;
        }

        float FontSize()
        {
            float size = this.resolvedStyle.fontSize;
            return float.IsNaN(size) || size <= 0f ? FALLBACK_FONT_SIZE : size;
        }

        // Spec §1/§5: the area where a drag can be started while editing.
        bool IsInScrubZone(Vector2 position)
        {
            if (_disabled)
            {
                return false;
            }

            if (!BarVisible)
            {
                // Unranged is a 24x24 grip at the left edge.
                return position.x <= GRAB_ZONE_WIDTH;
            }

            if (!InsideRange)
            {
                // There's no handle out of range, so the full-width strip becomes the grab zone.
                return IsInStrip(position.y);
            }

            // A handle stuck at the edge would get eaten by the corner radius, so instead of top/bottom, this uses one full-height zone.
            bool handleAtEdge = _value <= _min || _value >= _max;
            if (!IsInGrabZone(position.x))
            {
                return false;
            }

            return handleAtEdge || IsInStrip(position.y);
        }

        // Converts from panel coordinates to local so the coordinate system doesn't drift during capture either.
        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return this.WorldToLocal(new Vector2(position.x, position.y));
        }

        #endregion

        #region Painting

        void OnGenerateBackContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _backLayer == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _backLayer.contentRect;
            float width = rect.width;
            float height = rect.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }

            // Same stacking order as the original (bar -> step ticks -> scale -> handle).
            // When ScaleStyle=Values, the scale is an element (numeric labels), so it doesn't show up here.
            PaintTicks(painter, width, height);
            PaintScaleDots(painter, width, height);
            PaintHandle(painter, width, height);
            PaintOutOfRangeArrows(painter, width, height);

            // The hint isn't overlaid while the axis label occupies the left edge (same treatment as Vue's leftIcon).
            if (!BarVisible && _editing && !HasLeftLabel)
            {
                PaintGripHint(painter, height);
            }
        }

        void PaintTicks(Painter2D painter, float width, float height)
        {
            if (!BarVisible || _step <= 0.0)
            {
                return;
            }

            double range = _max - _min;
            double gap = _step / range * width;
            if (!TweeqMath.IsFinite(gap) || gap < MIN_TICK_GAP)
            {
                return;
            }

            painter.strokeColor = _theme.BorderSubtle;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();

            int count = 0;
            for (double x = 0.0; x < width && count < MAX_TICKS; x += gap, count++)
            {
                if (x < TICK_EDGE_MARGIN || x > width - TICK_EDGE_MARGIN)
                {
                    continue;
                }

                // Shifting a 1px-wide line's center by half a pixel makes it cover [x, x+1].
                float px = (float)x + 0.5f;
                painter.MoveTo(new Vector2(px, 0f));
                painter.LineTo(new Vector2(px, height));
            }

            painter.Stroke();
        }

        // The original's <line stroke-dasharray="0 gap" stroke-linecap="round"> is "a row of round dots."
        // Painter2D has no dasharray, so circles are placed directly instead (looks the same).
        void PaintScaleDots(Painter2D painter, float width, float height)
        {
            if (!_scrubbing || _disabled || !ScaleDotsVisible)
            {
                return;
            }

            // The phase is laid on the same "validated value" as the handle. Using the raw value would make
            // the handle and dots appear half a step off from each other on a stepped field.
            double phase = NumberLogic.ScaleDotPhase(
                BarVisible, _value, _min, _max, width, ScrubValuePerPixel);
            if (!TweeqMath.IsFinite(phase))
            {
                return;
            }

            double gestureSpeed = _gesture.Speed;
            float weight = ScaleOffsetWeight;
            float radius = Mathf.Max(
                (SCALE_DOT_DIAMETER_BASE - weight * SCALE_DOT_DIAMETER_WEIGHT) * 0.5f,
                SCALE_DOT_MIN_RADIUS);

            Color baseColor = ScaleColor;
            float centerY = height * 0.5f;

            for (int offset = 0; offset < SCALE_TRAIN_COUNT; offset++)
            {
                NumberLogic.ScaleDotLayer layer;
                if (!NumberLogic.TryBuildScaleDotLayer(
                        gestureSpeed, offset, phase, width, SCALE_MIN_OPACITY, out layer))
                {
                    continue;
                }

                Color color = baseColor;
                color.a *= (float)layer.Opacity;

                painter.fillColor = color;
                painter.BeginPath();

                for (int i = 0; i < layer.Count; i++)
                {
                    float x = (float)layer.DotX(i);
                    if (x < 0f || x > width)
                    {
                        continue;
                    }

                    // Arc draws a line from the current position to the arc's start point, so without a
                    // MoveTo reopening a new subpath for each dot, the circles would end up connected to each other.
                    painter.MoveTo(new Vector2(x + radius, centerY));
                    painter.Arc(
                        new Vector2(x, centerY),
                        radius,
                        Angle.Degrees(0f),
                        Angle.Degrees(360f));
                }

                painter.Fill();
            }
        }

        void PaintHandle(Painter2D painter, float width, float height)
        {
            if (!BarVisible)
            {
                return;
            }

            // The position is the validated output value. Out-of-range values aren't clamped; overflow clips them instead.
            float x = (width - 1f) * (float)Invlerp(_min, _max, _value);

            bool thick = _scrubbing || (_hovered && InsideRange && IsInGrabZone(_pointerPosition.x));
            float handleWidth = thick ? HANDLE_WIDTH_ACTIVE : HANDLE_WIDTH_IDLE;

            // The 3px version expands toward the center (Vue's margin-left: -1px).
            float left = thick ? x - 1f : x;

            Color color = _theme.Accent;
            color.a *= _hovered || _scrubbing ? 1f : HANDLE_OPACITY_IDLE;

            painter.fillColor = color;
            FillRect(painter, left, 0f, handleWidth, height);
        }

        void PaintOutOfRangeArrows(Painter2D painter, float width, float height)
        {
            if (!BarVisible || InsideRange)
            {
                return;
            }

            Color color = _theme.Accent;
            color.a *= _scrubbing ? 1f : ARROW_OPACITY_IDLE;
            painter.fillColor = color;

            float centerY = height * 0.5f;
            painter.BeginPath();

            // As in Vue's CSS (border-right/left triangle), the apex points outward, meaning "the value is further this way."
            // The first version of the spec saying "inward" was a mistake.
            if (_value < _min)
            {
                painter.MoveTo(new Vector2(ARROW_SIZE, centerY - ARROW_SIZE));
                painter.LineTo(new Vector2(ARROW_SIZE, centerY + ARROW_SIZE));
                painter.LineTo(new Vector2(0f, centerY));
            }
            else
            {
                painter.MoveTo(new Vector2(width - ARROW_SIZE, centerY - ARROW_SIZE));
                painter.LineTo(new Vector2(width - ARROW_SIZE, centerY + ARROW_SIZE));
                painter.LineTo(new Vector2(width, centerY));
            }

            painter.ClosePath();
            painter.Fill();
        }

        #endregion

        #region Scale trains

        /// <summary>One frame's worth of tick trains. Assembled together since all 3 trains share the same origin.</summary>
        struct ScaleTrain
        {
            // The "value" step per tick. D-2 rev2 quantizes this to a power of 10, so a coarse train's
            // ValueGap is always an integer multiple of a fine train's (this is what makes dedup work).
            public double ValueGap;

            // The on-screen spacing (px) = ValueGap / valuePerPixel.
            public double ScreenGap;

            // The x where the tick for value 0 lands.
            public double OriginX;

            public float Opacity;

            // The ordinal of the first tick at or after the screen's left edge (x=0). The value-0 tick is k=0.
            public int FirstIndex;

            public ScaleTrain(
                double valueGap, double screenGap, double originX, float opacity, int firstIndex)
            {
                ValueGap = valueGap;
                ScreenGap = screenGap;
                OriginX = originX;
                Opacity = opacity;
                FirstIndex = firstIndex;
            }
        }

        /// <summary>
        /// The most recent state for one pooled label. Re-setting text / color triggers layout and
        /// vertex rebuilding, so this exists to write back only when something actually changed (C-1's load mitigation).
        /// TickValue / Digits are numeric keys used to short-circuit before a string comparison. As long as
        /// these match, Format itself is never called, so a string is only generated when dv moves during a sensitivity crossfade.
        /// </summary>
        struct ScaleLabelSlot
        {
            public Label Element;
            public string Text;
            public double TickValue;
            public int Digits;
            public Color Color;
            public bool Visible;
        }

        // During a vertical drag (sensitivity adjustment), the color leans toward TextSubtle (spec §5).
        float ScaleOffsetWeight => (float)TweeqMath.Clamp(_gesture.HorizontalWeight, 0.0, 1.0);

        Color ScaleColor => Color.Lerp(_theme.Accent, _theme.TextSubtle, ScaleOffsetWeight);

        // Packs only the active trains at the front of _scaleTrains and returns their count.
        int BuildScaleTrains(float width)
        {
            if (_theme == null || width <= 0f)
            {
                return 0;
            }

            double gestureSpeed = _gesture.Speed;
            if (!TweeqMath.IsFinite(gestureSpeed) || gestureSpeed <= 0.0)
            {
                return 0;
            }

            // The phase is also divided by baseSpeed (dividing by gestureSpeed alone would make ticks not scroll 1:1 with mouse movement).
            double valuePerPixel = ScrubValuePerPixel;
            if (!TweeqMath.IsFinite(valuePerPixel) || valuePerPixel <= 0.0)
            {
                return 0;
            }

            // feedback-fixes-01.md D-2 rev2: with a bar, the handle position is the anchor.
            // x(v) = anchorX + (v - local)/vpp, so at speed=1 (vpp=(max-min)/width),
            // x(v) = (v-min)/(max-min)*width, matching bar coordinates exactly.
            // Without a range it's still center-anchored as before. The displayed _value jumps due to step
            // quantization, so the raw value _local is used instead.
            double anchorX = BarVisible
                ? Clamp01(Invlerp(_min, _max, _local)) * width
                : width * 0.5;

            double originX = anchorX - _local / valuePerPixel;
            if (!TweeqMath.IsFinite(originX))
            {
                return 0;
            }

            int count = 0;
            for (int offset = 0; offset < SCALE_TRAIN_COUNT; offset++)
            {
                double precision = TweeqMath.UnsignedMod(
                    -Math.Log10(gestureSpeed) + offset, SCALE_PRECISION_CYCLE);

                double idealGapPx = TweeqMath.Clamp(
                    Math.Pow(10.0, precision), SCALE_IDEAL_GAP_MIN, SCALE_IDEAL_GAP_MAX);
                if (!TweeqMath.IsFinite(idealGapPx) || idealGapPx <= 0.0)
                {
                    continue;
                }

                // D-2 rev2: quantizes the value step to a power of 10. This makes each label the exact
                // value k*dv, so awkward numbers like a 0.348 step no longer line up (screen spacing ends up 1/sqrt(10) to sqrt(10) times the ideal).
                double logValueGap = Math.Log10(idealGapPx * valuePerPixel);
                if (!TweeqMath.IsFinite(logValueGap))
                {
                    continue;
                }

                double valueGap = Math.Pow(10.0, Math.Round(logValueGap));
                if (!TweeqMath.IsFinite(valueGap) || valueGap <= 0.0)
                {
                    continue;
                }

                double screenGap = valueGap / valuePerPixel;
                if (!TweeqMath.IsFinite(screenGap) || screenGap <= 0.0)
                {
                    continue;
                }

                // D-2 rev2: opacity is decided from "the spacing actually visible on screen," not from precision.
                // Quantization shifts the spacing away from the ideal, so deriving it from precision would make opacity and density disagree.
                float opacity = Mathf.Sqrt(
                    (float)TweeqMath.Smoothstep(1.0, 2.0, Math.Log10(screenGap)));
                if (opacity < SCALE_MIN_OPACITY)
                {
                    continue;
                }

                double firstIndex = Math.Ceiling(-originX / screenGap);
                if (!TweeqMath.IsFinite(firstIndex) || Math.Abs(firstIndex) > MAX_SCALE_TICK_INDEX)
                {
                    continue;
                }

                _scaleTrains[count] = new ScaleTrain(
                    valueGap, screenGap, originX, opacity, (int)firstIndex);
                count++;
            }

            return count;
        }

        // feedback-fixes-01.md C-1 / D-2 rev2: sorts trains by descending ValueGap into _scaleOrder.
        // Since all trains share the same valuePerPixel, descending ValueGap equals descending ScreenGap,
        // and since opacity = sqrt(smoothstep(1,2,log10(screenGap))) is monotonically increasing, that's also descending opacity.
        // This ordering alone is what makes "keep the coarser = more opaque one" hold during dedup.
        void SortScaleTrainsByValueGap(int trainCount)
        {
            for (int i = 0; i < trainCount; i++)
            {
                _scaleOrder[i] = i;
            }

            // At most 3 elements, so insertion sort is enough (runs every frame, so no allocation is created).
            for (int i = 1; i < trainCount; i++)
            {
                int current = _scaleOrder[i];
                int j = i - 1;

                while (j >= 0 && _scaleTrains[_scaleOrder[j]].ValueGap < _scaleTrains[current].ValueGap)
                {
                    _scaleOrder[j + 1] = _scaleOrder[j];
                    j--;
                }

                _scaleOrder[j + 1] = current;
            }
        }

        // C-1 / D-2 rev2: a coarse train's ticks are a subset of a fine train's ticks, so the same value
        // would show up twice at the same x. The check is done on the tick's value itself, not a px offset
        // (since dv is quantized to a power of 10, a coarse train's dv is always an integer multiple of a
        // fine train's dv, i.e. decimally nested). All trains share the origin, so a matching value is directly a matching x too.
        bool IsCoveredByCoarserTrain(int orderIndex, double tickValue)
        {
            for (int i = 0; i < orderIndex; i++)
            {
                double valueGap = _scaleTrains[_scaleOrder[i]].ValueGap;
                if (valueGap <= 0.0)
                {
                    continue;
                }

                double quotient = tickValue / valueGap;
                if (Math.Abs(quotient - Math.Round(quotient)) < SCALE_LABEL_DEDUPE_EPSILON)
                {
                    return true;
                }
            }

            return false;
        }

        // Thins labels out to every 1st / 2nd / 4th so they don't overlap. Gives up if even every 4th isn't enough (A-4).
        static int LabelStride(double gap)
        {
            for (int stride = 1; stride <= SCALE_LABEL_MAX_STRIDE; stride *= 2)
            {
                if (gap * stride >= SCALE_LABEL_MIN_GAP)
                {
                    return stride;
                }
            }

            return 0;
        }

        // feedback-fixes-01.md C-1 / D-2 rev2: shows "the value reached if dragged this far" at each tick's position.
        // All 3 trains become numbers, and a train's opacity directly becomes the number's fade.
        void UpdateScaleLabels()
        {
            if (_scaleLabelLayer == null)
            {
                return;
            }

            // Nothing outside Values places any numbers at all. Everything below this is the reached-value label implementation unchanged.
            if (_scaleStyle != NumberScaleStyle.Values)
            {
                HideScaleLabelsFrom(0);
                return;
            }

            if (!_scrubbing || !ShowTweakScale || _disabled)
            {
                HideScaleLabelsFrom(0);
                return;
            }

            float width = Width;
            float height = Height;
            if (width <= 0f || height <= 0f)
            {
                HideScaleLabelsFrom(0);
                return;
            }

            int trainCount = BuildScaleTrains(width);
            if (trainCount <= 0)
            {
                HideScaleLabelsFrom(0);
                return;
            }

            SortScaleTrainsByValueGap(trainCount);

            Color baseColor = ScaleColor;

            // D-2 rev2: out-of-range on the side where Clamp is active gets folded along with the internal
            // value by D-3, i.e. it's unreachable, so it isn't drawn.
            bool clipMin = _clampMin && TweeqMath.IsFinite(_min);
            bool clipMax = _clampMax && TweeqMath.IsFinite(_max);

            // C-1: since the numbers now serve as the primary tick mark in place of dots, they're placed at vertical center rather than an annotation position.
            float top = Mathf.Max((height - SCALE_LABEL_HEIGHT) * 0.5f, 0f);

            int used = 0;
            for (int order = 0; order < trainCount && used < SCALE_LABEL_POOL_MAX; order++)
            {
                ScaleTrain train = _scaleTrains[_scaleOrder[order]];

                // C-1: a train that's too faint doesn't show its label at all (BuildScaleTrains already
                // filters this out too, but it's made explicit here as a threshold on the label side as well).
                if (train.Opacity < SCALE_MIN_OPACITY)
                {
                    continue;
                }

                int stride = LabelStride(train.ScreenGap);
                if (stride <= 0)
                {
                    continue;
                }

                // dv is a power of 10, so displaying with that many digits makes the shown value exactly the tick's true value (D-2 rev2).
                int digits = TweeqMath.PrecisionOf(train.ValueGap);
                double rangeEpsilon = train.ValueGap * SCALE_TICK_RANGE_EPSILON;

                Color color = baseColor;

                // The train's opacity is multiplied straight into alpha. Since swinging sensitivity swaps
                // which train is active, this alone produces the numbers' crossfade (C-1).
                color.a *= train.Opacity;

                int placed = 0;
                for (int k = train.FirstIndex;
                     placed < SCALE_LABEL_PER_TRAIN_MAX && used < SCALE_LABEL_POOL_MAX;
                     k++)
                {
                    double x = train.OriginX + k * train.ScreenGap;
                    if (x > width)
                    {
                        break;
                    }

                    // The value at tick k. This is exactly the inverse mapping of x(v) = OriginX + (v - local)/vpp,
                    // so dragging this far actually reaches v_k (preserved regardless of speed).
                    double tickValue = k * train.ValueGap;

                    // Unreachable ticks line up in the direction of increasing value, so it's fine to break out on the upper-bound side.
                    if (clipMax && tickValue > _max + rangeEpsilon)
                    {
                        break;
                    }

                    if (clipMin && tickValue < _min - rangeEpsilon)
                    {
                        continue;
                    }

                    if (UnsignedMod(k, stride) != 0)
                    {
                        continue;
                    }

                    if (IsCoveredByCoarserTrain(order, tickValue))
                    {
                        continue;
                    }

                    ApplyScaleLabel(
                        used,
                        tickValue,
                        digits,
                        color,
                        (float)x - SCALE_LABEL_WIDTH * 0.5f,
                        top);

                    used++;
                    placed++;
                }
            }

            HideScaleLabelsFrom(used);
            _scaleLabelLayer.style.display = used > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Position moves every frame, so it's written directly. Only text and color are compared against the previous value and left alone if unchanged (C-1).
        void ApplyScaleLabel(int index, double tickValue, int digits, Color color, float left, float top)
        {
            ScaleLabelSlot slot = GetScaleLabel(index);
            Label element = slot.Element;
            if (element == null)
            {
                return;
            }

            // Short-circuits on a numeric key before building a string. Ticks are stable at power-of-10
            // steps for both value and digit count, so most frames during a drag exit right here.
            if (slot.Digits != digits || !TweeqFormat.SameValueBits(slot.TickValue, tickValue))
            {
                string text = TweeqFormat.Format(tickValue, digits, false);

                // Even with a different digit count, trimming can still produce the same string, so writing back to element is still compared as before.
                if (!string.Equals(slot.Text, text, StringComparison.Ordinal))
                {
                    element.text = text;
                    slot.Text = text;
                }

                slot.TickValue = tickValue;
                slot.Digits = digits;
            }

            if (slot.Color != color)
            {
                element.style.color = color;
                slot.Color = color;
            }

            element.style.left = left;
            element.style.top = top;

            if (!slot.Visible)
            {
                element.style.display = DisplayStyle.Flex;
                slot.Visible = true;
            }

            _scaleLabels[index] = slot;
        }

        ScaleLabelSlot GetScaleLabel(int index)
        {
            while (_scaleLabels.Count <= index)
            {
                Label created = new Label(string.Empty)
                {
                    name = "tweeq-number-scale-label",
                    pickingMode = PickingMode.Ignore,
                };
                created.style.position = Position.Absolute;
                created.style.width = SCALE_LABEL_WIDTH;
                created.style.height = SCALE_LABEL_HEIGHT;
                created.style.marginLeft = 0f;
                created.style.marginRight = 0f;
                created.style.marginTop = 0f;
                created.style.marginBottom = 0f;
                created.style.paddingLeft = 0f;
                created.style.paddingRight = 0f;
                created.style.paddingTop = 0f;
                created.style.paddingBottom = 0f;
                created.style.fontSize = SCALE_LABEL_FONT_SIZE;
                created.style.unityTextAlign = TextAnchor.MiddleCenter;
                created.style.whiteSpace = WhiteSpace.NoWrap;
                created.style.display = DisplayStyle.None;

                // The pool only ever grows on first use, so applying the font here doesn't become a recurring cost during a scrub.
                if (_theme != null)
                {
                    TweeqFonts.Apply(created, _theme.FontNumeric);
                }

                _scaleLabelLayer.Add(created);

                // Leaving Text as null and Digits as -1 (a value PrecisionOf never returns) guarantees
                // Format and text= both run exactly once, on the first pass.
                _scaleLabels.Add(new ScaleLabelSlot
                {
                    Element = created,
                    Digits = -1,
                    Visible = false,
                });
            }

            return _scaleLabels[index];
        }

        void HideScaleLabelsFrom(int index)
        {
            if (index <= 0 && _scaleLabelLayer != null)
            {
                _scaleLabelLayer.style.display = DisplayStyle.None;
            }

            for (int i = index; i < _scaleLabels.Count; i++)
            {
                ScaleLabelSlot slot = _scaleLabels[i];
                if (!slot.Visible)
                {
                    continue;
                }

                if (slot.Element != null)
                {
                    slot.Element.style.display = DisplayStyle.None;
                }

                slot.Visible = false;
                _scaleLabels[i] = slot;
            }
        }

        #endregion

        #region Painting (misc)

        // The unranged grip hint (<->). Drawn as a shape to avoid depending on a font.
        void PaintGripHint(Painter2D painter, float height)
        {
            Color color = _theme.TextMuted;
            color.a *= GRIP_HINT_OPACITY;

            float length = ICON_SIZE * ICON_SCALE * 0.7f;
            float centerX = GRAB_ZONE_WIDTH * 0.5f;
            float centerY = height * 0.5f;
            float left = centerX - length * 0.5f;
            float right = centerX + length * 0.5f;

            painter.strokeColor = color;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(new Vector2(left, centerY));
            painter.LineTo(new Vector2(right, centerY));
            painter.MoveTo(new Vector2(left + GRIP_HINT_HEAD, centerY - GRIP_HINT_HEAD));
            painter.LineTo(new Vector2(left, centerY));
            painter.LineTo(new Vector2(left + GRIP_HINT_HEAD, centerY + GRIP_HINT_HEAD));
            painter.MoveTo(new Vector2(right - GRIP_HINT_HEAD, centerY - GRIP_HINT_HEAD));
            painter.LineTo(new Vector2(right, centerY));
            painter.LineTo(new Vector2(right - GRIP_HINT_HEAD, centerY + GRIP_HINT_HEAD));
            painter.Stroke();
        }

        static void FillRect(Painter2D painter, float x, float y, float width, float height)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }

        #endregion

        #region Helpers

        static double Invlerp(double from, double to, double value)
        {
            double range = to - from;
            if (range == 0.0 || !TweeqMath.IsFinite(range))
            {
                return 0.0;
            }

            return (value - from) / range;
        }

        static double Fit(double value, double fromMin, double fromMax, double toMin, double toMax)
        {
            return TweeqMath.Lerp(toMin, toMax, Clamp01(Invlerp(fromMin, fromMax, value)));
        }

        static double Clamp01(double value)
        {
            return TweeqMath.Clamp(value, 0.0, 1.0);
        }

        // Used for judging dot-ordinal thinning. C#'s % returns a negative result for negative values, so the sign is normalized here.
        static int UnsignedMod(int value, int modulo)
        {
            if (modulo <= 0)
            {
                return 0;
            }

            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        #endregion
    }
}
