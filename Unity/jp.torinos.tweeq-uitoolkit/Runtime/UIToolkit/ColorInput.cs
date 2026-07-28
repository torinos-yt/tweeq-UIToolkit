using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Using the whole Tweeq.Core namespace would make TweeqRect / TweeqVec2 ambiguous with the
// UnityEngine equivalents (same reason as TweeqPopover), so this file only pulls in the
// two types it actually uses, under aliases.
using HSVA = Tweeq.Core.Hsva;
using CoreRgba = Tweeq.Core.Rgba;
using TweeqColorLogic = Tweeq.Core.TweeqColorLogic;

namespace Tweeq.UIToolkit
{
    /// <summary>The axis currently being dragged inside the picker.</summary>
    public enum ColorPickerAxis
    {
        /// <summary>Not dragging.</summary>
        None,

        /// <summary>SV pad (saturation and value together).</summary>
        SaturationValue,

        /// <summary>Hue bar.</summary>
        Hue,

        /// <summary>Alpha bar.</summary>
        Alpha,
    }

    /// <summary>
    /// What moves when the swatch itself is dragged directly (channel scrub).
    /// Switched via modifier keys (tweakMode in m6-wave2-spec.md §A).
    /// </summary>
    public enum ColorTweakMode
    {
        /// <summary>No key held. Horizontal = saturation, vertical = value.</summary>
        Pad,

        /// <summary>Shift / H / F。</summary>
        Hue,

        /// <summary>S。</summary>
        Saturation,

        /// <summary>V. Vertical drag only.</summary>
        Value,

        /// <summary>R。</summary>
        Red,

        /// <summary>G。</summary>
        Green,

        /// <summary>B。</summary>
        Blue,

        /// <summary>Alt / A。</summary>
        Alpha,
    }

    /// <summary>
    /// Color input (string-color-spec.md "ColorInput").
    /// The field is a single 24x24 swatch; clicking it opens a picker on top of a
    /// <see cref="TweeqPopover"/>, holding an SV pad / hue bar / alpha bar / numeric row / presets.
    ///
    /// The value type is <see cref="UnityEngine.Color"/> (an intentional deviation: the Vue contract
    /// is a CSS string). HSVA is kept as internal state only while the picker is being operated, to
    /// avoid losing hue at black / zero saturation; the output is always folded back down to Color.
    ///
    /// Open/close, drag sessions, presets, and HEX sync are implemented as a panel-independent
    /// logic layer (<see cref="OpenPicker"/> / <see cref="BeginPickerDrag"/> /
    /// <see cref="PerformPresetClick"/> ...), with presentation sitting on top of it. EditMode tests
    /// exercise this layer directly (same design as DropdownInput).
    /// </summary>
    [UxmlElement]
    public partial class ColorInput
        : VisualElement, INotifyValueChanged<Color>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        /// <summary>colorSpace dropdown options. Same order and spelling as Vue's InputColorChannelValues.</summary>
        public const string COLOR_SPACE_RGB = "rgb";

        /// <summary>colorSpace: HSV.</summary>
        public const string COLOR_SPACE_HSV = "hsv";

        /// <summary>colorSpace: HEX.</summary>
        public const string COLOR_SPACE_HEX = "hex";

        // One checkerboard cell (Vue common.styl's background-checkerboard() uses size = 6px).
        const float CHECKER_CELL = 6f;

        // SV gradient texture resolution. Assumes bilinear upscaling, so 64 is plenty
        // (even stretched to a 240px width, HSV is close enough to linear that no banding shows).
        const int SV_TEXTURE_SIZE = 64;

        // The rainbow texture is a single row. One texture is shared across all instances.
        const int HUE_TEXTURE_WIDTH = 256;

        // Height of the hue / alpha bars. Vue's 0.7 * inputHeight = 16.8, rounded to an integer
        // (spec §ColorInput).
        const float SLIDER_HEIGHT = 17f;

        // Width of the colorSpace dropdown in the numeric row. Vue uses 5rem, but on a 240-wide
        // panel that would crush the 4 channels, so this is trimmed to the minimum that still
        // fits "RGB". DropdownInput reserves chevron width (16.8px) symmetrically on both sides,
        // so 56px isn't enough for a 3-character label (RGB/HSV/HEX) and it gets ellipsized.
        const float COLOR_SPACE_WIDTH = 72f;

        const float CURSOR_RADIUS = 6f;
        const float CURSOR_RING_WIDTH = 1.5f;
        const float CURSOR_SHADE_WIDTH = 1f;

        const float FIELD_OUTLINE_WIDTH = 1f;

        // Number of presets per row. 24 + gap6 = 30px * 7 = 210, which fits inside the 222px
        // content width.
        const int PRESETS_PER_ROW = 7;

        // Number of channels in the numeric row (RGB/HSV + A).
        const int CHANNEL_COUNT = 4;

        // Raw values outside this range get wrapped. HSVA's s/v/a are [0,1], h is [0,360).
        const double HUE_RANGE = 360.0;

        // Threshold that separates a swatch click from a channel scrub (same value as RotaryInput).
        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // Sensitivity baseline for scrubbing. Fallback for when Theme.PopupWidth is broken
        // (the 240 from spec §A).
        const float TWEAK_WIDTH_FALLBACK = 240f;

        #endregion

        #region Fields

        /// <summary>colorSpace options (RGB / HSV / HEX).</summary>
        static readonly string[] ColorSpaceOptions = { COLOR_SPACE_RGB, COLOR_SPACE_HSV, COLOR_SPACE_HEX };

        /// <summary>
        /// Default preset palette. The Vue version ships empty on the assumption that the app
        /// injects its own, but so this also works standalone, a "5 neutrals + 9 hues" set is
        /// provided here (swappable via <see cref="Presets"/>).
        /// </summary>
        static readonly Color[] DefaultPresetPalette =
        {
            new Color32(0x00, 0x00, 0x00, 0xFF),
            new Color32(0x40, 0x40, 0x40, 0xFF),
            new Color32(0x80, 0x80, 0x80, 0xFF),
            new Color32(0xC0, 0xC0, 0xC0, 0xFF),
            new Color32(0xFF, 0xFF, 0xFF, 0xFF),
            new Color32(0xFF, 0x00, 0x00, 0xFF),
            new Color32(0xFF, 0x80, 0x00, 0xFF),
            new Color32(0xFF, 0xFF, 0x00, 0xFF),
            new Color32(0x00, 0xFF, 0x00, 0xFF),
            new Color32(0x00, 0xFF, 0xFF, 0xFF),
            new Color32(0x00, 0x80, 0xFF, 0xFF),
            new Color32(0x00, 0x00, 0xFF, 0xFF),
            new Color32(0x80, 0x00, 0xFF, 0xFF),
            new Color32(0xFF, 0x00, 0xFF, 0xFF),
        };

        // The two checkerboard colors. Vue hardcodes white / #ddd (does not follow the theme).
        static readonly Color CheckerLight = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Color CheckerDark = new Color32(0xDD, 0xDD, 0xDD, 0xFF);

        // Cursor outline. Vue's box-shadow 0 0 0 1.5px #fff / inset 0 0 0 1px rgba(0,0,0,.2).
        static readonly Color CursorRing = new Color(1f, 1f, 1f, 1f);
        static readonly Color CursorShade = new Color(0f, 0f, 0f, 0.2f);

        // The rainbow texture depends on nothing but hue, so one instance is reused everywhere.
        static Texture2D SharedHueTexture;

        TweeqTheme _theme = TweeqTheme.Dark();

        Color _value = Color.white;

        // The source of truth while operating the picker. Kept separate from Color so hue isn't
        // lost at black / zero saturation.
        HSVA _hsva;

        Color[] _presets = DefaultPresetPalette;
        string _colorSpace = COLOR_SPACE_HSV;

        bool _disabled;
        bool _hovered;
        bool _focused;
        bool _open;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // The HEX string is only built when the value has actually changed AND the HEX row is
        // visible. If the RGB/HSV row is showing during an SV drag, not a single character is
        // allocated.
        string _hexText = string.Empty;
        bool _hexDirty = true;

        // While a value update is coming from keystrokes typed into the HEX field, writing the
        // normalized form back is suppressed (it would make the caret jump).
        bool _syncingHex;

        // The field
        VisualElement _swatch;

        // The picker (only built once a panel is attached; not rebuilt on every open).
        TweeqPopover _popover;
        VisualElement _picker;
        VisualElement _svPad;
        VisualElement _svCursor;
        VisualElement _hueBar;
        VisualElement _hueCursor;
        VisualElement _alphaBar;
        VisualElement _alphaChecker;
        VisualElement _alphaGradient;
        VisualElement _alphaCursor;
        InputGroup _valuesRow;
        DropdownInput<string> _spaceDropdown;
        readonly NumberInput[] _channels = new NumberInput[CHANNEL_COUNT];
        StringInput _hexField;
        VisualElement _presetsRow;
        readonly List<VisualElement> _presetButtons = new List<VisualElement>();

