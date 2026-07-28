using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// クラス側に string Label プロパティがあるため、Label 型は別名で参照する
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// オン／オフを持つボタン（仕様 §4）。見た目は <see cref="ButtonInput"/> と同じだが値を持つ。
    /// スワイプもキーショートカットも無く、クリック／Enter／Space のトグルだけ。
    /// アイコンスロットは v1 スコープ外。
    /// </summary>
    [UxmlElement]
    public partial class ButtonToggleInput
        : VisualElement, INotifyValueChanged<bool>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Vue の padding 0 .7em を rem12 換算した実寸
        const float LABEL_PADDING = 8.4f;

        const float DISABLED_OPACITY = 0.4f;
        const float FOCUS_RING_WIDTH = 1f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        bool _value;
        string _labelText = string.Empty;
        bool _disabled;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        readonly UILabel _label;
        readonly VisualElement _focusOuter;
        readonly VisualElement _focusInner;

        bool _hovered;
        bool _focused;
        int _pointerId = PointerId.invalidPointerId;

        #endregion

        #region Public API

        /// <summary>クリック（Enter / Space 含む）ごとに ChangeEvent と対で発火する（仕様 §4）。</summary>
        public event Action<bool> Confirmed;

        /// <summary>オン／オフ。</summary>
        [UxmlAttribute]
        public bool value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                bool previous = _value;
                SetValueWithoutNotify(value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>ボタン内に表示する文字列。</summary>
        // UXML 側は Vue の prop 名（text）に合わせる（ButtonInput と同じ判断）
        [UxmlAttribute("text")]
        public string Label
        {
            get => _labelText;
            set
            {
                string text = value ?? string.Empty;
                if (_labelText == text)
                {
                    return;
                }

                _labelText = text;
                ApplyContentLayout();
            }
        }

        /// <summary>操作不能状態。</summary>
        [UxmlAttribute("disabled")]
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
                _hovered = false;
                ApplyInteractivity();
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

        /// <summary>ChangeEvent を発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            Refresh();
        }

        /// <summary>
        /// プログラムからのクリック。値を反転し ChangeEvent と Confirmed を対で出す。
        /// Disabled のときは何もしない。パネル非依存なのでテストからの発火にも使える。
        /// </summary>
        public void PerformClick()
        {
            if (_disabled)
            {
                return;
            }

            bool next = !_value;
            this.value = next;
            Confirmed?.Invoke(next);
        }

        #endregion

        #region Construction

        public ButtonToggleInput()
        {
            this.AddToClassList("tweeq-button-toggle-input");

            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.justifyContent = Justify.Center;
            this.style.flexShrink = 0f;

            // 外周フォーカスリングを 1px 外に置くので、ここを Hidden にしてはいけない
            this.style.overflow = Overflow.Visible;

            _label = new UILabel(string.Empty) { pickingMode = PickingMode.Ignore };
            _label.style.marginLeft = 0f;
            _label.style.marginRight = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;
            _label.style.paddingLeft = 0f;
            _label.style.paddingRight = 0f;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.whiteSpace = WhiteSpace.NoWrap;
            _label.style.overflow = Overflow.Hidden;
            _label.style.textOverflow = TextOverflow.Ellipsis;
            _label.style.minWidth = 0f;
            _label.style.flexShrink = 1f;
            this.hierarchy.Add(_label);

            _focusInner = CreateRing(0f);
            _focusOuter = CreateRing(-FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusInner);
            this.hierarchy.Add(_focusOuter);

            ApplyStaticStyles();
            ApplyContentLayout();
            ApplyInteractivity();

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        public ButtonToggleInput(string label)
            : this()
        {
            this.Label = label;
        }

        VisualElement CreateRing(float inset)
        {
            VisualElement ring = new VisualElement
            {
                name = "tweeq-button-toggle-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            ring.style.position = Position.Absolute;
            ring.style.left = inset;
            ring.style.top = inset;
            ring.style.right = inset;
            ring.style.bottom = inset;
            ring.style.display = DisplayStyle.None;
            SetBorderWidth(ring, FOCUS_RING_WIDTH);
            return ring;
        }

        void ApplyStaticStyles()
        {
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            ApplyCornerRadius();

            // 仕様 §4: Checkbox の 64ms ではなく hover 系 0.15s（Vue 準拠）。
            // cubic-bezier(0.4,0,0.2,1) は UI Toolkit に無いので EaseInOutCubic で近似する
            ApplyTransition(
                this,
                _theme.HoverTransitionDuration,
                EasingMode.EaseInOutCubic,
                "background-color");
            ApplyTransition(
                _label, _theme.HoverTransitionDuration, EasingMode.EaseInOutCubic, "color");

            SetBorderColor(_focusInner, _theme.Input);
            SetBorderColor(_focusOuter, _theme.Accent);
        }

        void ApplyContentLayout()
        {
            bool hasLabel = !string.IsNullOrEmpty(_labelText);

            _label.text = _labelText;
            _label.style.display = hasLabel ? DisplayStyle.Flex : DisplayStyle.None;

            float padding = hasLabel ? LABEL_PADDING : 0f;
            this.style.paddingLeft = padding;
            this.style.paddingRight = padding;
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.focusable = !_disabled;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            if (_disabled)
            {
                _focused = false;
            }
        }

        // 仕様 §1 の角丸表。両軸の指定は OR で合成する（片方でも「潰す」なら潰す）
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
            SetCornerRadius(_focusInner, radius, topLeft, topRight, bottomLeft, bottomRight);

            // 外側リングは 1px 外に居るので、同じ見え方になるよう半径も 1px 太らせる
            SetCornerRadius(
                _focusOuter,
                radius + FOCUS_RING_WIDTH,
                topLeft,
                topRight,
                bottomLeft,
                bottomRight);
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            bool hovered = _hovered && !_disabled;

            Color background;
            Color text;

            if (_value)
            {
                background = hovered ? _theme.AccentHover : _theme.Accent;
                text = TweeqTheme.ContrastText(background);
            }
            else
            {
                background = hovered ? _theme.InputHover : _theme.Input;

                // 未チェックは面色が淡いので、Vue どおり通常の Text 色を使う
                text = _theme.Text;
            }

            this.style.backgroundColor = background;
            _label.style.color = text;

            bool ringVisible = _focused && !_disabled;

            // 仕様 §4: 未チェック=外周のみ / チェック=内側 Input + 外周 Accent
            _focusInner.style.display = ringVisible && _value
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _focusOuter.style.display = ringVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void NotifyValueChanged(bool previous, bool current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<bool> changeEvent = ChangeEvent<bool>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Events

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            _pointerId = evt.pointerId;

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
            }

            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || _pointerId == PointerId.invalidPointerId
                || evt.pointerId != _pointerId)
            {
                return;
            }

            int pointerId = _pointerId;
            _pointerId = PointerId.invalidPointerId;
            ReleasePointerSafely(pointerId);

            if (_disabled)
            {
                return;
            }

            Vector3 position = evt.position;
            bool inside = this.ContainsPoint(this.WorldToLocal(new Vector2(position.x, position.y)));

            // ポインタで得たフォーカスは離した時点で返す（Vue の @mousedown.prevent と同じ意図）
            if (_focused)
            {
                this.Blur();
            }

            if (inside)
            {
                PerformClick();
            }

            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _pointerId = PointerId.invalidPointerId;
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            if (_disabled)
            {
                return;
            }

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

            bool activate = evt.keyCode == KeyCode.Return
                || evt.keyCode == KeyCode.KeypadEnter
                || evt.keyCode == KeyCode.Space;

            if (!activate)
            {
                return;
            }

            PerformClick();
            evt.StopPropagation();
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

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            _hovered = false;
            _focused = false;
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
