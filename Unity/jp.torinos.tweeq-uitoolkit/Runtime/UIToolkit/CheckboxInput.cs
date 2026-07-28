using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 真偽値のチェックボックス（仕様 §1）。クリックでトグル、左右スワイプで true/false を直接指定できる。
    /// 角丸融合（<see cref="ITweeqInputBox"/>）に参加する。
    /// </summary>
    [UxmlElement]
    public partial class CheckboxInput
        : VisualElement, INotifyValueChanged<bool>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float ICON_SIZE = 18f;
        const float MARK_STROKE_WIDTH = 2f;

        // off のマークは TextSubtle の α を 0.3 に「する」（掛けるのではない。Vue の set-alpha）
        const float MARK_OFF_ALPHA = 0.3f;

        // active 系トランジション 64ms（仕様の遷移表）
        const float ACTIVE_TRANSITION_DURATION = 0.064f;

        const float FOCUS_RING_WIDTH = 1f;
        const float DISABLED_BORDER_WIDTH = 1f;

        // ラベルとの間隔は 1em（rem12 ＝ 12px）
        const float LABEL_GAP = 12f;

        // チェックマーク（18px アイコン内の正規化座標）。mdi:check-bold を 2 セグメントの折れ線に単純化した。
        // 折れ線の上下端が箱の中心に対して対称になるよう y を選んである
        static readonly Vector2 MARK_START = new Vector2(0.18f, 0.50f);
        static readonly Vector2 MARK_ELBOW = new Vector2(0.42f, 0.74f);
        static readonly Vector2 MARK_END = new Vector2(0.82f, 0.26f);

        #endregion

        #region Fields

        bool _value;
        string _label = string.Empty;
        bool _disabled;
        TweeqTheme _theme = TweeqTheme.Dark();

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // 角丸を潰す／残すの判定結果。style とフォーカスリングの描画で共有する
        bool _radiusTopLeft = true;
        bool _radiusTopRight = true;
        bool _radiusBottomLeft = true;
        bool _radiusBottomRight = true;

        VisualElement _box;
        VisualElement _ring;
        Label _labelElement;
        BoolTweakOverlay _overlay;

        readonly BoolSwipeGesture _gesture;

        bool _hovered;
        bool _focused;

        // UI Toolkit には :focus-visible が無いので、直近のフォーカスがポインタ由来かを自前で覚える。
        // Vue の checkbox は :focus-visible なので、クリックしただけではリングを出さない
        bool _focusFromPointer;

        #endregion

        #region Public API

        /// <summary>クリック／スワイプのリリース／キー入力ごとに 1 回発火する。</summary>
        public event Action<bool> Confirmed;

        /// <summary>チェック状態。</summary>
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

        /// <summary>箱の右に置くラベル。空文字なら非表示。</summary>
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

        /// <summary>操作不能状態（仕様 §1）。</summary>
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

                // 無効化の瞬間にドラッグが生きていると、離す手段が無くなる
                if (_disabled)
                {
                    _gesture.Cancel();
                }

                _gesture.Disabled = _disabled;
                this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
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

        /// <summary>横方向グループでの位置。設定すると箱の角丸が仕様 §1 の表どおりに潰れる。</summary>
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

        #endregion

        #region Construction

        public CheckboxInput()
        {
            this.AddToClassList("tweeq-checkbox-input");

            // キーボードショートカット（T/F/Space...）を受け取るため
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.flexShrink = 0f;

            // ドラッグ中のプレビューオーバーレイは箱の外へはみ出す
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

            this.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            Refresh();
        }

        void BuildChildren()
        {
            _box = new VisualElement { name = "tweeq-checkbox-box" };
            _box.style.flexShrink = 0f;
            _box.style.overflow = Overflow.Visible;

            // チェックマークは箱自身の generateVisualContent で描く。
            // 要素の背景 → 生成メッシュ → 子要素 の順に描かれるので、背景の上・リングの下に来る
            _box.generateVisualContent += OnGenerateBoxContent;
            _box.RegisterCallback<PointerEnterEvent>(OnBoxPointerEnter);
            _box.RegisterCallback<PointerLeaveEvent>(OnBoxPointerLeave);
            this.hierarchy.Add(_box);

            // フォーカスリングは箱の外側 1px にも出るので、箱と同じ矩形を持つ別レイヤに描く
            _ring = new VisualElement
            {
                name = "tweeq-checkbox-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _ring.style.position = Position.Absolute;
            _ring.style.left = 0f;
            _ring.style.top = 0f;
            _ring.style.right = 0f;
            _ring.style.bottom = 0f;
            _ring.style.overflow = Overflow.Visible;
            _ring.generateVisualContent += OnGenerateRingContent;
            _box.hierarchy.Add(_ring);

            _labelElement = new Label(string.Empty)
            {
                name = "tweeq-checkbox-label",
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

            float size = _theme.InputHeight;
            this.style.minHeight = size;

            if (_box != null)
            {
                _box.style.width = size;
                _box.style.height = size;

                // 仕様 §1: 箱の背景のみ 64ms。Vue は cubic-bezier(0.4,0,0.2,1) だが
                // UI Toolkit に同一カーブが無いため EaseInOutCubic で近似する
                _box.style.transitionProperty = new StyleList<StylePropertyName>(
                    new List<StylePropertyName> { new StylePropertyName("background-color") });
                _box.style.transitionDuration = new StyleList<TimeValue>(
                    new List<TimeValue> { new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second) });
                _box.style.transitionTimingFunction = new StyleList<EasingFunction>(
                    new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
            }

            ApplyCornerRadius();
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

        // 仕様 §1 の角丸表。両軸の指定は OR で合成する（片方でも「潰す」なら潰す）
        void ApplyCornerRadius()
        {
            _radiusTopLeft = true;
            _radiusTopRight = true;
            _radiusBottomLeft = true;
            _radiusBottomRight = true;

            switch (_inlinePosition)
            {
                case TweeqBoxPosition.Start:
                    _radiusTopRight = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    _radiusTopLeft = false;
                    _radiusTopRight = false;
                    _radiusBottomLeft = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    _radiusTopLeft = false;
                    _radiusBottomLeft = false;
                    break;
            }

            switch (_blockPosition)
            {
                case TweeqBoxPosition.Start:
                    _radiusBottomLeft = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    _radiusTopLeft = false;
                    _radiusTopRight = false;
                    _radiusBottomLeft = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    _radiusTopLeft = false;
                    _radiusTopRight = false;
                    break;
            }

            if (_box == null)
            {
                return;
            }

            float radius = _theme != null ? _theme.InputRadius : 0f;
            _box.style.borderTopLeftRadius = _radiusTopLeft ? radius : 0f;
            _box.style.borderTopRightRadius = _radiusTopRight ? radius : 0f;
            _box.style.borderBottomLeftRadius = _radiusBottomLeft ? radius : 0f;
            _box.style.borderBottomRightRadius = _radiusBottomRight ? radius : 0f;

            _ring?.MarkDirtyRepaint();
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

        void OnPointerDown(PointerDownEvent evt)
        {
            // BoolSwipeGesture が Focus() を呼ぶ前に「ポインタ由来のフォーカス」を記録しておく
            _focusFromPointer = true;
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // キーを触った時点で :focus-visible 相当に昇格させる
            if (_focusFromPointer)
            {
                _focusFromPointer = false;
                Refresh();
            }
        }

        void OnFocusIn(FocusInEvent evt)
        {
            _focused = true;
            Refresh();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            _focused = false;
            _focusFromPointer = false;
            Refresh();
        }

        void OnBoxPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnBoxPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _box?.MarkDirtyRepaint();
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

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            UpdateBoxBackground();
            UpdateLabelColor();
            UpdateOverlay();

            _box?.MarkDirtyRepaint();
            _ring?.MarkDirtyRepaint();
        }

        void UpdateBoxBackground()
        {
            if (_box == null)
            {
                return;
            }

            if (_disabled)
            {
                // 仕様 §1: 未チェックは透明＋1px Border 枠、チェック済みは TextSubtle 塗り
                if (_value)
                {
                    SetBorderWidth(_box, 0f);
                    _box.style.backgroundColor = _theme.TextSubtle;
                }
                else
                {
                    SetBorderWidth(_box, DISABLED_BORDER_WIDTH);
                    SetBorderColor(_box, _theme.Border);
                    _box.style.backgroundColor = Color.clear;
                }

                return;
            }

            SetBorderWidth(_box, 0f);

            if (_value)
            {
                _box.style.backgroundColor = _hovered ? _theme.AccentHover : _theme.Accent;
            }
            else
            {
                _box.style.backgroundColor = _hovered ? _theme.InputHover : _theme.Input;
            }
        }

        void UpdateLabelColor()
        {
            if (_labelElement == null)
            {
                return;
            }

            _labelElement.style.color = _disabled ? _theme.TextMuted : _theme.Text;
        }

        void UpdateOverlay()
        {
            if (_gesture == null || _box == null)
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
                _box.hierarchy.Add(_overlay);
                return;
            }

            _overlay.Sync(_theme, _gesture.PreviewValue, _theme.InputHeight);
        }

        #endregion

        #region Painting

        // 生成メッシュの座標原点は要素のボーダーボックス左上なので、layout の実寸をそのまま使う
        Rect BoxRect()
        {
            if (_box == null)
            {
                return Rect.zero;
            }

            float width = _box.layout.width;
            float height = _box.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height))
            {
                return Rect.zero;
            }

            return new Rect(0f, 0f, width, height);
        }

        // 仕様 §1: マークは常時描画して色だけ変える（遷移なし＝即時）
        void OnGenerateBoxContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = BoxRect();
            if (rect.width < ICON_SIZE || rect.height < ICON_SIZE)
            {
                return;
            }

            Rect icon = new Rect(
                rect.xMin + (rect.width - ICON_SIZE) * 0.5f,
                rect.yMin + (rect.height - ICON_SIZE) * 0.5f,
                ICON_SIZE,
                ICON_SIZE);

            Color color;
            if (_value)
            {
                // disabled でも塗りが TextSubtle に変わるだけで、マークは背景色のまま読める
                color = _theme.Background;
            }
            else
            {
                color = _theme.TextSubtle;
                color.a = MARK_OFF_ALPHA;
            }

            painter.strokeColor = color;
            painter.lineWidth = MARK_STROKE_WIDTH;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(MapToRect(icon, MARK_START));
            painter.LineTo(MapToRect(icon, MARK_ELBOW));
            painter.LineTo(MapToRect(icon, MARK_END));
            painter.Stroke();
        }

        // 仕様 §1: off＝外周 1px Accent / on＝内側 1px Input ＋ 外周 1px Accent の二重
        void OnGenerateRingContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || !ShowFocusRing)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = BoxRect();
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float radius = _theme.InputRadius;
            float half = FOCUS_RING_WIDTH * 0.5f;

            painter.lineWidth = FOCUS_RING_WIDTH;
            painter.lineCap = LineCap.Butt;

            if (_value)
            {
                // inset の 1px リング。線幅の半分だけ内側に寄せると [edge-1, edge] を覆う
                painter.strokeColor = _theme.Input;
                TraceRoundedRect(painter, Expand(rect, -half), radius - half);
                painter.Stroke();
            }

            // 外周の 1px リング（box-shadow 0 0 0 1px 相当）
            painter.strokeColor = _theme.Accent;
            TraceRoundedRect(painter, Expand(rect, half), radius + half);
            painter.Stroke();
        }

        bool ShowFocusRing => _focused && !_focusFromPointer && !_disabled;

        void TraceRoundedRect(Painter2D painter, Rect rect, float radius)
        {
            float limit = Mathf.Min(rect.width, rect.height) * 0.5f;
            float clamped = Mathf.Clamp(radius, 0f, limit);

            float topLeft = _radiusTopLeft ? clamped : 0f;
            float topRight = _radiusTopRight ? clamped : 0f;
            float bottomLeft = _radiusBottomLeft ? clamped : 0f;
            float bottomRight = _radiusBottomRight ? clamped : 0f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + topLeft, rect.yMin));

            TraceCorner(
                painter,
                new Vector2(rect.xMax - topRight, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax - topRight, rect.yMin + topRight),
                topRight,
                -90f,
                0f);

            TraceCorner(
                painter,
                new Vector2(rect.xMax, rect.yMax - bottomRight),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMax - bottomRight, rect.yMax - bottomRight),
                bottomRight,
                0f,
                90f);

            TraceCorner(
                painter,
                new Vector2(rect.xMin + bottomLeft, rect.yMax),
                new Vector2(rect.xMin, rect.yMax),
                new Vector2(rect.xMin + bottomLeft, rect.yMax - bottomLeft),
                bottomLeft,
                90f,
                180f);

            TraceCorner(
                painter,
                new Vector2(rect.xMin, rect.yMin + topLeft),
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMin + topLeft, rect.yMin + topLeft),
                topLeft,
                180f,
                270f);

            painter.ClosePath();
        }

        // 辺の終点まで直線を引いてから角を丸める。半径 0 の角は Arc が退化するので直線で畳む
        static void TraceCorner(
            Painter2D painter,
            Vector2 edgeEnd,
            Vector2 sharpCorner,
            Vector2 arcCenter,
            float radius,
            float startAngle,
            float endAngle)
        {
            if (radius <= 0f)
            {
                painter.LineTo(sharpCorner);
                return;
            }

            painter.LineTo(edgeEnd);
            painter.Arc(
                arcCenter,
                radius,
                new Angle(startAngle, AngleUnit.Degree),
                new Angle(endAngle, AngleUnit.Degree));
        }

        static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        static Vector2 MapToRect(Rect rect, Vector2 normalized)
        {
            return new Vector2(
                rect.xMin + rect.width * normalized.x,
                rect.yMin + rect.height * normalized.y);
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

        #endregion
    }
}
