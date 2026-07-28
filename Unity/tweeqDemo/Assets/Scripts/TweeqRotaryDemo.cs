using System.Collections.Generic;
using System.Globalization;
using Tweeq.UIToolkit;
using TweeqDemo.CustomWidgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace TweeqDemo
{
    /// <summary>
    /// Port of the tweeq index.html demo: one RotaryInput plus three kinds of NumberInput on a
    /// ParameterGrid, with PositionInput / SizeInput inside collapsible groups.
    /// </summary>
    public class TweeqRotaryDemo : MonoBehaviour
    {
        #region Constants

        const float INITIAL_VALUE = 45f;
        const double STEP = 0.1;
        const float GAP = 16f;

        const float COLUMN_WIDTH = 360f;
        const float ROW_LABEL_FONT_SIZE = 11f;

        const double VECTOR_STEP = 0.1;

        // For AngleInput: the +/-180 range belongs to the number field, the knob still spins past it
        const float INITIAL_ANGLE = 30f;
        const double ANGLE_LIMIT = 180.0;
        const double ANGLE_STEP = 0.1;
        const double ANGLE_SNAP = 45.0;

        // 16:9 up front so the ratio lock is obvious at a glance
        static readonly Vector2 INITIAL_SIZE = new Vector2(320f, 180f);

        // feedback-fixes-01.md D-4: both clamps off, so this row can leave [0,1] and raise the arrows
        const float OVERSHOOT_INITIAL = 0.5f;

        // ParameterGroup persists its open state in PlayerPrefs under these names
        const string VECTOR_GROUP_NAME = "tweeqDemo.vector";
        const string BOOLEAN_GROUP_NAME = "tweeqDemo.boolean";
        const string SELECT_GROUP_NAME = "tweeqDemo.select";
        const string TIME_GROUP_NAME = "tweeqDemo.time";
        const string TIMELINE_GROUP_NAME = "tweeqDemo.timeline";
        const string DIALOG_GROUP_NAME = "tweeqDemo.dialog";
        const string CUSTOM_GROUP_NAME = "tweeqDemo.custom";

        // Initial values for the widget built in an external asmdef (EndpointInput)
        const string INITIAL_ENDPOINT = "192.168.0.1";
        const string INITIAL_ENDPOINT_PORT = "10.0.0.8:8080";

        // The Vue PaneModalTabs width:44rem depends on the root font size, which is ambiguous, so
        // 528px is used instead (rem = 12 in design units; m8-modal-tabs-spec.md section E)
        const float DIALOG_WIDTH = 528f;

        // The active tab persists under "tweeq.demo-settings.active"
        const string SETTINGS_TABS_NAME = "demo-settings";

        const string OPACITY_TOOLTIP = "Opacity from 0 to 1. Drag sideways to scrub, up and down for sensitivity";

        const string INITIAL_TEXT = "Hello, tweeq";

        // The ColorInput starts from the theme accent. Alpha is lowered so the swatch checkerboard
        // (the alpha visualisation) reads at a glance
        const float TINT_ALPHA = 0.8f;

        // Initial TimeInput value. It reads 01:30:12 at 24fps and 00:36:12 at 60fps, so switching
        // the rate shows that the value itself stays at 2172 frames and only the display moves
        const float INITIAL_TIME_FRAMES = 2172f;

        const double INITIAL_FRAME_RATE = 24.0;

        // 10 seconds at the initial 24fps, so the timecode ruler shows whole minutes and seconds
        const double TIMELINE_RANGE_END = 240.0;
        const float TIMELINE_HEIGHT = 96f;
        const float RULER_HEIGHT = 16f;

        // In/Out are seeded so "Focus In/Out" has something to jump to on the first click
        const double TIMELINE_IN = 24.0;
        const double TIMELINE_OUT = 120.0;

        // The library default of 60px/frame only shows 6 frames in this 360px column, so the demo
        // opens the zoom range far enough to fit all 240 frames and start from the whole picture
        const double TIMELINE_FRAME_WIDTH_MIN = 1.0;
        const double TIMELINE_FRAME_WIDTH = 1.5;

        // Unity reports single-digit wheel deltas while the original's coefficients are tuned for
        // the browser's ~100-per-notch pixel deltas, so one notch would otherwise barely move
        const double TIMELINE_WHEEL_SENSITIVITY = 40.0;

        const double RULER_LABEL_GAP = 64.0;

        const float CLIP_TOP = 24f;
        const float CLIP_HEIGHT = 40f;
        const float PLAYHEAD_WIDTH = 1f;

        // Away from both In and Out, so the playhead line is not mistaken for one of them
        const double TIMELINE_PLAYHEAD_START = 60.0;

        static readonly double[] ClipStarts = { 12.0, 72.0, 150.0 };
        static readonly double[] ClipLengths = { 36.0, 48.0, 60.0 };

        static readonly string[] FrameRates = { "24", "30", "60" };

        static readonly string[] Fruits = { "Apple", "Banana", "Cherry" };

        static readonly string[] Easings = { "Linear", "Ease In", "Ease Out", "Ease In Out" };

        // A long option list to exercise the fuzzy search (e.g. "eio" -> Elastic In Out)
        static readonly string[] SearchEasings =
        {
            "Linear",
            "Sine In", "Sine Out", "Sine In Out",
            "Quad In", "Quad Out", "Quad In Out",
            "Cubic In", "Cubic Out", "Cubic In Out",
            "Quart In", "Quart Out", "Quart In Out",
            "Quint In", "Quint Out", "Quint In Out",
            "Expo In", "Expo Out", "Expo In Out",
            "Circ In", "Circ Out", "Circ In Out",
            "Back In", "Back Out", "Back In Out",
            "Elastic In", "Elastic Out", "Elastic In Out",
            "Bounce In", "Bounce Out", "Bounce In Out",
        };

        #endregion

        #region Fields

        [SerializeField] UIDocument _document;

        TweeqTheme _theme;
        VisualElement _root;
        RotaryInput _rotary;
        Label _valueLabel;

        NumberInput _opacity;
        NumberInput _rotation;
        NumberInput _offsetX;
        NumberInput _overshoot;
        PositionInput _position;
        SizeInput _size;
        Label _confirmedLabel;

        CheckboxInput _visible;
        SwitchInput _loop;
        ButtonToggleInput _mute;
        RadioInput _fruit;
        DropdownInput<string> _easing;
        DropdownInput<string> _search;
        ShuffleInput<string> _shuffle;
        AngleInput _angle;
        TimeInput _time;
        StringDropdownInput _frameRate;
        ButtonToggleInput _timecodeMode;
        TweeqTimeline _timeline;
        TweeqRuler _ruler;
        VisualElement _playhead;
        ButtonInput _focusInOutButton;
        StringInput _text;
        ColorInput _tint;
        CubicBezierInput _curve;
        ButtonInput _flashButton;
        ButtonInput _plusButton;

        ButtonInput _openSettingsButton;
        TweeqModalDialog _settingsDialog;
        TweeqTabs _settingsTabs;
        SwitchInput _dialogVsync;
        NumberInput _dialogVolume;
        ColorInput _dialogAccent;

        // ModalComplex-style sample: a form body with an evenly split Save / Cancel footer
        ButtonInput _openProfileButton;
        TweeqModalDialog _profileDialog;
        StringInput _profileName;
        NumberInput _profileAge;

        // Custom widgets built in an external asmdef (Tweeq.Demo.CustomWidgets)
        EndpointInput _endpoint;
        EndpointInput _endpointWithPort;

        // Plain PaneModal-style sample: the owner closes it, an outside click only bounces
        ButtonInput _openAboutButton;
        TweeqModal _aboutModal;
        ButtonInput _aboutCloseButton;

        float _opacityConfirmed = 1f;
        float _rotationConfirmed;
        float _offsetConfirmed;
        float _overshootConfirmed = OVERSHOOT_INITIAL;
        Vector2 _positionConfirmed;
        Vector2 _sizeConfirmed = INITIAL_SIZE;

        bool _visibleConfirmed = true;
        bool _loopConfirmed;
        bool _muteConfirmed;
        int _fruitConfirmed;
        string _easingConfirmed = Easings[0];
        string _searchConfirmed = SearchEasings[0];
        float _angleConfirmed = INITIAL_ANGLE;
        float _timeConfirmed = INITIAL_TIME_FRAMES;
        float _timeLive = INITIAL_TIME_FRAMES;
        string _textConfirmed = INITIAL_TEXT;
        Color _tintConfirmed = Color.white;
        Vector4 _curveConfirmed = CubicBezierInput.DEFAULT_VALUE;
        Vector4 _curveLive = CubicBezierInput.DEFAULT_VALUE;
        int _flashClicks;
        int _plusClicks;

        // There is no Scheme, so rolling a dialog back on Cancel is the caller's job (m8 spec B).
        // This demonstrates it: the values at open time are stashed here and restored on Cancelled
        bool _dialogVsyncAtOpen;
        float _dialogVolumeAtOpen;
        Color _dialogAccentAtOpen;
        string _dialogResult = "-";

        string _profileNameAtOpen = string.Empty;
        float _profileAgeAtOpen;

        string _endpointConfirmed = INITIAL_ENDPOINT;
        string _endpointPortConfirmed = INITIAL_ENDPOINT_PORT;
        string _endpointLive = INITIAL_ENDPOINT;

        // The ruler's scales are rebuilt on every visible range change, so the list is reused
        readonly List<RulerScale> _rulerScales = new List<RulerScale>();
        double _playheadFrame = TIMELINE_PLAYHEAD_START;

        #endregion

        #region Unity

        void OnEnable()
        {
            if (_document == null)
            {
                Debug.LogError($"{nameof(TweeqRotaryDemo)}: UIDocument is not assigned.", this);
                return;
            }

            _root = _document.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError($"{nameof(TweeqRotaryDemo)}: rootVisualElement could not be resolved.", this);
                return;
            }

            // The Vue demo uses accentColor '#0000ff', but that assumes light mode. On a dark
            // background Radix keeps the seed as step9, which reads far too dark, so a brighter
            // blue was chosen instead (user decision, 2026-07-27)
            _theme = TweeqTheme.Dark().WithAccent(new Color32(0x4a, 0x76, 0xff, 0xff));

            BuildUi();
        }

        void OnDisable()
        {
            if (_rotary != null)
            {
                _rotary.UnregisterValueChangedCallback(OnValueChanged);
                _rotary = null;
            }

            if (_opacity != null)
            {
                // The tooltip is a shared instance that would keep a reference, so always detach
                TweeqTooltip.Detach(_opacity);
                _opacity.Confirmed -= OnOpacityConfirmed;
                _opacity = null;
            }

            if (_rotation != null)
            {
                _rotation.Confirmed -= OnRotationConfirmed;
                _rotation = null;
            }

            if (_offsetX != null)
            {
                _offsetX.Confirmed -= OnOffsetConfirmed;
                _offsetX = null;
            }

            if (_overshoot != null)
            {
                _overshoot.Confirmed -= OnOvershootConfirmed;
                _overshoot = null;
            }

            if (_position != null)
            {
                _position.Confirmed -= OnPositionConfirmed;
                _position = null;
            }

            if (_size != null)
            {
                _size.Confirmed -= OnSizeConfirmed;
                _size.KeepRatioChanged -= OnSizeKeepRatioChanged;
                _size = null;
            }

            if (_visible != null)
            {
                _visible.Confirmed -= OnVisibleConfirmed;
                _visible = null;
            }

            if (_loop != null)
            {
                _loop.Confirmed -= OnLoopConfirmed;
                _loop = null;
            }

            if (_mute != null)
            {
                _mute.Confirmed -= OnMuteConfirmed;
                _mute = null;
            }

            if (_fruit != null)
            {
                _fruit.Confirmed -= OnFruitConfirmed;
                _fruit = null;
            }

            if (_easing != null)
            {
                _easing.Confirmed -= OnEasingConfirmed;
                _easing = null;
            }

            if (_search != null)
            {
                _search.Confirmed -= OnSearchConfirmed;
                _search = null;
            }

            if (_shuffle != null)
            {
                _shuffle.Confirmed -= OnShuffleConfirmed;

                // Generate is a demo-side delegate, so drop that reference as well
                _shuffle.Generate = null;
                _shuffle = null;
            }

            if (_angle != null)
            {
                _angle.Confirmed -= OnAngleConfirmed;
                _angle = null;
            }

            if (_time != null)
            {
                _time.UnregisterValueChangedCallback(OnTimeChanged);
                _time.Confirmed -= OnTimeConfirmed;
                _time = null;
            }

            if (_frameRate != null)
            {
                _frameRate.Confirmed -= OnFrameRateConfirmed;
                _frameRate = null;
            }

            if (_timecodeMode != null)
            {
                _timecodeMode.Confirmed -= OnTimecodeModeConfirmed;
                _timecodeMode = null;
            }

            if (_ruler != null)
            {
                _ruler.Dragged -= OnRulerDragged;
                _ruler.UnregisterCallback<GeometryChangedEvent>(OnRulerGeometryChanged);

                // The ruler holds the demo-owned scale buffer, so drop that reference too
                _ruler.Scales = null;
                _ruler = null;
            }

            if (_timeline != null)
            {
                _timeline.VisibleRangeChanged -= OnTimelineVisibleRangeChanged;

                if (_playhead != null)
                {
                    _timeline.UnpinItem(_playhead);
                }

                _timeline = null;
            }

            _playhead = null;

            if (_focusInOutButton != null)
            {
                _focusInOutButton.Clicked -= OnFocusInOutClicked;
                _focusInOutButton = null;
            }

            if (_text != null)
            {
                _text.Confirmed -= OnTextConfirmed;
                _text = null;
            }

            if (_tint != null)
            {
                _tint.Confirmed -= OnTintConfirmed;
                _tint = null;
            }

            if (_curve != null)
            {
                _curve.Confirmed -= OnCurveConfirmed;
                _curve.UnregisterValueChangedCallback(OnCurveChanged);
                _curve = null;
            }

            if (_flashButton != null)
            {
                _flashButton.Clicked -= OnFlashClicked;
                _flashButton = null;
            }

            if (_plusButton != null)
            {
                _plusButton.Clicked -= OnPlusClicked;
                _plusButton = null;
            }

            if (_openSettingsButton != null)
            {
                _openSettingsButton.Clicked -= OnOpenSettingsClicked;
                _openSettingsButton = null;
            }

            if (_settingsDialog != null)
            {
                // The backdrop lives on the overlay layer (outside rootVisualElement), so
                // _root.Clear() does not take it down: close explicitly before letting go
                _settingsDialog.Open = false;
                _settingsDialog.Confirmed -= OnDialogConfirmed;
                _settingsDialog.Cancelled -= OnDialogCancelled;
                _settingsDialog = null;
            }

            _settingsTabs = null;
            _dialogVsync = null;
            _dialogVolume = null;
            _dialogAccent = null;

            if (_openProfileButton != null)
            {
                _openProfileButton.Clicked -= OnOpenProfileClicked;
                _openProfileButton = null;
            }

            if (_profileDialog != null)
            {
                _profileDialog.Open = false;
                _profileDialog.Confirmed -= OnProfileConfirmed;
                _profileDialog.Cancelled -= OnProfileCancelled;
                _profileDialog = null;
            }

            _profileName = null;
            _profileAge = null;

            if (_openAboutButton != null)
            {
                _openAboutButton.Clicked -= OnOpenAboutClicked;
                _openAboutButton = null;
            }

            if (_aboutCloseButton != null)
            {
                _aboutCloseButton.Clicked -= OnAboutCloseClicked;
                _aboutCloseButton = null;
            }

            if (_aboutModal != null)
            {
                _aboutModal.Open = false;
                _aboutModal = null;
            }

            if (_endpoint != null)
            {
                _endpoint.UnregisterValueChangedCallback(OnEndpointChanged);
                _endpoint.Confirmed -= OnEndpointConfirmed;
                _endpoint = null;
            }

            if (_endpointWithPort != null)
            {
                _endpointWithPort.UnregisterValueChangedCallback(OnEndpointPortChanged);
                _endpointWithPort.Confirmed -= OnEndpointPortConfirmed;
                _endpointWithPort = null;
            }

            _valueLabel = null;
            _confirmedLabel = null;

            if (_root != null)
            {
                // UIDocument reuses rootVisualElement, so the C-3 suppression is undone here too
                TweeqNavigation.EnableArrowFocusNavigation(_root);
                _root.Clear();
                _root = null;
            }
        }

        #endregion

        #region UI

        void BuildUi()
        {
            _root.Clear();
            _root.style.flexGrow = 1f;
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.justifyContent = Justify.Center;
            _root.style.alignItems = Align.Center;
            _root.style.backgroundColor = _theme.Background;

            // feedback-fixes-01.md C-3: arrow keys drive values only on this panel. That is not
            // the library default, so the application opts in
            TweeqNavigation.DisableArrowFocusNavigation(_root);

            _rotary = new RotaryInput
            {
                Theme = _theme,
                Step = STEP,
            };
            _rotary.SetValueWithoutNotify(INITIAL_VALUE);
            _rotary.RegisterValueChangedCallback(OnValueChanged);
            _root.Add(_rotary);

            _valueLabel = new Label(FormatAngle(_rotary.value));
            _valueLabel.style.marginTop = GAP;
            _valueLabel.style.color = _theme.Text;
            _root.Add(_valueLabel);

            _root.Add(BuildParameterSection());

            // The modal bodies draw nothing in the tree; Open moves them onto the overlay layer
            _root.Add(BuildSettingsDialog());
            _root.Add(BuildProfileDialog());
            _root.Add(BuildAboutModal());
        }

        VisualElement BuildParameterSection()
        {
            VisualElement column = new VisualElement();
            column.style.width = COLUMN_WIDTH;
            column.style.marginTop = GAP * 2f;
            column.style.flexDirection = FlexDirection.Column;

            ParameterGrid grid = new ParameterGrid();
            column.Add(grid);

            grid.Add(new ParameterHeading("InputNumber"));

            _opacity = new NumberInput
            {
                Theme = _theme,
                Min = 0.0,
                Max = 1.0,
                Step = 0.01,
                SnapStep = 0.1,
                Precision = 3,
            };
            _opacity.SetValueWithoutNotify(1f);
            _opacity.Confirmed += OnOpacityConfirmed;

            // Exercises the tooltip, which reuses a single instance app-wide (popover-spec.md)
            TweeqTooltip.Attach(_opacity, OPACITY_TOOLTIP);
            grid.Add(BuildRow("Opacity", _opacity));

            _rotation = new NumberInput
            {
                Theme = _theme,
                Min = -180.0,
                Max = 180.0,
                Step = 1.0,
                SnapStep = 15.0,
                Suffix = "°",
            };
            _rotation.SetValueWithoutNotify(0f);
            _rotation.Confirmed += OnRotationConfirmed;
            grid.Add(BuildRow("Rotation", _rotation));

            // Leaving Min/Max infinite makes the field unranged (no bar; scrub from the grips)
            _offsetX = new NumberInput
            {
                Theme = _theme,
                Step = 0.1,
                SnapStep = 10.0,
                Suffix = " px",
            };
            _offsetX.SetValueWithoutNotify(0f);
            _offsetX.Confirmed += OnOffsetConfirmed;
            grid.Add(BuildRow("Offset X", _offsetX));

            // feedback-fixes-01.md D-4: a bar with both clamps off. Leaving the range raises the
            // out-of-range arrows, which also contrasts with D-3 folding the clamped sides
            _overshoot = new NumberInput
            {
                Theme = _theme,
                Min = 0.0,
                Max = 1.0,
                Step = 0.01,
                SnapStep = 0.1,
                ClampMin = false,
                ClampMax = false,
                Precision = 3,
            };
            _overshoot.SetValueWithoutNotify(OVERSHOOT_INITIAL);
            _overshoot.Confirmed += OnOvershootConfirmed;
            grid.Add(BuildRow("Overshoot", _overshoot));

            grid.Add(BuildVectorGroup());
            grid.Add(BuildBooleanGroup());
            grid.Add(BuildSelectGroup());
            grid.Add(BuildTimeGroup());
            grid.Add(BuildTimelineGroup());
            grid.Add(BuildDialogGroup());
            grid.Add(BuildCustomGroup());

            // The Grid hands the theme down to every Parameter / Heading / Group below it
            grid.Theme = _theme;

            _confirmedLabel = new Label(FormatConfirmed());
            _confirmedLabel.style.marginTop = _theme.GapControl;
            _confirmedLabel.style.fontSize = ROW_LABEL_FONT_SIZE;
            _confirmedLabel.style.color = _theme.TextMuted;
            _confirmedLabel.style.whiteSpace = WhiteSpace.Normal;
            column.Add(_confirmedLabel);

            return column;
        }

        ParameterGroup BuildVectorGroup()
        {
            ParameterGroup group = new ParameterGroup(VECTOR_GROUP_NAME, "Vector");

            _position = new PositionInput
            {
                Theme = _theme,
                Step = VECTOR_STEP,
            };
            _position.SetValueWithoutNotify(Vector2.zero);
            _position.Confirmed += OnPositionConfirmed;
            group.Content.Add(BuildRow("Position", _position));

            // keepRatio starts on and releases itself when both axes move to a new ratio at once
            _size = new SizeInput
            {
                Theme = _theme,
                Step = new[] { VECTOR_STEP },
                Min = new[] { 0.0 },
            };
            _size.SetValueWithoutNotify(INITIAL_SIZE);
            _size.Confirmed += OnSizeConfirmed;

            // That release is easy to miss, so the label reports it as well
            _size.KeepRatioChanged += OnSizeKeepRatioChanged;
            group.Content.Add(BuildRow("Size", _size));

            group.RefreshContentGaps();
            return group;
        }

        ParameterGroup BuildBooleanGroup()
        {
            ParameterGroup group = new ParameterGroup(BOOLEAN_GROUP_NAME, "Boolean and actions");

            _visible = new CheckboxInput
            {
                Theme = _theme,
                Label = "Visible",
            };
            _visible.SetValueWithoutNotify(true);
            _visible.Confirmed += OnVisibleConfirmed;
            group.Content.Add(BuildRow("Visible", _visible));

            _loop = new SwitchInput
            {
                Theme = _theme,
            };
            _loop.SetValueWithoutNotify(false);
            _loop.Confirmed += OnLoopConfirmed;
            group.Content.Add(BuildRow("Loop", _loop));

            _mute = new ButtonToggleInput
            {
                Theme = _theme,
                Label = "Mute",
            };
            _mute.SetValueWithoutNotify(false);
            _mute.Confirmed += OnMuteConfirmed;
            group.Content.Add(BuildRow("Mute", _mute));

            _fruit = new RadioInput
            {
                Theme = _theme,
                Options = Fruits,
            };
            _fruit.SetValueWithoutNotify(0);
            _fruit.Confirmed += OnFruitConfirmed;
            group.Content.Add(BuildRow("Fruit", _fruit));

            group.Content.Add(BuildActionRow());

            group.RefreshContentGaps();
            return group;
        }

        ParameterGroup BuildSelectGroup()
        {
            ParameterGroup group = new ParameterGroup(SELECT_GROUP_NAME, "Select");

            _easing = new DropdownInput<string>(Easings)
            {
                Theme = _theme,
            };
            _easing.SetValueWithoutNotify(Easings[0]);
            _easing.Confirmed += OnEasingConfirmed;
            group.Content.Add(BuildRow("Easing", _easing));

            // Typing while open filters through the fuzzy search (e.g. "eio" -> Elastic In Out)
            _search = new DropdownInput<string>(SearchEasings)
            {
                Theme = _theme,
            };
            _search.SetValueWithoutNotify(SearchEasings[0]);
            _search.Confirmed += OnSearchConfirmed;

            // Draws a random option on each press and reads as one box with the dropdown
            _shuffle = new ShuffleInput<string>
            {
                Theme = _theme,
                Generate = PickRandomEasing,
            };
            _shuffle.SetValueWithoutNotify(_searchConfirmed);
            _shuffle.Confirmed += OnShuffleConfirmed;

            InputGroup searchGroup = new InputGroup { Theme = _theme };
            searchGroup.Add(_search);
            searchGroup.Add(_shuffle);
            group.Content.Add(BuildRow("Search", searchGroup));

            // Knob plus number field: the field is capped at +/-180, the knob spins past it
            _angle = new AngleInput
            {
                Theme = _theme,
                Min = -ANGLE_LIMIT,
                Max = ANGLE_LIMIT,
                Step = ANGLE_STEP,
                Snap = ANGLE_SNAP,
            };
            _angle.SetValueWithoutNotify(INITIAL_ANGLE);
            _angle.Confirmed += OnAngleConfirmed;
            group.Content.Add(BuildRow("Angle", _angle));

            _text = new StringInput
            {
                Theme = _theme,
            };
            _text.SetValueWithoutNotify(INITIAL_TEXT);
            _text.Confirmed += OnTextConfirmed;
            group.Content.Add(BuildRow("Text", _text));

            Color tint = _theme.Accent;
            tint.a = TINT_ALPHA;

            _tint = new ColorInput
            {
                Theme = _theme,
            };
            _tint.SetValueWithoutNotify(tint);
            _tint.Confirmed += OnTintConfirmed;
            _tintConfirmed = tint;
            group.Content.Add(BuildRow("Tint", _tint));

            // Easing curve: the preview button opens a pad where both control points are dragged.
            // ChangeEvent tracks the drag live, Confirmed lands once per drag
            _curve = new CubicBezierInput
            {
                Theme = _theme,
            };
            _curve.SetValueWithoutNotify(CubicBezierInput.DEFAULT_VALUE);
            _curve.RegisterValueChangedCallback(OnCurveChanged);
            _curve.Confirmed += OnCurveConfirmed;
            group.Content.Add(BuildRow("Curve", _curve));

            group.RefreshContentGaps();
            return group;
        }

        // Demonstrates the variable frame rate: changing it leaves the TimeInput value (a frame
        // count) alone and only rebuilds the printed form
        ParameterGroup BuildTimeGroup()
        {
            ParameterGroup group = new ParameterGroup(TIME_GROUP_NAME, "Time");

            _time = new TimeInput
            {
                Theme = _theme,
                FrameRate = INITIAL_FRAME_RATE,
                DisplayMode = TimeDisplayMode.Timecode,
                DefaultValue = 0.0,
            };
            _time.SetValueWithoutNotify(INITIAL_TIME_FRAMES);
            _time.RegisterValueChangedCallback(OnTimeChanged);
            _time.Confirmed += OnTimeConfirmed;
            group.Content.Add(BuildRow("Time", _time));

            _frameRate = new StringDropdownInput(FrameRates)
            {
                Theme = _theme,
                Suffix = " fps",
            };
            _frameRate.SetValueWithoutNotify(FrameRates[0]);
            _frameRate.Confirmed += OnFrameRateConfirmed;
            group.Content.Add(BuildRow("Frame rate", _frameRate));

            _timecodeMode = new ButtonToggleInput
            {
                Theme = _theme,
                Label = "SMPTE Timecode",
            };
            _timecodeMode.SetValueWithoutNotify(true);
            _timecodeMode.Confirmed += OnTimecodeModeConfirmed;
            group.Content.Add(BuildRow("Display", _timecodeMode));

            group.RefreshContentGaps();
            return group;
        }

        // Timeline and Ruler are independent primitives, so the ruler is stacked above the
        // timeline and fed from VisibleRangeChanged rather than living inside it
        ParameterGroup BuildTimelineGroup()
        {
            ParameterGroup group = new ParameterGroup(TIMELINE_GROUP_NAME, "Timeline");

            _ruler = new TweeqRuler
            {
                Theme = _theme,
            };
            _ruler.style.height = RULER_HEIGHT;
            _ruler.Scales = _rulerScales;
            _ruler.Dragged += OnRulerDragged;

            // The scales depend on the pixel width, which is only known once laid out
            _ruler.RegisterCallback<GeometryChangedEvent>(OnRulerGeometryChanged);

            _timeline = new TweeqTimeline
            {
                Theme = _theme,
                RangeStart = 0.0,
                RangeEnd = TIMELINE_RANGE_END,

                // The bound has to widen before the zoom itself can go below the library minimum
                FrameWidthMin = TIMELINE_FRAME_WIDTH_MIN,
                FrameWidth = TIMELINE_FRAME_WIDTH,
                WheelSensitivity = TIMELINE_WHEEL_SENSITIVITY,
                InPoint = TIMELINE_IN,
                OutPoint = TIMELINE_OUT,
            };
            _timeline.style.height = TIMELINE_HEIGHT;
            _timeline.VisibleRangeChanged += OnTimelineVisibleRangeChanged;

            for (int index = 0; index < ClipStarts.Length; index++)
            {
                _timeline.Add(BuildClip(index));
            }

            _playhead = new VisualElement { name = "demo-playhead" };
            _playhead.style.top = 0f;
            _playhead.style.bottom = 0f;
            _playhead.style.width = PLAYHEAD_WIDTH;
            _playhead.style.backgroundColor = _theme.Accent;
            _playhead.pickingMode = PickingMode.Ignore;
            _timeline.Add(_playhead);

            VisualElement stack = new VisualElement { name = "demo-timeline-stack" };
            stack.style.flexDirection = FlexDirection.Column;
            stack.Add(_ruler);
            stack.Add(_timeline);
            group.Content.Add(stack);

            _focusInOutButton = new ButtonInput("Focus In/Out")
            {
                Theme = _theme,
            };
            _focusInOutButton.style.flexGrow = 1f;
            _focusInOutButton.Clicked += OnFocusInOutClicked;
            group.Content.Add(BuildRow("In/Out", _focusInOutButton));

            SetPlayhead(_playheadFrame, false);
            SyncRuler();

            group.RefreshContentGaps();
            return group;
        }

        // A clip only declares its frame and length; the timeline owns the horizontal geometry
        VisualElement BuildClip(int index)
        {
            VisualElement clip = new VisualElement { name = "demo-clip-" + index };
            clip.style.top = CLIP_TOP;
            clip.style.height = CLIP_HEIGHT;

            // Surface resolves to almost the same value as the track in the dark theme, so the
            // block borrows Neutral, the step meant to read as a raised surface
            clip.style.backgroundColor = _theme.Neutral;
            clip.style.borderTopLeftRadius = _theme.InputRadius;
            clip.style.borderTopRightRadius = _theme.InputRadius;
            clip.style.borderBottomLeftRadius = _theme.InputRadius;
            clip.style.borderBottomRightRadius = _theme.InputRadius;

            _timeline.PinItem(clip, ClipStarts[index], ClipLengths[index]);
            return clip;
        }

        ParameterGroup BuildDialogGroup()
        {
            ParameterGroup group = new ParameterGroup(DIALOG_GROUP_NAME, "Dialog");

            _openSettingsButton = new ButtonInput("Open Settings…")
            {
                Theme = _theme,
            };
            _openSettingsButton.style.flexGrow = 1f;
            _openSettingsButton.Clicked += OnOpenSettingsClicked;
            group.Content.Add(BuildRow("Settings", _openSettingsButton));

            _openProfileButton = new ButtonInput("Edit Profile…")
            {
                Theme = _theme,
            };
            _openProfileButton.style.flexGrow = 1f;
            _openProfileButton.Clicked += OnOpenProfileClicked;
            group.Content.Add(BuildRow("Profile", _openProfileButton));

            _openAboutButton = new ButtonInput("About…")
            {
                Theme = _theme,
            };
            _openAboutButton.style.flexGrow = 1f;
            _openAboutButton.Clicked += OnOpenAboutClicked;
            group.Content.Add(BuildRow("About", _openAboutButton));

            group.RefreshContentGaps();
            return group;
        }

        // Shows that a custom widget from an external asmdef sits in line with the library rows
        ParameterGroup BuildCustomGroup()
        {
            ParameterGroup group = new ParameterGroup(CUSTOM_GROUP_NAME, "Custom");

            _endpoint = new EndpointInput
            {
                Theme = _theme,
            };
            _endpoint.SetValueWithoutNotify(INITIAL_ENDPOINT);
            _endpoint.RegisterValueChangedCallback(OnEndpointChanged);
            _endpoint.Confirmed += OnEndpointConfirmed;
            group.Content.Add(BuildRow("Endpoint", _endpoint));

            _endpointWithPort = new EndpointInput
            {
                Theme = _theme,
                PortEnabled = true,
            };
            _endpointWithPort.SetValueWithoutNotify(INITIAL_ENDPOINT_PORT);
            _endpointWithPort.RegisterValueChangedCallback(OnEndpointPortChanged);
            _endpointWithPort.Confirmed += OnEndpointPortConfirmed;
            group.Content.Add(BuildRow("Endpoint:Port", _endpointWithPort));

            group.RefreshContentGaps();
            return group;
        }

        // PaneModalTabs layout: title, vertical tabs and a right-aligned footer (Done)
        TweeqModalDialog BuildSettingsDialog()
        {
            _settingsDialog = new TweeqModalDialog
            {
                Title = "Settings",
                ConfirmLabel = "Done",
                FooterStretch = false,
            };
            _settingsDialog.Pane.style.width = DIALOG_WIDTH;
            _settingsDialog.Confirmed += OnDialogConfirmed;
            _settingsDialog.Cancelled += OnDialogCancelled;

            _settingsTabs = new TweeqTabs(SETTINGS_TABS_NAME)
            {
                Vertical = true,
            };

            TweeqTab general = new TweeqTab("General");
            _dialogVsync = new SwitchInput();
            _dialogVsync.SetValueWithoutNotify(true);
            _dialogVolume = new NumberInput
            {
                Min = 0.0,
                Max = 1.0,
                Step = 0.01,
                SnapStep = 0.1,
                Precision = 2,
            };
            _dialogVolume.SetValueWithoutNotify(0.8f);

            ParameterGrid generalGrid = new ParameterGrid();
            generalGrid.Add(BuildRow("VSync", _dialogVsync));
            generalGrid.Add(BuildRow("Volume", _dialogVolume));
            general.Add(generalGrid);
            _settingsTabs.Add(general);

            // Also checks a picker (popover) stacking on top of the modal
            TweeqTab appearance = new TweeqTab("Appearance");
            _dialogAccent = new ColorInput();
            _dialogAccent.SetValueWithoutNotify(_theme.Accent);

            ParameterGrid appearanceGrid = new ParameterGrid();
            appearanceGrid.Add(BuildRow("Accent", _dialogAccent));
            appearance.Add(appearanceGrid);
            _settingsTabs.Add(appearance);

            // Checks how a disabled tab looks and how it is skipped (keyboard moves, default
            // resolution). It is unselectable on purpose, which the label spells out
            _settingsTabs.Add(new TweeqTab("Advanced (disabled)") { Id = "advanced", IsDisabled = true });

            _settingsDialog.Add(_settingsTabs);

            // Theme goes last: the dialog hands it to the backdrop, balloon and tab contents
            _settingsDialog.Theme = _theme;
            return _settingsDialog;
        }

        // PaneModalComplex layout: a form body with an evenly split Save / Cancel footer
        TweeqModalDialog BuildProfileDialog()
        {
            _profileDialog = new TweeqModalDialog
            {
                Title = "Edit Profile",
            };
            _profileDialog.Confirmed += OnProfileConfirmed;
            _profileDialog.Cancelled += OnProfileCancelled;

            _profileName = new StringInput();
            _profileName.SetValueWithoutNotify("Tsumugi");

            _profileAge = new NumberInput
            {
                Min = 0.0,
                Max = 120.0,
                Step = 1.0,
                Precision = 0,
            };
            _profileAge.SetValueWithoutNotify(18f);

            ParameterGrid grid = new ParameterGrid();
            grid.Add(BuildRow("Name", _profileName));
            grid.Add(BuildRow("Age", _profileAge));
            _profileDialog.Add(grid);

            _profileDialog.Theme = _theme;
            return _profileDialog;
        }

        // Plain PaneModal: no keys, no footer, and only the owner (this OK button) closes it.
        // An outside click bounces instead of closing, which is the point of the sample
        TweeqModal BuildAboutModal()
        {
            _aboutModal = new TweeqModal();

            // A plain Label is not ITweeqThemed, so the theme never reaches it: paint text here
            Label text = new Label(
                "tweeq UI Toolkit port\n\noriginal tweeq by Baku Hashimoto (MIT)")
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = GAP,
                    color = _theme.Text,
                },
            };
            _aboutModal.Add(text);

            _aboutCloseButton = new ButtonInput("OK");
            _aboutCloseButton.Clicked += OnAboutCloseClicked;
            _aboutModal.Add(_aboutCloseButton);

            _aboutModal.Theme = _theme;
            return _aboutModal;
        }

        // Puts "Flash me" and the narrow "+" into one InputContainer
        Parameter BuildActionRow()
        {
            Parameter parameter = new Parameter("Action")
            {
                Theme = _theme,
            };

            _flashButton = new ButtonInput("Flash me")
            {
                Theme = _theme,
            };
            _flashButton.style.flexGrow = 1f;
            _flashButton.Clicked += OnFlashClicked;
            parameter.InputContainer.Add(_flashButton);

            // narrow exists for its tight width, so it is never stretched
            _plusButton = new ButtonInput("+")
            {
                Theme = _theme,
                Subtle = true,
                Narrow = true,
            };
            _plusButton.Clicked += OnPlusClicked;
            parameter.InputContainer.Add(_plusButton);

            parameter.RefreshInputGaps();
            return parameter;
        }

        Parameter BuildRow(string label, VisualElement input)
        {
            Parameter parameter = new Parameter(label)
            {
                Theme = _theme,
            };

            input.style.flexGrow = 1f;
            parameter.InputContainer.Add(input);
            parameter.RefreshInputGaps();
            return parameter;
        }

        #endregion

        #region Events

        void OnValueChanged(ChangeEvent<float> evt)
        {
            if (evt == null || _valueLabel == null)
            {
                return;
            }

            _valueLabel.text = FormatAngle(evt.newValue);
        }

        void OnOpacityConfirmed(float value)
        {
            _opacityConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnRotationConfirmed(float value)
        {
            _rotationConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnOffsetConfirmed(float value)
        {
            _offsetConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnOvershootConfirmed(float value)
        {
            _overshootConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnPositionConfirmed(Vector2 value)
        {
            _positionConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnSizeConfirmed(Vector2 value)
        {
            _sizeConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnSizeKeepRatioChanged(bool keepRatio)
        {
            RefreshConfirmedLabel();
        }

        void OnVisibleConfirmed(bool value)
        {
            _visibleConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnLoopConfirmed(bool value)
        {
            _loopConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnMuteConfirmed(bool value)
        {
            _muteConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnFruitConfirmed(int value)
        {
            _fruitConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnEasingConfirmed(string value)
        {
            _easingConfirmed = value ?? string.Empty;
            RefreshConfirmedLabel();
        }

        void OnSearchConfirmed(string value)
        {
            _searchConfirmed = value ?? string.Empty;

            // The next shuffle seeds from the current value, so keep this side in sync
            _shuffle?.SetValueWithoutNotify(_searchConfirmed);
            RefreshConfirmedLabel();
        }

        void OnShuffleConfirmed(string value)
        {
            _searchConfirmed = value ?? string.Empty;

            // The dropdown only mirrors the result; notifying back would confirm twice
            _search?.SetValueWithoutNotify(_searchConfirmed);
            RefreshConfirmedLabel();
        }

        void OnAngleConfirmed(float value)
        {
            _angleConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnTimeChanged(ChangeEvent<float> evt)
        {
            if (evt == null)
            {
                return;
            }

            _timeLive = evt.newValue;

            // The field and the playhead are two views of the same frame, so the field drives the
            // timeline as well. Notifying back is skipped, otherwise the two would ping-pong
            SetPlayhead(evt.newValue, false);
            RefreshConfirmedLabel();
        }

        void OnTimeConfirmed(float value)
        {
            _timeConfirmed = value;
            RefreshConfirmedLabel();
        }

        // Only swaps the rate. The value is untouched, so the frame count survives the switch
        void OnFrameRateConfirmed(string value)
        {
            if (_time == null
                || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double frameRate))
            {
                return;
            }

            _time.FrameRate = frameRate;

            // Only the labels change: the ruler still spans the same frames
            RefreshRulerScales();
            RefreshConfirmedLabel();
        }

        void OnTimelineVisibleRangeChanged()
        {
            SyncRuler();
        }

        void OnRulerGeometryChanged(GeometryChangedEvent evt)
        {
            RefreshRulerScales();
        }

        void OnRulerDragged(double frame)
        {
            SetPlayhead(frame, true);
        }

        void OnFocusInOutClicked()
        {
            _timeline?.FocusInOut();
        }

        void SyncRuler()
        {
            if (_ruler == null || _timeline == null)
            {
                return;
            }

            _ruler.RangeStart = _timeline.VisibleStart;
            _ruler.RangeEnd = _timeline.VisibleEnd;
            RefreshRulerScales();
        }

        void RefreshRulerScales()
        {
            if (_ruler == null)
            {
                return;
            }

            double frameRate = _time != null ? _time.FrameRate : INITIAL_FRAME_RATE;

            TweeqRulerScales.BuildTimecode(
                _rulerScales, _ruler.RangeStart, _ruler.RangeEnd, frameRate, RULER_LABEL_GAP,
                _ruler.ViewportWidth);

            // The ruler already holds this list, so only the redraw has to be requested
            _ruler.Refresh();
        }

        void SetPlayhead(double frame, bool syncTimeInput)
        {
            _playheadFrame = frame;

            if (_timeline != null && _playhead != null)
            {
                _timeline.PinItem(_playhead, _playheadFrame);
            }

            if (syncTimeInput)
            {
                _time?.SetValueWithoutNotify((float)_playheadFrame);
            }

            RefreshConfirmedLabel();
        }

        void OnTimecodeModeConfirmed(bool timecode)
        {
            if (_time == null)
            {
                return;
            }

            _time.DisplayMode = timecode ? TimeDisplayMode.Timecode : TimeDisplayMode.Frames;
            RefreshConfirmedLabel();
        }

        // Drawing the same option looks like a dead button, so nudge to the neighbour then
        static string PickRandomEasing(string previous)
        {
            if (SearchEasings.Length == 0)
            {
                return previous;
            }

            int index = UnityEngine.Random.Range(0, SearchEasings.Length);
            if (SearchEasings.Length > 1 && string.Equals(SearchEasings[index], previous))
            {
                index = (index + 1) % SearchEasings.Length;
            }

            return SearchEasings[index];
        }

        void OnTextConfirmed(string value)
        {
            _textConfirmed = value ?? string.Empty;
            RefreshConfirmedLabel();
        }

        void OnTintConfirmed(Color value)
        {
            _tintConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnCurveChanged(ChangeEvent<Vector4> evt)
        {
            _curveLive = evt.newValue;
            RefreshConfirmedLabel();
        }

        void OnCurveConfirmed(Vector4 value)
        {
            _curveConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnFlashClicked()
        {
            _flashClicks++;

            // Flashes itself (the imperative Flash of spec section 3)
            _flashButton?.Flash();
            RefreshConfirmedLabel();
        }

        void OnPlusClicked()
        {
            _plusClicks++;
            RefreshConfirmedLabel();
        }

        void OnOpenSettingsClicked()
        {
            if (_settingsDialog == null)
            {
                return;
            }

            // Stash for the Cancel rollback (m8 spec B: restoring values is the caller's job)
            _dialogVsyncAtOpen = _dialogVsync != null && _dialogVsync.value;
            _dialogVolumeAtOpen = _dialogVolume != null ? _dialogVolume.value : 0f;
            _dialogAccentAtOpen = _dialogAccent != null ? _dialogAccent.value : Color.white;

            _settingsDialog.Open = true;
        }

        void OnDialogConfirmed()
        {
            _dialogResult = "done (vsync " + (_dialogVsync != null && _dialogVsync.value)
                + ", vol " + Format(_dialogVolume != null ? _dialogVolume.value : 0f) + ")";
            RefreshConfirmedLabel();
        }

        void OnDialogCancelled()
        {
            _dialogVsync?.SetValueWithoutNotify(_dialogVsyncAtOpen);
            _dialogVolume?.SetValueWithoutNotify(_dialogVolumeAtOpen);
            _dialogAccent?.SetValueWithoutNotify(_dialogAccentAtOpen);

            _dialogResult = "cancelled";
            RefreshConfirmedLabel();
        }

        void OnOpenProfileClicked()
        {
            if (_profileDialog == null)
            {
                return;
            }

            _profileNameAtOpen = _profileName != null ? _profileName.value : string.Empty;
            _profileAgeAtOpen = _profileAge != null ? _profileAge.value : 0f;

            _profileDialog.Open = true;
        }

        void OnProfileConfirmed()
        {
            _dialogResult = "profile saved (\"" + (_profileName != null ? _profileName.value : string.Empty)
                + "\", " + Format(_profileAge != null ? _profileAge.value : 0f) + ")";
            RefreshConfirmedLabel();
        }

        void OnProfileCancelled()
        {
            _profileName?.SetValueWithoutNotify(_profileNameAtOpen);
            _profileAge?.SetValueWithoutNotify(_profileAgeAtOpen);

            _dialogResult = "profile cancelled";
            RefreshConfirmedLabel();
        }

        void OnOpenAboutClicked()
        {
            if (_aboutModal != null)
            {
                _aboutModal.Open = true;
            }
        }

        void OnAboutCloseClicked()
        {
            if (_aboutModal != null)
            {
                _aboutModal.Open = false;
            }
        }

        void OnEndpointChanged(ChangeEvent<string> evt)
        {
            if (evt == null)
            {
                return;
            }

            _endpointLive = evt.newValue;
            RefreshConfirmedLabel();
        }

        void OnEndpointConfirmed(string value)
        {
            _endpointConfirmed = value;
            RefreshConfirmedLabel();
        }

        void OnEndpointPortChanged(ChangeEvent<string> evt)
        {
            if (evt == null)
            {
                return;
            }

            RefreshConfirmedLabel();
        }

        void OnEndpointPortConfirmed(string value)
        {
            _endpointPortConfirmed = value;
            RefreshConfirmedLabel();
        }

        void RefreshConfirmedLabel()
        {
            if (_confirmedLabel == null)
            {
                return;
            }

            _confirmedLabel.text = FormatConfirmed();
        }

        string FormatConfirmed()
        {
            return "confirmed: "
                + Format(_opacityConfirmed)
                + " / "
                + Format(_rotationConfirmed)
                + "° / "
                + Format(_offsetConfirmed)
                + "px / over "
                + Format(_overshootConfirmed)
                + " / ("
                + Format(_positionConfirmed.x)
                + ", "
                + Format(_positionConfirmed.y)
                + ") / "
                + Format(_sizeConfirmed.x)
                + "×"
                + Format(_sizeConfirmed.y)
                + (_size != null && _size.KeepRatio ? " (locked)" : string.Empty)
                + " / visible " + _visibleConfirmed
                + " / loop " + _loopConfirmed
                + " / mute " + _muteConfirmed
                + " / fruit " + FruitLabel(_fruitConfirmed)
                + " / easing " + _easingConfirmed
                + " / search " + _searchConfirmed
                + " / angle " + Format(_angleConfirmed) + "°"
                + " / time " + Format(_timeConfirmed) + "F"
                + " (live " + Format(_timeLive) + "F, " + TimeDisplay() + ")"
                + " / playhead " + Format((float)_playheadFrame) + "F"
                + " / text \"" + _textConfirmed + "\""
                + " / tint #" + ColorUtility.ToHtmlStringRGBA(_tintConfirmed)
                + " / curve " + FormatCurve(_curveConfirmed)
                + " (live " + FormatCurve(_curveLive) + ")"
                + " / flash " + _flashClicks
                + " / plus " + _plusClicks
                + " / dialog " + _dialogResult
                + " / endpoint " + _endpointConfirmed
                + " (live " + _endpointLive + ")"
                + " / endpoint:port " + _endpointPortConfirmed;
        }

        // Shows how the value reads at the current rate, not the value itself
        string TimeDisplay()
        {
            return _time != null ? _time.DisplayText : "-";
        }

        static string FruitLabel(int index)
        {
            return index >= 0 && index < Fruits.Length ? Fruits[index] : "-";
        }

        static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // The pad has no numeric fields, so the label is where the actual control points are read off
        static string FormatCurve(Vector4 curve)
        {
            return "("
                + curve.x.ToString("F2", CultureInfo.InvariantCulture) + ","
                + curve.y.ToString("F2", CultureInfo.InvariantCulture) + ","
                + curve.z.ToString("F2", CultureInfo.InvariantCulture) + ","
                + curve.w.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }

        static string FormatAngle(float angle)
        {
            return angle.ToString("0.0", CultureInfo.InvariantCulture) + "°";
        }

        #endregion
    }
}
