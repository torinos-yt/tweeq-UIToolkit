using System.Globalization;
using Tweeq.UIToolkit;
using TweeqDemo.CustomWidgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace TweeqDemo
{
    /// <summary>
    /// tweeq 本家 index.html デモの移植。RotaryInput 1 個と、
    /// ParameterGrid に載せた NumberInput 3 種 + 折りたたみグループの PositionInput / SizeInput を並べる。
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

        // AngleInput の確認用。±180 は数値欄側のレンジで、ノブは多回転のまま回せる
        const float INITIAL_ANGLE = 30f;
        const double ANGLE_LIMIT = 180.0;
        const double ANGLE_STEP = 0.1;
        const double ANGLE_SNAP = 45.0;

        // 比率ロックの追従が一目で分かるよう、初期値は 16:9 にしておく
        static readonly Vector2 INITIAL_SIZE = new Vector2(320f, 180f);

        // feedback-fixes-01.md D-4: 範囲外矢印の確認用。両側 Clamp を切って [0,1] の外へ出せる行
        const float OVERSHOOT_INITIAL = 0.5f;

        // ParameterGroup の開閉状態はこの名前で PlayerPrefs に残る
        const string VECTOR_GROUP_NAME = "tweeqDemo.vector";
        const string BOOLEAN_GROUP_NAME = "tweeqDemo.boolean";
        const string SELECT_GROUP_NAME = "tweeqDemo.select";
        const string DIALOG_GROUP_NAME = "tweeqDemo.dialog";
        const string CUSTOM_GROUP_NAME = "tweeqDemo.custom";

        // 外部 asmdef 製ウィジェット（EndpointInput）の実演用の初期値
        const string INITIAL_ENDPOINT = "192.168.0.1";
        const string INITIAL_ENDPOINT_PORT = "10.0.0.8:8080";

        // Vue PaneModalTabs の width:44rem は root font-size 依存の曖昧値なので、
        // デザイン単位 rem=12 換算の 528px を採用（m8-modal-tabs-spec.md §E）
        const float DIALOG_WIDTH = 528f;

        // アクティブタブは "tweeq.demo-settings.active" に永続化される
        const string SETTINGS_TABS_NAME = "demo-settings";

        const string OPACITY_TOOLTIP = "0〜1 の不透明度。左右ドラッグでスクラブ、上下で感度";

        const string INITIAL_TEXT = "Hello, tweeq";

        // ColorInput の初期値はテーマの accent から作る。α を落としてあるのは、
        // スウォッチのチェッカーボード（α 可視化）が一目で分かるようにするため
        const float TINT_ALPHA = 0.8f;

        static readonly string[] Fruits = { "Apple", "Banana", "Cherry" };

        static readonly string[] Easings = { "Linear", "Ease In", "Ease Out", "Ease In Out" };

        // ファジー検索の動作確認用に多めの候補（"eio" → Elastic In Out などのサブシーケンス確認）
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
        StringInput _text;
        ColorInput _tint;
        ButtonInput _flashButton;
        ButtonInput _plusButton;

        ButtonInput _openSettingsButton;
        TweeqModalDialog _settingsDialog;
        TweeqTabs _settingsTabs;
        SwitchInput _dialogVsync;
        NumberInput _dialogVolume;
        ColorInput _dialogAccent;

        // ModalComplex 風（本文=フォーム・Save/Cancel 均等割）のサンプル
        ButtonInput _openProfileButton;
        TweeqModalDialog _profileDialog;
        StringInput _profileName;
        NumberInput _profileAge;

        // 外部 asmdef（Tweeq.Demo.CustomWidgets）で作ったカスタムウィジェットの実演
        EndpointInput _endpoint;
        EndpointInput _endpointWithPort;

        // 素の PaneModal 風（閉じる責務は所有者・外側クリックはバウンスのみ）のサンプル
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
        string _textConfirmed = INITIAL_TEXT;
        Color _tintConfirmed = Color.white;
        int _flashClicks;
        int _plusClicks;

        // ダイアログの Cancel ロールバックは Scheme が無いので利用者責務（m8 仕様 §B）。
        // その実演として、開いた瞬間の値をここへ退避して Cancelled で書き戻す
        bool _dialogVsyncAtOpen;
        float _dialogVolumeAtOpen;
        Color _dialogAccentAtOpen;
        string _dialogResult = "-";

        string _profileNameAtOpen = string.Empty;
        float _profileAgeAtOpen;

        string _endpointConfirmed = INITIAL_ENDPOINT;
        string _endpointPortConfirmed = INITIAL_ENDPOINT_PORT;
        string _endpointLive = INITIAL_ENDPOINT;

        #endregion

        #region Unity

        void OnEnable()
        {
            if (_document == null)
            {
                Debug.LogError($"{nameof(TweeqRotaryDemo)}: UIDocument が未設定。", this);
                return;
            }

            _root = _document.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError($"{nameof(TweeqRotaryDemo)}: rootVisualElement が取得できない。", this);
                return;
            }

            // Vue デモは accentColor '#0000ff' だが light モード前提の色。dark 背景では
            // Radix が seed をそのまま step9 に据えるため暗すぎる → 明るめの青へ変更（2026-07-27 ユーザー裁定）
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
                // ツールチップは共有インスタンスに参照が残るので、必ず外す
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

                // Generate はデモ側のデリゲートなので、参照を残さず切っておく
                _shuffle.Generate = null;
                _shuffle = null;
            }

            if (_angle != null)
            {
                _angle.Confirmed -= OnAngleConfirmed;
                _angle = null;
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
                // backdrop はオーバーレイ層（rootVisualElement の外）に居るので、
                // _root.Clear() では降りない。明示的に閉じてから手放す
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
                // rootVisualElement は UIDocument が使い回すので、C-3 の抑止も自分で外す
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

            // feedback-fixes-01.md C-3: このパネルでは矢印キーを値操作専用にする。
            // ライブラリ既定ではないので、アプリ側でオプトインする
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

            // モーダル本体。ツリー内では何も描かず、Open でオーバーレイ層に載る
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

            // ツールチップの動作確認用。アプリ全体で 1 インスタンスを使い回す（popover-spec.md）
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

            // Min/Max を無限のままにすると unranged（バー無し・grip でスクラブ）になる
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

            // feedback-fixes-01.md D-4: Clamp を両側とも切ったバー付き。
            // レンジ外へ出ると範囲外矢印が出る（D-3 でクランプ有効側は畳まれるので、その対比にもなる）
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
            grid.Add(BuildDialogGroup());
            grid.Add(BuildCustomGroup());

            // Theme は Grid が配下の Parameter / Heading / Group へ配る
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

            // keepRatio は既定 on。両軸を同時に別比率へ書き換えると自動で外れる
            _size = new SizeInput
            {
                Theme = _theme,
                Step = new[] { VECTOR_STEP },
                Min = new[] { 0.0 },
            };
            _size.SetValueWithoutNotify(INITIAL_SIZE);
            _size.Confirmed += OnSizeConfirmed;

            // 自動解除は「気付いたら外れている」挙動なので、ラベル側にも出す
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

            // 開いて文字を打つとファジー検索で絞り込まれる（例: "eio" → Elastic In Out）
            _search = new DropdownInput<string>(SearchEasings)
            {
                Theme = _theme,
            };
            _search.SetValueWithoutNotify(SearchEasings[0]);
            _search.Confirmed += OnSearchConfirmed;

            // 押すたびに候補からランダムで 1 つ引く。Dropdown と 1 つながりの箱に見せる
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

            // ノブと数値欄の複合。欄側は ±180 だが、ノブは多回転でその外へも回せる
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

            group.RefreshContentGaps();
            return group;
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

        // 外部 asmdef のカスタムウィジェットがライブラリ製の行と同じ見た目・操作感で並ぶことの実演
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

        // PaneModalTabs 相当の構成: タイトル + 縦タブ + 右寄せフッター（Done）
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

            // モーダルの上にピッカー（ポップオーバー）が重なる構成の確認も兼ねる
            TweeqTab appearance = new TweeqTab("Appearance");
            _dialogAccent = new ColorInput();
            _dialogAccent.SetValueWithoutNotify(_theme.Accent);

            ParameterGrid appearanceGrid = new ParameterGrid();
            appearanceGrid.Add(BuildRow("Accent", _dialogAccent));
            appearance.Add(appearanceGrid);
            _settingsTabs.Add(appearance);

            // disabled タブの見た目とスキップ（キーボード移動・既定解決）の確認用。
            // 選択できないのはデモの意図（IsDisabled=true）で、名前でそれが伝わるようにする
            _settingsTabs.Add(new TweeqTab("Advanced (disabled)") { Id = "advanced", IsDisabled = true });

            _settingsDialog.Add(_settingsTabs);

            // Theme は最後に。ダイアログが backdrop / balloon / タブ配下まで配る
            _settingsDialog.Theme = _theme;
            return _settingsDialog;
        }

        // PaneModalComplex 相当の構成: フォーム本文 + Save/Cancel 均等割フッター
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

        // 素の PaneModal 相当: キーもフッターも無く、閉じるのは所有者（この OK ボタン）だけ。
        // 外側クリックで閉じずにバウンスする「閉じないモーダル」の体験用
        TweeqModal BuildAboutModal()
        {
            _aboutModal = new TweeqModal();

            // 素の Label は ITweeqThemed ではなくテーマ配布が当たらないので、文字色は自分で塗る
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

        // 「Flash me」と narrow な「+」を 1 つの InputContainer に並べる
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

            // narrow は自前の詰まった幅が持ち味なので伸ばさない
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

            // 次のシャッフルは「今の値」を種に取るので、こちらにも現在値を持たせておく
            _shuffle?.SetValueWithoutNotify(_searchConfirmed);
            RefreshConfirmedLabel();
        }

        void OnShuffleConfirmed(string value)
        {
            _searchConfirmed = value ?? string.Empty;

            // Dropdown 側は結果を映すだけ。通知し返すと同じ確定が 2 周する
            _search?.SetValueWithoutNotify(_searchConfirmed);
            RefreshConfirmedLabel();
        }

        void OnAngleConfirmed(float value)
        {
            _angleConfirmed = value;
            RefreshConfirmedLabel();
        }

        // 同じ値を引くと「押しても変わらない」ように見えるので、その時だけ隣へずらす
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

        void OnFlashClicked()
        {
            _flashClicks++;

            // 自分自身を光らせる（仕様 §3 の命令的 Flash）
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

            // Cancel ロールバック用の退避（m8 仕様 §B: 値の復元は利用者責務）
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
                + " / text \"" + _textConfirmed + "\""
                + " / tint #" + ColorUtility.ToHtmlStringRGBA(_tintConfirmed)
                + " / flash " + _flashClicks
                + " / plus " + _plusClicks
                + " / dialog " + _dialogResult
                + " / endpoint " + _endpointConfirmed
                + " (live " + _endpointLive + ")"
                + " / endpoint:port " + _endpointPortConfirmed;
        }

        static string FruitLabel(int index)
        {
            return index >= 0 && index < Fruits.Length ? Fruits[index] : "-";
        }

        static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string FormatAngle(float angle)
        {
            return angle.ToString("0.0", CultureInfo.InvariantCulture) + "°";
        }

        #endregion
    }
}
