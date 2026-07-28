using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>テキストの水平整列（Vue の InputAlign 相当）。</summary>
    public enum TweeqTextAlign
    {
        Left,
        Center,
        Right,
    }

    /// <summary>
    /// 文字列入力欄（string-color-spec.md「StringInput」節）。
    ///
    /// NumberInput と違い、テキスト編集を邪魔するジェスチャ（スクラブ）が無いので
    /// TextField を常時前面に置いたままにする。表示用オーバーレイと編集用 TextField を
    /// 切り替える二段構えを取らないのは、仕様の「ポインタ由来のフォーカスでは全選択せず
    /// クリック位置にキャレット」を成立させるため（クリック後に TextField を出す方式だと
    /// 押した座標のキャレットが失われる）。
    ///
    /// 編集セッションの状態機械（<see cref="BeginEditing" /> / <see cref="SetEditingText" /> /
    /// <see cref="CommitEditing" /> / <see cref="EndEditing" /> / <see cref="CancelEditing" />）は
    /// panel 非依存にしてある。実 UI のフォーカス・キー入力はこの層を叩くだけなので、
    /// panel 未接続でも例外を出さずに状態だけが進む（EditMode テストはこの層を叩く）。
    /// </summary>
    [UxmlElement]
    public partial class StringInput
        : VisualElement, INotifyValueChanged<string>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // 編集中のテキストの文字サイズ。NumberInput A-6 と同じく明示指定する
        const float TEXT_FONT_SIZE = 12f;

        // 仕様: 左右パディング 0.5em。fontSize 12px 基準で 6px
        const float TEXT_PADDING = 6f;

        const float FOCUS_RING_WIDTH = 1f;
        const float DISABLED_BORDER_WIDTH = 1f;

        // TextField の内側要素（背景・枠を消して高さを使い切るために触る）
        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

        #region Fields

        string _value = string.Empty;

        // 表示中の生テキスト。リジェクト中は _value と乖離する（Vue の display ref 相当）
        string _display = string.Empty;

        // 編集開始時の値。Escape の復元先
        string _valueAtEditStart = string.Empty;

        Func<string, bool> _validator;

        // _display が validator を通っていない状態。文字色を Error にするだけで表示は据え置く
        bool _rejected;

        TweeqTextAlign _align = TweeqTextAlign.Left;
        bool _disabled;
        bool _invalid;
        bool _editing;
        bool _hovered;

        // 直近のフォーカスがポインタ由来か（NumberInput C-2 と同じ手）。
        // Tab 由来なら全選択、クリック由来ならキャレット位置を尊重する
        bool _focusFromPointer;

        // 現在の編集セッションが全選択で始まったか。panel 非依存に「Tab 全選択」を検証するために持つ
        bool _selectedAllAtEditStart;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        TweeqTheme _theme = TweeqTheme.Dark();

        TextField _textField;
        VisualElement _textInput;
        TextElement _textElement;
        VisualElement _focusRing;

        #endregion

        #region Public API

        /// <summary>
        /// validator を通過したキー入力ごとに発火する（値が実際に動いたときだけ）。
        /// Escape による巻き戻しでも発火する。
        /// </summary>
        public event Action<string> ValueChanged;

        /// <summary>blur / Enter のときだけ発火する。キー入力・Escape では発火しない。</summary>
        public event Action<string> Confirmed;

        /// <summary>検証済みの出力値。</summary>
        [UxmlAttribute]
        public string value
        {
            get => _value;
            set
            {
                string next = value ?? string.Empty;
                if (_value == next)
                {
                    return;
                }

                string previous = _value;
                SetValueWithoutNotify(next);
                ValueChanged?.Invoke(_value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>
        /// 入力の受理判定。null なら常に許容する。
        /// false を返した入力は表示だけ残り、<see cref="value" /> は据え置かれる（Vue の validLocal 方式）。
        /// </summary>
        public Func<string, bool> Validator
        {
            get => _validator;
            set
            {
                _validator = value;

                // 判定基準が変わったので、今出ている表示を評価し直す
                _rejected = !IsAccepted(_display);
                Refresh();
            }
        }

        /// <summary>テキストの整列。既定は左寄せ（Number と違い文中編集を想定するため）。</summary>
        [UxmlAttribute]
        public TweeqTextAlign Align
        {
            get => _align;
            set
            {
                if (_align == value)
                {
                    return;
                }

                _align = value;
                ApplyAlign();
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

                if (_disabled && _editing)
                {
                    // 無効化の瞬間に編集が生きていると、確定する手段が無くなる。
                    // ただし「操作していないのに Confirmed が飛ぶ」のは事故なので確定はしない
                    // （NumberInput の Disabled → SetEditing(false) と同じ扱い）
                    FinishEditing(false);
                }

                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>外部から与える不正値表示。validator によるリジェクトと OR で合成される。</summary>
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

        /// <summary>横方向グループでの位置。設定すると角丸が潰れる。</summary>
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

        /// <summary>表示中の生テキスト。リジェクト中は <see cref="value" /> と食い違う。</summary>
        public string DisplayText => _display;

        /// <summary>編集セッション中か。</summary>
        public bool IsEditing => _editing;

        /// <summary>現在の表示が validator に弾かれているか。</summary>
        public bool IsRejected => _rejected;

        /// <summary>編集開始時の値（Escape の復元先）。</summary>
        public string ValueAtEditStart => _valueAtEditStart;

        /// <summary>現在の編集セッションが全選択で始まったか（Tab フォーカス）。</summary>
        public bool SelectedAllAtEditStart => _selectedAllAtEditStart;

        /// <summary>ChangeEvent / ValueChanged を発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(string newValue)
        {
            _value = newValue ?? string.Empty;

            // 編集中の打鍵を外部設定で壊さない（Vue の display watcher と同じ条件）
            if (!_editing)
            {
                _display = _value;
                _rejected = !IsAccepted(_display);
                SyncTextField();
            }

            Refresh();
        }

        #endregion

        #region Editing session

        /// <summary>
        /// 編集セッションを開始する。fromPointer=false（Tab などキーボード由来）なら全選択する。
        /// 既に編集中なら何もしない。
        /// </summary>
        /// <param name="fromPointer">ポインタ由来のフォーカスか。true なら全選択しない。</param>
        public void BeginEditing(bool fromPointer = false)
        {
            if (_disabled || _editing)
            {
                return;
            }

            _focusFromPointer = fromPointer;
            BeginEditingInternal();

            if (this.panel != null && _textField != null)
            {
                _textField.Focus();
            }

            if (!fromPointer)
            {
                SelectAll();
            }
        }

        /// <summary>
        /// 打鍵 1 回ぶんの表示更新。validator を通れば値へ反映して
        /// <see cref="ValueChanged" /> を出し、弾かれたら表示だけ残す。
        /// </summary>
        /// <param name="text">入力欄の新しい表示テキスト。null は空文字として扱う。</param>
        public void SetEditingText(string text)
        {
            if (_disabled)
            {
                return;
            }

            string next = text ?? string.Empty;
            _display = next;
            SyncTextField();

            if (IsAccepted(next))
            {
                _rejected = false;

                // 値が動いたときだけ ValueChanged が出る（setter 側の等値ガード）
                this.value = next;
            }
            else
            {
                _rejected = true;
            }

            Refresh();
        }

        /// <summary>
        /// Enter 確定。リジェクト中の表示を <see cref="value" /> へ巻き戻し、
        /// <see cref="Confirmed" /> を 1 回発火する。編集セッションは続く（Enter では blur しない）。
        /// </summary>
        public void CommitEditing()
        {
            if (!_editing)
            {
                return;
            }

            RollbackDisplayToValue();
            Refresh();
            Confirmed?.Invoke(_value);
        }

        /// <summary>blur 確定。<see cref="CommitEditing" /> と同じ確定をしてから編集を終える。</summary>
        public void EndEditing()
        {
            if (!_editing)
            {
                return;
            }

            FinishEditing(true);
        }

        /// <summary>
        /// Escape。編集開始時の値へ復元して編集を終える（<see cref="Confirmed" /> は発火しない）。
        ///
        /// 原典に Escape の取り消しは無いが、Number / Rotary で採用済みの
        /// 「Escape = 開始値復元」との一貫性を優先する意図的逸脱（string-color-spec.md）。
        /// </summary>
        public void CancelEditing()
        {
            if (!_editing)
            {
                return;
            }

            // blur 経路（OnFocusOut → EndEditing）に確定させないよう、先にセッションを畳む
            _editing = false;

            // ドラッグのキャンセルと同じく、途中で通知した値を巻き戻す方向の通知も出す
            this.value = _valueAtEditStart;

            // 値が動かなかった（リジェクト表示だけだった）場合もここで表示が揃う
            RollbackDisplayToValue();
            Refresh();
            BlurTextField();
        }

        /// <summary>表示テキストを全選択する。panel 未接続なら記録だけ行う。</summary>
        public void SelectAll()
        {
            _selectedAllAtEditStart = true;

            if (_textField == null || this.panel == null)
            {
                return;
            }

            // フォーカスが確定した次のフレームでないと選択範囲が上書きされる（NumberInput と同じ）
            this.schedule.Execute(() =>
            {
                if (_textField != null && _editing)
                {
                    _textField.SelectAll();
                }
            }).StartingIn(0);
        }

        void FinishEditing(bool confirm)
        {
            RollbackDisplayToValue();
            _editing = false;
            Refresh();

            if (confirm)
            {
                Confirmed?.Invoke(_value);
            }
        }

        void BeginEditingInternal()
        {
            if (_editing)
            {
                return;
            }

            _editing = true;
            _valueAtEditStart = _value;
            _selectedAllAtEditStart = false;
            Refresh();
        }

        // 確定時の巻き戻し。Vue の confirm() が display = local = model を置き直すのと同じ
        void RollbackDisplayToValue()
        {
            _display = _value;
            _rejected = !IsAccepted(_display);
            SyncTextField();
        }

        bool IsAccepted(string text)
        {
            return _validator == null || _validator(text);
        }

        #endregion

        #region Construction

        public StringInput()
        {
            this.AddToClassList("tweeq-string-input");

            // ルート自身はフォーカスを取らない。内包する TextField が唯一のタブストップになる
            // （ルートも focusable にすると 1 フィールドで 2 回 Tab が止まる）
            this.focusable = false;
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;

            BuildChildren();
            ApplyStaticStyles();
            ApplyInteractivity();

            // TextField より先に Enter / Escape を横取りするため TrickleDown で登録する
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // フォーカス移動より先に「ポインタで始まったか」を立てたいので TrickleDown
            this.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);

            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);

            Refresh();
        }

        void BuildChildren()
        {
            _textField = new TextField
            {
                name = "tweeq-string-text",

                // 1 文字ごとに ValueChanged を出す必要がある（仕様の2層イベント契約）。
                // isDelayed = true だと Enter / blur まで ChangeEvent が来ない
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
            _textField.RegisterValueChangedCallback(OnTextChanged);
            this.hierarchy.Add(_textField);

            _textInput = _textField.Q(TEXT_INPUT_NAME);

            // 実際に字を描くのは unity-text-input の中の TextElement。
            // 縦潰れは input 側だけ直しても残るのでこちらにも同じ指定を掛ける（NumberInput A-6）
            _textElement = _textInput != null ? _textInput.Q<TextElement>() : null;

            // フォーカスリングは別レイヤの border で描く。ルート側に border を足すと
            // 絶対配置の子が 1px 内側へずれてしまう
            _focusRing = new VisualElement
            {
                name = "tweeq-string-focus-ring",
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

            // 背景のみ 0.15s / cubic-bezier(0.4,0,0.2,1)。UI Toolkit に同一カーブが無いので
            // EaseInOutCubic で近似する（NumberInput / RotaryInput と同じ判断）
            ApplyBackgroundTransition(this);

            if (_focusRing != null)
            {
                SetBorderColor(_focusRing, _theme.Accent);
            }

            if (_textInput != null)
            {
                _textInput.style.backgroundColor = Color.clear;
                SetBorderWidth(_textInput, 0f);
                SetBorderColor(_textInput, Color.clear);
                _textInput.style.paddingLeft = TEXT_PADDING;
                _textInput.style.paddingRight = TEXT_PADDING;
                _textInput.style.marginLeft = 0f;
                _textInput.style.marginRight = 0f;

                // NumberInput A-6: 既定 USS の上下 padding／auto 高さのままだと 24px の枠内で
                // 行が潰れて読めなくなる。高さと文字サイズを明示して 24px を使い切る
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
                _textElement.style.height = Length.Percent(100f);
                _textElement.style.minHeight = 0f;
                _textElement.style.paddingTop = 0f;
                _textElement.style.paddingBottom = 0f;
                _textElement.style.marginTop = 0f;
                _textElement.style.marginBottom = 0f;
                _textElement.style.fontSize = TEXT_FONT_SIZE;
            }

            if (_textField != null)
            {
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

                // Align プロパティ（TweeqTextAlign）が同名を先に解決してしまうので完全修飾する
                _textField.style.alignItems = UnityEngine.UIElements.Align.Stretch;
            }

            ApplyAlign();
        }

        void ApplyBackgroundTransition(VisualElement element)
        {
            if (element == null || _theme == null)
            {
                return;
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("background-color") });
            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(_theme.HoverTransitionDuration, TimeUnit.Second) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
        }

        void ApplyAlign()
        {
            TextAnchor anchor;
            switch (_align)
            {
                case TweeqTextAlign.Center:
                    anchor = TextAnchor.MiddleCenter;
                    break;

                case TweeqTextAlign.Right:
                    anchor = TextAnchor.MiddleRight;
                    break;

                default:
                    anchor = TextAnchor.MiddleLeft;
                    break;
            }

            if (_textField != null)
            {
                _textField.style.unityTextAlign = anchor;
            }

            if (_textInput != null)
            {
                _textInput.style.unityTextAlign = anchor;
            }

            if (_textElement != null)
            {
                _textElement.style.unityTextAlign = anchor;
            }
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;

            if (_textField != null)
            {
                _textField.SetEnabled(!_disabled);
                _textField.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            }
        }

        // 両軸の指定は OR で合成する（片方でも「潰す」なら潰す）
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

        #region Events

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            // 降ろすのは FocusOut のときだけ（＝「今のフォーカスはポインタで始まった」を意味する）
            _focusFromPointer = true;
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

        void OnFocusIn(FocusInEvent evt)
        {
            if (evt == null || _disabled || !IsTextTarget(evt.target))
            {
                return;
            }

            BeginEditingInternal();
            ScheduleKeyboardSelectAll();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (evt == null || !IsTextTarget(evt.target))
            {
                return;
            }

            // 次に来る FocusIn は「新しいフォーカスセッションの開始」として判定し直す
            _focusFromPointer = false;

            // Escape 経路は先にセッションを畳んでいるので、ここでは確定しない
            EndEditing();
        }

        // ポインタ由来かどうかは「同じフレームの PointerDown を処理し終えたあと」でないと確定しない。
        // schedule はそのフレームのイベント処理がすべて終わってから走るので、そこで判定する
        void ScheduleKeyboardSelectAll()
        {
            if (this.panel == null)
            {
                return;
            }

            this.schedule.Execute(() =>
            {
                if (_focusFromPointer || _disabled || !_editing || _selectedAllAtEditStart)
                {
                    return;
                }

                SelectAll();
            }).StartingIn(0);
        }

        void OnTextChanged(ChangeEvent<string> evt)
        {
            if (evt == null)
            {
                return;
            }

            SetEditingText(evt.newValue);
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
                    if (_editing)
                    {
                        CommitEditing();
                        evt.StopPropagation();
                    }

                    break;

                case KeyCode.Escape:
                    if (_editing)
                    {
                        CancelEditing();
                        evt.StopPropagation();
                    }

                    break;
            }
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

        // TextField は delegatesFocus なので、実際に focus を持つのは内側の要素。
        // _textField.Blur() では外れないことがあるため、focusedElement 側から降ろす
        void BlurTextField()
        {
            if (_textField == null || this.panel == null)
            {
                return;
            }

            FocusController controller = this.focusController;
            if (controller == null)
            {
                return;
            }

            VisualElement focused = controller.focusedElement as VisualElement;
            if (focused != null && IsTextTarget(focused))
            {
                focused.Blur();
            }
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

        #region Refresh

        // 打鍵ごとに通る経路なので、文字列を作らない・比較だけで済ませる
        void SyncTextField()
        {
            if (_textField == null || _textField.value == _display)
            {
                return;
            }

            _textField.SetValueWithoutNotify(_display);
        }

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            UpdateBackground();
            UpdateTextColor();

            if (_focusRing != null)
            {
                _focusRing.style.display = _editing && !_disabled
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        void UpdateBackground()
        {
            if (_disabled)
            {
                // 背景透明 + 1px Border のインセット枠（NumberInput と同じ）
                this.style.backgroundColor = Color.clear;
                SetBorderWidth(this, DISABLED_BORDER_WIDTH);
                SetBorderColor(this, _theme.Border);
                return;
            }

            SetBorderWidth(this, 0f);
            this.style.backgroundColor = _hovered ? _theme.InputHover : _theme.Input;
        }

        // 仕様: invalid は文字色を Error に変えるだけ（枠線・アイコンは変えない）
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

        bool ShowInvalid => _invalid || _rejected;

        #endregion

        #region Helpers

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