        // SV gradient. Only re-baked when hue changes; the buffer is reused.
        Texture2D _svTexture;
        Color32[] _svPixels;
        double _svTextureHue = double.NaN;

        ColorPickerAxis _dragAxis = ColorPickerAxis.None;
        int _dragPointerId = PointerId.invalidPointerId;

        // Value at the start of the drag. Where Escape rolls back to.
        Color _valueOnDragStart;

        // Light dismiss also fires on a swatch press (the popover picks it up via TrickleDown on
        // the panel root, so it closes before our own PointerDown runs). Only same-frame reopens
        // are suppressed, turning the interaction into a toggle.
        bool _suppressReopen;
        IVisualElementScheduledItem _reopenGuardItem;

        // Swatch press. Until the threshold is exceeded, this stays a pending click candidate
        // (i.e. a picker toggle).
        bool _swatchPressed;
        int _swatchPointerId = PointerId.invalidPointerId;
        Vector2 _pressPanelPosition;
        float _scrubThreshold = MOUSE_DRAG_THRESHOLD;

        // Open/close state at the moment of the press. Light dismiss runs before PointerDown, so
        // the material for deciding the toggle is locked in at press time rather than carried
        // over to PointerUp.
        bool _openOnPress;

        // Channel scrub. The value is decided by "base HSVA + displacement from the base
        // position." No delta is accumulated, so re-anchoring the base on a mode switch is
        // enough to avoid the value jumping (spec §A).
        bool _scrubbing;
        ColorTweakMode _scrubMode = ColorTweakMode.Pad;
        Vector2 _scrubOrigin;
        Vector2 _scrubAnchor;
        Vector2 _scrubPointer;
        HSVA _scrubBase;
        Color _valueOnScrubStart;
        bool _cursorHidden;
        ColorTweakOverlay _scrubOverlay;

        // Inputs used to derive tweakMode. Modifier keys come from the event's modifiers; letter
        // keys have their held state tracked manually.
        bool _shiftHeld;
        bool _altHeld;
        bool _hueKeyHeld;
        bool _fillKeyHeld;
        bool _satKeyHeld;
        bool _valKeyHeld;
        bool _redKeyHeld;
        bool _greenKeyHeld;
        bool _blueKeyHeld;
        bool _alphaKeyHeld;

        // Kept as reusable instances for registration/deregistration, so a method-group
        // conversion doesn't allocate a new delegate every time.
        readonly EventCallback<PointerDownEvent> _onSvPointerDown;
        readonly EventCallback<PointerMoveEvent> _onSvPointerMove;
        readonly EventCallback<PointerUpEvent> _onSvPointerUp;

        #endregion

        #region Public API

        /// <summary>Fires every time the value changes. During a drag, fires on every pointermove.</summary>
        public event Action<Color> ValueChanged;

        /// <summary>
        /// Fires once per operation: on drag end, preset click, or confirmation of a field inside
        /// the picker.
        /// </summary>
        public event Action<Color> Confirmed;

        /// <summary>The current color. Alpha is part of the value too, so supply it as #RRGGBBAA in UXML.</summary>
        [UxmlAttribute]
        public Color value
        {
            get => _value;
            set
            {
                if (SameColor(_value, value))
                {
                    return;
                }

                Color previous = _value;
                SetValueWithoutNotify(value);
                ValueChanged?.Invoke(_value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>Sets the value without firing ChangeEvent / ValueChanged. Also re-derives HSVA.</summary>
        public void SetValueWithoutNotify(Color newValue)
        {
            _value = newValue;
            _hsva = DeriveHsva(newValue, _hsva);
            _hexDirty = true;
            Refresh();
        }

        /// <summary>Color theme. Falls back to Dark() if null is passed.</summary>
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

        /// <summary>Whether the control is disabled. Closes the picker too if it's open.</summary>
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
                    // If the picker were left open at the moment of disabling, there'd be no way to close it.
                    CancelPickerDrag();
                    CancelChannelScrub();
                    ClosePicker();
                }

                this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;

                if (_swatch != null)
                {
                    _swatch.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
                }

                Refresh();
            }
        }

        /// <summary>Position within a horizontal group.</summary>
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

        /// <summary>A copy of the default preset palette. The initial value of <see cref="Presets"/>.</summary>
        public static Color[] DefaultPresets
        {
            get
            {
                Color[] copy = new Color[DefaultPresetPalette.Length];
                Array.Copy(DefaultPresetPalette, copy, DefaultPresetPalette.Length);
                return copy;
            }
        }

        /// <summary>
        /// The preset palette. Both get and set go through a copy (to decouple the caller's array
        /// from internal state). Passing null / empty removes the presets row.
        /// </summary>
        public Color[] Presets
        {
            get
            {
                Color[] copy = new Color[_presets.Length];
                Array.Copy(_presets, copy, _presets.Length);
                return copy;
            }

            set
            {
                if (value == null)
                {
                    _presets = Array.Empty<Color>();
                }
                else
                {
                    _presets = new Color[value.Length];
                    Array.Copy(value, _presets, value.Length);
                }

                RebuildPresetButtons();
                Refresh();
            }
        }

        /// <summary>Display format of the numeric row (<see cref="COLOR_SPACE_RGB"/> / HSV / HEX).</summary>
        [UxmlAttribute]
        public string ColorSpace
        {
            get => _colorSpace;
            set
            {
                string next = NormalizeColorSpace(value);
                if (_colorSpace == next)
                {
                    return;
                }

                _colorSpace = next;

                if (_spaceDropdown != null)
                {
                    _spaceDropdown.SetValueWithoutNotify(_colorSpace);
                }

                RebuildValuesRow();
                Refresh();
            }
        }

        /// <summary>Whether the picker is open (logical state; works even without a panel).</summary>
        public bool IsPickerOpen => _open;

        /// <summary>The axis currently being dragged.</summary>
        public ColorPickerAxis ActiveAxis => _dragAxis;

        /// <summary>The current HSVA (h in degrees, s/v/a in [0,1]).</summary>
        public HSVA Hsva => _hsva;

        /// <summary>
        /// The HEX notation. 6 digits when alpha=1, 8 digits when alpha&lt;1 (the contract of
        /// <see cref="TweeqColorLogic.FormatHex"/>). Only built when actually read, so no string
        /// is created if the HEX row isn't showing during a drag.
        /// </summary>
        public string HexText
        {
            get
            {
                EnsureHexText();
                return _hexText;
            }
        }

        /// <summary>Opens the picker. Does nothing while disabled.</summary>
        public void OpenPicker()
        {
            if (_open || _disabled)
            {
                return;
            }

            _open = true;
            ShowPicker();
            Refresh();
        }

