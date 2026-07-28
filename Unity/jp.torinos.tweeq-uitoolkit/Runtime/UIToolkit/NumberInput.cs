using System;
using System.Collections.Generic;
using System.Globalization;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 数値入力欄。テキスト編集・レンジバー・横ドラッグによるスクラブを 1 つのフィールドで兼ねる。
    /// 内部計算は double、外部 API は UI Toolkit の流儀に合わせて float。
    /// </summary>
    [UxmlElement]
    public partial class NumberInput
        : VisualElement, INotifyValueChanged<float>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // 閾値に届かなくても長押しでスクラブへ入る（仕様 §1）
        const long HOLD_DRAG_DELAY_MS = 500;

        // ハンドルの掴み代。Vue の .handle:before（left/right とも -inputHeight/2）と同じ幅
        const float GRAB_ZONE_WIDTH = 24f;

        // 目盛りグリッドはこの間隔を下回ったら描かない（仕様 §5）
        const float MIN_TICK_GAP = 10f;

        // 左右端 1px は描かない（Vue の mask 相当）
        const float TICK_EDGE_MARGIN = 1f;
        const int MAX_TICKS = 512;

        const float HANDLE_WIDTH_IDLE = 1f;
        const float HANDLE_WIDTH_ACTIVE = 3f;
        const float HANDLE_OPACITY_IDLE = 0.3f;

        const float ARROW_SIZE = 4f;
        const float ARROW_OPACITY_IDLE = 0.3f;

        // 系統ごとの「理想の画面間隔」（px）。実際の間隔は D-2改2 の dv 量子化で
        // この 1/√10 〜 √10 倍へ丸められる
        const double SCALE_IDEAL_GAP_MIN = 1.0;
        const double SCALE_IDEAL_GAP_MAX = 1000.0;

        // 間隔が 10px まで詰まると目盛りが数百個になるが、その帯は
        // smoothstep(1,2,log10(screenGap)) が 0 なので出す必要がない。閾値以下は丸ごと捨てる。
        // 実間隔 > 10px が保証されることで、1 系統あたりの走査回数も width/10 に収まる
        const float SCALE_MIN_OPACITY = 0.01f;
        const int SCALE_TRAIN_COUNT = 3;
        const double SCALE_PRECISION_CYCLE = 3.0;

        // 位相は value/(baseSpeed*speed) px なので、値が巨大だとインデックスが int を溢れる。
        // 溢れる帯はどうせ画面外なので、その系統は丸ごと捨てる
        const double MAX_SCALE_TICK_INDEX = 1e9;

        // feedback-fixes-01.md C-1: 目盛りはドットではなく「到達値の数字」そのもの
        const float SCALE_LABEL_FONT_SIZE = 9f;

        // 幅は固定して中央揃えで x に載せる（テキスト幅を実測せずに中心を合わせるため）
        const float SCALE_LABEL_WIDTH = 48f;
        const float SCALE_LABEL_HEIGHT = 11f;

        // ラベル同士が重ならない最小間隔。これを割るなら 2 個／4 個に 1 個へ間引く
        const float SCALE_LABEL_MIN_GAP = 32f;
        const int SCALE_LABEL_MAX_STRIDE = 4;

        // C-1: 3 系統すべてが数字になるので、プールは「1 系統ぶん × 系統数」。
        // 間引きが効いている限り実際に使うのは 1 系統 10 個程度
        const int SCALE_LABEL_PER_TRAIN_MAX = 16;
        const int SCALE_LABEL_POOL_MAX = SCALE_TRAIN_COUNT * SCALE_LABEL_PER_TRAIN_MAX;

        // 粗い系統と同じ x に同じ値が来る細かい系統のラベルを捨てるときの許容誤差（C-1）
        const double SCALE_LABEL_DEDUPE_EPSILON = 1e-6;

        // Clamp 有効側の到達可能判定に使う相対許容誤差（D-2改2）。dv 倍して絶対値にする。
        // 端ちょうどの目盛り（v=min / v=max）が浮動小数誤差で消えないようにするための遊び
        const double SCALE_TICK_RANGE_EPSILON = 1e-6;

        // スクラブゾーンの上下ストリップ（仕様 §5: max((24 - 1em) / 2, 4px)）
        const float STRIP_MIN_HEIGHT = 4f;
        const float FALLBACK_FONT_SIZE = 12f;

        // 編集中のテキストの文字サイズ。A-6 で明示指定するため定数にする
        const float TEXT_FONT_SIZE = 12f;

        // grip のヒントアイコン。18px アイコンを scale 0.8 で描く前提のガイド幅
        const float ICON_SIZE = 18f;
        const float ICON_SCALE = 0.8f;
        const float GRIP_HINT_OPACITY = 0.5f;
        const float GRIP_HINT_HEAD = 3f;

        const float FOCUS_RING_WIDTH = 1f;
        const float DISABLED_BORDER_WIDTH = 1f;

        const float TEXT_PADDING = 4f;

        // 軸ラベル（仕様 §5-4）。掴み代 24px より狭くして、grip 全域を塞いだように見せない
        const float LEFT_LABEL_WIDTH = 18f;
        const float LEFT_LABEL_FONT_SIZE = 11f;
        const int LEFT_LABEL_MAX_LENGTH = 2;

        // TextField の内側要素（背景・枠を消して中央揃えにするために触る）
        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

        #region Fields

        float _value;

        // スクラブ／入力中の生値。量子化とスナップは出力側にだけ掛け、ここには残さない
        double _local;

        // 直近に組み立てた表示文字列。displayPrecision の入力にもなる（Vue の display ref 相当）
        string _display = string.Empty;

        // ComposeDisplayText のメモ。キー = (値のビット列, 桁数, スクラブ中か)
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

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        TweeqTheme _theme = TweeqTheme.Dark();

        VisualElement _barFill;
        VisualElement _backLayer;
        VisualElement _focusRing;
        VisualElement _displayOverlay;
        VisualElement _scaleLabelLayer;
        Label _prefixLabel;
        Label _valueLabel;
        Label _suffixLabel;
        Label _leftLabel;
        TextField _textField;
        VisualElement _textInput;
        TextElement _textElement;

        // 到達値ラベルは毎フレーム作らずプールを使い回す（feedback-fixes-01.md A-4 / C-1）
        readonly List<ScaleLabelSlot> _scaleLabels = new List<ScaleLabelSlot>();

        readonly ScaleTrain[] _scaleTrains = new ScaleTrain[SCALE_TRAIN_COUNT];

        // 系統を gap の降順（= opacity の降順）に並べた索引。重複排除の走査順に使う（C-1）
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

        // feedback-fixes-01.md C-2: 直近のフォーカスがポインタ由来かを覚える（CheckboxInput と同じ手）。
        // Tab 由来なら編集モードへ入り、クリック／ドラッグ由来なら従来どおり PointerUp の判定に任せる
        bool _focusFromPointer;

        // テキストのパースに失敗している間だけ立つ。次に有効な入力が来たら降ろす
        bool _parseFailed;

        #endregion

        #region Public API

        /// <summary>ドラッグ終了・Enter・blur で発火する。矢印キーでは発火しない。</summary>
        public event Action<float> Confirmed;

        /// <summary>検証済みの出力値。</summary>
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

        /// <summary>バーの下限。既定 -∞（バー無し）。</summary>
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

        /// <summary>バーの上限。既定 +∞（バー無し）。</summary>
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

        /// <summary>コミット値の量子化幅と矢印キーの増分。0 で無効。</summary>
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

        /// <summary>Q スナップの間隔。Shift の加速倍率も兼ねる。既定 10。</summary>
        public double SnapStep
        {
            get => _snapStep;
            set
            {
                _snapStep = value;
                Refresh();
            }
        }

        /// <summary>バーを表示するか。既定 true。</summary>
        // UXML 名は bar-visible。真偽値なので「bar」だけでは表示切替と読めない
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

        /// <summary>バーの塗りの基点。既定 0。</summary>
        public double BarOrigin
        {
            get => _barOrigin;
            set
            {
                _barOrigin = value;
                Refresh();
            }
        }

        /// <summary>値を Min でクランプするか。false ならバー表示域の外へ出られる。</summary>
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

        /// <summary>値を Max でクランプするか。</summary>
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

        /// <summary>静止時に表示する最大小数桁。既定 4。</summary>
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

        /// <summary>非フォーカス時のオーバーレイに前置される文字列。</summary>
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

        /// <summary>非フォーカス時のオーバーレイに後置される文字列。</summary>
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

        /// <summary>操作不能状態。</summary>
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
                    // 無効化の瞬間にドラッグが生きていると、離す手段が無くなる
                    CancelScrub(false);
                    SetEditing(false);
                }

                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>外部から与える不正値表示。</summary>
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

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
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
        /// 左端に常時表示する 1〜2 文字のラベル（軸名など。仕様 §5-4）。
        /// Vue の leftIcon 相当なので grip のヒントアイコンは抑止するが、掴み代そのものは残る。
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

        /// <summary>横方向グループでの位置。設定すると角丸が仕様 §1 の表どおりに潰れる。</summary>
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

        /// <summary>縦方向グループでの位置。</summary>
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

        /// <summary>ChangeEvent を発火せずに値を設定する。生値も同期される。</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = newValue;

            // 外部からの設定はドラッグ／編集セッションの外にあるので、累積器も揃えておく
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

            // 非編集時のドラッグ中に Q / Shift / Escape を受け取るため、ルート自身もフォーカス可能にする。
            // ここへフォーカスしてもテキスト編集には入らない（仕様 §6 の「DOM フォーカスは移らない」）
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

            // TextField より先に矢印・Enter・Escape を横取りするため TrickleDown で登録する
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);

            // 矢印キーは KeyDown とは別に NavigationMoveEvent も飛ばし、そちらがフォーカスを
            // 動かしてしまう（feedback-fixes-01.md A-5）。TrickleDown なら TextField 内の
            // TextElement が target のときもここで先に潰せる
            this.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);

            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        void BuildChildren()
        {
            // バーだけは実要素にする。Painter2D は色をトランジションできないが、
            // 仕様 §5 は背景色の 0.15s 遷移を要求しているため（インライン transition で実現する）
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

            // ドットの直上・値テキストの直下に挟む（追加順がそのまま描画順になる）
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

                // 1 文字ごとに値へ反映する必要がある（仕様 §3「数字/. 入力は live で値更新」）。
                // isDelayed = true だと Enter/blur まで ChangeEvent が来ないので false 固定にし、
                // Enter の確定はこちら側の KeyDown で処理する
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

            // 実際に字を描くのは unity-text-input の中の TextElement。
            // 縦潰れ（A-6）は input 側だけ直しても残るのでこちらにも同じ指定を掛ける
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

            // 編集中も見えている必要があるので、オーバーレイではなく独立レイヤに置く
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

            // フォーカスリングは要素の border で描く。ルート側に border を足すと
            // 絶対配置の子（＝バーとハンドル）が 1px 内側へずれてしまうため、別レイヤに分ける
            _focusRing = new VisualElement
            {
                name = "tweeq-number-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _focusRing.style.position = Position.Absolute;
            _focusRing.style.left = 0f;
            _focusRing.style.top = 0f;
            _focusRing.style.right = 0f;
            _focusRing.style.bottom = 0f;
            _focusRing.style.display = DisplayStyle.None;
            TweeqInputBoxStyles.SetBorderWidth(_focusRing, FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusRing);
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

            // 仕様 §5: 背景のみ 0.15s / cubic-bezier(0.4,0,0.2,1)。
            // UI Toolkit に同一カーブが無いので EaseInOutCubic で近似する（RotaryInput と同じ判断）
            TweeqInputBoxStyles.ApplyBackgroundTransition(this, _theme);

            if (_barFill != null)
            {
                TweeqInputBoxStyles.ApplyBackgroundTransition(_barFill, _theme);
            }

            if (_focusRing != null)
            {
                TweeqInputBoxStyles.SetBorderColor(_focusRing, _theme.Accent);
            }

            ApplyLeftLabelLayout();

            if (_textInput != null)
            {
                _textInput.style.backgroundColor = Color.clear;
                TweeqInputBoxStyles.SetBorderWidth(_textInput, 0f);
                TweeqInputBoxStyles.SetBorderColor(_textInput, Color.clear);
                _textInput.style.paddingLeft = TEXT_PADDING;
                _textInput.style.paddingRight = TEXT_PADDING;
                _textInput.style.marginLeft = 0f;
                _textInput.style.marginRight = 0f;
                _textInput.style.unityTextAlign = TextAnchor.MiddleCenter;

                // feedback-fixes-01.md A-6: 既定の USS が入れる上下 padding／auto 高さのままだと
                // 24px の枠内で行が潰れて読めなくなる。高さと文字サイズを明示して 24px を使い切る
                _textInput.style.height = Length.Percent(100f);
                _textInput.style.minHeight = 0f;
                _textInput.style.paddingTop = 0f;
                _textInput.style.paddingBottom = 0f;
                _textInput.style.marginTop = 0f;
                _textInput.style.marginBottom = 0f;
                _textInput.style.fontSize = TEXT_FONT_SIZE;
                _textInput.style.whiteSpace = WhiteSpace.NoWrap;
            }

            if (_textElement != null)
            {
                // A-6: input の中の TextElement も同じ扱いにする
                _textElement.style.height = Length.Percent(100f);
                _textElement.style.minHeight = 0f;
                _textElement.style.paddingTop = 0f;
                _textElement.style.paddingBottom = 0f;
                _textElement.style.marginTop = 0f;
                _textElement.style.marginBottom = 0f;
                _textElement.style.fontSize = TEXT_FONT_SIZE;
                _textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            if (_textField != null)
            {
                _textField.style.unityTextAlign = TextAnchor.MiddleCenter;
                _textField.style.fontSize = TEXT_FONT_SIZE;

                // キャレット・選択色は USS 既定（黒）のままだと暗背景で見えない。
                // selectionColor は obsolete だが、推奨の --unity-selection-color は
                // C# からインスタンス単位で設定できない（テーマは TweeqTheme 駆動）ため使い続ける
#pragma warning disable 618
                _textField.textSelection.cursorColor = _theme.Text;
                _textField.textSelection.selectionColor = _theme.AccentSoft;
#pragma warning restore 618

                // A-6: root の inset 0 いっぱいに置く。BaseField 既定の余白を残さない
                _textField.style.paddingTop = 0f;
                _textField.style.paddingBottom = 0f;
                _textField.style.paddingLeft = 0f;
                _textField.style.paddingRight = 0f;
                _textField.style.minHeight = 0f;
                _textField.style.alignItems = Align.Stretch;
            }

            ApplyFonts();
        }

        // 数字が並ぶ箇所だけ FontNumeric にする（m7-wave2-spec.md のマッピング）。
        // Prefix / Suffix は単位語なので UI フォント側に残す。
        // テーマ適用時にしか呼ばないので、スクラブ中はここを通らない
        void ApplyFonts()
        {
            if (_theme == null)
            {
                return;
            }

            FontDefinition numeric = _theme.FontNumeric;

            TweeqFonts.Apply(_valueLabel, numeric);
            TweeqFonts.Apply(_textField, numeric);

            // TextField の中身は自前で fontSize を明示している階層なので、
            // 継承だけに頼らず input / TextElement にも同じ指定を降ろす
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

            // フォーカスリングは別レイヤなので同じ角丸を掛け直す
            TweeqInputBoxStyles.ApplyCornerRadius(
                _focusRing, _theme, _inlinePosition, _blockPosition);
        }

        // 軸ラベルを置いた分だけ、テキストと表示オーバーレイを左から逃がす
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

        // 値が [min, max] の内側にあるか。外側にはハンドルが無いので、掴み代の作り方が変わる
        bool InsideRange => _min <= _value && _value <= _max;

        // feedback-fixes-01.md A-1: スクラブ中は常に表示する。
        // 旧（Vue 準拠）は step && clampMin && clampMax && range 完備なら非表示だったが、
        // バー付きでは D-2改2 のハンドルアンカーによりバー座標に乗った「値のものさし」になるので、
        // step 付きレンジ完備のフィールドでも出す意味がある
        bool ShowTweakScale => true;

        // 修飾キー由来の感度。TweakGesture 内部の keySpeed と同じ式（表示桁の計算に使う）
        double KeySpeed => (_altHeld ? 0.1 : 1.0) * (_shiftHeld ? Math.Max(_snapStep, 1.0) : 1.0);

        double CurrentSpeed => KeySpeed * _gesture.Speed;

        // feedback-fixes-01.md D-1: A-2（バー付きも step/20 に統一）を取り消し、バー付きは
        // Vue 本来の「バー幅＝レンジ」へ戻す。speed=1 でハンドルがマウスに 1:1 で吸い付くほうが
        // バー操作としては素直で、speed=1 で目盛りをバー座標に一致させる D-2改2 とも噛み合う。
        // レンジなしは A-2 のまま（step/20 or 1）
        double ScrubBaseSpeed => NumberLogic.BaseSpeed(BarVisible, _min, _max, Width, _step);

        // feedback-fixes-01.md D-1 補足: Vue のレンジ有り minSpeed（step の px 密度由来）は
        // Opacity のような「1step が 1px 以上」のバーで 1 に張り付き、縦ドラッグの感度調整が
        // 死んでしまう。開始 1:1（maxSpeed=1）は維持しつつ、下限だけレンジなしと同じ
        // 10^-precision まで下げられるようにする（意図的逸脱）
        double ScrubMinSpeed =>
            NumberLogic.MinSpeed(false, _min, _max, Width, _step, _precision);

        double ScrubMaxSpeed => NumberLogic.MaxSpeed(BarVisible);

        // スクラブ中に「画面 1px が何値ぶんか」。目盛りの位相・値の刻み・到達値の共通分母（D-2改2）
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

            // C-2: this.Focus() や TextField のキャレット配置より前に立てる。
            // 降ろすのは FocusOut のときだけ（＝「今のフォーカスはポインタで始まった」を意味する）
            _focusFromPointer = true;

            Vector2 position = LocalPosition(evt);

            // 編集中はスクラブゾーンの上だけがドラッグ開始点。それ以外は
            // 捕まえも止めもせず TextField のキャレット操作に委ねる（仕様 §1）
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
                // Q / Shift / Escape を受け取るためにルートへフォーカスする。
                // テキスト編集へは入らないので、表示はオーバーレイのまま
                this.Focus();
            }

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);

                // 閾値に届かなくても長押しでスクラブへ入る
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
                    // ハンドルの太りはポインタ位置に追従するので、ホバー中は毎回描き直す
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
                // 閾値未満で離した＝クリック。ここで初めてテキスト編集へ入る（仕様 §1）
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
            // 幅が変わると baseSpeed も目盛り間隔も変わる
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

            // 非フォーカスで範囲内・ハンドル以外を押した場合だけ、押した位置へ即ジャンプする（仕様 §1）。
            // Fit は t を [0,1] に畳んでから lerp するので結果は必ず [min,max] 内。
            // D-3 のクランプを別途掛ける必要はない
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

            // feedback-fixes-01.md D-1: バー付きは (max-min)/width、レンジなしは step/20。
            // 幅は Scrub*Speed 側で毎フレーム読む（レイアウト変化に追従させる）
            double baseSpeed = ScrubBaseSpeed;
            double minSpeed = ScrubMinSpeed;
            double maxSpeed = ScrubMaxSpeed;

            GestureUpdate update = _gesture.Update(
                dx, dy, baseSpeed, modifiers, _snapStep, minSpeed, maxSpeed);

            _local += update.Delta;

            // feedback-fixes-01.md D-3: Clamp が有効な側は、バー有無に関わらずドラッグ中も
            // 生値ごと畳む（Vue は local 非クランプだが、範囲外の数字が見えるのは事故のもと）。
            // Clamp 無効な側は据え置きなのでオーバーシュートでき、範囲外矢印がそのまま出る
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
                // テキスト編集へ戻す。打ち直しがそのまま置き換えになるよう選択し直す
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

            // ドラッグ中に通知した値を巻き戻すので、こちらも通知する
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

        // 生値を clamp → step → snap に通して出力へ反映する（仕様 §2: コミット時だけでなく毎フレーム）
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
                // display:none のままでは Focus() が通らないので、必ず表示を先に切り替える
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

            // フォーカスが確定した次のフレームでないと選択範囲が上書きされる（Vue の nextTick 相当）
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

        // 式入力モードはスコープ外（仕様 §7-2）。プレーンな数値パースのみ行う
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

            // パース失敗時は値を据え置き、次に有効な入力が来るまで invalid 表示にする
            _parseFailed = true;
        }

        // Enter / blur の確定。表示を出力値で組み直し、Confirmed を発火する
        void Commit()
        {
            if (_editing && _textField != null)
            {
                // 打鍵ごとに反映済みのはずだが、取りこぼしがあっても Enter/blur で必ず確定させる
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

            // feedback-fixes-01.md C-2: Tab でルートにフォーカスが来たらクリックと同じ編集状態へ入る
            if (!ReferenceEquals(evt.target, this))
            {
                return;
            }

            ScheduleEnterEditingFromFocus();
        }

        // ポインタ由来かどうかは「同じフレームの PointerDown を処理し終えたあと」でないと確定しない
        // （パネル側のフォーカス移動と自前のハンドラのどちらが先かに依存しないようにする）。
        // schedule はそのフレームのイベント処理がすべて終わってから走るので、そこで判定する（C-2）
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

                // 1 tick の間に Tab がもう一度押されていたら、奪い返さない
                if (this.focusController == null || !ReferenceEquals(this.focusController.focusedElement, this))
                {
                    return;
                }

                // クリック経路（OnPointerUp）と同じ入口。SelectAll もこの中で予約される
                BeginEditing();
            }).StartingIn(0);
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // C-2: フォーカスが抜けたらポインタ由来フラグも畳む。
            // 次に来る FocusIn は「新しいフォーカスセッションの開始」として判定し直す
            _focusFromPointer = false;

            if (!IsTextTarget(evt.target))
            {
                // ルート自身のフォーカスが外れた場合は修飾キーの押しっぱなしだけ解除する
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
                        // スナップの切り替えは出力へ即座に反映する（生値は動かさない）
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

        // feedback-fixes-01.md A-5: ↑/↓ は値変更だけ。UI Toolkit は矢印キーで
        // NavigationMoveEvent も飛ばすので、KeyDown の StopPropagation だけではフォーカスが移る。
        // Next/Previous（Tab）は仕様 §3 の「Tab で blur → confirm」を残すため通す
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
                    // 編集中の ←→ はキャレット移動として TextField が処理する。
                    // 非編集時はルートにフォーカスがあるだけなので、飛ばさず食い止める
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

            // Unity 6 で「フォーカス移動そのもの」を止められるのはこちら（PreventDefault は非推奨）
            this.focusController?.IgnoreEvent(evt);
        }

        // 仕様 §3。Confirmed は発火しない
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

        // force=false のときは編集中のテキストを壊さない（Vue の watcher と同じ条件）
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

            // ドラッグ中の表示だけは生値（末尾ゼロ維持）。桁数がそのまま感度のフィードバックになる
            double source = _scrubbing ? _local : _value;

            // Format は入力が同じなら結果も同じ純粋関数。ポインタが止まっているフレームでも
            // Refresh は走るので、キーが一致する限り文字列生成ごと省く。
            // _display はテキスト入力でも書き換わるため、キャッシュは別フィールドに持つ
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

            // ラベルは要素なので、描画コールバック（generateVisualContent）ではなくここで置く。
            // レイアウトを触る処理を repaint 中に混ぜると再レイアウトのループになる
            UpdateScaleLabels();

            if (_focusRing != null)
            {
                _focusRing.style.display = _editing && !_disabled
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            _backLayer?.MarkDirtyRepaint();
        }

        void UpdateBackground()
        {
            if (_disabled)
            {
                // 仕様 §5: 背景透明 + 1px Border のインセット枠
                this.style.backgroundColor = Color.clear;
                TweeqInputBoxStyles.SetBorderWidth(this, DISABLED_BORDER_WIDTH);
                TweeqInputBoxStyles.SetBorderColor(this, _theme.Border);
                return;
            }

            TweeqInputBoxStyles.SetBorderWidth(this, 0f);
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
                // レイアウトは維持したまま隠す（仕様 §5）
                _barFill.style.visibility = Visibility.Hidden;
                return;
            }

            _barFill.style.visibility = Visibility.Visible;

            // 塗りは検証済みの出力値で決める（生値は使わない）
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

        // ハンドル中心の幅 24px。端では 24px 丸ごとがフィールド内に収まるよう寄せる（Vue の zoneStyle）
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

        // 上下のストリップ（中央はテキスト選択用に空ける）
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

        // 仕様 §1/§5: 編集中にドラッグを開始できる領域
        bool IsInScrubZone(Vector2 position)
        {
            if (_disabled)
            {
                return false;
            }

            if (!BarVisible)
            {
                // unranged は左端 24×24 の grip
                return position.x <= GRAB_ZONE_WIDTH;
            }

            if (!InsideRange)
            {
                // 範囲外はハンドルが無いので全幅ストリップが掴み代になる
                return IsInStrip(position.y);
            }

            // 端に張り付いたハンドルは角丸に食われるので、上下ではなく全高 1 本にする
            bool handleAtEdge = _value <= _min || _value >= _max;
            if (!IsInGrabZone(position.x))
            {
                return false;
            }

            return handleAtEdge || IsInStrip(position.y);
        }

        // キャプチャ中も座標系がぶれないよう、パネル座標からローカルへ変換する
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

            // feedback-fixes-01.md C-1: スケールのドット描画は廃止し、目盛りは
            // UpdateScaleLabels が置く数字ラベルだけになった（ここはバー・ハンドル・矢印のみ）
            PaintTicks(painter, width, height);
            PaintHandle(painter, width, height);
            PaintOutOfRangeArrows(painter, width, height);

            // 軸ラベルが左端を占めている間はヒントを重ねない（Vue の leftIcon と同じ扱い）
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

                // 1px 幅の線は中心を半ピクセルずらすと [x, x+1] を覆う
                float px = (float)x + 0.5f;
                painter.MoveTo(new Vector2(px, 0f));
                painter.LineTo(new Vector2(px, height));
            }

            painter.Stroke();
        }

        void PaintHandle(Painter2D painter, float width, float height)
        {
            if (!BarVisible)
            {
                return;
            }

            // 位置は検証済みの出力値。範囲外はクランプせず overflow でクリップさせる
            float x = (width - 1f) * (float)Invlerp(_min, _max, _value);

            bool thick = _scrubbing || (_hovered && InsideRange && IsInGrabZone(_pointerPosition.x));
            float handleWidth = thick ? HANDLE_WIDTH_ACTIVE : HANDLE_WIDTH_IDLE;

            // 3px は中心へ拡張する（Vue の margin-left: -1px）
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

            // Vue の CSS（border-right/left 三角）どおり頂点が外側＝「値はこの先にある」を指す。
            // 初版仕様書の「内向き」は誤記だった
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

        /// <summary>1 フレーム分の目盛り列。3 系統が同じ原点を共有するのでまとめて組む。</summary>
        struct ScaleTrain
        {
            // 目盛り 1 つぶんの「値」の刻み。D-2改2 で 10 のべき乗に量子化されるので、
            // 粗い系統の ValueGap は必ず細かい系統の整数倍になる（重複除去がこれで成立する）
            public double ValueGap;

            // 画面上の間隔（px）= ValueGap / valuePerPixel
            public double ScreenGap;

            // 値 0 の目盛りが載る x
            public double OriginX;

            public float Opacity;

            // 画面左端（x=0）以降で最初に来る目盛りの序数。値 0 の目盛りが k=0
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
        /// プールされたラベル 1 個ぶんの直近の状態。text / color の再設定はレイアウトや
        /// 頂点の作り直しを誘発するので、変化したときだけ書き戻すために持つ（C-1 の負荷対策）。
        /// TickValue / Digits は文字列比較の手前で弾くための数値キー。これが一致する限り
        /// Format 自体を呼ばないので、感度クロスフェードで dv が動いたときだけ文字列が作られる。
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

        // 縦ドラッグ中（感度変更中）は色を TextSubtle 側へ寄せる（仕様 §5）
        float ScaleOffsetWeight => (float)TweeqMath.Clamp(_gesture.HorizontalWeight, 0.0, 1.0);

        Color ScaleColor => Color.Lerp(_theme.Accent, _theme.TextSubtle, ScaleOffsetWeight);

        // 有効な系統だけを _scaleTrains の先頭へ詰め、その本数を返す
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

            // 位相は baseSpeed でも割る（gestureSpeed だけで割ると目盛りがマウス移動量と 1:1 で流れない）
            double valuePerPixel = ScrubValuePerPixel;
            if (!TweeqMath.IsFinite(valuePerPixel) || valuePerPixel <= 0.0)
            {
                return 0;
            }

            // feedback-fixes-01.md D-2改2: バー付きはハンドル位置がアンカー。
            // x(v) = anchorX + (v - local)/vpp なので、speed=1（vpp=(max-min)/width）のとき
            // x(v) = (v-min)/(max-min)*width ＝ バー座標と完全一致する。
            // レンジなしは従来どおり中央アンカー。表示値 _value は step 量子化で飛ぶので生値 _local を使う
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

                // D-2改2: 値の刻みを 10 のべき乗へ量子化する。ラベルが k*dv の真値になり、
                // 0.348 刻みのような中途半端な数が並ばなくなる（画面間隔は理想の 1/√10〜√10 倍）
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

                // D-2改2: 濃さは precision ではなく「実際に見える間隔」から決める。
                // 量子化で間隔が理想からずれるので、precision 由来だと濃さと密度が食い違う
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

        // feedback-fixes-01.md C-1 / D-2改2: 系統を ValueGap の降順に並べ替えて _scaleOrder に入れる。
        // 全系統が同じ valuePerPixel を共有するので ValueGap 降順＝ ScreenGap 降順であり、
        // opacity = sqrt(smoothstep(1,2,log10(screenGap))) は単調増加なので opacity 降順にもなる。
        // 重複排除で「粗い＝濃いほうを残す」がこの順序だけで成立する
        void SortScaleTrainsByValueGap(int trainCount)
        {
            for (int i = 0; i < trainCount; i++)
            {
                _scaleOrder[i] = i;
            }

            // 高々 3 要素なので挿入ソートで十分（毎フレーム走るのでアロケーションを作らない）
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

        // C-1 / D-2改2: 粗い系統の目盛りは細かい系統の目盛りの部分集合なので、同じ x に同じ値が
        // 二重に出る。判定は px オフセットではなく目盛りの値そのもので行う（dv が 10 のべき乗に
        // 量子化されたので、粗い系統の dv は必ず細かい系統の dv の整数倍＝十進の入れ子になる）。
        // 全系統が原点を共有するため、値の一致はそのまま x の一致でもある
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

        // ラベルが重ならないよう 1 / 2 / 4 個おきに間引く。4 個おきでも足りなければ諦める（A-4）
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

        // feedback-fixes-01.md C-1 / D-2改2: 各目盛りの位置に「そこまでドラッグしたときの到達値」を出す。
        // 3 系統すべてが数字になり、系統の opacity がそのまま数字のフェードになる
        void UpdateScaleLabels()
        {
            if (_scaleLabelLayer == null)
            {
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

            // D-2改2: Clamp が効いている側の範囲外は D-3 で内部値ごと畳まれる＝到達不能なので描かない
            bool clipMin = _clampMin && TweeqMath.IsFinite(_min);
            bool clipMax = _clampMax && TweeqMath.IsFinite(_max);

            // C-1: 数字がドットに代わる主目盛りになったので、注釈位置ではなく縦中央に置く
            float top = Mathf.Max((height - SCALE_LABEL_HEIGHT) * 0.5f, 0f);

            int used = 0;
            for (int order = 0; order < trainCount && used < SCALE_LABEL_POOL_MAX; order++)
            {
                ScaleTrain train = _scaleTrains[_scaleOrder[order]];

                // C-1: 薄すぎる系統はラベルごと出さない（BuildScaleTrains でも弾いているが、
                // ラベル側の閾値としても明示しておく）
                if (train.Opacity < SCALE_MIN_OPACITY)
                {
                    continue;
                }

                int stride = LabelStride(train.ScreenGap);
                if (stride <= 0)
                {
                    continue;
                }

                // dv は 10 のべき乗なので、その桁数で出せば表示値＝目盛りの真値になる（D-2改2）
                int digits = TweeqMath.PrecisionOf(train.ValueGap);
                double rangeEpsilon = train.ValueGap * SCALE_TICK_RANGE_EPSILON;

                Color color = baseColor;

                // 列の opacity をそのままアルファに掛ける。感度を振ると系統が入れ替わるので、
                // これだけで数字がクロスフェードする（C-1）
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

                    // 目盛り k の値。x(v) = OriginX + (v - local)/vpp の逆写像そのものなので、
                    // ここまでドラッグすれば実際に v_k に届く（速度によらず保存される）
                    double tickValue = k * train.ValueGap;

                    // 到達不能な目盛りは値の増加方向へ並ぶので、上限側は打ち切ってよい
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

        // 位置は毎フレーム動くので素直に書く。text と color だけ前回値と比べて据え置く（C-1）
        void ApplyScaleLabel(int index, double tickValue, int digits, Color color, float left, float top)
        {
            ScaleLabelSlot slot = GetScaleLabel(index);
            Label element = slot.Element;
            if (element == null)
            {
                return;
            }

            // 文字列を作る前に数値キーで弾く。目盛りは値も桁数も 10 のべき乗刻みで安定しているので、
            // ドラッグ中の大半のフレームはここで抜ける
            if (slot.Digits != digits || !TweeqFormat.SameValueBits(slot.TickValue, tickValue))
            {
                string text = TweeqFormat.Format(tickValue, digits, false);

                // 桁数違いでも trim 後に同じ文字列になることがあるので、element への書き戻しは従来どおり比較する
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

                // プールの伸長は初回だけなので、ここでフォントを貼ってもスクラブ中の常設コストにならない
                if (_theme != null)
                {
                    TweeqFonts.Apply(created, _theme.FontNumeric);
                }

                _scaleLabelLayer.Add(created);

                // Text は null、Digits は -1（PrecisionOf が返さない値）にしておくと、
                // 最初の 1 回だけ確実に Format と text= が走る
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

        // unranged の grip ヒント（⇄）。フォント依存を避けるため図形で描く
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

        // ドット序数の間引き判定用。C# の % は負値で負を返すので符号を揃える
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
