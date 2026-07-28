using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 真偽値のトグルスイッチ（仕様 §2）。クリックでトグル、左右スワイプで true/false を直接指定できる。
    /// 角丸融合には参加せず、disabled も持たない（Vue 準拠。仕様 Unity 向け決定事項 3）。
    /// </summary>
    [UxmlElement]
    public partial class SwitchInput : VisualElement, INotifyValueChanged<bool>, ITweeqThemed
    {
        #region Constants

        // トラックは 48×24（高さの 2 倍）
        const float TRACK_WIDTH_FACTOR = 2f;

        // ハンドルは inset 4px の 16×16。ドラッグ中は 4px 中央側へ伸びて 20px になる
        const float HANDLE_INSET = 4f;

        // active 系トランジション 64ms（仕様の遷移表）
        const float ACTIVE_TRANSITION_DURATION = 0.064f;

        // フォーカスリングは inset -3px の 1px ピル
        const float FOCUS_RING_INSET = 3f;
        const float FOCUS_RING_WIDTH = 1f;

        // ラベルとの間隔は 1em（rem12 ＝ 12px）
        const float LABEL_GAP = 12f;

        #endregion

        #region Fields

        bool _value;
        string _label = string.Empty;
        TweeqTheme _theme = TweeqTheme.Dark();

        VisualElement _track;
        VisualElement _handle;
        VisualElement _ring;
        Label _labelElement;
        BoolTweakOverlay _overlay;

        readonly BoolSwipeGesture _gesture;

        bool _hovered;
        bool _focused;

        #endregion

        #region Public API

        /// <summary>クリック／スワイプのリリース／キー入力ごとに 1 回発火する。</summary>
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

        /// <summary>トラックの右に置くラベル。空文字なら非表示。</summary>
        [UxmlAttribute("label")]
        public string Label
        {
            get => _label;
            set
            {
                _label = value ?? string.Empty;
                ApplyLabel();
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

        /// <summary>ChangeEvent を発火せずに値を設定する。</summary>
        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            Refresh();
        }

        #endregion

        #region Construction

        public SwitchInput()
        {
            this.AddToClassList("tweeq-switch-input");

            // キーボードショートカット（T/F/Space...）を受け取るため
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.flexShrink = 0f;

            // フォーカスリングとプレビューオーバーレイはトラックの外へはみ出す
            this.style.overflow = Overflow.Visible;

            BuildChildren();
            ApplyStaticStyles();
            ApplyLabel();

            _gesture = new BoolSwipeGesture(this)
            {
                ValueGetter = () => _value,
                ValueChanged = OnGestureValueChanged,
                Confirmed = OnGestureConfirmed,
                StateChanged = OnGestureStateChanged,
            };

            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            Refresh();
        }

        void BuildChildren()
        {
            _track = new VisualElement { name = "tweeq-switch-track" };
            _track.style.flexShrink = 0f;
            _track.style.overflow = Overflow.Visible;
            _track.RegisterCallback<PointerEnterEvent>(OnTrackPointerEnter);
            _track.RegisterCallback<PointerLeaveEvent>(OnTrackPointerLeave);
            this.hierarchy.Add(_track);

            _handle = new VisualElement
            {
                name = "tweeq-switch-handle",
                pickingMode = PickingMode.Ignore,
            };
            _handle.style.position = Position.Absolute;
            _handle.style.top = HANDLE_INSET;
            _track.hierarchy.Add(_handle);

            // リングはトラックの外側 3px にも出るので、トラックと同じ矩形を持つ別レイヤに描く
            _ring = new VisualElement
            {
                name = "tweeq-switch-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _ring.style.position = Position.Absolute;
            _ring.style.left = 0f;
            _ring.style.top = 0f;
            _ring.style.right = 0f;
            _ring.style.bottom = 0f;
            _ring.style.overflow = Overflow.Visible;
            _ring.generateVisualContent += OnGenerateRingContent;
            _track.hierarchy.Add(_ring);

            _labelElement = new Label(string.Empty)
            {
                name = "tweeq-switch-label",
                pickingMode = PickingMode.Ignore,
            };
            _labelElement.style.marginLeft = LABEL_GAP;
            _labelElement.style.marginRight = 0f;
            _labelElement.style.marginTop = 0f;
            _labelElement.style.marginBottom = 0f;
            _labelElement.style.paddingLeft = 0f;
            _labelElement.style.paddingRight = 0f;
            this.hierarchy.Add(_labelElement);
        }

        void ApplyStaticStyles()
        {
            if (_theme == null)
            {
                return;
            }

            float height = _theme.InputHeight;
            this.style.minHeight = height;

            if (_track != null)
            {
                _track.style.width = height * TRACK_WIDTH_FACTOR;
                _track.style.height = height;

                // border-radius 9999px ＝ 高さの半分でピルになる
                SetBorderRadius(_track, height * 0.5f);
                ApplyTransition(_track, new[] { "background-color" });
            }

            if (_handle != null)
            {
                float size = HandleSize;
                _handle.style.height = size;
                SetBorderRadius(_handle, size * 0.5f);
                ApplyTransition(_handle, new[] { "left", "width", "background-color" });
            }
        }

        // 仕様 §2: トラック背景・ハンドルの left/width/背景 すべて 64ms。
        // Vue は cubic-bezier(0.4,0,0.2,1) だが UI Toolkit に同一カーブが無いため
        // EaseInOutCubic で近似する（RotaryInput / NumberInput と同じ判断）
        static void ApplyTransition(VisualElement element, string[] properties)
        {
            if (element == null || properties == null)
            {
                return;
            }

            List<StylePropertyName> names = new List<StylePropertyName>(properties.Length);
            List<TimeValue> durations = new List<TimeValue>(properties.Length);
            List<EasingFunction> easings = new List<EasingFunction>(properties.Length);

            for (int index = 0; index < properties.Length; index++)
            {
                names.Add(new StylePropertyName(properties[index]));
                durations.Add(new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second));
                easings.Add(new EasingFunction(EasingMode.EaseInOutCubic));
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(durations);
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
        }

        void ApplyLabel()
        {
            if (_labelElement == null)
            {
                return;
            }

            _labelElement.text = _label;
            _labelElement.style.display = string.IsNullOrEmpty(_label)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        #endregion

        #region Events

        void OnGestureValueChanged(bool next)
        {
            this.value = next;
        }

        void OnGestureConfirmed(bool confirmed)
        {
            Confirmed?.Invoke(confirmed);
        }

        void OnGestureStateChanged()
        {
            Refresh();
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

        void OnTrackPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnTrackPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _ring?.MarkDirtyRepaint();
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

        #region Refresh

        float TrackWidth => _theme != null ? _theme.InputHeight * TRACK_WIDTH_FACTOR : 0f;

        float HandleSize => _theme != null ? _theme.InputHeight - HANDLE_INSET * 2f : 0f;

        // ドラッグ中は 4px 中央側へ伸びる（外側エッジは固定）
        float HandleTweakingWidth => _theme != null ? _theme.InputHeight - HANDLE_INSET : 0f;

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            UpdateTrack();
            UpdateHandle();
            UpdateLabelColor();
            UpdateOverlay();

            _ring?.MarkDirtyRepaint();
        }

        void UpdateTrack()
        {
            if (_track == null)
            {
                return;
            }

            if (_value)
            {
                _track.style.backgroundColor = _hovered ? _theme.AccentHover : _theme.Accent;
            }
            else
            {
                _track.style.backgroundColor = _hovered ? _theme.InputHover : _theme.Input;
            }
        }

        void UpdateHandle()
        {
            if (_handle == null)
            {
                return;
            }

            bool tweaking = _gesture != null && _gesture.Dragging;
            float width = tweaking ? HandleTweakingWidth : HandleSize;

            // on 側は右端（トラック幅 - inset）に外側エッジを固定したまま太る
            float left = _value ? TrackWidth - HANDLE_INSET - width : HANDLE_INSET;

            _handle.style.width = width;
            _handle.style.left = left;
            _handle.style.backgroundColor = _value ? _theme.Background : _theme.TextSubtle;
        }

        void UpdateLabelColor()
        {
            if (_labelElement == null)
            {
                return;
            }

            _labelElement.style.color = _theme.Text;
        }

        void UpdateOverlay()
        {
            if (_gesture == null || _track == null)
            {
                return;
            }

            if (!_gesture.Dragging)
            {
                if (_overlay != null)
                {
                    _overlay.RemoveFromHierarchy();
                    _overlay = null;
                }

                return;
            }

            if (_overlay == null)
            {
                _overlay = new BoolTweakOverlay();
                _overlay.Sync(_theme, _gesture.PreviewValue, _theme.InputHeight);
                _track.hierarchy.Add(_overlay);
                return;
            }

            _overlay.Sync(_theme, _gesture.PreviewValue, _theme.InputHeight);
        }

        #endregion

        #region Painting

        // 仕様 §2: :focus 相当なので、クリックでフォーカスした場合もリングを出す
        void OnGenerateRingContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || !_focused || _track == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            float width = _track.layout.width;
            float height = _track.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }

            // Vue は inset -3px の要素に 1px ボーダー＝線の中心は -2.5px
            float offset = FOCUS_RING_INSET - FOCUS_RING_WIDTH * 0.5f;
            Rect ring = new Rect(
                -offset,
                -offset,
                width + offset * 2f,
                height + offset * 2f);

            painter.strokeColor = _theme.Accent;
            painter.lineWidth = FOCUS_RING_WIDTH;
            painter.lineCap = LineCap.Butt;
            TracePill(painter, ring);
            painter.Stroke();
        }

        static void TracePill(Painter2D painter, Rect rect)
        {
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f)
            {
                return;
            }

            float centerY = rect.yMin + rect.height * 0.5f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + radius, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax - radius, rect.yMin));
            painter.Arc(
                new Vector2(rect.xMax - radius, centerY),
                radius,
                new Angle(-90f, AngleUnit.Degree),
                new Angle(90f, AngleUnit.Degree));
            painter.LineTo(new Vector2(rect.xMin + radius, rect.yMax));
            painter.Arc(
                new Vector2(rect.xMin + radius, centerY),
                radius,
                new Angle(90f, AngleUnit.Degree),
                new Angle(270f, AngleUnit.Degree));
            painter.ClosePath();
        }

        static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        #endregion
    }
}