        /// <summary>Closes the picker. The color is not rolled back (changes while open have already been notified incrementally).</summary>
        public void ClosePicker()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _popover?.Close();
            Refresh();
        }

        /// <summary>Closes it if open, opens it if closed.</summary>
        public void TogglePicker()
        {
            if (_open)
            {
                ClosePicker();
            }
            else
            {
                OpenPicker();
            }
        }

        /// <summary>
        /// Sets HSVA directly. <see cref="ValueChanged"/> fires, but <see cref="Confirmed"/> does not.
        /// h is in degrees (out-of-range values wrap into 0-360); s/v/a are clamped to [0,1].
        /// </summary>
        public void SetHsva(double h, double s, double v, double a)
        {
            ApplyHsva(new HSVA(WrapHue(h), Clamp01(s), Clamp01(v), Clamp01(a)));
        }

        /// <summary>
        /// Begins a drag session. If a different axis was already being held, it switches without confirming that one.
        /// </summary>
        public void BeginPickerDrag(ColorPickerAxis axis)
        {
            if (_disabled || axis == ColorPickerAxis.None)
            {
                return;
            }

            _dragAxis = axis;
            _valueOnDragStart = _value;
            Refresh();
        }

        /// <summary>
        /// Reflects the position while dragging. x / y are normalized coordinates within the target element (0-1, y is 0 at the top).
        /// Meant to be called on every pointermove, so <see cref="ValueChanged"/> fires every time (no throttling, matching Vue).
        /// </summary>
        public void UpdatePickerDrag(float normalizedX, float normalizedY)
        {
            if (_dragAxis == ColorPickerAxis.None || _disabled)
            {
                return;
            }

            double x = Clamp01(normalizedX);
            double y = Clamp01(normalizedY);

            switch (_dragAxis)
            {
                case ColorPickerAxis.SaturationValue:
                    // Vertically, the top is v=1. Same orientation as Vue's pad.
                    ApplyHsva(new HSVA(_hsva.H, x, 1.0 - y, _hsva.A));
                    break;

                case ColorPickerAxis.Hue:
                    ApplyHsva(new HSVA(x * HUE_RANGE, _hsva.S, _hsva.V, _hsva.A));
                    break;

                case ColorPickerAxis.Alpha:
                    ApplyHsva(new HSVA(_hsva.H, _hsva.S, _hsva.V, x));
                    break;
            }
        }

        /// <summary>Ends the drag and fires <see cref="Confirmed"/> exactly once.</summary>
        public void EndPickerDrag()
        {
            if (_dragAxis == ColorPickerAxis.None)
            {
                return;
            }

            _dragAxis = ColorPickerAxis.None;
            Refresh();
            Confirmed?.Invoke(_value);
        }

        /// <summary>Ends the drag by reverting to the value at drag start. <see cref="Confirmed"/> does not fire.</summary>
        public void CancelPickerDrag()
        {
            if (_dragAxis == ColorPickerAxis.None)
            {
                return;
            }

            _dragAxis = ColorPickerAxis.None;

            // The value notified during the drag is being rolled back, so notify the reverted direction too.
            this.value = _valueOnDragStart;
            Refresh();
        }

        /// <summary>Whether a channel scrub is in progress.</summary>
        public bool IsScrubbing => _scrubbing;

        /// <summary>The current tweakMode. Persists as the initial mode for the next grab even outside of scrubbing.</summary>
        public ColorTweakMode ScrubMode => _scrubMode;

        /// <summary>The movement (px, mouse) that separates a click from a scrub.</summary>
        public static float ScrubThreshold => MOUSE_DRAG_THRESHOLD;

        /// <summary>
        /// Begins a channel scrub. <paramref name="panelPosition"/> is both the overlay's origin
        /// and the reference point for movement (in panel coordinates).
        /// Closes the picker if it's open (Vue: open=false once tweaking begins).
        /// </summary>
        public void BeginChannelScrub(Vector2 panelPosition)
        {
            if (_disabled || _scrubbing)
            {
                return;
            }

            _scrubbing = true;
            _scrubOrigin = panelPosition;
            _scrubAnchor = panelPosition;
            _scrubPointer = panelPosition;
            _scrubBase = _hsva;
            _valueOnScrubStart = _value;

            ClosePicker();
            HideCursor();
            AcquireScrubOverlay();
            Refresh();
        }

        /// <summary>
        /// Reflects the pointer position (panel coordinates) during a scrub. The movement from the reference
        /// point maps directly to the value, so <see cref="ValueChanged"/> fires on every move (no throttling).
        /// </summary>
        public void UpdateChannelScrub(Vector2 panelPosition)
        {
            if (!_scrubbing || _disabled)
            {
                return;
            }

            _scrubPointer = panelPosition;
            ApplyScrub();
        }

        /// <summary>
        /// Switches tweakMode. While scrubbing, the current value and position are recaptured as the new
        /// reference, so the value doesn't jump at the moment of switching (another reference implementation
        /// keeps the accumulated delta across the switch, which causes a jump).
        /// </summary>
        public void SetScrubMode(ColorTweakMode mode)
        {
            if (_scrubMode == mode)
            {
                return;
            }

            _scrubMode = mode;

            if (!_scrubbing)
            {
                return;
            }

            _scrubBase = _hsva;
            _scrubAnchor = _scrubPointer;
            Refresh();
        }

        /// <summary>Ends the scrub and fires <see cref="Confirmed"/> exactly once.</summary>
        public void EndChannelScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            StopScrub();
            Refresh();
            Confirmed?.Invoke(_value);
        }

        /// <summary>
        /// Ends the scrub by reverting to the color at scrub start (Escape). <see cref="Confirmed"/> does not fire.
        /// </summary>
        public void CancelChannelScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            Color restored = _valueOnScrubStart;
            StopScrub();

            // The value notified during the scrub is being rolled back, so notify the reverted direction too.
            this.value = restored;
            Refresh();
        }

        /// <summary>
        /// A preset click. Fires <see cref="ValueChanged"/> and <see cref="Confirmed"/> as a pair
        /// (the original has a bug where confirm never fires; this instead follows the contract used by
        /// a fixed-up reference implementation together with test-contracts inputColor.ts).
        /// </summary>
        public void PerformPresetClick(int index)
        {
            if (_disabled || index < 0 || index >= _presets.Length)
            {
                return;
            }

            this.value = _presets[index];
            Confirmed?.Invoke(_value);
        }

        /// <summary>
        /// HEX field input (fired on every validator pass). Only reflects into the value when it can be parsed.
        /// <see cref="Confirmed"/> does not fire (confirmation happens in <see cref="PerformHexConfirm"/>).
        /// </summary>
        public void PerformHexInput(string text)
        {
            if (_disabled || !TryParseHex(text, out Color parsed))
            {
                return;
            }

            // Writing back the canonical form over what was actually typed would make the caret jump mid-edit,
            // so re-sync triggered by reflecting the value is suppressed here. Display is aligned at confirm time (PerformHexConfirm).
            _syncingHex = true;

            try
            {
                this.value = parsed;
            }
            finally
            {
                _syncingHex = false;
            }

            _hexText = text;
            _hexDirty = false;
        }

        /// <summary>Confirms the HEX field (blur / Enter). Aligns the display to canonical form and fires <see cref="Confirmed"/>.</summary>
        public void PerformHexConfirm()
        {
            if (_disabled)
            {
                return;
            }

            _hexDirty = true;
            RefreshHexField(true);
            Confirmed?.Invoke(_value);
        }

        /// <summary>Whether this is valid as a HEX string (can be passed directly as StringInput's Validator).</summary>
        public static bool IsValidHex(string text)
        {
            return TryParseHex(text, out _);
        }

        #endregion

        #region Construction

        public ColorInput()
        {
            this.AddToClassList("tweeq-color-input");

            // The root itself holds focus in order to receive Enter / Space / Escape.
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.flexShrink = 0f;

            _onSvPointerDown = OnPickerPointerDown;
            _onSvPointerMove = OnPickerPointerMove;
            _onSvPointerUp = OnPickerPointerUp;

            BuildField();
            ApplyStaticStyles();

            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<KeyUpEvent>(OnKeyUp);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            _hsva = DeriveHsva(_value, new HSVA(0.0, 0.0, 1.0, 1.0));
            Refresh();
        }

        void BuildField()
        {
            _swatch = new VisualElement { name = "tweeq-color-swatch" };
            _swatch.style.flexShrink = 0f;
            _swatch.style.overflow = Overflow.Hidden;
            _swatch.generateVisualContent += OnGenerateSwatch;
            _swatch.RegisterCallback<PointerDownEvent>(OnSwatchPointerDown);
            _swatch.RegisterCallback<PointerMoveEvent>(OnSwatchPointerMove);
            _swatch.RegisterCallback<PointerUpEvent>(OnSwatchPointerUp);
            _swatch.RegisterCallback<PointerCaptureOutEvent>(OnSwatchPointerCaptureOut);
            _swatch.RegisterCallback<PointerEnterEvent>(OnSwatchPointerEnter);
            _swatch.RegisterCallback<PointerLeaveEvent>(OnSwatchPointerLeave);
            this.hierarchy.Add(_swatch);
        }

        void ApplyStaticStyles()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;

            if (_swatch != null)
            {
                _swatch.style.width = _theme.InputHeight;
                _swatch.style.height = _theme.InputHeight;
            }

            ApplyCornerRadius();
            ApplyPickerStyles();
        }

        // Corner-radius table from spec §1. The corner radius applies to the swatch side (the root is just a box that stretches along the row).
        void ApplyCornerRadius()
        {
            if (_swatch == null)
            {
                return;
            }

            float radius = _theme != null ? _theme.InputRadius : 0f;

            bool topLeft = true;
            bool topRight = true;
            bool bottomLeft = true;
            bool bottomRight = true;

            switch (_inlinePosition)
            {
                case TweeqBoxPosition.Start:
                    topRight = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    bottomLeft = false;
                    break;
            }

            switch (_blockPosition)
            {
                case TweeqBoxPosition.Start:
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    topRight = false;
                    break;
            }

            SetCornerRadius(_swatch, radius, topLeft, topRight, bottomLeft, bottomRight);
        }

        #endregion

        #region Picker construction

        // The picker's actual elements are built exactly once after the panel is attached. Logical state
        // (open/close, drag, presets) can proceed without this, so EditMode tests don't go through here.
        void EnsurePickerElements()
        {
            if (_picker != null || _theme == null)
            {
                return;
            }

            _picker = new VisualElement { name = "tweeq-color-picker" };
            _picker.style.flexDirection = FlexDirection.Column;

            _svPad = new VisualElement { name = "tweeq-color-sv-pad" };
            _svPad.style.overflow = Overflow.Hidden;
            StretchBackground(_svPad);
            _svPad.RegisterCallback(_onSvPointerDown);
            _svPad.RegisterCallback(_onSvPointerMove);
            _svPad.RegisterCallback(_onSvPointerUp);
            _svPad.RegisterCallback<PointerCaptureOutEvent>(OnPickerPointerCaptureOut);
            _svPad.RegisterCallback<GeometryChangedEvent>(OnSvPadGeometryChanged);
            _picker.Add(_svPad);

            _svCursor = CreateOverlay("tweeq-color-sv-cursor");
            _svCursor.generateVisualContent += OnGenerateSvCursor;
            _svPad.Add(_svCursor);

            _hueBar = new VisualElement { name = "tweeq-color-hue-bar" };
            _hueBar.style.overflow = Overflow.Hidden;
            _hueBar.style.backgroundImage = new StyleBackground(GetHueTexture());
            StretchBackground(_hueBar);
            _hueBar.RegisterCallback(_onSvPointerDown);
            _hueBar.RegisterCallback(_onSvPointerMove);
            _hueBar.RegisterCallback(_onSvPointerUp);
            _hueBar.RegisterCallback<PointerCaptureOutEvent>(OnPickerPointerCaptureOut);
            _picker.Add(_hueBar);

            _hueCursor = CreateOverlay("tweeq-color-hue-cursor");
            _hueCursor.generateVisualContent += OnGenerateHueCursor;
            _hueBar.Add(_hueCursor);

            _alphaBar = new VisualElement { name = "tweeq-color-alpha-bar" };
            _alphaBar.style.overflow = Overflow.Hidden;
            _alphaBar.RegisterCallback(_onSvPointerDown);
            _alphaBar.RegisterCallback(_onSvPointerMove);
            _alphaBar.RegisterCallback(_onSvPointerUp);
            _alphaBar.RegisterCallback<PointerCaptureOutEvent>(OnPickerPointerCaptureOut);
            _picker.Add(_alphaBar);

            // The checker, gradient, and cursor are split into separate elements so the hierarchy guarantees
            // draw order (the order between Painter2D and Allocate within a single element isn't guaranteed).
            // This also makes "the checker isn't redrawn when the color changes" hold at the same time.
            _alphaChecker = CreateOverlay("tweeq-color-alpha-checker");
            _alphaChecker.generateVisualContent += OnGenerateAlphaChecker;
            _alphaBar.Add(_alphaChecker);

            _alphaGradient = CreateOverlay("tweeq-color-alpha-gradient");
            _alphaGradient.generateVisualContent += OnGenerateAlphaGradient;
            _alphaBar.Add(_alphaGradient);

            _alphaCursor = CreateOverlay("tweeq-color-alpha-cursor");
            _alphaCursor.generateVisualContent += OnGenerateAlphaCursor;
            _alphaBar.Add(_alphaCursor);

            // InputGroup's default flexGrow 1 is meant for "stretching horizontally within a row."
            // In a vertically-stacked picker this would also stretch the height, so it's dropped here.
            _valuesRow = new InputGroup { Theme = _theme };
            _valuesRow.style.flexGrow = 0f;
            _valuesRow.style.flexShrink = 0f;
            _picker.Add(_valuesRow);

            _spaceDropdown = new DropdownInput<string>(ColorSpaceOptions)
            {
                Theme = _theme,
                Labelizer = ToUpperLabel,
            };
            _spaceDropdown.SetValueWithoutNotify(_colorSpace);
            _spaceDropdown.ValueChanged += OnColorSpaceChanged;

            // InputGroup.ApplyStretch defaults to "flexGrow 1 if unspecified," so set it explicitly here first to keep a fixed width.
            _spaceDropdown.style.flexGrow = 0f;
            _spaceDropdown.style.flexShrink = 0f;
            _spaceDropdown.style.flexBasis = COLOR_SPACE_WIDTH;
            _spaceDropdown.style.width = COLOR_SPACE_WIDTH;

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                _channels[i] = new NumberInput
                {
                    Theme = _theme,
                    Bar = false,
                    Precision = 0,
                    Step = 1.0,
                };
            }

            _channels[0].RegisterValueChangedCallback(OnChannel0Changed);
            _channels[1].RegisterValueChangedCallback(OnChannel1Changed);
            _channels[2].RegisterValueChangedCallback(OnChannel2Changed);
            _channels[3].RegisterValueChangedCallback(OnChannel3Changed);

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                _channels[i].Confirmed += OnChildConfirmed;
            }

            _hexField = new StringInput
            {
                Theme = _theme,
                Validator = IsValidHex,
            };
            _hexField.ValueChanged += OnHexFieldChanged;
            _hexField.Confirmed += OnHexFieldConfirmed;

            _presetsRow = new VisualElement { name = "tweeq-color-presets" };
            _presetsRow.style.flexDirection = FlexDirection.Row;
            _presetsRow.style.flexWrap = Wrap.Wrap;
            _picker.Add(_presetsRow);

            RebuildValuesRow();
            RebuildPresetButtons();
            ApplyPickerStyles();
        }

        // background-size defaults to auto (i.e. native resolution), so small 64x64 / 256x1
        // textures show up as a dot in the center unless explicitly stretched.
        static void StretchBackground(VisualElement element)
        {
            element.style.backgroundSize =
                new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            element.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
        }

        static VisualElement CreateOverlay(string name)
        {
            VisualElement overlay = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore,
            };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            return overlay;
        }

        void ApplyPickerStyles()
        {
            if (_picker == null || _theme == null)
            {
                return;
            }

            // PopupWidth is the outer size. A popover with Chrome=true draws its own PopupPadding,
            // so the content is given the width inside that padding.
            float contentWidth = Mathf.Max(0f, _theme.PopupWidth - _theme.PopupPadding * 2f);
            _picker.style.width = contentWidth;

            float gap = _theme.GapControl;
            float radius = _theme.InputRadius;

            _svPad.style.marginBottom = gap;
            SetCornerRadius(_svPad, radius, true, true, true, true);

            _hueBar.style.height = SLIDER_HEIGHT;
            _hueBar.style.marginBottom = gap;
            SetCornerRadius(_hueBar, radius, true, true, true, true);

            _alphaBar.style.height = SLIDER_HEIGHT;
            _alphaBar.style.marginBottom = gap;
            SetCornerRadius(_alphaBar, radius, true, true, true, true);

            _valuesRow.style.marginBottom = gap;
            _valuesRow.Theme = _theme;

            if (_spaceDropdown != null)
            {
                _spaceDropdown.Theme = _theme;
            }

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                if (_channels[i] != null)
                {
                    _channels[i].Theme = _theme;
                }
            }

            if (_hexField != null)
            {
                _hexField.Theme = _theme;

                // HEX uses a monospace font (FontCode) since jittering digit widths hurt readability.
                // unityFontDefinition is an inherited property, so applying it to StringInput's root reaches the inner TextField too.
                TweeqFonts.Apply(_hexField, _theme.FontCode);
            }

            ApplyPresetStyles();
        }

        // Swaps the contents of the value row per colorSpace. Switching is a human action, so element
        // additions here are never on the drag path.
        void RebuildValuesRow()
        {
            if (_valuesRow == null)
            {
                return;
            }

            // The leading colorSpace dropdown stays put; only the trailing fields are swapped.
            // Calling Clear would detach the dropdown itself and close its currently-open popup
            // (the switch is triggered via the dropdown's ValueChanged, i.e. while it's still open).
            for (int i = _valuesRow.childCount - 1; i >= 1; i--)
            {
                _valuesRow.Remove(_valuesRow.ElementAt(i));
            }

            if (_valuesRow.childCount == 0)
            {
                _valuesRow.Add(_spaceDropdown);
            }

            if (_colorSpace == COLOR_SPACE_HEX)
            {
                _valuesRow.Add(_hexField);
                RefreshHexField(true);
                _valuesRow.RefreshPositions();
                return;
            }

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                NumberInput channel = _channels[i];
                if (channel == null)
                {
                    continue;
                }

                ApplyChannelRange(channel, i);
                _valuesRow.Add(channel);
            }

            _valuesRow.RefreshPositions();
            RefreshChannelFields();
        }

        void ApplyChannelRange(NumberInput channel, int index)
        {
            bool hsv = _colorSpace == COLOR_SPACE_HSV;

            if (index == 3)
            {
                channel.Min = 0.0;
                channel.Max = 100.0;
                channel.Suffix = "%";
                return;
            }

            if (!hsv)
            {
                channel.Min = 0.0;
                channel.Max = 255.0;
                channel.Suffix = string.Empty;
                return;
            }

            if (index == 0)
            {
                channel.Min = 0.0;
                channel.Max = HUE_RANGE;
                channel.Suffix = "°";
                return;
            }

            channel.Min = 0.0;
            channel.Max = 100.0;
            channel.Suffix = "%";
        }

        void RebuildPresetButtons()
        {
            if (_presetsRow == null)
            {
                return;
            }

            for (int i = _presetButtons.Count - 1; i >= _presets.Length; i--)
            {
                _presetsRow.Remove(_presetButtons[i]);
                _presetButtons.RemoveAt(i);
            }

            while (_presetButtons.Count < _presets.Length)
            {
                VisualElement button = new VisualElement { name = "tweeq-color-preset" };
                button.style.overflow = Overflow.Hidden;
                button.generateVisualContent += OnGeneratePreset;

                // Rather than a per-row callback, the pressed element's index is looked up on the parent side (same approach as RadioInput).
                button.RegisterCallback<PointerDownEvent>(OnPresetPointerDown);
                _presetsRow.Add(button);
                _presetButtons.Add(button);
            }

            ApplyPresetStyles();
        }

        void ApplyPresetStyles()
        {
            if (_theme == null || _presetsRow == null)
            {
                return;
            }

            float size = _theme.InputHeight;
            float gap = _theme.RelatedGap;
            int count = _presetButtons.Count;
            int lastRow = count == 0 ? 0 : (count - 1) / PRESETS_PER_ROW;

            for (int i = 0; i < count; i++)
            {
                VisualElement button = _presetButtons[i];
                button.style.width = size;
                button.style.height = size;
                button.style.flexShrink = 0f;

                // There's no gap property, so margins substitute for it. Margins on the row-end/last row are dropped
                // to avoid adding extra whitespace on top of the popup's padding.
                button.style.marginRight = (i + 1) % PRESETS_PER_ROW == 0 ? 0f : gap;
                button.style.marginBottom = i / PRESETS_PER_ROW == lastRow ? 0f : gap;
                SetCornerRadius(button, _theme.InputRadius, true, true, true, true);
                button.MarkDirtyRepaint();
            }

            if (_valuesRow != null)
            {
                _valuesRow.style.marginBottom = count > 0 ? _theme.GapControl : 0f;
            }
        }

        #endregion

        #region Picker presentation

        void ShowPicker()
        {
            if (this.panel == null || _theme == null)
            {
                // With no panel attached there's nowhere to place it. Logical state still advances; no exception is thrown.
                return;
            }

            EnsurePickerElements();

            if (_popover == null)
            {
                // The chrome (surface, border, padding, shadow) is left to the popover side.
                // Closing on Escape / outside click is also left to the popover's LightDismiss (spec §ColorInput).
                _popover = new TweeqPopover
                {
                    Context = this,
                    Theme = _theme,
                    Arrow = false,
                    Chrome = true,
                    Placement = Tweeq.Core.PopoverPlacement.BottomStart,
                };
                _popover.Closed += OnPopoverClosed;
                _popover.Add(_picker);
            }

            _popover.Theme = _theme;
            _popover.Open(_swatch);
        }

        void OnPopoverClosed()
        {
            if (!_open)
            {
                return;
            }

            _open = false;

            // Light dismiss also runs on a swatch press. Only re-opening within the same frame is suppressed.
            _suppressReopen = true;

            if (this.panel != null)
            {
                if (_reopenGuardItem == null)
                {
                    _reopenGuardItem = this.schedule.Execute(ClearReopenGuard);
                }

                _reopenGuardItem.ExecuteLater(0L);
            }
            else
            {
                _suppressReopen = false;
            }

            Refresh();
        }

        void ClearReopenGuard()
        {
            _suppressReopen = false;
        }

        #endregion

        #region Value

        // Update originating from the picker. Rebuilds Color while keeping HSVA authoritative (so hue isn't lost).
        void ApplyHsva(HSVA hsva)
        {
            _hsva = hsva;

            Color next = ToColor(hsva);
            if (SameColor(next, _value))
            {
                RefreshPicker();
                return;
            }

            Color previous = _value;
            _value = next;
            _hexDirty = true;
            Refresh();
            ValueChanged?.Invoke(_value);
            NotifyValueChanged(previous, _value);
        }

        // Hue / saturation can't be defined for black (v=0) or achromatic colors (s=0).
        // Just as Vue's setHSVAChannel fills NaN with the old value, this carries over the previous value.
        static HSVA DeriveHsva(Color color, HSVA previous)
        {
            HSVA next = ToHsva(color);

            if (next.V <= 0.0)
            {
                return new HSVA(previous.H, previous.S, next.V, next.A);
            }

            if (next.S <= 0.0)
            {
                return new HSVA(previous.H, next.S, next.V, next.A);
            }

            return next;
        }

        void NotifyValueChanged(Color previous, Color current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<Color> changeEvent = ChangeEvent<Color>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        void EnsureHexText()
        {
            if (!_hexDirty)
            {
                return;
            }

            _hexText = FormatHex(_value);
            _hexDirty = false;
        }

        #endregion

        #region Field interaction

        // Toggling the picker is deferred until PointerUp. Moving 3px branches into a channel scrub;
        // otherwise it becomes the usual toggle (spec §A).
        void OnSwatchPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled || _swatchPressed)
            {
                return;
            }

            if (this.panel != null)
            {
                this.Focus();
            }

            _swatchPressed = true;
            _swatchPointerId = evt.pointerId;
            _pressPanelPosition = PanelPosition(evt);
            _openOnPress = _open || _suppressReopen;
            _scrubThreshold = evt.pointerType == UnityEngine.UIElements.PointerType.mouse
                ? MOUSE_DRAG_THRESHOLD
                : TOUCH_DRAG_THRESHOLD;

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            // If S or Shift was already held before the press, grab with that mode (Vue's tweakMode is a computed).
            _scrubMode = ResolveScrubMode();

            if (this.panel != null && _swatch != null)
            {
                _swatch.CapturePointer(_swatchPointerId);
            }

            evt.StopPropagation();
        }

        void OnSwatchPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_swatchPressed || evt.pointerId != _swatchPointerId || _disabled)
            {
                return;
            }

            // For modifier keys during a drag, pointermove misses fewer changes than key events do.
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            Vector2 position = PanelPosition(evt);

            if (!_scrubbing)
            {
                if (Vector2.Distance(position, _pressPanelPosition) < _scrubThreshold)
                {
                    return;
                }

                BeginChannelScrub(_pressPanelPosition);
            }

            SetScrubMode(ResolveScrubMode());
            UpdateChannelScrub(position);
            evt.StopPropagation();
        }

        void OnSwatchPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_swatchPressed || evt.pointerId != _swatchPointerId)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            bool wasOpen = _openOnPress;
            int pointerId = _swatchPointerId;

            _swatchPressed = false;
            _swatchPointerId = PointerId.invalidPointerId;

            // Releasing runs PointerCaptureOut. If confirmation already happened there, _scrubbing is already cleared.
            ReleaseSwatchPointer(pointerId);

            if (_scrubbing)
            {
                EndChannelScrub();
            }
            else if (!wasScrubbing)
            {
                // Below the threshold counts as a click. Open/close is decided by the state at the time of the press.
                if (wasOpen)
                {
                    ClosePicker();
                }
                else
                {
                    OpenPicker();
                }
            }

            evt.StopPropagation();
        }

        // Losing the grab means the operation is treated as finished there (rolling back is Escape's job alone).
        // The confirm path is also funneled through here so the cursor and overlay aren't left stranded.
        void OnSwatchPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _swatchPressed = false;
            _swatchPointerId = PointerId.invalidPointerId;

            if (_scrubbing)
            {
                EndChannelScrub();
            }
        }

        void ReleaseSwatchPointer(int pointerId)
        {
            if (this.panel == null || _swatch == null || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (_swatch.HasPointerCapture(pointerId))
            {
                _swatch.ReleasePointer(pointerId);
            }
        }

        // The overlay is drawn in panel coordinates, so the raw, untransformed position is used.
        static Vector2 PanelPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return new Vector2(position.x, position.y);
        }

        void OnSwatchPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnSwatchPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;
            bool modeKey = SetModeKey(evt.keyCode, true);

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    if (!_scrubbing)
                    {
                        TogglePicker();
                        evt.StopPropagation();
                    }

                    return;

                case KeyCode.Escape:
                    // Revert to the start value while an operation is in progress. Otherwise just close (the color isn't rolled back).
                    if (_scrubbing)
                    {
                        CancelChannelScrub();
                        evt.StopPropagation();
                    }
                    else if (_dragAxis != ColorPickerAxis.None)
                    {
                        CancelPickerDrag();
                        evt.StopPropagation();
                    }
                    else if (_open)
                    {
                        ClosePicker();
                        evt.StopPropagation();
                    }

                    return;
            }

            // Shift / Alt alone also changes the mode, so it's recomputed on every key event (spec §A).
            SetScrubMode(ResolveScrubMode());

            if (_scrubbing && modeKey)
            {
                evt.StopPropagation();
            }
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;
            bool modeKey = SetModeKey(evt.keyCode, false);

            SetScrubMode(ResolveScrubMode());

            if (_scrubbing && modeKey)
            {
                evt.StopPropagation();
            }
        }

        // Updates the pressed state and returns whether the key was one relevant to tweakMode.
        bool SetModeKey(KeyCode keyCode, bool held)
        {
            switch (keyCode)
            {
                case KeyCode.H:
                    _hueKeyHeld = held;
                    return true;

                case KeyCode.F:
                    _fillKeyHeld = held;
                    return true;

                case KeyCode.S:
                    _satKeyHeld = held;
                    return true;

                case KeyCode.V:
                    _valKeyHeld = held;
                    return true;

                case KeyCode.R:
                    _redKeyHeld = held;
                    return true;

                case KeyCode.G:
                    _greenKeyHeld = held;
                    return true;

                case KeyCode.B:
                    _blueKeyHeld = held;
                    return true;

                case KeyCode.A:
                    _alphaKeyHeld = held;
                    return true;
            }

            return false;
        }

        void ClearModeKeys()
        {
            _shiftHeld = false;
            _altHeld = false;
            _hueKeyHeld = false;
            _fillKeyHeld = false;
            _satKeyHeld = false;
            _valKeyHeld = false;
            _redKeyHeld = false;
            _greenKeyHeld = false;
            _blueKeyHeld = false;
            _alphaKeyHeld = false;
        }

        // The priority order matches Vue's tweakMode (computed) exactly. On simultaneous presses, the one listed first wins.
        ColorTweakMode ResolveScrubMode()
        {
            if (_shiftHeld || _hueKeyHeld || _fillKeyHeld)
            {
                return ColorTweakMode.Hue;
            }

            if (_satKeyHeld)
            {
                return ColorTweakMode.Saturation;
            }

            if (_valKeyHeld)
            {
                return ColorTweakMode.Value;
            }

            if (_redKeyHeld)
            {
                return ColorTweakMode.Red;
            }

            if (_greenKeyHeld)
            {
                return ColorTweakMode.Green;
            }

            if (_blueKeyHeld)
            {
                return ColorTweakMode.Blue;
            }

            if (_altHeld || _alphaKeyHeld)
            {
                return ColorTweakMode.Alpha;
            }

            return ColorTweakMode.Pad;
        }

        void OnFocusIn(FocusInEvent evt)
        {
            _focused = true;
            Refresh();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            // This also fires just from focus moving to a field inside the picker. Closing is left to the
            // 3 paths of outside click / Escape / detach (same judgment as DropdownInput).
            _focused = false;

            // Once focus is lost, KeyUp no longer arrives. Don't leave a key stuck in the "held" state.
            ClearModeKeys();
            SetScrubMode(ResolveScrubMode());

            Refresh();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // Has the SV texture discarded on detach get rebaked (it's created again on the next Refresh).
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            CancelPickerDrag();

            // Re-parenting is an interruption of the operation, not a cancel or a confirm.
            // That said, the cursor and overlay must always be restored (same judgment as RotaryInput).
            StopScrub();
            ClosePicker();

            _swatchPressed = false;
            _swatchPointerId = PointerId.invalidPointerId;
            ClearModeKeys();

            _hovered = false;
            _focused = false;
            _suppressReopen = false;

            // The SV gradient belongs to the element instance. It isn't kept alive across re-parenting
            // (on reconnection, the hue cache is stale, so it gets rebaked the next time it's opened).
            DestroyTexture(_svTexture);
            _svTexture = null;
            _svTextureHue = double.NaN;
        }

        #endregion

        #region Picker interaction

        void OnSvPadGeometryChanged(GeometryChangedEvent evt)
        {
            if (_svPad == null)
            {
                return;
            }

            // There's no aspect-ratio property, so once the width is settled, the same value is copied to height.
            // Writing it back re-enters this event, so once it has converged, do nothing.
            float width = _svPad.layout.width;
            if (float.IsNaN(width) || width <= 0f || Mathf.Approximately(_svPad.layout.height, width))
            {
                return;
            }

            _svPad.style.height = width;
        }

        void OnPickerPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            VisualElement target = ResolveDragElement(evt.target);
            ColorPickerAxis axis = AxisOf(target);
            if (axis == ColorPickerAxis.None)
            {
                return;
            }

            _dragPointerId = evt.pointerId;
            BeginPickerDrag(axis);
            ApplyPointer(target, evt.position);

            if (this.panel != null)
            {
                target.CapturePointer(_dragPointerId);
            }

            evt.StopPropagation();
        }

        void OnPickerPointerMove(PointerMoveEvent evt)
        {
            if (evt == null
                || _dragAxis == ColorPickerAxis.None
                || evt.pointerId != _dragPointerId)
            {
                return;
            }

            ApplyPointer(ResolveDragElement(evt.currentTarget), evt.position);
            evt.StopPropagation();
        }

        void OnPickerPointerUp(PointerUpEvent evt)
        {
            if (evt == null
                || _dragAxis == ColorPickerAxis.None
                || evt.pointerId != _dragPointerId)
            {
                return;
            }

            VisualElement target = ResolveDragElement(evt.currentTarget);
            if (this.panel != null && target != null && target.HasPointerCapture(_dragPointerId))
            {
                target.ReleasePointer(_dragPointerId);
            }

            _dragPointerId = PointerId.invalidPointerId;
            EndPickerDrag();
            evt.StopPropagation();
        }

        void OnPickerPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragAxis == ColorPickerAxis.None)
            {
                return;
            }

            // Losing the grab means the operation is treated as finished there. Rolling back is Escape's job alone.
            _dragPointerId = PointerId.invalidPointerId;
            EndPickerDrag();
        }

        // currentTarget can never be the cursor overlay (pickingMode=Ignore), but target is resolved by
        // walking up the parent chain so it won't break if pickable children are added in the future.
        VisualElement ResolveDragElement(IEventHandler handler)
        {
            VisualElement element = handler as VisualElement;

            while (element != null)
            {
                if (element == _svPad || element == _hueBar || element == _alphaBar)
                {
                    return element;
                }

                element = element.hierarchy.parent;
            }

            return null;
        }

        ColorPickerAxis AxisOf(VisualElement element)
        {
            if (element == null)
            {
                return ColorPickerAxis.None;
            }

            if (element == _svPad)
            {
                return ColorPickerAxis.SaturationValue;
            }

            if (element == _hueBar)
            {
                return ColorPickerAxis.Hue;
            }

            return element == _alphaBar ? ColorPickerAxis.Alpha : ColorPickerAxis.None;
        }

        void ApplyPointer(VisualElement element, Vector3 worldPosition)
        {
            if (element == null)
            {
                return;
            }

            Rect rect = element.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Vector2 local = element.WorldToLocal(new Vector2(worldPosition.x, worldPosition.y));
            UpdatePickerDrag(local.x / rect.width, local.y / rect.height);
        }

        void OnPresetPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            int index = _presetButtons.IndexOf(evt.currentTarget as VisualElement);
            if (index < 0)
            {
                return;
            }

            PerformPresetClick(index);
            evt.StopPropagation();
        }

        void OnColorSpaceChanged(string space)
        {
            this.ColorSpace = space;
        }

        void OnChannel0Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(0, evt);
        }

        void OnChannel1Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(1, evt);
        }

        void OnChannel2Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(2, evt);
        }

        void OnChannel3Changed(ChangeEvent<float> evt)
        {
            ApplyChannel(3, evt);
        }

        void ApplyChannel(int index, ChangeEvent<float> evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            // Values written back by this code use SetValueWithoutNotify, so they never reach here.
            // Just in case, target is checked here too, to reject secondhand events relayed from a nested field.
            if (!ReferenceEquals(evt.target, _channels[index]))
            {
                return;
            }

            double raw = evt.newValue;

            if (index == 3)
            {
                ApplyHsva(new HSVA(_hsva.H, _hsva.S, _hsva.V, Clamp01(raw / 100.0)));
                return;
            }

            if (_colorSpace == COLOR_SPACE_HSV)
            {
                switch (index)
                {
                    case 0:
                        ApplyHsva(new HSVA(WrapHue(raw), _hsva.S, _hsva.V, _hsva.A));
                        break;

                    case 1:
                        ApplyHsva(new HSVA(_hsva.H, Clamp01(raw / 100.0), _hsva.V, _hsva.A));
                        break;

                    default:
                        ApplyHsva(new HSVA(_hsva.H, _hsva.S, Clamp01(raw / 100.0), _hsva.A));
                        break;
                }

                return;
            }

            // For RGB, the Color side is rewritten first, then HSVA is re-derived from it.
            // To avoid losing hue when it becomes achromatic, this goes through the path that carries over the old HSVA.
            Color next = _value;
            float channel = (float)Clamp01(raw / 255.0);

            switch (index)
            {
                case 0:
                    next.r = channel;
                    break;

                case 1:
                    next.g = channel;
                    break;

                default:
                    next.b = channel;
                    break;
            }

            ApplyHsva(DeriveHsva(next, _hsva));
        }

        // A confirm from a field inside the picker is surfaced directly as ColorInput's own confirm
        // (the composed color is passed, not the argument's channel value).
        void OnChildConfirmed(float channelValue)
        {
            Confirmed?.Invoke(_value);
        }

        void OnHexFieldChanged(string text)
        {
            PerformHexInput(text);
        }

        void OnHexFieldConfirmed(string text)
        {
            PerformHexConfirm();
        }

        #endregion

        #region Channel scrub

        // The value is determined by the base HSVA plus the movement from the base position. Since no
        // accumulated delta is kept, simply recapturing the base on a mode switch keeps the value from jumping (spec §A's recapture).
        void ApplyScrub()
        {
            float width = TweakWidth();

            // Normalized: right is positive, up is positive (spec §A's mapping).
            double dx = (_scrubPointer.x - _scrubAnchor.x) / width;
            double dy = -(_scrubPointer.y - _scrubAnchor.y) / width;

            switch (_scrubMode)
            {
                case ColorTweakMode.Hue:
                    ApplyHsva(new HSVA(
                        WrapHue(_scrubBase.H + dx * HUE_RANGE), _scrubBase.S, _scrubBase.V, _scrubBase.A));
                    break;

                case ColorTweakMode.Saturation:
                    ApplyHsva(new HSVA(
                        _scrubBase.H, Clamp01(_scrubBase.S + dx), _scrubBase.V, _scrubBase.A));
                    break;

                case ColorTweakMode.Value:
                    ApplyHsva(new HSVA(
                        _scrubBase.H, _scrubBase.S, Clamp01(_scrubBase.V + dy), _scrubBase.A));
                    break;

                case ColorTweakMode.Alpha:
                    ApplyHsva(new HSVA(
                        _scrubBase.H, _scrubBase.S, _scrubBase.V, Clamp01(_scrubBase.A + dx)));
                    break;

                case ColorTweakMode.Red:
                case ColorTweakMode.Green:
                case ColorTweakMode.Blue:
                    ApplyRgbScrub(dx);
                    break;

                default:
                    ApplyHsva(new HSVA(
                        _scrubBase.H,
                        Clamp01(_scrubBase.S + dx),
                        Clamp01(_scrubBase.V + dy),
                        _scrubBase.A));
                    break;
            }
        }

        void ApplyRgbScrub(double dx)
        {
            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(_scrubBase);

            switch (_scrubMode)
            {
                case ColorTweakMode.Red:
                    rgba.R = Clamp01(rgba.R + dx);
                    break;

                case ColorTweakMode.Green:
                    rgba.G = Clamp01(rgba.G + dx);
                    break;

                default:
                    rgba.B = Clamp01(rgba.B + dx);
                    break;
            }

            Color next = new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);

            // To avoid losing hue even when falling into achromatic/black, this goes through the path that carries over the current HSVA.
            ApplyHsva(DeriveHsva(next, _hsva));
        }

        void StopScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;
            RestoreCursor();
            ReleaseScrubOverlay();
        }

        // The sensitivity baseline is tweakWidth = PopupWidth = 240 (spec §A).
        float TweakWidth()
        {
            float width = _theme != null ? _theme.PopupWidth : TWEAK_WIDTH_FALLBACK;
            return width > 0f && !float.IsNaN(width) ? width : TWEAK_WIDTH_FALLBACK;
        }

        void HideCursor()
        {
            // No panel means execution of only the logical layer, e.g. an EditMode test. The OS cursor is left untouched.
            if (_cursorHidden || this.panel == null)
            {
                return;
            }

            _cursorHidden = true;
            UnityEngine.Cursor.visible = false;
        }

        void RestoreCursor()
        {
            if (!_cursorHidden)
            {
                return;
            }

            _cursorHidden = false;
            UnityEngine.Cursor.visible = true;
        }

        void AcquireScrubOverlay()
        {
            if (_scrubOverlay != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // Give up on the guide if no panel is attached (the operation itself still proceeds).
                return;
            }

            _scrubOverlay = new ColorTweakOverlay();
            layer.Add(_scrubOverlay);
        }

        void ReleaseScrubOverlay()
        {
            if (_scrubOverlay == null)
            {
                return;
            }

            _scrubOverlay.RemoveFromHierarchy();
            _scrubOverlay = null;
        }

        void UpdateScrubOverlay()
        {
            if (_scrubOverlay == null)
            {
                return;
            }

            if (!_scrubbing || _theme == null)
            {
                ReleaseScrubOverlay();
                return;
            }

            // The SV surface is only needed in pad mode. It's set up so it can bake even if the picker has never been opened.
            Texture2D svTexture = null;
            if (_scrubMode == ColorTweakMode.Pad)
            {
                RebuildSvTextureIfNeeded();
                svTexture = _svTexture;
            }

            ColorTweakOverlayState state = new ColorTweakOverlayState
            {
                Theme = _theme,
                Origin = _scrubOrigin,
                Mode = _scrubMode,
                Hsva = _hsva,
                Value = _value,
                TweakWidth = TweakWidth(),
                SvTexture = svTexture,
            };

            _scrubOverlay.Sync(in state);
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_swatch != null)
            {
                _swatch.MarkDirtyRepaint();
            }

            RefreshPicker();
            UpdateScrubOverlay();
        }

        void RefreshPicker()
        {
            if (_picker == null)
            {
                return;
            }

            // While closed, writing back is invisible, yet string generation for the number fields would still run.
            // Channel scrubbing runs with "a picker that has been opened at least once" left closed, so
            // without stopping here, 4 strings' worth would get allocated on every single move.
            // OpenPicker calls Refresh when opening, so display tracking isn't lost.
            if (!_open)
            {
                return;
            }

            RebuildSvTextureIfNeeded();

            _svCursor?.MarkDirtyRepaint();
            _hueCursor?.MarkDirtyRepaint();
            _alphaGradient?.MarkDirtyRepaint();
            _alphaCursor?.MarkDirtyRepaint();

            RefreshChannelFields();
            RefreshHexField(false);
        }

        // Only writes back the fields for the currently displayed space.
        // Not touching rows hidden during a drag directly reduces string generation.
        void RefreshChannelFields()
        {
            if (_colorSpace == COLOR_SPACE_HEX || _channels[0] == null)
            {
                return;
            }

            if (_colorSpace == COLOR_SPACE_HSV)
            {
                _channels[0].SetValueWithoutNotify((float)_hsva.H);
                _channels[1].SetValueWithoutNotify((float)(_hsva.S * 100.0));
                _channels[2].SetValueWithoutNotify((float)(_hsva.V * 100.0));
            }
            else
            {
                _channels[0].SetValueWithoutNotify(_value.r * 255f);
                _channels[1].SetValueWithoutNotify(_value.g * 255f);
                _channels[2].SetValueWithoutNotify(_value.b * 255f);
            }

            _channels[3].SetValueWithoutNotify((float)(_hsva.A * 100.0));
        }

        // When force=false, this only builds when "the HEX row is visible and the value has actually changed."
        void RefreshHexField(bool force)
        {
            if (_hexField == null)
            {
                return;
            }

            if (!force && (_syncingHex || _colorSpace != COLOR_SPACE_HEX || !_hexDirty))
            {
                return;
            }

            EnsureHexText();

            if (_hexField.value != _hexText)
            {
                _hexField.SetValueWithoutNotify(_hexText);
            }
        }

        #endregion

        #region Textures

        void RebuildSvTextureIfNeeded()
        {
            // The bake target is either the picker's SV pad or the scrub overlay.
            // No texture is allocated when neither exists (to keep the panel-independent logical layer clean).
            if (_svPad == null && _scrubOverlay == null)
            {
                return;
            }

            if (_svTexture != null && _svTextureHue == _hsva.H)
            {
                return;
            }

            EnsureSvTexture();

            int size = SV_TEXTURE_SIZE;
            double hue = _hsva.H;
            double denominator = size - 1;

            for (int y = 0; y < size; y++)
            {
                // Texture2D row 0 is the bottom edge; v=0 (black) is placed at the bottom.
                double v = y / denominator;
                int rowOffset = y * size;

                for (int x = 0; x < size; x++)
                {
                    double s = x / denominator;
                    CoreRgba rgba = TweeqColorLogic.HsvaToRgba(new HSVA(hue, s, v, 1.0));

                    _svPixels[rowOffset + x] = new Color32(
                        ToByte(rgba.R), ToByte(rgba.G), ToByte(rgba.B), ToByte(rgba.A));
                }
            }

            _svTexture.SetPixels32(_svPixels);
            _svTexture.Apply(false);
            _svTextureHue = hue;

            if (_svPad != null)
            {
                _svPad.style.backgroundImage = new StyleBackground(_svTexture);
            }
        }

        void EnsureSvTexture()
        {
            if (_svPixels == null)
            {
                _svPixels = new Color32[SV_TEXTURE_SIZE * SV_TEXTURE_SIZE];
            }

            if (_svTexture != null)
            {
                return;
            }

            _svTexture = new Texture2D(SV_TEXTURE_SIZE, SV_TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                name = "tweeq-color-sv",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        static Texture2D GetHueTexture()
        {
            if (SharedHueTexture != null)
            {
                return SharedHueTexture;
            }

            SharedHueTexture = new Texture2D(HUE_TEXTURE_WIDTH, 1, TextureFormat.RGBA32, false)
            {
                name = "tweeq-color-hue",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color32[] pixels = new Color32[HUE_TEXTURE_WIDTH];
            double denominator = HUE_TEXTURE_WIDTH - 1;

            for (int x = 0; x < HUE_TEXTURE_WIDTH; x++)
            {
                CoreRgba rgba = TweeqColorLogic.HsvaToRgba(
                    new HSVA(x / denominator * HUE_RANGE, 1.0, 1.0, 1.0));

                pixels[x] = new Color32(ToByte(rgba.R), ToByte(rgba.G), ToByte(rgba.B), ToByte(rgba.A));
            }

            SharedHueTexture.SetPixels32(pixels);
            SharedHueTexture.Apply(false);
            return SharedHueTexture;
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        #endregion

        #region Painting

        void OnGenerateSwatch(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _swatch == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _swatch.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            PaintCheckerboard(painter, rect.width, rect.height);

            painter.fillColor = _value;
            FillRect(painter, 0f, 0f, rect.width, rect.height);

            // A 1px frame is always drawn so the outline stays readable even for colors that blend into the background.
            // It switches to the accent color during hover / focus / while open (spec §ColorInput).
            painter.strokeColor = _hovered || _focused || _open ? _theme.Accent : _theme.Border;
            painter.lineWidth = FIELD_OUTLINE_WIDTH;

            float inset = FIELD_OUTLINE_WIDTH * 0.5f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(inset, inset));
            painter.LineTo(new Vector2(rect.width - inset, inset));
            painter.LineTo(new Vector2(rect.width - inset, rect.height - inset));
            painter.LineTo(new Vector2(inset, rect.height - inset));
            painter.ClosePath();
            painter.Stroke();
        }

        void OnGeneratePreset(MeshGenerationContext context)
        {
            if (context == null || context.visualElement == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            int index = _presetButtons.IndexOf(context.visualElement);
            if (index < 0 || index >= _presets.Length)
            {
                return;
            }

            Rect rect = context.visualElement.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            PaintCheckerboard(painter, rect.width, rect.height);

            painter.fillColor = _presets[index];
            FillRect(painter, 0f, 0f, rect.width, rect.height);
        }

        void OnGenerateSvCursor(MeshGenerationContext context)
        {
            if (context == null || _svCursor == null)
            {
                return;
            }

            Rect rect = _svCursor.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            // The cursor's center is placed at the value's actual position (full-width linear, same as the input mapping).
            // Folding it inward would make it diverge further from the OS cursor the closer it gets to the edge,
            // so at the edges the ring is allowed to be half-clipped by overflow:Hidden instead (matching how web-style pickers look).
            float x = (float)_hsva.S * rect.width;
            float y = (float)(1.0 - _hsva.V) * rect.height;

            PaintCursor(context.painter2D, new Vector2(x, y), OpaqueValue());
        }

        void OnGenerateHueCursor(MeshGenerationContext context)
        {
            if (context == null || _hueCursor == null)
            {
                return;
            }

            Rect rect = _hueCursor.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            float radius = Mathf.Min(CURSOR_RADIUS, rect.height * 0.5f);
            // The center is the value's actual position (same judgment as the SV cursor; the ring gets clipped at the edges).
            float x = (float)(_hsva.H / HUE_RANGE) * rect.width;

            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(new HSVA(_hsva.H, 1.0, 1.0, 1.0));

            PaintCursor(
                context.painter2D,
                new Vector2(x, rect.height * 0.5f),
                new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A),
                radius);
        }

        void OnGenerateAlphaCursor(MeshGenerationContext context)
        {
            if (context == null || _alphaCursor == null)
            {
                return;
            }

            Rect rect = _alphaCursor.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            float radius = Mathf.Min(CURSOR_RADIUS, rect.height * 0.5f);
            // The center is the value's actual position (same judgment as the SV cursor; the ring gets clipped at the edges).
            float x = (float)_hsva.A * rect.width;

            PaintCursor(context.painter2D, new Vector2(x, rect.height * 0.5f), OpaqueValue(), radius);
        }

        void OnGenerateAlphaChecker(MeshGenerationContext context)
        {
            if (context == null || _alphaChecker == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _alphaChecker.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            PaintCheckerboard(painter, rect.width, rect.height);
        }

        // A transparent-to-opaque gradient. Since inline styles have no gradient support,
        // vertex colors are placed at the 4 corners and interpolated on the GPU (smoother than tiling bands, and no allocation either).
        void OnGenerateAlphaGradient(MeshGenerationContext context)
        {
            if (context == null || _alphaGradient == null)
            {
                return;
            }

            Rect rect = _alphaGradient.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            Color opaque = OpaqueValue();
            Color transparent = opaque;
            transparent.a = 0f;

            MeshWriteData mesh = context.Allocate(4, 6);

            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(0f, 0f, Vertex.nearZ),
                tint = transparent,
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.width, 0f, Vertex.nearZ),
                tint = opaque,
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.width, rect.height, Vertex.nearZ),
                tint = opaque,
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(0f, rect.height, Vertex.nearZ),
                tint = transparent,
            });

            mesh.SetNextIndex(0);
            mesh.SetNextIndex(1);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(3);
        }

        void PaintCursor(Painter2D painter, Vector2 center, Color fill)
        {
            PaintCursor(painter, center, fill, CURSOR_RADIUS);
        }

        // Vue common.styl's circle(): a white outer ring plus a faint dark inner shade. The fill is "the current color."
        void PaintCursor(Painter2D painter, Vector2 center, Color fill, float radius)
        {
            if (painter == null || radius <= 0f)
            {
                return;
            }

            painter.fillColor = fill;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = CursorRing;
            painter.lineWidth = CURSOR_RING_WIDTH;
            painter.BeginPath();
            painter.Arc(
                center,
                radius + CURSOR_RING_WIDTH * 0.5f,
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();

            painter.strokeColor = CursorShade;
            painter.lineWidth = CURSOR_SHADE_WIDTH;
            painter.BeginPath();
            painter.Arc(
                center,
                Mathf.Max(0.5f, radius - CURSOR_SHADE_WIDTH * 0.5f),
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        static void PaintCheckerboard(Painter2D painter, float width, float height)
        {
            painter.fillColor = CheckerLight;
            FillRect(painter, 0f, 0f, width, height);

            painter.fillColor = CheckerDark;

            int columns = Mathf.CeilToInt(width / CHECKER_CELL);
            int rows = Mathf.CeilToInt(height / CHECKER_CELL);

            for (int row = 0; row < rows; row++)
            {
                float y = row * CHECKER_CELL;
                float cellHeight = Mathf.Min(CHECKER_CELL, height - y);

                for (int column = (row & 1) == 0 ? 1 : 0; column < columns; column += 2)
                {
                    float x = column * CHECKER_CELL;
                    FillRect(painter, x, y, Mathf.Min(CHECKER_CELL, width - x), cellHeight);
                }
            }
        }

        static void FillRect(Painter2D painter, float x, float y, float width, float height)
        {
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }

        #endregion

        #region Color logic bridge

        // TweeqColorLogic lives on the Core (noEngineReferences) side and doesn't know about UnityEngine.Color.
        // Conversion calls are confined to these 4 methods, so if Core's signature changes, only these need fixing.
        static Color ToColor(HSVA hsva)
        {
            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(hsva);
            return new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);
        }

        static HSVA ToHsva(Color color)
        {
            return TweeqColorLogic.RgbaToHsva(new CoreRgba(color.r, color.g, color.b, color.a));
        }

        static bool TryParseHex(string text, out Color color)
        {
            if (TweeqColorLogic.TryParseHex(text, out CoreRgba rgba))
            {
                color = new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);
                return true;
            }

            color = Color.clear;
            return false;
        }

        static string FormatHex(Color color)
        {
            return TweeqColorLogic.FormatHex(new CoreRgba(color.r, color.g, color.b, color.a));
        }

        #endregion

        #region Helpers

        // The "color" of the cursor and gradient is the current color with alpha stripped out. If alpha were
        // included too, the cursor would disappear when transparent and its position would become unreadable.
        Color OpaqueValue()
        {
            Color opaque = _value;
            opaque.a = 1f;
            return opaque;
        }

        static string ToUpperLabel(string space)
        {
            return space == null ? string.Empty : space.ToUpperInvariant();
        }

        static string NormalizeColorSpace(string space)
        {
            if (space == COLOR_SPACE_RGB || space == COLOR_SPACE_HSV || space == COLOR_SPACE_HEX)
            {
                return space;
            }

            return COLOR_SPACE_HSV;
        }

        // Color's == is an approximate comparison and misses changes smaller than 1/255. Compare each component exactly instead.
        static bool SameColor(Color a, Color b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static bool IsUsableRect(Rect rect)
        {
            return !float.IsNaN(rect.width)
                && !float.IsNaN(rect.height)
                && rect.width > 0f
                && rect.height > 0f;
        }

        static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0.0;
            }

            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        // C#'s % returns a negative result for negative values, so the sign is normalized here.
        static double WrapHue(double hue)
        {
            if (double.IsNaN(hue) || double.IsInfinity(hue))
            {
                return 0.0;
            }

            double wrapped = hue % HUE_RANGE;
            return wrapped < 0.0 ? wrapped + HUE_RANGE : wrapped;
        }

        static byte ToByte(double value)
        {
            double scaled = Math.Round(Clamp01(value) * 255.0, MidpointRounding.AwayFromZero);
            return (byte)scaled;
        }

        static void SetCornerRadius(
            VisualElement element,
            float radius,
            bool topLeft,
            bool topRight,
            bool bottomLeft,
            bool bottomRight)
        {
            element.style.borderTopLeftRadius = topLeft ? radius : 0f;
            element.style.borderTopRightRadius = topRight ? radius : 0f;
            element.style.borderBottomLeftRadius = bottomLeft ? radius : 0f;
            element.style.borderBottomRightRadius = bottomRight ? radius : 0f;
        }

        #endregion
    }
}
