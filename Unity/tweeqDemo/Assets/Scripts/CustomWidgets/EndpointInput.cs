using System;
using System.Globalization;
using System.Text;
using Tweeq.Core;
using Tweeq.UIToolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TweeqDemo.CustomWidgets
{
    #region Address

    /// <summary>
    /// IPv4 アドレス（+ 任意のポート）のパース結果。
    /// </summary>
    /// <remarks>
    /// オクテット 4 つを配列ではなくフィールドで持つのは、打鍵ごとに走る正規化で
    /// ヒープを触らないため。値域のクランプは <see cref="TryParse"/> の桁積み上げ中に
    /// 行うので、10 進の桁が何桁来ても long が溢れない。
    /// </remarks>
    public readonly struct EndpointAddress
    {
        /// <summary>オクテットの個数。</summary>
        public const int OCTET_COUNT = 4;

        /// <summary>オクテットの上限。</summary>
        public const int OCTET_MAX = 255;

        /// <summary>ポートの上限（16bit）。</summary>
        public const int PORT_MAX = 65535;

        readonly int _octet0;
        readonly int _octet1;
        readonly int _octet2;
        readonly int _octet3;

        /// <summary>ポート番号。<see cref="HasPort"/> が false のときは 0。</summary>
        public readonly int Port;

        /// <summary>元の文字列が ":port" を含んでいたか。</summary>
        public readonly bool HasPort;

        public EndpointAddress(int octet0, int octet1, int octet2, int octet3, int port, bool hasPort)
        {
            _octet0 = octet0;
            _octet1 = octet1;
            _octet2 = octet2;
            _octet3 = octet3;
            Port = port;
            HasPort = hasPort;
        }

        /// <summary>0 起点のオクテット。範囲外は 0 を返す（境界で例外を出さない方針）。</summary>
        public int GetOctet(int index)
        {
            switch (index)
            {
                case 0: return _octet0;
                case 1: return _octet1;
                case 2: return _octet2;
                case 3: return _octet3;
                default: return 0;
            }
        }

        /// <summary>正規化済みの文字列へ書き出す。</summary>
        public string Format(bool includePort)
        {
            StringBuilder builder = new StringBuilder(21);
            for (int index = 0; index < OCTET_COUNT; index++)
            {
                if (index > 0)
                {
                    builder.Append('.');
                }

                builder.Append(GetOctet(index).ToString(CultureInfo.InvariantCulture));
            }

            if (includePort)
            {
                builder.Append(':');
                builder.Append(Port.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>
        /// "1.2.3.4" / "1.2.3.4:8080" をパースする。桁の重複ゼロは許容し、
        /// 値域を超えた数値は上限へ丸める。数字以外が混ざった時点で失敗。
        /// </summary>
        public static bool TryParse(string text, out EndpointAddress address)
        {
            address = default;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            string host = trimmed;
            int port = 0;
            bool hasPort = false;

            int colon = trimmed.IndexOf(':');
            if (colon >= 0)
            {
                // ":" が 2 つ以上ある文字列は IPv6 かタイプミス。どちらも扱わない
                if (trimmed.IndexOf(':', colon + 1) >= 0)
                {
                    return false;
                }

                host = trimmed.Substring(0, colon);
                if (!TryParseClamped(trimmed.Substring(colon + 1), PORT_MAX, out port))
                {
                    return false;
                }

                hasPort = true;
            }

            string[] parts = host.Split('.');
            if (parts.Length != OCTET_COUNT)
            {
                return false;
            }

            int octet0;
            int octet1;
            int octet2;
            int octet3;
            if (!TryParseClamped(parts[0], OCTET_MAX, out octet0)
                || !TryParseClamped(parts[1], OCTET_MAX, out octet1)
                || !TryParseClamped(parts[2], OCTET_MAX, out octet2)
                || !TryParseClamped(parts[3], OCTET_MAX, out octet3))
            {
                return false;
            }

            address = new EndpointAddress(octet0, octet1, octet2, octet3, port, hasPort);
            return true;
        }

        // 上限で頭打ちにしながら桁を積むので、"99999999999999999999" でも溢れない
        static bool TryParseClamped(string text, int max, out int result)
        {
            result = 0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            long accumulated = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                if (c < '0' || c > '9')
                {
                    return false;
                }

                accumulated = accumulated * 10 + (c - '0');
                if (accumulated > max)
                {
                    accumulated = max;
                }
            }

            result = (int)accumulated;
            return true;
        }
    }

    #endregion

    /// <summary>
    /// tweeq の入力欄クロームと操作感を持つエンドポイント（IPv4[:port]）入力。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 外部 asmdef（<c>Tweeq.Demo.CustomWidgets</c>）から <c>Tweeq.Core</c> / <c>Tweeq.UIToolkit</c>
    /// の public API だけでカスタムウィジェットが作れることの実証サンプル
    /// （ext-custom-widgets-spec.md EXT-02）。パッケージの internal には一切依存しない。
    /// </para>
    /// <para>
    /// 状態機械（セッション・セグメント編集・スクラブ）は panel 非依存にしてあり、
    /// 実 UI のフォーカス／ポインタ配線はその層を叩くだけ。EditMode テストは
    /// パネル無しで契約を検証できる（StringInput と同じ設計）。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class EndpointInput
        : VisualElement, INotifyValueChanged<string>, ITweeqThemed, ITweeqInputBox,
          ITweeqConfirmable<string>
    {
        #region Constants

        /// <summary>オクテットの個数。</summary>
        public const int OCTET_COUNT = EndpointAddress.OCTET_COUNT;

        /// <summary>ポートを含めたセグメントの最大個数。</summary>
        public const int MAX_SEGMENT_COUNT = OCTET_COUNT + 1;

        /// <summary>スクラブ感度。仕様 EXT-02: 4px で 1 段（0-255 を約 1000px で横断）。</summary>
        public const float PIXELS_PER_STEP = 4f;

        /// <summary>Shift（fast）の倍率。</summary>
        public const double FAST_MULTIPLIER = 10.0;

        const float TEXT_FONT_SIZE = 12f;
        const float BOX_PADDING = 6f;
        const float OCTET_WIDTH = 26f;
        const float PORT_WIDTH = 38f;
        const float DISABLED_OPACITY = 0.4f;

        #endregion

        #region Fields

        readonly EndpointSegment[] _segments = new EndpointSegment[MAX_SEGMENT_COUNT];
        readonly Label[] _separators = new Label[OCTET_COUNT];

        TweeqFocusRing _focusRing;

        TweeqTheme _theme = TweeqTheme.Dark();
        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        bool _portEnabled;
        bool _disabled;
        bool _invalid;
        bool _hovered;

        bool _sessionActive;
        string _valueAtSessionStart = string.Empty;

        int _focusedSegment;

        // ComposeValue → value setter → ApplyAddress の往復で、打鍵途中の
        // 表示（空欄や "00"）を書き戻さないためのガード
        bool _composing;

        #endregion

        #region Public API

        /// <summary>編集セッションの終了時に、値が変わっていた場合だけ 1 回発火する。</summary>
        public event Action<string> Confirmed;

        /// <summary>
        /// 正規化済みのエンドポイント文字列。<see cref="PortEnabled"/> が true のときだけ ":port" が付く。
        /// </summary>
        /// <remarks>
        /// setter はパースできない文字列を黙って捨てる（プログラム誤用で公演中に例外を出さない）。
        /// </remarks>
        [UxmlAttribute]
        public string value
        {
            get { return ComposeValue(); }
            set
            {
                string previous = ComposeValue();
                SetValueWithoutNotify(value);

                string current = ComposeValue();
                if (previous == current)
                {
                    return;
                }

                NotifyValueChanged(previous, current);
            }
        }

        /// <summary>ポートセグメント（0-65535）を表示するか。</summary>
        [UxmlAttribute]
        public bool PortEnabled
        {
            get { return _portEnabled; }
            set
            {
                if (_portEnabled == value)
                {
                    return;
                }

                string previous = ComposeValue();
                _portEnabled = value;

                // 隠れているセグメントにフォーカスが残ると Tab 順が飛ぶ
                if (!_portEnabled && _focusedSegment >= SegmentCount)
                {
                    _focusedSegment = SegmentCount - 1;
                }

                ApplyPortVisibility();

                string current = ComposeValue();
                if (previous != current)
                {
                    NotifyValueChanged(previous, current);
                }
            }
        }

        /// <summary>操作不能状態。opacity を落とし、ポインタ・フォーカスを遮断する。</summary>
        [UxmlAttribute]
        public bool Disabled
        {
            get { return _disabled; }
            set
            {
                if (_disabled == value)
                {
                    return;
                }

                _disabled = value;

                // 無効化の瞬間に確定を飛ばすのは「操作していないのに Confirmed」になるので避ける
                if (_disabled && _sessionActive)
                {
                    EndSession(false);
                }

                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>外部から与える不正値表示。NumberInput の慣例に合わせ文字色だけを Error にする。</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get { return _invalid; }
            set
            {
                if (_invalid == value)
                {
                    return;
                }

                _invalid = value;
                Refresh();
            }
        }

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get { return _theme; }
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                Refresh();
            }
        }

        /// <summary>横方向グループでの位置。設定すると角丸が潰れる。</summary>
        public TweeqBoxPosition InlinePosition
        {
            get { return _inlinePosition; }
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
            get { return _blockPosition; }
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

        /// <summary>現在表示しているセグメント数（ポート有効なら 5）。</summary>
        public int SegmentCount
        {
            get { return _portEnabled ? MAX_SEGMENT_COUNT : OCTET_COUNT; }
        }

        /// <summary>キー入力の対象になっているセグメントの添字。</summary>
        public int FocusedSegment
        {
            get { return _focusedSegment; }
        }

        /// <summary>編集セッション中か。</summary>
        public bool IsSessionActive
        {
            get { return _sessionActive; }
        }

        /// <summary>セッション開始時の値（Escape の復元先）。</summary>
        public string ValueAtSessionStart
        {
            get { return _valueAtSessionStart; }
        }

        /// <summary>ChangeEvent を発火せずに値を設定する。パース不能なら現値を維持する。</summary>
        public void SetValueWithoutNotify(string newValue)
        {
            EndpointAddress address;
            if (!EndpointAddress.TryParse(newValue, out address))
            {
                return;
            }

            ApplyAddress(address);
        }

        /// <summary>セグメントの現在値。範囲外の添字は 0。</summary>
        public int GetSegment(int index)
        {
            return IsValidSegment(index) ? _segments[index].Value : 0;
        }

        /// <summary>セグメントへ値を書き込み、値が動けば ChangeEvent を出す。</summary>
        public void SetSegment(int index, int segmentValue)
        {
            if (!IsValidSegment(index))
            {
                return;
            }

            string previous = ComposeValue();
            _segments[index].SetValue(segmentValue, true);

            string current = ComposeValue();
            if (previous != current)
            {
                NotifyValueChanged(previous, current);
            }
        }

        /// <summary>セグメントに表示されている生テキスト（編集中は空欄もありうる）。</summary>
        public string GetSegmentText(int index)
        {
            return IsValidSegment(index) ? _segments[index].Text : string.Empty;
        }

        /// <summary>
        /// セグメントへの打鍵 1 回ぶん。数字以外は落とし、"." / ":" が含まれていたら
        /// 次のセグメントへ移動して全選択する（Windows の IP 入力欄と同じ操作感）。
        /// </summary>
        public void SetSegmentText(int index, string text)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _segments[index].ApplyUserText(text);
        }

        /// <summary>セグメントへフォーカスを移す（panel があれば実フォーカスも動かす）。</summary>
        public void FocusSegment(int index)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _focusedSegment = index;
            BeginSession();
            _segments[index].FocusAndSelectAll();
        }

        /// <summary>現在のセグメントから相対移動する。両端では動かない。</summary>
        public void MoveSegment(int delta)
        {
            int next = _focusedSegment + delta;
            if (next < 0 || next >= SegmentCount)
            {
                return;
            }

            FocusSegment(next);
        }

        #endregion

        #region Editing session

        /// <summary>編集セッションを開始する。既に開始済みなら何もしない。</summary>
        public void BeginSession()
        {
            if (_disabled || _sessionActive)
            {
                return;
            }

            _sessionActive = true;
            _valueAtSessionStart = ComposeValue();
            Refresh();
        }

        /// <summary>
        /// Enter 確定。表示を正規化（空欄→0）してから、値が変わっていれば
        /// <see cref="Confirmed"/> を 1 回出す。セッションはそのまま続く。
        /// </summary>
        public void CommitEditing()
        {
            if (!_sessionActive)
            {
                return;
            }

            NormalizeSegmentText();
            FireConfirmedIfChanged();
        }

        /// <summary>Escape。セッション開始値へ復元し、確定を出さずにセッションを畳む。</summary>
        public void CancelEditing()
        {
            if (!_sessionActive)
            {
                return;
            }

            // blur 経路（OnFocusOut → EndSession）に確定させないよう、先にセッションを畳む
            _sessionActive = false;

            this.value = _valueAtSessionStart;
            NormalizeSegmentText();
            Refresh();
        }

        /// <summary>フォーカスがコンポーネント外へ出た。確定してセッションを閉じる。</summary>
        public void EndSession()
        {
            EndSession(true);
        }

        void EndSession(bool confirm)
        {
            if (!_sessionActive)
            {
                return;
            }

            NormalizeSegmentText();
            _sessionActive = false;

            if (confirm)
            {
                FireConfirmedIfChanged();
            }

            Refresh();
        }

        void FireConfirmedIfChanged()
        {
            string current = ComposeValue();
            if (current == _valueAtSessionStart)
            {
                return;
            }

            // 基準を進めておかないと、Enter のあとの blur でもう 1 回飛ぶ
            _valueAtSessionStart = current;

            Action<string> confirmed = Confirmed;
            if (confirmed != null)
            {
                confirmed(current);
            }
        }

        void NormalizeSegmentText()
        {
            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                _segments[index].NormalizeText();
            }
        }

        #endregion

        #region Scrub (状態機械の入口。実 UI では TweeqScrubManipulator が叩く)

        /// <summary>セグメントのスクラブを開始する。</summary>
        public void BeginSegmentScrub(int index)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _focusedSegment = index;
            BeginSession();
            _segments[index].BeginScrub();
        }

        /// <summary>スクラブ 1 サンプル。移動量は px。</summary>
        public void UpdateSegmentScrub(int index, float deltaX, float deltaY, bool shift, bool alt)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _segments[index].UpdateScrub(new ScrubUpdate(deltaX, deltaY, shift, alt));
        }

        /// <summary>スクラブ終了（コミット）。確定はセッション終了時なのでここでは出さない。</summary>
        public void EndSegmentScrub(int index)
        {
            if (!IsValidSegment(index))
            {
                return;
            }

            _segments[index].EndScrub();
        }

        /// <summary>スクラブ中断。開始時の値へ戻す。</summary>
        public void CancelSegmentScrub(int index)
        {
            if (!IsValidSegment(index))
            {
                return;
            }

            _segments[index].CancelScrub();
        }

        #endregion

        #region Construction

        public EndpointInput()
        {
            this.AddToClassList("tweeq-endpoint-input");

            // ルート自身はフォーカスを取らない。タブストップは内包する各 TextField
            this.focusable = false;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.justifyContent = Justify.Center;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;
            this.style.paddingLeft = BOX_PADDING;
            this.style.paddingRight = BOX_PADDING;

            BuildChildren();
            ApplyStaticStyles();
            ApplyPortVisibility();
            ApplyInteractivity();

            // TextField より先に Enter / Escape を横取りするため TrickleDown で登録する
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);

            Refresh();
        }

        void BuildChildren()
        {
            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                bool isPort = index == OCTET_COUNT;

                EndpointSegment segment = new EndpointSegment(
                    index,
                    isPort ? EndpointAddress.PORT_MAX : EndpointAddress.OCTET_MAX,
                    isPort ? PORT_WIDTH : OCTET_WIDTH);

                segment.Changed += OnSegmentChanged;
                segment.Clicked += OnSegmentClicked;
                segment.MoveRequested += OnSegmentMoveRequested;
                segment.FocusGained += OnSegmentFocusGained;

                _segments[index] = segment;

                if (index > 0)
                {
                    Label separator = new Label(index == OCTET_COUNT ? ":" : ".")
                    {
                        name = "tweeq-endpoint-separator",
                        pickingMode = PickingMode.Ignore,
                    };
                    separator.style.fontSize = TEXT_FONT_SIZE;
                    separator.style.unityTextAlign = TextAnchor.MiddleCenter;
                    separator.style.marginLeft = 0f;
                    separator.style.marginRight = 0f;
                    separator.style.marginTop = 0f;
                    separator.style.marginBottom = 0f;
                    separator.style.paddingLeft = 0f;
                    separator.style.paddingRight = 0f;
                    separator.style.flexShrink = 0f;

                    _separators[index - 1] = separator;
                    this.hierarchy.Add(separator);
                }

                this.hierarchy.Add(segment);
            }

            // フォーカスリングは別レイヤの border で描く。ルート側に border を足すと
            // 中身が 1px ずれるため（StringInput と同じ理由）
            _focusRing = TweeqFocusRing.Attach(this);
            _focusRing.name = "tweeq-endpoint-focus-ring";
        }

        void ApplyStaticStyles()
        {
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;

            ApplyCornerRadius();
            TweeqInputBoxStyles.SetBorderColor(this, _theme.Border);
            TweeqInputBoxStyles.ApplyBackgroundTransition(this, _theme);

            for (int index = 0; index < _separators.Length; index++)
            {
                Label separator = _separators[index];
                if (separator == null)
                {
                    continue;
                }

                separator.style.color = _theme.TextSubtle;
                TweeqFonts.Apply(separator, TweeqFonts.NumericFont);
            }

            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                _segments[index].ApplyTheme(_theme);
            }
        }

        void ApplyCornerRadius()
        {
            TweeqInputBoxStyles.ApplyCornerRadius(this, _theme, _inlinePosition, _blockPosition);

            // フォーカスリングは別レイヤなので同じ角丸を掛け直す
            if (_focusRing != null)
            {
                _focusRing.Apply(_theme, _inlinePosition, _blockPosition);
            }
        }

        void ApplyPortVisibility()
        {
            DisplayStyle display = _portEnabled ? DisplayStyle.Flex : DisplayStyle.None;

            _segments[OCTET_COUNT].style.display = display;
            _segments[OCTET_COUNT].SetInteractive(_portEnabled && !_disabled);

            Label separator = _separators[OCTET_COUNT - 1];
            if (separator != null)
            {
                separator.style.display = display;
            }
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                bool live = !_disabled && (index < OCTET_COUNT || _portEnabled);
                _segments[index].SetInteractive(live);
            }
        }

        #endregion

        #region Composition

        bool IsValidSegment(int index)
        {
            return index >= 0 && index < SegmentCount;
        }

        string ComposeValue()
        {
            EndpointAddress address = new EndpointAddress(
                _segments[0].Value,
                _segments[1].Value,
                _segments[2].Value,
                _segments[3].Value,
                _segments[OCTET_COUNT].Value,
                _portEnabled);

            return address.Format(_portEnabled);
        }

        void ApplyAddress(EndpointAddress address)
        {
            // 打鍵経路（_composing）からの往復では表示を書き戻さない。
            // "" や "00" のまま打ち続けられるようにするため
            bool syncDisplay = !_composing;

            for (int index = 0; index < OCTET_COUNT; index++)
            {
                _segments[index].SetValue(address.GetOctet(index), syncDisplay);
            }

            // ポート未指定の文字列は「ポート 0」を意味させる。値文字列が状態の全体になるので、
            // 同じ文字列を入れ直せば必ず同じ状態へ戻る
            _segments[OCTET_COUNT].SetValue(address.HasPort ? address.Port : 0, syncDisplay);
        }

        void NotifyValueChanged(string previous, string current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<string> changeEvent = ChangeEvent<string>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Segment callbacks

        void OnSegmentChanged(EndpointSegment segment)
        {
            _composing = true;
            try
            {
                this.value = ComposeValue();
            }
            finally
            {
                _composing = false;
            }

            Refresh();
        }

        void OnSegmentClicked(EndpointSegment segment)
        {
            // 閾値未満の解放＝クリック。そのセグメントを打ち直せるよう全選択する
            FocusSegment(segment.Index);
        }

        void OnSegmentMoveRequested(EndpointSegment segment, int delta)
        {
            _focusedSegment = segment.Index;

            // 離れる区画に空欄を残さない（"." で抜けた直後に "" のままになるのを防ぐ）
            segment.NormalizeText();

            MoveSegment(delta);
        }

        void OnSegmentFocusGained(EndpointSegment segment)
        {
            _focusedSegment = segment.Index;
            BeginSession();
            Refresh();
        }

        #endregion

        #region Events

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

        void OnFocusIn(FocusInEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            BeginSession();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // セグメント間の移動ではセッションを畳まない（1 セッション = 部品を出るまで）
            VisualElement related = evt.relatedTarget as VisualElement;
            if (related != null && this.Contains(related))
            {
                return;
            }

            EndSession();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_sessionActive)
                    {
                        CommitEditing();
                        evt.StopPropagation();
                    }

                    break;

                case KeyCode.Escape:
                    if (_sessionActive)
                    {
                        CancelEditing();
                        evt.StopPropagation();
                    }

                    break;
            }
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            UpdateBackground();
            UpdateTextColor();

            if (_focusRing != null)
            {
                _focusRing.Visible = _sessionActive && !_disabled;
            }
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

        void UpdateTextColor()
        {
            Color color = _invalid ? _theme.Error : _theme.Text;

            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                _segments[index].ApplyTextColor(color);
            }
        }

        #endregion
    }

    #region Segment

    /// <summary>
    /// エンドポイント 1 区画の view + 状態。スクラブとテキスト編集の両方を受ける。
    /// </summary>
    /// <remarks>
    /// <see cref="TweeqScrubManipulator"/> は PointerDown を握り潰さないので、
    /// 常時前面に居る TextField のフォーカス・キャレットと共存できる。
    /// クリック（閾値未満）は Clicked で降りてくるので、そこで全選択に切り替える。
    /// </remarks>
    sealed class EndpointSegment : VisualElement
    {
        #region Constants

        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

        #region Fields

        readonly TextField _field;
        readonly VisualElement _textInput;
        readonly TextElement _textElement;
        readonly TweeqScrubManipulator _scrub = new TweeqScrubManipulator();
        readonly TweakGesture _gesture = new TweakGesture();

        readonly int _max;

        int _value;
        int _valueAtScrubStart;
        double _scrubLocal;
        bool _scrubbing;

        #endregion

        #region Public API

        /// <summary>0 起点の並び順。ポートは <see cref="EndpointInput.OCTET_COUNT"/>。</summary>
        public int Index { get; }

        /// <summary>クランプ済みの現在値。</summary>
        public int Value
        {
            get { return _value; }
        }

        /// <summary>表示中の生テキスト。打鍵の途中では空欄になりうる。</summary>
        public string Text
        {
            get { return _field.value ?? string.Empty; }
        }

        /// <summary>利用者操作で値が動いた。</summary>
        public event Action<EndpointSegment> Changed;

        /// <summary>閾値未満のポインタ解放（＝クリック）。</summary>
        public event Action<EndpointSegment> Clicked;

        /// <summary>セグメント間移動の要求（+1 / -1）。</summary>
        public event Action<EndpointSegment, int> MoveRequested;

        /// <summary>このセグメント配下がフォーカスを得た。</summary>
        public event Action<EndpointSegment> FocusGained;

        public EndpointSegment(int index, int max, float width)
        {
            Index = index;
            _max = max;

            this.AddToClassList("tweeq-endpoint-segment");

            // テストと UI Builder から個別に掴めるよう並び順を名前に残す
            this.name = "tweeq-endpoint-segment-" + index.ToString(CultureInfo.InvariantCulture);
            this.style.width = width;
            this.style.height = Length.Percent(100f);
            this.style.flexShrink = 0f;

            _field = new TextField
            {
                name = "tweeq-endpoint-segment-text",

                // 1 打鍵ごとに値を組み直す必要があるので遅延させない
                isDelayed = false,
                multiline = false,
                maxLength = max.ToString(CultureInfo.InvariantCulture).Length,
            };
            _field.style.position = Position.Absolute;
            _field.style.left = 0f;
            _field.style.top = 0f;
            _field.style.right = 0f;
            _field.style.bottom = 0f;
            _field.RegisterValueChangedCallback(OnTextChanged);
            this.hierarchy.Add(_field);

            _textInput = _field.Q(TEXT_INPUT_NAME);
            _textElement = _textInput != null ? _textInput.Q<TextElement>() : null;

            _scrub.ScrubBegan += BeginScrub;
            _scrub.ScrubUpdated += UpdateScrub;
            _scrub.ScrubEnded += EndScrub;
            _scrub.ScrubCancelled += CancelScrub;
            _scrub.Clicked += OnScrubClicked;
            this.AddManipulator(_scrub);

            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);

            SyncText();
        }

        /// <summary>値を書き込む。<paramref name="syncDisplay"/> が false なら表示は触らない。</summary>
        public void SetValue(int newValue, bool syncDisplay)
        {
            int clamped = Mathf.Clamp(newValue, 0, _max);
            if (_value == clamped)
            {
                if (syncDisplay)
                {
                    SyncText();
                }

                return;
            }

            _value = clamped;

            if (syncDisplay)
            {
                SyncText();
            }
        }

        /// <summary>表示を値どおりに直す（空欄 → "0"）。</summary>
        public void NormalizeText()
        {
            SyncText();
        }

        /// <summary>
        /// 打鍵 1 回ぶんを適用する。数字以外は捨て、"." / ":" が含まれていれば
        /// 次のセグメントへの移動を要求する。
        /// </summary>
        public void ApplyUserText(string raw)
        {
            string source = raw ?? string.Empty;

            bool advance = false;
            StringBuilder digits = new StringBuilder(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                char c = source[index];
                if (c >= '0' && c <= '9')
                {
                    if (digits.Length < _field.maxLength)
                    {
                        digits.Append(c);
                    }

                    continue;
                }

                if (c == '.' || c == ':')
                {
                    advance = true;
                }
            }

            string filtered = digits.ToString();
            int parsed = 0;
            if (filtered.Length > 0)
            {
                // maxLength で桁数が抑えられているので int で溢れない
                parsed = int.Parse(filtered, CultureInfo.InvariantCulture);
                if (parsed > _max)
                {
                    parsed = _max;
                    filtered = _max.ToString(CultureInfo.InvariantCulture);
                }
            }

            SetText(filtered);
            _value = parsed;

            Action<EndpointSegment> changed = Changed;
            if (changed != null)
            {
                changed(this);
            }

            if (!advance)
            {
                return;
            }

            Action<EndpointSegment, int> move = MoveRequested;
            if (move != null)
            {
                move(this, 1);
            }
        }

        /// <summary>フォーカスして全選択する。panel が無ければ何もしない。</summary>
        public void FocusAndSelectAll()
        {
            if (this.panel == null)
            {
                return;
            }

            _field.Focus();

            // フォーカスが確定した次のフレームでないと選択範囲が上書きされる
            this.schedule.Execute(() =>
            {
                if (_field != null)
                {
                    _field.SelectAll();
                }
            }).StartingIn(0);
        }

        /// <summary>操作可能／不可の切り替え。</summary>
        public void SetInteractive(bool interactive)
        {
            _field.SetEnabled(interactive);
            _field.focusable = interactive;
            _field.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
            this.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
        }

        /// <summary>テーマ由来の静的スタイルを流し込む。</summary>
        public void ApplyTheme(TweeqTheme theme)
        {
            if (theme == null)
            {
                return;
            }

            // TextField を 24px の枠に収める正規化とキャレット色は公開ヘルパ任せ（EXT-03-A）
            TweeqInputBoxStyles.ApplyTextField(_field, theme);

            // 区画の数字は中央寄せ。整列は widget 固有なのでヘルパの後に足す
            if (_textInput != null)
            {
                _textInput.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            if (_textElement != null)
            {
                _textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            _field.style.unityTextAlign = TextAnchor.MiddleCenter;

            // 数値欄なので Geist（fontNumeric）を当てる
            TweeqFonts.Apply(_field, TweeqFonts.NumericFont);
            TweeqFonts.Apply(_textInput, TweeqFonts.NumericFont);
            TweeqFonts.Apply(_textElement, TweeqFonts.NumericFont);
        }

        /// <summary>文字色を差し替える（Invalid 表示）。</summary>
        public void ApplyTextColor(Color color)
        {
            _field.style.color = color;

            if (_textInput != null)
            {
                _textInput.style.color = color;
            }

            if (_textElement != null)
            {
                _textElement.style.color = color;
            }
        }

        #endregion

        #region Scrub

        /// <summary>スクラブ開始。感度は固定なので TweakGesture の speed 域も 1 に固定する。</summary>
        public void BeginScrub()
        {
            _scrubbing = true;
            _valueAtScrubStart = _value;
            _scrubLocal = _value;
            _gesture.Reset();
        }

        /// <summary>スクラブ 1 サンプル。</summary>
        public void UpdateScrub(ScrubUpdate update)
        {
            if (!_scrubbing)
            {
                // manipulator を介さない経路（テスト・キーボード）でも取りこぼさない
                BeginScrub();
            }

            GestureModifiers modifiers = new GestureModifiers(update.Alt, update.Shift, false);

            // 仕様 EXT-02: 感度は固定 4px = 1。縦ドラッグの感度変化は使わないので
            // min/max speed をどちらも 1 に潰す
            GestureUpdate gesture = _gesture.Update(
                update.DeltaX, update.DeltaY,
                1.0 / EndpointInput.PIXELS_PER_STEP,
                modifiers, EndpointInput.FAST_MULTIPLIER, 1.0, 1.0);

            // 生値ごとクランプする。範囲外の数字が見えるのは事故のもと（NumberInput D-3 と同じ判断）
            _scrubLocal = TweeqMath.Clamp(_scrubLocal + gesture.Delta, 0.0, _max);

            NumberValidation validation = NumberValidator.Validate(
                _scrubLocal, 0.0, _max, 1.0, 1.0, false);

            int next = (int)Math.Round(validation.Value, MidpointRounding.AwayFromZero);
            if (next == _value)
            {
                return;
            }

            _value = next;
            SyncText();

            Action<EndpointSegment> changed = Changed;
            if (changed != null)
            {
                changed(this);
            }
        }

        /// <summary>スクラブ終了。</summary>
        public void EndScrub()
        {
            _scrubbing = false;
        }

        /// <summary>スクラブ中断。開始時の値へ戻す。</summary>
        public void CancelScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;

            if (_value == _valueAtScrubStart)
            {
                return;
            }

            _value = _valueAtScrubStart;
            SyncText();

            Action<EndpointSegment> changed = Changed;
            if (changed != null)
            {
                changed(this);
            }
        }

        void OnScrubClicked()
        {
            Action<EndpointSegment> clicked = Clicked;
            if (clicked != null)
            {
                clicked(this);
            }
        }

        #endregion

        #region Events

        void OnTextChanged(ChangeEvent<string> evt)
        {
            if (evt == null)
            {
                return;
            }

            // 表示の書き戻しは SetValueWithoutNotify なので、ここへ来るのは利用者の打鍵だけ
            ApplyUserText(evt.newValue);
        }

        void OnFocusIn(FocusInEvent evt)
        {
            Action<EndpointSegment> gained = FocusGained;
            if (gained != null)
            {
                gained(this);
            }
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            int delta;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                    delta = -1;
                    break;

                case KeyCode.RightArrow:
                    delta = 1;
                    break;

                default:
                    return;
            }

            // キャレットが端に居るときだけセグメントを跨ぐ。文中では通常のキャレット移動
            int caret = _field.textSelection.cursorIndex;
            int length = Text.Length;
            bool atEdge = delta < 0 ? caret <= 0 : caret >= length;
            if (!atEdge)
            {
                return;
            }

            Action<EndpointSegment, int> move = MoveRequested;
            if (move != null)
            {
                move(this, delta);
            }

            evt.StopPropagation();
        }

        #endregion

        #region Text

        void SyncText()
        {
            SetText(_value.ToString(CultureInfo.InvariantCulture));
        }

        void SetText(string text)
        {
            if (_field.value == text)
            {
                return;
            }

            _field.SetValueWithoutNotify(text);
        }

        #endregion
    }

    #endregion
}
