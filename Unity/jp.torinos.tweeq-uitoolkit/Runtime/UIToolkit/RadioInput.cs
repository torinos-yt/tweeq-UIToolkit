using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// セグメントは Label 要素で作る。命名衝突は無いが、他の Input と表記を揃えるため別名にする
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// セグメント切替（仕様 §5）。選択中のセグメントの実測 rect へスライドする
    /// インジケーターを背後に敷く。角丸融合には参加しない。
    ///
    /// Vue はジェネリックな options を取るが、Unity 版は string[] + インデックスに固定する
    /// （Unity 決定事項 2）。アイコン列とレスポンシブ段階（rowIcon/colFull/colIcon）は v1 スコープ外。
    /// </summary>
    [UxmlElement]
    public partial class RadioInput : VisualElement, INotifyValueChanged<int>, ITweeqThemed
    {
        #region Constants

        // Vue の padding 0 .75em を rem12 換算した実寸
        const float SEGMENT_PADDING = 9f;

        // 仕様 §5: セグメント間 gap 1px
        const float SEGMENT_GAP = 1f;

        const float FOCUS_RING_WIDTH = 1f;

        // 仕様 §5: ユーザー起因の値変更だけスライドさせる。フラグの保持時間（Vue の 250ms）
        const long ANIMATING_HOLD_MS = 250;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        int _value;
        string[] _options = Array.Empty<string>();

        readonly List<UILabel> _segments = new List<UILabel>();
        readonly VisualElement _indicator;
        readonly VisualElement _focusRing;

        int _hoveredIndex = -1;
        bool _dragging;
        bool _focused;
        int _pointerId = PointerId.invalidPointerId;

        // true の間だけインジケーターに遷移を掛ける。リサイズでのスライドは「バグに見える」ので殺す
        bool _animating;
        IVisualElementScheduledItem _animatingItem;

        #endregion

        #region Public API

        /// <summary>ドラッグのリリース・矢印キー操作ごとに発火する。</summary>
        public event Action<int> Confirmed;

        /// <summary>
        /// 選択肢。設定・取得ともにコピーを通す（呼び出し側の配列と内部状態を切り離す）。
        /// 選択中インデックスが新しい長さから外れた場合は、通知せず範囲内へ畳む。
        /// </summary>
        // UXML では要素数可変の string[]（カンマ区切り）として書ける
        [UxmlAttribute("options")]
        public string[] Options
        {
            get
            {
                string[] copy = new string[_options.Length];
                Array.Copy(_options, copy, _options.Length);
                return copy;
            }

            set
            {
                if (value == null)
                {
                    _options = Array.Empty<string>();
                }
                else
                {
                    _options = new string[value.Length];
                    for (int i = 0; i < value.Length; i++)
                    {
                        _options[i] = value[i] ?? string.Empty;
                    }
                }

                if (_value >= _options.Length)
                {
                    _value = _options.Length > 0 ? _options.Length - 1 : 0;
                }

                RebuildSegments();
                Refresh();
            }
        }

        /// <summary>選択インデックス。範囲外の代入は無視する（仕様 API 契約）。</summary>
        // UXML の属性は宣言順に適用されるため、Options より後に置く。
        // 逆順だと options 未設定＝要素数 0 の状態で範囲外判定に捨てられ、value が効かない
        [UxmlAttribute]
        public int value
        {
            get => _value;
            set
            {
                if (!IsValidIndex(value) || _value == value)
                {
                    return;
                }

                int previous = _value;
                SetValueWithoutNotify(value);
                NotifyValueChanged(previous, _value);
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

        /// <summary>ChangeEvent を発火せずに値を設定する。範囲外は無視する。</summary>
        public void SetValueWithoutNotify(int newValue)
        {
            if (!IsValidIndex(newValue))
            {
                return;
            }

            _value = newValue;
            Refresh();
        }

        /// <summary>
        /// 矢印キー移動用のラップアラウンド計算（仕様 §5。egui 由来の意図的補完）。
        /// count が 0 以下なら 0 を返す。
        /// </summary>
        public static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        #endregion

        #region Construction

        public RadioInput()
        {
            this.AddToClassList("tweeq-radio-input");

            // 矢印キーを受け取るためルート自身がフォーカスを持つ（セグメントは非フォーカス）
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Stretch;
            this.style.flexShrink = 0f;

            // 仕様 §5: インジケーターが角からはみ出さないようクリップする
            this.style.overflow = Overflow.Hidden;

            _indicator = new VisualElement
            {
                name = "tweeq-radio-indicator",
                pickingMode = PickingMode.Ignore,
            };
            _indicator.style.position = Position.Absolute;
            _indicator.style.left = 0f;
            _indicator.style.top = 0f;
            _indicator.style.width = 0f;
            _indicator.style.height = 0f;
            _indicator.style.display = DisplayStyle.None;

            // セグメントより先に追加する＝描画順が下になる（UI Toolkit に z-index は無い）
            this.hierarchy.Add(_indicator);

            _focusRing = new VisualElement
            {
                name = "tweeq-radio-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _focusRing.style.position = Position.Absolute;
            _focusRing.style.left = 0f;
            _focusRing.style.top = 0f;
            _focusRing.style.right = 0f;
            _focusRing.style.bottom = 0f;
            _focusRing.style.display = DisplayStyle.None;
            SetBorderWidth(_focusRing, FOCUS_RING_WIDTH);

            // 常に最前面。セグメントを組み直すたびに RebuildSegments が末尾へ付け直す
            this.hierarchy.Add(_focusRing);

            ApplyStaticStyles();

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown);

            // 矢印キーは KeyDown と別に NavigationMoveEvent も飛ばし、そちらがフォーカスを
            // 動かしてしまう（feedback-fixes-01.md A-5）
            this.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        public RadioInput(string[] options)
            : this()
        {
            this.Options = options;
        }

        void ApplyStaticStyles()
        {
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            this.style.backgroundColor = _theme.Input;
            SetCornerRadius(this, _theme.InputRadius);
            SetCornerRadius(_focusRing, _theme.InputRadius);
            SetBorderColor(_focusRing, _theme.Accent);
            SetCornerRadius(_indicator, _theme.InputRadius);

            ApplyIndicatorTransition(_animating);

            for (int i = 0; i < _segments.Count; i++)
            {
                ApplySegmentStyles(_segments[i], i);
            }
        }

        void RebuildSegments()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                this.hierarchy.Remove(_segments[i]);
            }

            _segments.Clear();
            _hoveredIndex = -1;

            for (int i = 0; i < _options.Length; i++)
            {
                UILabel segment = new UILabel(_options[i])
                {
                    name = "tweeq-radio-segment",

                    // ヒットテストはルート側で layout 矩形を見て行う（キャプチャ中も同じ経路にしたい）
                    pickingMode = PickingMode.Ignore,
                };
                ApplySegmentStyles(segment, i);
                this.hierarchy.Add(segment);
                _segments.Add(segment);
            }

            // フォーカスリングは常に最前面。セグメントを足し直したら付け直す
            if (_focusRing.parent == this)
            {
                this.hierarchy.Remove(_focusRing);
            }

            this.hierarchy.Add(_focusRing);
        }

        void ApplySegmentStyles(UILabel segment, int index)
        {
            segment.style.flexGrow = 1f;
            segment.style.flexShrink = 1f;
            segment.style.minWidth = 0f;
            segment.style.paddingLeft = SEGMENT_PADDING;
            segment.style.paddingRight = SEGMENT_PADDING;
            segment.style.paddingTop = 0f;
            segment.style.paddingBottom = 0f;
            segment.style.marginTop = 0f;
            segment.style.marginBottom = 0f;
            segment.style.marginRight = 0f;

            // UI Toolkit のインラインスタイルに flex gap が無いので、先頭以外のマージンで作る
            segment.style.marginLeft = index == 0 ? 0f : SEGMENT_GAP;

            segment.style.unityTextAlign = TextAnchor.MiddleCenter;
            segment.style.whiteSpace = WhiteSpace.NoWrap;
            segment.style.overflow = Overflow.Hidden;
            segment.style.textOverflow = TextOverflow.Ellipsis;
            SetCornerRadius(segment, _theme.InputRadius);

            ApplyTransition(
                segment,
                _theme.HoverTransitionDuration,
                EasingMode.EaseInOutCubic,
                "background-color",
                "color");
        }

        // インジケーターだけは plain ease（仕様 §5 の明示された例外）。
        // animating が降りている間は遷移時間 0 にして、リサイズでのスライドを殺す
        void ApplyIndicatorTransition(bool animate)
        {
            float duration = animate ? _theme.HoverTransitionDuration : 0f;

            List<StylePropertyName> names = new List<StylePropertyName>
            {
                new StylePropertyName("translate"),
                new StylePropertyName("width"),
                new StylePropertyName("height"),
                new StylePropertyName("background-color"),
            };

            List<TimeValue> durations = new List<TimeValue>
            {
                new TimeValue(duration, TimeUnit.Second),
                new TimeValue(duration, TimeUnit.Second),
                new TimeValue(duration, TimeUnit.Second),

                // 色だけはユーザー起因かどうかに関係なく hover 系の 0.15s で追従させる
                new TimeValue(_theme.HoverTransitionDuration, TimeUnit.Second),
            };

            List<EasingFunction> easings = new List<EasingFunction>
            {
                new EasingFunction(EasingMode.Ease),
                new EasingFunction(EasingMode.Ease),
                new EasingFunction(EasingMode.Ease),
                new EasingFunction(EasingMode.EaseInOutCubic),
            };

            _indicator.style.transitionProperty = new StyleList<StylePropertyName>(names);
            _indicator.style.transitionDuration = new StyleList<TimeValue>(durations);
            _indicator.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            Color activeText = TweeqTheme.ContrastText(_theme.Accent);

            for (int i = 0; i < _segments.Count; i++)
            {
                UILabel segment = _segments[i];
                bool active = i == _value;
                bool hovered = i == _hoveredIndex;

                segment.style.color = active ? activeText : _theme.Text;

                // 非アクティブだけ hover 面色を出す。アクティブ側はインジケーターの色で表現する
                segment.style.backgroundColor = !active && hovered
                    ? _theme.InputHover
                    : Color.clear;
            }

            _focusRing.style.display = _focused ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateIndicator();
        }

        void UpdateIndicator()
        {
            if (_value < 0 || _value >= _segments.Count)
            {
                _indicator.style.display = DisplayStyle.None;
                return;
            }

            Rect rect = _segments[_value].layout;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                // レイアウト未確定。GeometryChangedEvent で改めて呼ばれる
                return;
            }

            _indicator.style.display = DisplayStyle.Flex;

            // 遷移設定は幾何を書く前に確定させる（同フレームで duration が効くように）
            ApplyIndicatorTransition(_animating);

            _indicator.style.translate = new Translate(rect.x, rect.y);
            _indicator.style.width = rect.width;
            _indicator.style.height = rect.height;

            // ドラッグ中／アクティブ hover 中は「掴んでいる」表現として hover 側の色にする
            bool held = _dragging || _hoveredIndex == _value;
            _indicator.style.backgroundColor = held ? _theme.AccentHover : _theme.Accent;
        }

        void NotifyValueChanged(int previous, int current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<int> changeEvent = ChangeEvent<int>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Interaction

        // ユーザー起因の値変更。スライド遷移を許可したうえで通常の value 経路へ流す
        bool SetValueFromUser(int next)
        {
            if (!IsValidIndex(next) || _value == next)
            {
                return false;
            }

            MarkAnimating();
            this.value = next;
            return true;
        }

        void MarkAnimating()
        {
            _animatingItem?.Pause();
            _animatingItem = null;

            if (this.panel == null)
            {
                // スケジューラが回らない＝フラグを降ろせない。立てっぱなしを避けて何もしない
                _animating = false;
                return;
            }

            _animating = true;
            _animatingItem = this.schedule.Execute(() =>
            {
                _animatingItem = null;
                _animating = false;
            }).StartingIn(ANIMATING_HOLD_MS);
        }

        // 主軸（X）でのヒットテスト。セグメントの実測 rect を左から見て最初に収まったものを返す
        int IndexAt(float x)
        {
            if (_segments.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < _segments.Count; i++)
            {
                Rect rect = _segments[i].layout;
                if (float.IsNaN(rect.xMax))
                {
                    continue;
                }

                if (x < rect.xMax)
                {
                    return i;
                }
            }

            return _segments.Count - 1;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _segments.Count == 0)
            {
                return;
            }

            _pointerId = evt.pointerId;
            _dragging = true;

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
                this.Focus();
            }

            Vector2 local = LocalPosition(evt);
            _hoveredIndex = IndexAt(local.x);
            SetValueFromUser(_hoveredIndex);
            Refresh();

            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            Vector2 local = LocalPosition(evt);

            if (!_dragging)
            {
                int hovered = IndexAt(local.x);
                if (hovered == _hoveredIndex)
                {
                    return;
                }

                _hoveredIndex = hovered;
                Refresh();
                return;
            }

            if (evt.pointerId != _pointerId)
            {
                return;
            }

            // ドラッグ中は「離した時に決める」のではなく、跨いだ時点で即選択を移す（仕様 §5）
            _hoveredIndex = IndexAt(local.x);
            SetValueFromUser(_hoveredIndex);
            Refresh();
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_dragging || evt.pointerId != _pointerId)
            {
                return;
            }

            int pointerId = _pointerId;
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            ReleasePointerSafely(pointerId);

            Refresh();
            Confirmed?.Invoke(_value);
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // キャプチャを奪われた場合は確定させずにドラッグだけ畳む
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            Refresh();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            if (evt == null || _dragging)
            {
                return;
            }

            // 入った瞬間に動かさない使い方でも hover 面色が出るよう、ここでも当たりを取る
            _hoveredIndex = IndexAt(LocalPosition(evt).x);
            Refresh();
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (_dragging)
            {
                return;
            }

            _hoveredIndex = -1;
            Refresh();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _segments.Count == 0)
            {
                return;
            }

            int direction;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                case KeyCode.UpArrow:
                    direction = -1;
                    break;

                case KeyCode.RightArrow:
                case KeyCode.DownArrow:
                    direction = 1;
                    break;

                default:
                    return;
            }

            int next = WrapIndex(_value + direction, _segments.Count);
            if (SetValueFromUser(next))
            {
                Confirmed?.Invoke(_value);
            }

            evt.StopPropagation();
        }

        // feedback-fixes-01.md A-5: ←→↑↓ は選択変更だけ。フォーカスは動かさない。
        // Next/Previous（Tab）は通常のフォーカス送りとして残す
        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
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

        void OnFocusOut(FocusOutEvent evt)
        {
            _focused = false;
            Refresh();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // ルート／セグメントどちらの変化でもインジケーターの追従先が変わる。
            // ユーザー起因でなければ _animating が降りているので、遷移せず貼り直すだけになる
            UpdateIndicator();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            _animatingItem?.Pause();
            _animatingItem = null;
            _animating = false;
            _dragging = false;
            _focused = false;
            _hoveredIndex = -1;
            _pointerId = PointerId.invalidPointerId;
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

        // キャプチャ中も座標系がぶれないよう、パネル座標からローカルへ変換する
        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return this.WorldToLocal(new Vector2(position.x, position.y));
        }

        bool IsValidIndex(int index)
        {
            return index >= 0 && index < _options.Length;
        }

        #endregion

        #region Helpers

        static void ApplyTransition(
            VisualElement element, float duration, EasingMode easing, params string[] properties)
        {
            if (element == null || properties == null || properties.Length == 0)
            {
                return;
            }

            List<StylePropertyName> names = new List<StylePropertyName>(properties.Length);
            List<TimeValue> durations = new List<TimeValue>(properties.Length);
            List<EasingFunction> easings = new List<EasingFunction>(properties.Length);

            for (int i = 0; i < properties.Length; i++)
            {
                names.Add(new StylePropertyName(properties[i]));
                durations.Add(new TimeValue(duration, TimeUnit.Second));
                easings.Add(new EasingFunction(easing));
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(durations);
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
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

        static void SetCornerRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        #endregion
    }
}
