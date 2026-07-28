using System;
using System.Collections.Generic;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

// 選択肢の行は Label で作る。他の Input と表記を揃えるため別名にする
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// ドロップダウン選択（popover-spec.md「DropdownInput&lt;T&gt;」）。
    /// 閉状態は入力枠 1 行、開状態は macOS 風に「選択中の option がフィールドに重なる」位置へ
    /// ポップアップを出す。
    ///
    /// 開閉の状態機械（<see cref="Open"/> / <see cref="Close"/> / <see cref="Commit"/> /
    /// <see cref="Cancel"/> / <see cref="MoveSelection"/> / <see cref="PerformPointerUp"/>）は
    /// panel 非依存にしてある。ポップアップの表示はその上に乗るだけなので、panel 未接続でも
    /// 例外を出さずに状態だけが進む（EditMode テストはこの層を叩く）。
    /// </summary>
    public class DropdownInput<T> : VisualElement, INotifyValueChanged<T>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Vue InputDropdown.vue 53-55 の定数。SELECT_CHROME（=2）は Vue の .select の
        // margin+border 実測値なので、こちらでは実際の padding+border を測って渡す
        const float VIEWPORT_MARGIN = 6f;
        const float AUTO_SCROLL_SPEED = 8f;

        // Vue は requestAnimationFrame。UI Toolkit のスケジューラには「毎フレーム」が無いので
        // 60fps 相当の間隔で近似する（Every の item は使い回すので確保は 1 回だけ）
        const long AUTO_SCROLL_INTERVAL_MS = 16;

        // Vue onPointerupWhileOpen: 開いてこの時間以内の pointerup は「押しっぱなしドラッグ選択の
        // 途中」とみなして無視する。超えていれば確定して閉じる
        const long CONFIRM_GRACE_MS = 500;

        // Vue の $chevron-width = .7 * inputHeight
        const float CHEVRON_WIDTH_RATIO = 0.7f;
        const float CHEVRON_IDLE_OPACITY = 0.4f;
        const float CHEVRON_TRIANGLE_WIDTH = 8f;
        const float CHEVRON_TRIANGLE_HEIGHT = 4f;
        const float CHEVRON_TRIANGLE_GAP = 3f;

        const float FOCUS_RING_WIDTH = 1f;
        const float DISABLED_BORDER_WIDTH = 1f;
        const float POPUP_BORDER_WIDTH = 1f;

        // option 行の左右余白（Vue は左 .5em / 右 chevron 幅だが、中央揃えなので左右対称にする）
        const float OPTION_PADDING = 6f;

        const float TEXT_FONT_SIZE = 12f;

        // box-shadow が無いので、半透明角丸を数枚重ねて 0 0 20px を近似する（popover-spec.md）
        const float SHADOW_SPREAD = 20f;
        const int SHADOW_LAYERS = 5;

        // スクロール端の判定に使う遊び（Vue updateScrollArrows の 0.5px）
        const float SCROLL_EPSILON = 0.5f;

        const float SCROLL_ARROW_RATIO = 0.7f;

        // フィルタ用 TextField の内側要素（背景・枠を消して高さを使い切るために触る）
        const string TEXT_INPUT_NAME = "unity-text-input";

        // フィルタ入力の左右余白。option 行と同じ 6px にして、打鍵中も字の位置が飛ばないようにする
        const float FILTER_PADDING = OPTION_PADDING;

        #endregion

        #region Fields

        static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;

        TweeqTheme _theme = TweeqTheme.Dark();

        T _value;
        T[] _options = Array.Empty<T>();
        string[] _labelCache = Array.Empty<string>();
        Func<T, string> _labelizer;
        string[] _labels;
        string _prefix = string.Empty;
        string _suffix = string.Empty;
        string _displayText = string.Empty;

        // _value の options 内での位置。見つからなければ -1。毎フレーム探索しないよう保持する
        int _valueIndex = -1;

        bool _disabled;
        bool _invalid;
        bool _hovered;
        bool _focused;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // フィールド側
        UILabel _fieldLabel;
        VisualElement _chevron;
        VisualElement _focusRing;

        // フィルタ（ファジー検索）。TextField は初回のフィルタまで作らない。
        // 一度も打鍵されないドロップダウンに TextField の内部階層を持たせないため
        TextField _filterField;
        VisualElement _filterInput;
        TextElement _filterText;

        bool _filtering;
        string _filterQuery = string.Empty;

        // 絞り込み結果（options のインデックス列）。打鍵ごとに詰め直すので List は使い回す
        readonly List<int> _filtered = new List<int>();

        // ポップアップ側（options 変更時にだけ組み直し、開閉では使い回す）
        TweeqPopover _popover;
        VisualElement _surface;
        VisualElement _shadowLayer;
        VisualElement _viewport;
        VisualElement _list;
        VisualElement _arrowUp;
        VisualElement _arrowDown;
        readonly List<UILabel> _rows = new List<UILabel>();

        bool _open;
        T _valueAtStart;

        // 開いた時点の値の位置。current 表示のたびに線形探索しないよう Open で 1 回だけ引く
        int _valueAtStartIndex = -1;
        long _openTimeMs;

        // popover の内部構造に依存せず「もう入れたか」を覚える
        bool _popupAttached;

        float _scrollOffset;
        float _visibleHeight;
        float _listHeight;

        // オートスクロールの scheduled item は 1 個だけ作って Resume/Pause で使い回す
        IVisualElementScheduledItem _autoScrollItem;
        int _autoScrollDirection;

        // スクロール矢印の上で離した時は確定させない（Vue の @pointerup.stop 相当）
        bool _pointerOverArrow;

        // 開いている間だけ panel root に付ける外側クリック／リリースの検知
        VisualElement _dismissRoot;

        #endregion

        #region Public API

        /// <summary>値が変わるたびに発火する。矢印キー・ホバー選択でも飛ぶ。</summary>
        public event Action<T> ValueChanged;

        /// <summary>クリック確定・Enter 確定のときだけ、1 操作につき 1 回発火する。</summary>
        public event Action<T> Confirmed;

        /// <summary>選択中の値。</summary>
        public T value
        {
            get => _value;
            set
            {
                if (Comparer.Equals(_value, value))
                {
                    return;
                }

                T previous = _value;
                SetValueWithoutNotify(value);
                ValueChanged?.Invoke(_value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>
        /// 選択肢。設定・取得ともにコピーを通す（呼び出し側の配列と内部状態を切り離す）。
        /// 現在値が新しい選択肢に含まれない場合も値は動かさない（勝手な通知を出さないため）。
        /// フィールドには <see cref="Labelizer"/> 経由の表示だけが残る。
        /// </summary>
        public T[] Options
        {
            get
            {
                T[] copy = new T[_options.Length];
                Array.Copy(_options, copy, _options.Length);
                return copy;
            }

            set
            {
                if (value == null)
                {
                    _options = Array.Empty<T>();
                }
                else
                {
                    _options = new T[value.Length];
                    Array.Copy(value, _options, value.Length);
                }

                RebuildLabelCache();

                // 絞り込み結果は「旧 options のインデックス列」なので、行を組み直す前に引き直す。
                // 先に RebuildRows へ入ると範囲外のインデックスを参照してしまう
                RefreshFilterResults();

                RebuildRows();
                _valueIndex = IndexOf(_value);

                // 選択肢が消えたらポップアップの中身も意味を失う
                if (_options.Length == 0 && _open)
                {
                    Close();
                }

                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>
        /// 値からラベルを作る関数。<see cref="Labels"/> より優先される。
        /// 結果は options 変更時に一括生成してキャッシュするので、毎フレームは呼ばれない。
        /// </summary>
        public Func<T, string> Labelizer
        {
            get => _labelizer;
            set
            {
                _labelizer = value;
                RebuildLabelCache();
                RefreshFilterResults();
                ApplyRowTexts();
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>options とインデックスで対応するラベル列。<see cref="Labelizer"/> 未設定時に使う。</summary>
        public string[] Labels
        {
            get => _labels;
            set
            {
                _labels = value;
                RebuildLabelCache();
                RefreshFilterResults();
                ApplyRowTexts();
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>フィールド表示に前置される文字列。option 行には付かない（Vue の InputString 側の責務）。</summary>
        public string Prefix
        {
            get => _prefix;
            set
            {
                _prefix = value ?? string.Empty;
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>フィールド表示に後置される文字列。</summary>
        public string Suffix
        {
            get => _suffix;
            set
            {
                _suffix = value ?? string.Empty;
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>操作不能状態。</summary>
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
                    // 無効化の瞬間に開いたままだと閉じる手段が無くなる
                    Close();
                }

                this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
                Refresh();
            }
        }

        /// <summary>
        /// 外部から与える不正値表示。文字色を Error にするだけで、枠・シェブロンは変えない。
        /// </summary>
        /// <remarks>
        /// Vue の InputDropdown は表示を内部 InputString へ委譲しているので invalid も持つ
        /// （m7-disabled-invalid-spec.md）。ここでは委譲先が無いため、フィールドのラベルと
        /// フィルタ用 TextField の両方に <see cref="StringInput"/> と同じ表現を掛ける。
        /// </remarks>
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

        /// <summary>横方向グループでの位置。</summary>
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

        /// <summary>ポップアップが開いているか（論理状態。panel が無くても進む）。</summary>
        public bool IsOpen => _open;

        /// <summary>フィールドに出ている文字列（Prefix + ラベル + Suffix）。フィルタ中は隠れている。</summary>
        public string DisplayText => _displayText;

        /// <summary>ファジー検索で絞り込んでいる最中か。</summary>
        public bool IsFiltering => _filtering;

        /// <summary>フィルタ中に打ち込まれているクエリ。非フィルタ時は空文字。</summary>
        public string FilterQuery => _filterQuery;

        /// <summary>ポップアップに出ている候補の件数。非フィルタ時は選択肢の総数。</summary>
        public int VisibleCount => _filtering ? _filtered.Count : _options.Length;

        /// <summary>表示上 visibleIndex 番目の候補が options の何番目か。範囲外は -1。</summary>
        public int OptionIndexAt(int visibleIndex)
        {
            if (visibleIndex < 0)
            {
                return -1;
            }

            if (!_filtering)
            {
                return visibleIndex < _options.Length ? visibleIndex : -1;
            }

            if (visibleIndex >= _filtered.Count)
            {
                return -1;
            }

            int index = _filtered[visibleIndex];
            return index >= 0 && index < _options.Length ? index : -1;
        }

        /// <summary>開いた時点の値。Escape のロールバック先。</summary>
        public T ValueAtStart => _valueAtStart;

        /// <summary>
        /// ミリ秒時刻の供給元。既定は Time.realtimeSinceStartup。
        /// 500ms ルールを EditMode で検証できるよう差し替え可能にしてある。
        /// </summary>
        public Func<long> TimeSource { get; set; }

        /// <summary>ChangeEvent / ValueChanged を発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(T newValue)
        {
            _value = newValue;
            _valueIndex = IndexOf(newValue);
            RefreshDisplayText();
            Refresh();
        }

        /// <summary>
        /// ポップアップを開く。開いた時点の値を <see cref="ValueAtStart"/> に控え、
        /// 500ms ルールの起点を打つ。選択肢が空・無効化中は何もしない。
        /// </summary>
        public void Open()
        {
            if (_open || _disabled || _options.Length == 0)
            {
                return;
            }

            _open = true;
            _valueAtStart = _value;
            _valueAtStartIndex = _valueIndex;
            _openTimeMs = Now();
            _pointerOverArrow = false;

            ShowPopup();
            Refresh();
        }

        /// <summary>確定せずに閉じる。現在値はそのまま（Vue の外側クリック相当）。</summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _pointerOverArrow = false;
            StopAutoScroll();

            // 仕様 §B: どの経路で閉じてもフィルタは解除し、表示はラベルへ戻す。
            // 行の表示を戻してから popup を畳む（次に開いたとき全件で始まる）
            EndFilter();

            HidePopup();
            Refresh();
        }

        /// <summary>現在値を確定して閉じる。Confirmed は 1 操作につきここでだけ 1 回発火する。</summary>
        public void Commit()
        {
            if (!_open)
            {
                return;
            }

            Close();
            Confirmed?.Invoke(_value);
        }

        /// <summary>開いた時点の値へロールバックして閉じる（Escape）。Confirmed は発火しない。</summary>
        public void Cancel()
        {
            if (!_open)
            {
                return;
            }

            // 変更済みなら戻す方向の ValueChanged も出す（ドラッグのキャンセルと同じ扱い）
            this.value = _valueAtStart;
            Close();
        }

        /// <summary>
        /// 隣接候補へラップアラウンドで移動する（direction: -1=前 / +1=次）。
        /// 開閉どちらでも値が動く（Vue の onPressArrow と同じく active == 現在値）。
        /// フィルタ中は絞り込み結果の中だけでラップする。
        /// </summary>
        public void MoveSelection(int direction)
        {
            if (_disabled || direction == 0 || _options.Length == 0)
            {
                return;
            }

            int count = VisibleCount;
            if (count == 0)
            {
                return;
            }

            int current = VisibleIndexOfValue();

            // 現在値が候補に無い（options 外・絞り込みで落ちた）。どちら向きでも先頭から始める
            int next = current < 0 ? 0 : WrapIndex(current + (direction > 0 ? 1 : -1), count);

            int option = OptionIndexAt(next);
            if (option < 0)
            {
                return;
            }

            this.value = _options[option];

            if (_open)
            {
                ScrollActiveIntoView();
            }
        }

        /// <summary>
        /// ポインタを離した時の共通経路。開いてから 500ms 以内なら押しっぱなしドラッグ選択の
        /// 途中とみなして無視し、超えていれば確定して閉じる（Vue onPointerupWhileOpen）。
        /// </summary>
        public void PerformPointerUp()
        {
            if (!_open || _pointerOverArrow)
            {
                return;
            }

            if (Now() - _openTimeMs <= CONFIRM_GRACE_MS)
            {
                return;
            }

            Commit();
        }

        #endregion

        #region Filter session

        /// <summary>
        /// ファジー検索モードへ入り、クエリを query で置き換える。閉じていれば開く（Vue 準拠）。
        /// 選択肢が空・無効化中は何もしない。
        /// </summary>
        public void BeginFilter(string query)
        {
            if (_disabled || _options.Length == 0)
            {
                return;
            }

            if (!_filtering)
            {
                // Open() が valueAtStart を控える前に絞り込みが値を動かすと Escape の戻り先がずれる。
                // 「開くのが先・絞り込むのが後」の順序はここで固定する
                _filtering = true;
                ShowFilterField();
                Open();
            }

            SetFilterQuery(query);
        }

        /// <summary>
        /// フィルタ中のクエリを差し替えて絞り込み直す。フィルタ中でなければ何もしない。
        /// 空クエリは「絞り込み無し（全件）」。
        /// </summary>
        public void SetFilterQuery(string query)
        {
            if (!_filtering)
            {
                return;
            }

            _filterQuery = query ?? string.Empty;
            SyncFilterField();
            ApplyFilter();
        }

        /// <summary>
        /// フィルタを解除し、フィールドの表示をラベルへ戻す。候補も全件に戻る。
        /// ポップアップの開閉には触らない（閉じるのは <see cref="Close"/> の責務）。
        /// </summary>
        public void EndFilter()
        {
            if (!_filtering)
            {
                return;
            }

            _filtering = false;
            _filterQuery = string.Empty;
            _filtered.Clear();

            HideFilterField();
            ApplyRowTexts();
            RefreshDisplayText();
            Refresh();
        }

        // 打鍵ごとに通る経路。文字列も List も作らず、使い回しのバッファへ詰め直すだけ
        void ApplyFilter()
        {
            RefreshFilterResults();

            // Vue: 絞り込みから現在値が外れたら先頭へ寄せる。↑↓ の起点を必ず候補内に置くため
            if (_filtered.Count > 0 && VisibleIndexOfValue() < 0)
            {
                this.value = _options[_filtered[0]];
            }

            ApplyRowTexts();

            // 絞り込み直後は必ず先頭から見せる（Vue の scrollTop = 0）
            SetScroll(0f);

            RelayoutPopup();
            Refresh();
        }

        void RefreshFilterResults()
        {
            if (!_filtering)
            {
                return;
            }

            FuzzySearch.Filter(_filterQuery, _labelCache, _filtered);
        }

        // 現在値が「表示上の何番目か」。非フィルタ時は options のインデックスそのもの
        int VisibleIndexOfValue()
        {
            if (_valueIndex < 0)
            {
                return -1;
            }

            if (!_filtering)
            {
                return _valueIndex;
            }

            for (int i = 0; i < _filtered.Count; i++)
            {
                if (_filtered[i] == _valueIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion

        #region Construction

        public DropdownInput()
        {
            this.AddToClassList("tweeq-dropdown-input");

            // 矢印・Enter・Escape を受け取るためルート自身がフォーカスを持つ
            this.focusable = true;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;

            BuildField();
            ApplyStaticStyles();

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            // フィルタ中はフォーカスが内側の TextField にあるので、Enter / Escape / ↑↓ を
            // TextField より先に横取りする必要がある（NumberInput と同じ TrickleDown 登録）
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // 矢印キーは KeyDown とは別に NavigationMoveEvent も飛ばし、そちらがフォーカスを
            // 動かしてしまう（feedback-fixes-01.md A-5 / NumberInput と同じ手当て）
            this.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        public DropdownInput(T[] options)
            : this()
        {
            this.Options = options;
        }

        void BuildField()
        {
            _fieldLabel = new UILabel(string.Empty)
            {
                name = "tweeq-dropdown-label",
                pickingMode = PickingMode.Ignore,
            };
            _fieldLabel.style.position = Position.Absolute;
            _fieldLabel.style.left = 0f;
            _fieldLabel.style.top = 0f;
            _fieldLabel.style.right = 0f;
            _fieldLabel.style.bottom = 0f;
            _fieldLabel.style.marginLeft = 0f;
            _fieldLabel.style.marginRight = 0f;
            _fieldLabel.style.marginTop = 0f;
            _fieldLabel.style.marginBottom = 0f;
            _fieldLabel.style.paddingTop = 0f;
            _fieldLabel.style.paddingBottom = 0f;
            _fieldLabel.style.fontSize = TEXT_FONT_SIZE;
            _fieldLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _fieldLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _fieldLabel.style.overflow = Overflow.Hidden;
            _fieldLabel.style.textOverflow = TextOverflow.Ellipsis;
            this.hierarchy.Add(_fieldLabel);

            _chevron = new VisualElement
            {
                name = "tweeq-dropdown-chevron",
                pickingMode = PickingMode.Ignore,
            };
            _chevron.style.position = Position.Absolute;
            _chevron.style.top = 0f;
            _chevron.style.bottom = 0f;
            _chevron.style.right = 0f;
            _chevron.generateVisualContent += OnGenerateChevron;
            this.hierarchy.Add(_chevron);

            // フォーカスリングはルートの border ではなく別レイヤで描く（NumberInput と同じ理由）
            _focusRing = new VisualElement
            {
                name = "tweeq-dropdown-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _focusRing.style.position = Position.Absolute;
            _focusRing.style.left = 0f;
            _focusRing.style.top = 0f;
            _focusRing.style.right = 0f;
            _focusRing.style.bottom = 0f;
            _focusRing.style.display = DisplayStyle.None;
            SetBorderWidth(_focusRing, FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusRing);
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
            SetBorderColor(this, _theme.Border);
            ApplyTransition(this, _theme.HoverTransitionDuration, "background-color");

            float chevronWidth = _theme.InputHeight * CHEVRON_WIDTH_RATIO;

            if (_chevron != null)
            {
                _chevron.style.width = chevronWidth;
                ApplyTransition(_chevron, _theme.HoverTransitionDuration, "opacity");
            }

            if (_fieldLabel != null)
            {
                // シェブロンぶんだけ左右から逃がして、テキストの中心を枠の中心に保つ
                _fieldLabel.style.paddingLeft = chevronWidth;
                _fieldLabel.style.paddingRight = chevronWidth;
            }

            if (_focusRing != null)
            {
                SetBorderColor(_focusRing, _theme.Accent);
            }

            ApplyFilterFieldStyles();
            ApplyPopupStyles();
        }

        // 仕様 §1 の角丸表。両軸の指定は OR で合成する
        void ApplyCornerRadius()
        {
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

            SetCornerRadius(this, radius, topLeft, topRight, bottomLeft, bottomRight);

            if (_focusRing != null)
            {
                SetCornerRadius(_focusRing, radius, topLeft, topRight, bottomLeft, bottomRight);
            }
        }

        #endregion

        #region Filter field

        // NumberInput の編集モード切替と同じ二段構え（表示は Label、打鍵中だけ TextField）。
        // StringInput のように TextField を常時前面へ置くと、フィールドの押下＝ポップアップを開く
        // という Dropdown の主操作と ↑↓ の横取りが TextField 側に吸われる。
        // Dropdown はクリック位置キャレットを必要としない（打ち始めは常に空クエリ）ので、
        // StringInput が二段構えを避けた理由がこちらには当てはまらない
        void EnsureFilterField()
        {
            if (_filterField != null)
            {
                return;
            }

            _filterField = new TextField
            {
                name = "tweeq-dropdown-filter",

                // 1 打鍵ごとに絞り込む必要がある。isDelayed = true だと Enter まで変更が来ない
                isDelayed = false,
                multiline = false,
            };
            _filterField.style.position = Position.Absolute;
            _filterField.style.left = 0f;
            _filterField.style.top = 0f;
            _filterField.style.right = 0f;
            _filterField.style.bottom = 0f;
            _filterField.style.marginLeft = 0f;
            _filterField.style.marginRight = 0f;
            _filterField.style.marginTop = 0f;
            _filterField.style.marginBottom = 0f;
            _filterField.style.display = DisplayStyle.None;
            _filterField.pickingMode = PickingMode.Ignore;
            _filterField.RegisterValueChangedCallback(OnFilterTextChanged);
            this.hierarchy.Add(_filterField);

            _filterInput = _filterField.Q(TEXT_INPUT_NAME);

            // 実際に字を描くのは unity-text-input の中の TextElement。
            // 縦潰れ（feedback-fixes-01.md A-6）は input 側だけ直しても残る
            _filterText = _filterInput != null ? _filterInput.Q<TextElement>() : null;

            ApplyFilterFieldStyles();
        }

        void ApplyFilterFieldStyles()
        {
            if (_theme == null || _filterField == null)
            {
                return;
            }

            float chevronWidth = _theme.InputHeight * CHEVRON_WIDTH_RATIO;

            if (_filterInput != null)
            {
                _filterInput.style.backgroundColor = Color.clear;
                SetBorderWidth(_filterInput, 0f);
                SetBorderColor(_filterInput, Color.clear);

                // 閉状態のラベルと同じくシェブロン幅ぶん逃がして、モード切替で字が横に飛ばないようにする
                _filterInput.style.paddingLeft = chevronWidth + FILTER_PADDING;
                _filterInput.style.paddingRight = chevronWidth + FILTER_PADDING;
                _filterInput.style.marginLeft = 0f;
                _filterInput.style.marginRight = 0f;
                _filterInput.style.unityTextAlign = TextAnchor.MiddleCenter;

                // A-6: 既定 USS の上下 padding／auto 高さのままだと 24px の枠内で行が潰れる
                _filterInput.style.height = Length.Percent(100f);
                _filterInput.style.minHeight = 0f;
                _filterInput.style.paddingTop = 0f;
                _filterInput.style.paddingBottom = 0f;
                _filterInput.style.marginTop = 0f;
                _filterInput.style.marginBottom = 0f;
                _filterInput.style.fontSize = TEXT_FONT_SIZE;
                _filterInput.style.whiteSpace = WhiteSpace.NoWrap;
            }

            if (_filterText != null)
            {
                _filterText.style.height = Length.Percent(100f);
                _filterText.style.minHeight = 0f;
                _filterText.style.paddingTop = 0f;
                _filterText.style.paddingBottom = 0f;
                _filterText.style.marginTop = 0f;
                _filterText.style.marginBottom = 0f;
                _filterText.style.fontSize = TEXT_FONT_SIZE;
                _filterText.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            _filterField.style.unityTextAlign = TextAnchor.MiddleCenter;
            _filterField.style.fontSize = TEXT_FONT_SIZE;

            // キャレット・選択色は USS 既定（黒）のままだと暗背景で見えない。
            // selectionColor は obsolete だが、推奨の --unity-selection-color は
            // C# からインスタンス単位で設定できない（NumberInput / StringInput と同じ判断）
#pragma warning disable 618
            _filterField.textSelection.cursorColor = _theme.Text;
            _filterField.textSelection.selectionColor = _theme.AccentSoft;
#pragma warning restore 618

            _filterField.style.paddingTop = 0f;
            _filterField.style.paddingBottom = 0f;
            _filterField.style.paddingLeft = 0f;
            _filterField.style.paddingRight = 0f;
            _filterField.style.minHeight = 0f;
            _filterField.style.alignItems = Align.Stretch;

            // 初回のフィルタ生成はここを直接通る（Refresh を経ない）ので、文字色を貼り直す
            UpdateFilterTextColor();
        }

        void ShowFilterField()
        {
            EnsureFilterField();

            if (_filterField == null)
            {
                return;
            }

            if (_fieldLabel != null)
            {
                _fieldLabel.style.display = DisplayStyle.None;
            }

            // display:none のままでは Focus() が通らないので、必ず表示を先に切り替える
            _filterField.style.display = DisplayStyle.Flex;
            _filterField.pickingMode = PickingMode.Position;

            if (this.panel != null)
            {
                _filterField.Focus();
                ScheduleCaretToEnd();
            }
        }

        void HideFilterField()
        {
            if (_fieldLabel != null)
            {
                _fieldLabel.style.display = DisplayStyle.Flex;
            }

            if (_filterField == null)
            {
                return;
            }

            bool hadFocus = HasFilterFocus();

            _filterField.SetValueWithoutNotify(string.Empty);
            _filterField.style.display = DisplayStyle.None;
            _filterField.pickingMode = PickingMode.Ignore;

            // フィルタ中のフォーカスは TextField 側にある。畳んだあとも Enter / ↑↓ を
            // 受け取り続けられるよう、ルートへ戻す
            if (hadFocus && this.panel != null)
            {
                this.Focus();
            }
        }

        // 先頭の 1 文字はこちらが流し込むので、フォーカス直後のキャレットを末尾へ送る。
        // フォーカスが確定した次のフレームでないと選択範囲が上書きされる（NumberInput と同じ）
        void ScheduleCaretToEnd()
        {
            if (this.panel == null)
            {
                return;
            }

            this.schedule.Execute(() =>
            {
                if (_filterField == null || !_filtering)
                {
                    return;
                }

                int caret = _filterQuery.Length;
                _filterField.SelectRange(caret, caret);
            }).StartingIn(0);
        }

        void SyncFilterField()
        {
            if (_filterField == null || _filterField.value == _filterQuery)
            {
                return;
            }

            _filterField.SetValueWithoutNotify(_filterQuery);
        }

        void OnFilterTextChanged(ChangeEvent<string> evt)
        {
            if (evt == null || !_filtering)
            {
                return;
            }

            SetFilterQuery(evt.newValue);
        }

        bool HasFilterFocus()
        {
            if (_filterField == null || this.focusController == null)
            {
                return false;
            }

            VisualElement focused = this.focusController.focusedElement as VisualElement;
            return focused != null && (focused == _filterField || _filterField.Contains(focused));
        }

        // フィルタ中はキャレットを持つ TextField、それ以外はルートがキー入力の受け皿
        void FocusSelf()
        {
            if (this.panel == null)
            {
                return;
            }

            if (_filtering && _filterField != null)
            {
                _filterField.Focus();
                return;
            }

            this.Focus();
        }

        #endregion

        #region Labels

        // options / labelizer / labels のどれかが変わった時だけ走る。ここで一括生成して
        // 以降は配列参照で済ませる（毎フレーム Format しない）
        void RebuildLabelCache()
        {
            if (_labelCache.Length != _options.Length)
            {
                _labelCache = _options.Length == 0
                    ? Array.Empty<string>()
                    : new string[_options.Length];
            }

            for (int i = 0; i < _options.Length; i++)
            {
                _labelCache[i] = ComposeLabel(_options[i], i);
            }
        }

        // 優先順は Labelizer > Labels > value.ToString()（popover-spec.md）
        string ComposeLabel(T option, int index)
        {
            if (_labelizer != null)
            {
                return _labelizer(option) ?? string.Empty;
            }

            if (_labels != null && index >= 0 && index < _labels.Length && _labels[index] != null)
            {
                return _labels[index];
            }

            return option == null ? string.Empty : option.ToString();
        }

        void RefreshDisplayText()
        {
            string label = _valueIndex >= 0 && _valueIndex < _labelCache.Length
                ? _labelCache[_valueIndex]
                : ComposeLabel(_value, -1);

            // 前後が空なら連結せずキャッシュをそのまま使う（値変更ごとの string 確保を避ける）
            _displayText = _prefix.Length == 0 && _suffix.Length == 0
                ? label
                : _prefix + label + _suffix;

            if (_fieldLabel != null)
            {
                _fieldLabel.text = _displayText;
            }
        }

        int IndexOf(T target)
        {
            for (int i = 0; i < _options.Length; i++)
            {
                if (Comparer.Equals(_options[i], target))
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion

        #region Popup construction

        // option 行は options 変更時にだけ作り直す。開閉では使い回すので、開くたびの確保は無い
        void RebuildRows()
        {
            EnsurePopupElements();

            for (int i = _rows.Count - 1; i >= _options.Length; i--)
            {
                _list.Remove(_rows[i]);
                _rows.RemoveAt(i);
            }

            while (_rows.Count < _options.Length)
            {
                UILabel row = new UILabel(string.Empty)
                {
                    name = "tweeq-dropdown-option",

                    // 当たり判定は _list 側で layout の y から引く（行ごとのコールバックを持たない）
                    pickingMode = PickingMode.Ignore,
                };
                ApplyRowStyles(row);
                _list.Add(row);
                _rows.Add(row);
            }

            ApplyRowTexts();
        }

        // 行プールは options 数のまま据え置き、絞り込みで余った行だけ畳む。
        // 打鍵ごとに要素を作り直さないので、フィルタ中の確保はゼロ
        void ApplyRowTexts()
        {
            int visible = VisibleCount;

            for (int i = 0; i < _rows.Count; i++)
            {
                UILabel row = _rows[i];

                if (i >= visible)
                {
                    row.style.display = DisplayStyle.None;
                    continue;
                }

                row.style.display = DisplayStyle.Flex;

                int option = OptionIndexAt(i);
                row.text = option >= 0 && option < _labelCache.Length
                    ? _labelCache[option]
                    : string.Empty;
            }
        }

        void EnsurePopupElements()
        {
            if (_surface != null)
            {
                return;
            }

            // 位置決めは popover の責務なので、ここでは絶対配置にしない
            _surface = new VisualElement { name = "tweeq-dropdown-popup" };

            // 影は surface の外へはみ出して描くので、クリップは掛けない
            _surface.style.overflow = Overflow.Visible;

            _shadowLayer = new VisualElement
            {
                name = "tweeq-dropdown-shadow",
                pickingMode = PickingMode.Ignore,
            };
            _shadowLayer.style.position = Position.Absolute;
            _shadowLayer.style.left = -SHADOW_SPREAD;
            _shadowLayer.style.right = -SHADOW_SPREAD;
            _shadowLayer.style.top = -SHADOW_SPREAD;
            _shadowLayer.style.bottom = -SHADOW_SPREAD;
            _shadowLayer.generateVisualContent += OnGenerateShadow;
            _surface.Add(_shadowLayer);

            _viewport = new VisualElement { name = "tweeq-dropdown-viewport" };
            _viewport.style.overflow = Overflow.Hidden;
            _viewport.style.position = Position.Relative;
            _viewport.RegisterCallback<PointerMoveEvent>(OnListPointerMove);
            _viewport.RegisterCallback<PointerDownEvent>(OnListPointerDown);
            _viewport.RegisterCallback<WheelEvent>(OnListWheel);
            _surface.Add(_viewport);

            _list = new VisualElement
            {
                name = "tweeq-dropdown-list",
                pickingMode = PickingMode.Ignore,
            };
            _list.style.position = Position.Absolute;
            _list.style.left = 0f;
            _list.style.right = 0f;
            _list.style.top = 0f;
            _viewport.Add(_list);

            // 矢印は list より後に足す＝上に重なる
            _arrowUp = new VisualElement { name = "tweeq-dropdown-scroll-up" };
            SetupScrollArrow(_arrowUp, true);
            _arrowUp.generateVisualContent += OnGenerateArrowUp;
            _arrowUp.RegisterCallback<PointerEnterEvent>(OnArrowEnterUp);
            _viewport.Add(_arrowUp);

            _arrowDown = new VisualElement { name = "tweeq-dropdown-scroll-down" };
            SetupScrollArrow(_arrowDown, false);
            _arrowDown.generateVisualContent += OnGenerateArrowDown;
            _arrowDown.RegisterCallback<PointerEnterEvent>(OnArrowEnterDown);
            _viewport.Add(_arrowDown);

            ApplyPopupStyles();
        }

        void SetupScrollArrow(VisualElement arrow, bool up)
        {
            arrow.style.position = Position.Absolute;
            arrow.style.left = 0f;
            arrow.style.right = 0f;
            arrow.style.display = DisplayStyle.None;

            if (up)
            {
                arrow.style.top = 0f;
            }
            else
            {
                arrow.style.bottom = 0f;
            }

            arrow.RegisterCallback<PointerLeaveEvent>(OnArrowLeave);
        }

        void ApplyRowStyles(UILabel row)
        {
            if (_theme == null || row == null)
            {
                return;
            }

            row.style.height = _theme.InputHeight;
            row.style.paddingLeft = OPTION_PADDING;
            row.style.paddingRight = OPTION_PADDING;
            row.style.paddingTop = 0f;
            row.style.paddingBottom = 0f;
            row.style.marginLeft = 0f;
            row.style.marginRight = 0f;
            row.style.marginTop = 0f;
            row.style.marginBottom = 0f;
            row.style.fontSize = TEXT_FONT_SIZE;
            row.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.style.whiteSpace = WhiteSpace.NoWrap;
            row.style.overflow = Overflow.Hidden;
            row.style.textOverflow = TextOverflow.Ellipsis;
            SetCornerRadius(row, _theme.InputRadius, true, true, true, true);
        }

        void ApplyPopupStyles()
        {
            if (_theme == null || _surface == null)
            {
                return;
            }

            // ブラーが無い UITK では半透明 Surface だと背面の行が透けて読める → 不透明合成
            _surface.style.backgroundColor = _theme.SurfaceOpaque;
            SetBorderWidth(_surface, POPUP_BORDER_WIDTH);
            SetBorderColor(_surface, _theme.Border);
            SetCornerRadius(_surface, _theme.RadiusPopup, true, true, true, true);

            float padding = _theme.PopupPadding;
            _surface.style.paddingLeft = padding;
            _surface.style.paddingRight = padding;
            _surface.style.paddingTop = padding;
            _surface.style.paddingBottom = padding;

            float arrowHeight = _theme.InputHeight * SCROLL_ARROW_RATIO;

            if (_arrowUp != null)
            {
                _arrowUp.style.height = arrowHeight;
            }

            if (_arrowDown != null)
            {
                _arrowDown.style.height = arrowHeight;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                ApplyRowStyles(_rows[i]);
            }
        }

        #endregion

        #region Popup placement

        void ShowPopup()
        {
            if (this.panel == null || _theme == null)
            {
                // panel 未接続なら見た目は出せない。論理状態だけ進めて例外は出さない
                return;
            }

            EnsurePopupElements();
            ApplyPopupStyles();

            if (_popover == null)
            {
                // popover-spec.md: Dropdown は LightDismiss=false。閉じるのは所有者（＝ここ）の責務。
                // 外装は行幅とフィールドの位置合わせのため自前で描くので Chrome=false の素通しホストにする
                _popover = new TweeqPopover { Context = this, LightDismiss = false, Chrome = false };
                _popover.Closed += OnPopoverClosed;
            }

            if (!_popupAttached)
            {
                _popover.Add(_surface);
                _popupAttached = true;
            }

            Vector2 position = Layout();
            _popover.Open(position);

            AttachDismissHandlers();

            // 初回はレイアウト未確定なので、サイズが決まった次のフレームで貼り直す
            this.schedule.Execute(RelayoutPopup).StartingIn(0);
        }

        void HidePopup()
        {
            DetachDismissHandlers();

            if (_popover == null)
            {
                return;
            }

            _popover.Close();
        }

        void OnPopoverClosed()
        {
            // Popover 側から閉じられた場合も状態を揃える（LightDismiss=false なので通常は来ない）
            if (!_open)
            {
                return;
            }

            Close();
        }

        void RelayoutPopup()
        {
            if (!_open || _popover == null)
            {
                return;
            }

            Vector2 position = Layout();
            _popover.Open(position);
        }

        // 選択中の option がフィールドに重なる位置を Core に逆算させ、
        // 収まらない分は内部スクロールへ回す（popover-spec.md「macOS 風配置」）
        Vector2 Layout()
        {
            float itemHeight = _theme.InputHeight;
            float padding = _theme.PopupPadding;

            Rect field = this.worldBound;
            float fieldTop = float.IsNaN(field.yMin) ? 0f : field.yMin;
            float fieldLeft = float.IsNaN(field.xMin) ? 0f : field.xMin;
            float fieldWidth = float.IsNaN(field.width) || field.width <= 0f
                ? _theme.InputHeight
                : field.width;

            float fieldHeight = float.IsNaN(field.height) || field.height <= 0f
                ? _theme.InputHeight
                : field.height;

            float viewportHeight = ViewportHeight();
            float chromeTop = padding + POPUP_BORDER_WIDTH;
            int index = _valueIndex < 0 ? 0 : _valueIndex;

            _listHeight = VisibleCount * itemHeight;

            double top;
            if (_filtering)
            {
                // 仕様 §B: 打鍵で絞り込んでいる間は macOS 風の逆算をやめてフィールド直下に落とす。
                // 候補が入れ替わるたびに「選択項目をフィールドへ重ねる」と popup が跳ね回るため（Vue 準拠）
                top = fieldTop + fieldHeight;
            }
            else
            {
                // Core の純関数。selectChrome には popup 自身の上端クロム（border + padding）を渡す。
                // listHeight を省略すると下端クランプが「未実測＝リスト過大」の安全側に倒れるため必ず渡す
                // fieldInset=0: UIToolkit 版フィールドは Vue の border+outline 2px を持たない。
                // 行の worldBound がフィールドと完全一致することが E2E の検証条件
                top = DropdownLogic.GetDropdownTop(
                    fieldTop,
                    index,
                    itemHeight,
                    viewportHeight,
                    VIEWPORT_MARGIN,
                    chromeTop,
                    _listHeight,
                    0.0);
            }

            float available = viewportHeight - (float)top - VIEWPORT_MARGIN - chromeTop * 2f;
            _visibleHeight = Mathf.Max(itemHeight, Mathf.Min(_listHeight, available));

            // 行の幅をフィールドと一致させたいので、padding と border のぶんだけ外へ広げる
            _surface.style.width = fieldWidth + chromeTop * 2f;
            _viewport.style.height = _visibleHeight;

            if (_filtering)
            {
                // 絞り込み結果は常に先頭から見せる（Vue の scrollTop = 0）
                SetScroll(0f);
            }
            else
            {
                AlignActiveToField(fieldTop, (float)top + chromeTop, itemHeight);
            }

            return new Vector2(fieldLeft - chromeTop, (float)top);
        }

        float ViewportHeight()
        {
            if (this.panel != null && this.panel.visualTree != null)
            {
                float height = this.panel.visualTree.layout.height;
                if (!float.IsNaN(height) && height > 0f)
                {
                    return height;
                }
            }

            return Screen.height > 0 ? Screen.height : 0f;
        }

        // Vue alignCurrentToTrigger: 選択中の行がフィールドの上に来るようスクロールを合わせる
        void AlignActiveToField(float fieldTop, float listTop, float itemHeight)
        {
            int index = _valueIndex < 0 ? 0 : _valueIndex;
            SetScroll(listTop + index * itemHeight - fieldTop);
        }

        #endregion

        #region Scrolling

        void SetScroll(float offset)
        {
            float max = Mathf.Max(0f, _listHeight - _visibleHeight);
            _scrollOffset = Mathf.Clamp(offset, 0f, max);

            if (_list != null)
            {
                _list.style.top = -_scrollOffset;
            }

            UpdateScrollArrows();
        }

        void UpdateScrollArrows()
        {
            if (_arrowUp == null || _arrowDown == null)
            {
                return;
            }

            bool canUp = _scrollOffset > SCROLL_EPSILON;
            bool canDown = _scrollOffset + _visibleHeight < _listHeight - SCROLL_EPSILON;

            _arrowUp.style.display = canUp ? DisplayStyle.Flex : DisplayStyle.None;
            _arrowDown.style.display = canDown ? DisplayStyle.Flex : DisplayStyle.None;

            if (_autoScrollDirection < 0 && !canUp)
            {
                StopAutoScroll();
            }
            else if (_autoScrollDirection > 0 && !canDown)
            {
                StopAutoScroll();
            }
        }

        void ScrollActiveIntoView()
        {
            int visibleIndex = VisibleIndexOfValue();
            if (_theme == null || visibleIndex < 0)
            {
                return;
            }

            float itemHeight = _theme.InputHeight;
            float rowTop = visibleIndex * itemHeight;
            float rowBottom = rowTop + itemHeight;

            if (rowTop < _scrollOffset)
            {
                SetScroll(rowTop);
                return;
            }

            if (rowBottom > _scrollOffset + _visibleHeight)
            {
                SetScroll(rowBottom - _visibleHeight);
            }
        }

        void StartAutoScroll(int direction)
        {
            if (!_open || direction == 0)
            {
                return;
            }

            _autoScrollDirection = direction;

            if (this.panel == null)
            {
                return;
            }

            if (_autoScrollItem == null)
            {
                // scheduled item は 1 個だけ作って使い回す（毎フレームのクロージャ確保を避ける）
                _autoScrollItem = this.schedule
                    .Execute(OnAutoScrollTick)
                    .Every(AUTO_SCROLL_INTERVAL_MS);
            }

            _autoScrollItem.Resume();
        }

        void StopAutoScroll()
        {
            _autoScrollDirection = 0;
            _autoScrollItem?.Pause();
        }

        void OnAutoScrollTick()
        {
            if (!_open || _autoScrollDirection == 0)
            {
                StopAutoScroll();
                return;
            }

            SetScroll(_scrollOffset + _autoScrollDirection * AUTO_SCROLL_SPEED);
        }

        #endregion

        #region Popup interaction

        // 行ごとにコールバックを持たず、リスト内のローカル y から引く（RadioInput と同じ手）。
        // 返すのは「表示上の何番目か」なので、options のインデックスへは OptionIndexAt で変換する
        int RowIndexAt(float localY)
        {
            int visible = VisibleCount;
            if (_theme == null || visible == 0)
            {
                return -1;
            }

            float itemHeight = _theme.InputHeight;
            if (itemHeight <= 0f)
            {
                return -1;
            }

            int index = Mathf.FloorToInt((localY + _scrollOffset) / itemHeight);
            return index < 0 || index >= visible ? -1 : index;
        }

        void OnListPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_open || _viewport == null)
            {
                return;
            }

            SelectRowAt(evt);
        }

        void OnListPointerDown(PointerDownEvent evt)
        {
            if (evt == null || !_open || _viewport == null)
            {
                return;
            }

            // ポップアップ内の押下は外側クリック扱いにしない。
            // 併せてフィールドへフォーカスを戻し、Enter / Escape を効かせ続ける
            SelectRowAt(evt);

            // フィルタ中はキャレットを持つ TextField を、それ以外はルートを受け皿に戻す
            FocusSelf();

            evt.StopPropagation();
        }

        void SelectRowAt(IPointerEvent evt)
        {
            // 矢印の上のイベントも viewport までバブルしてくる。帯に隠れた行を掴まない
            if (_pointerOverArrow)
            {
                return;
            }

            Vector3 position = evt.position;
            Vector2 local = _viewport.WorldToLocal(new Vector2(position.x, position.y));
            int option = OptionIndexAt(RowIndexAt(local.y));
            if (option < 0)
            {
                return;
            }

            // Vue は option の pointerenter で model を更新する（ホバーが即プレビューになる）
            this.value = _options[option];
        }

        void OnListWheel(WheelEvent evt)
        {
            if (evt == null || !_open)
            {
                return;
            }

            SetScroll(_scrollOffset + evt.delta.y * (_theme != null ? _theme.InputHeight : 1f));
            evt.StopPropagation();
        }

        void OnArrowEnterUp(PointerEnterEvent evt)
        {
            _pointerOverArrow = true;
            StartAutoScroll(-1);
        }

        void OnArrowEnterDown(PointerEnterEvent evt)
        {
            _pointerOverArrow = true;
            StartAutoScroll(1);
        }

        void OnArrowLeave(PointerLeaveEvent evt)
        {
            _pointerOverArrow = false;
            StopAutoScroll();
        }

        #endregion

        #region Dismiss handling

        // popover は LightDismiss=false なので、外側クリックとリリースはこちらで拾う。
        // pointerup は BubbleUp で受けて「矢印の上で離した時だけ無視」を成立させる
        void AttachDismissHandlers()
        {
            if (this.panel == null || _dismissRoot != null)
            {
                return;
            }

            _dismissRoot = this.panel.visualTree;
            if (_dismissRoot == null)
            {
                return;
            }

            _dismissRoot.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            _dismissRoot.RegisterCallback<PointerUpEvent>(OnRootPointerUp);
        }

        void DetachDismissHandlers()
        {
            if (_dismissRoot == null)
            {
                return;
            }

            _dismissRoot.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            _dismissRoot.UnregisterCallback<PointerUpEvent>(OnRootPointerUp);
            _dismissRoot = null;
        }

        void OnRootPointerDown(PointerDownEvent evt)
        {
            if (evt == null || !_open)
            {
                return;
            }

            VisualElement target = evt.target as VisualElement;
            if (target != null && (IsInsideField(target) || IsInsidePopup(target)))
            {
                return;
            }

            // 外側クリックは valueAtStart へロールバック（Vue の onPopoverUpdateOpen 準拠）。
            // ホバー・↑↓・フィルタ入力で値がライブに動くため、確定操作（Enter / option クリック）
            // 以外で閉じたら開いた時点へ戻す（2026-07-27 ユーザー裁定で M5 の「現在値のまま」から変更）
            Cancel();
        }

        void OnRootPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_open)
            {
                return;
            }

            PerformPointerUp();
        }

        bool IsInsideField(VisualElement element)
        {
            return element == this || this.Contains(element);
        }

        bool IsInsidePopup(VisualElement element)
        {
            return _surface != null && (element == _surface || _surface.Contains(element));
        }

        #endregion

        #region Field interaction

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            if (_filtering)
            {
                // フィルタ中のフィールド押下はキャレット操作。TextField へ素通しする
                return;
            }

            if (this.panel != null)
            {
                // ポインタは掴まない。掴むとポップアップの行が PointerMove を受け取れなくなる
                this.Focus();
            }

            Open();
            evt.StopPropagation();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnPointerLeave(PointerLeaveEvent evt)
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

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    evt.StopPropagation();
                    break;

                case KeyCode.DownArrow:
                    MoveSelection(1);
                    evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_open)
                    {
                        Commit();
                    }
                    else
                    {
                        Open();
                    }

                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    if (_open)
                    {
                        Cancel();
                        evt.StopPropagation();
                    }

                    break;

                default:
                    TryBeginFilterFromKey(evt);
                    break;
            }
        }

        // 仕様 §B: フォーカス中の印字可能文字でフィルタモードへ入り、閉じていても開く。
        // Unity は 1 打鍵につき「keyCode 付き（character = '\0'）」と「character 付き
        // （keyCode = None）」の 2 発を投げるので、character 側だけを拾う。
        // フィルタ開始後の打鍵は TextField が直接受けるので、ここは初回の 1 文字だけを扱う
        void TryBeginFilterFromKey(KeyDownEvent evt)
        {
            if (_filtering || _options.Length == 0)
            {
                return;
            }

            // Ctrl/Cmd 併用はショートカットであって文字入力ではない
            const EventModifiers commandKeys = EventModifiers.Control | EventModifiers.Command;
            if ((evt.modifiers & commandKeys) != 0)
            {
                return;
            }

            if (!IsPrintable(evt.character))
            {
                return;
            }

            BeginFilter(evt.character.ToString());
            evt.StopPropagation();
        }

        static bool IsPrintable(char character)
        {
            // '\0'（keyCode 側のイベント）と Enter / Tab / Backspace / Escape / DEL を弾く
            return character >= ' ' && character != (char)127;
        }

        // feedback-fixes-01.md A-5: ↑↓ は選択変更だけ。フォーカスは動かさない
        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                    break;

                default:
                    return;
            }

            evt.StopPropagation();

            // Unity 6 で「フォーカス移動そのもの」を止められるのはこちら（PreventDefault は非推奨）
            this.focusController?.IgnoreEvent(evt);
        }

        void OnFocusIn(FocusInEvent evt)
        {
            _focused = true;
            Refresh();
        }

        // ポップアップ内の要素は非フォーカスなので、option をクリックした瞬間にここへ来る。
        // ここで Close すると click 確定が成立しないため、閉じるのは外側クリック・Escape・
        // detach の 3 経路だけに任せる（Vue も blur では閉じない）
        void OnFocusOut(FocusOutEvent evt)
        {
            _focused = false;
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            StopAutoScroll();
            _autoScrollItem = null;
            DetachDismissHandlers();

            if (_open)
            {
                Close();
            }

            // 開いていなくてもフィルタだけ残っていることは無いはずだが、
            // 剥がされた要素に打鍵状態を持ち越さないよう畳んでおく
            EndFilter();

            _hovered = false;
            _focused = false;
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            if (_disabled)
            {
                // 仕様 §5: 背景透明 + 1px Border のインセット枠
                this.style.backgroundColor = Color.clear;
                SetBorderWidth(this, DISABLED_BORDER_WIDTH);
                SetBorderColor(this, _theme.Border);
            }
            else
            {
                SetBorderWidth(this, 0f);
                this.style.backgroundColor = _hovered || _open ? _theme.InputHover : _theme.Input;
            }

            if (_fieldLabel != null)
            {
                _fieldLabel.style.color = TextColor;
            }

            UpdateFilterTextColor();

            if (_chevron != null)
            {
                _chevron.style.opacity = _hovered || _focused || _open ? 1f : CHEVRON_IDLE_OPACITY;
                _chevron.MarkDirtyRepaint();
            }

            if (_focusRing != null)
            {
                _focusRing.style.display = _focused && !_disabled
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            RefreshRows();
        }

        // disabled が invalid より強い（無効なフィールドの赤字は「操作できる不正値」と読み違えられる）
        Color TextColor
        {
            get
            {
                if (_disabled)
                {
                    return _theme.TextSubtle;
                }

                return _invalid ? _theme.Error : _theme.Text;
            }
        }

        void UpdateFilterTextColor()
        {
            if (_filterField == null)
            {
                return;
            }

            Color color = TextColor;
            _filterField.style.color = color;

            if (_filterInput != null)
            {
                _filterInput.style.color = color;
            }
        }

        void RefreshRows()
        {
            if (_rows.Count == 0)
            {
                return;
            }

            Color onAccent = TweeqTheme.ContrastText(_theme.Accent);
            int currentIndex = _open ? _valueAtStartIndex : -1;
            int visible = VisibleCount;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= visible)
                {
                    // 畳んである行は塗り直す意味がない
                    continue;
                }

                UILabel row = _rows[i];
                int option = OptionIndexAt(i);
                bool active = option >= 0 && option == _valueIndex;
                bool current = option >= 0 && option == currentIndex;

                row.style.backgroundColor = active
                    ? _theme.Accent
                    : current ? _theme.AccentSoft : Color.clear;
                row.style.color = active ? onAccent : _theme.Text;
            }
        }

        void NotifyValueChanged(T previous, T current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<T> changeEvent = ChangeEvent<T>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Painting

        // mdi:unfold-more-horizontal 相当。フォント依存を避けるため上下の小三角で描く
        void OnGenerateChevron(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _chevron == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _chevron.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float centerX = rect.width * 0.5f;
            float centerY = rect.height * 0.5f;
            float half = CHEVRON_TRIANGLE_WIDTH * 0.5f;

            painter.fillColor = _theme.TextSubtle;

            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX, centerY - CHEVRON_TRIANGLE_GAP - CHEVRON_TRIANGLE_HEIGHT));
            painter.LineTo(new Vector2(centerX + half, centerY - CHEVRON_TRIANGLE_GAP));
            painter.LineTo(new Vector2(centerX - half, centerY - CHEVRON_TRIANGLE_GAP));
            painter.ClosePath();
            painter.Fill();

            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX, centerY + CHEVRON_TRIANGLE_GAP + CHEVRON_TRIANGLE_HEIGHT));
            painter.LineTo(new Vector2(centerX + half, centerY + CHEVRON_TRIANGLE_GAP));
            painter.LineTo(new Vector2(centerX - half, centerY + CHEVRON_TRIANGLE_GAP));
            painter.ClosePath();
            painter.Fill();
        }

        void OnGenerateArrowUp(MeshGenerationContext context)
        {
            PaintScrollArrow(context, _arrowUp, true);
        }

        void OnGenerateArrowDown(MeshGenerationContext context)
        {
            PaintScrollArrow(context, _arrowDown, false);
        }

        // 切れている端を覆う帯 + 三角。Vue は linear-gradient だが、UI Toolkit の
        // インラインスタイルにグラデーションが無いので Surface のべた塗りで代用する
        void PaintScrollArrow(MeshGenerationContext context, VisualElement arrow, bool up)
        {
            if (context == null || _theme == null || arrow == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = arrow.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // ポップアップ面（SurfaceOpaque）と同色にしないと矢印の下で継ぎ目が見える
            painter.fillColor = _theme.SurfaceOpaque;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, 0f));
            painter.LineTo(new Vector2(rect.width, 0f));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0f, rect.height));
            painter.ClosePath();
            painter.Fill();

            float centerX = rect.width * 0.5f;
            float centerY = rect.height * 0.5f;
            float half = CHEVRON_TRIANGLE_WIDTH * 0.5f;
            float halfHeight = CHEVRON_TRIANGLE_HEIGHT * 0.5f;
            float tipY = up ? centerY - halfHeight : centerY + halfHeight;
            float baseY = up ? centerY + halfHeight : centerY - halfHeight;

            painter.fillColor = _theme.Text;
            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX, tipY));
            painter.LineTo(new Vector2(centerX + half, baseY));
            painter.LineTo(new Vector2(centerX - half, baseY));
            painter.ClosePath();
            painter.Fill();
        }

        // box-shadow が無いので、外側へ広がる角丸を数枚重ねて 0 0 20px を近似する
        void OnGenerateShadow(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _shadowLayer == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _shadowLayer.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= SHADOW_SPREAD * 2f || rect.height <= SHADOW_SPREAD * 2f)
            {
                return;
            }

            Color shadow = _theme.Shadow;
            float radius = _theme.RadiusPopup;

            for (int i = SHADOW_LAYERS; i >= 1; i--)
            {
                float grow = SHADOW_SPREAD * i / SHADOW_LAYERS;
                Color layer = shadow;

                // 外側ほど薄い。重ね合わせでおおよそガウス状の減衰になる
                layer.a = shadow.a / SHADOW_LAYERS;
                painter.fillColor = layer;

                TraceRoundedRect(
                    painter,
                    SHADOW_SPREAD - grow,
                    SHADOW_SPREAD - grow,
                    rect.width - (SHADOW_SPREAD - grow) * 2f,
                    rect.height - (SHADOW_SPREAD - grow) * 2f,
                    radius + grow);
                painter.Fill();
            }
        }

        static void TraceRoundedRect(
            Painter2D painter, float x, float y, float width, float height, float radius)
        {
            float limit = Mathf.Min(width, height) * 0.5f;
            float r = Mathf.Clamp(radius, 0f, limit);

            painter.BeginPath();
            painter.MoveTo(new Vector2(x + r, y));
            painter.ArcTo(new Vector2(x + width, y), new Vector2(x + width, y + height), r);
            painter.ArcTo(new Vector2(x + width, y + height), new Vector2(x, y + height), r);
            painter.ArcTo(new Vector2(x, y + height), new Vector2(x, y), r);
            painter.ArcTo(new Vector2(x, y), new Vector2(x + width, y), r);
            painter.ClosePath();
        }

        #endregion

        #region Helpers

        long Now()
        {
            Func<long> source = TimeSource;
            if (source != null)
            {
                return source();
            }

            return (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        // C# の % は負値で負を返すので符号を揃える
        static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        static void ApplyTransition(VisualElement element, float duration, string property)
        {
            if (element == null)
            {
                return;
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName(property) });
            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
        }

        static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
        }

        static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
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
