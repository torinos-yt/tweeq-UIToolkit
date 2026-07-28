using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 押すたびに <see cref="Generate"/> で次の値を作るボタン（Vue InputShuffle 相当）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 自分では値の意味を知らず「現在値を種にして次を作らせる」だけなので、
    /// <c>INotifyValueChanged</c> は実装しない（ChangeEvent の相手になる型が定まらない）。
    /// </para>
    /// <para>
    /// サイコロの目は Vue のコメントどおり演出専用（"the die face is just flair"）で、
    /// 値とは一切対応しない。クリックのたびに 90° 回して目を振り直す。
    /// </para>
    /// </remarks>
    // ジェネリックは [UxmlElement] にできないため UXML 化しない（string 特化ラッパー側で対応する）
    public class ShuffleInput<T> : VisualElement, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Vue の SvgIcon は viewBox 32 / stroke-width 2。座標をそのまま持ち、描画時に縮める
        const float VIEWBOX_SIZE = 32f;
        const float STROKE_WIDTH = 2f;

        // 本体の角丸正方形（Vue のパス "M24,29H8c-2.8,0..." の外形）
        const float BODY_MIN = 3f;
        const float BODY_MAX = 29f;
        const float BODY_RADIUS = 5f;

        // 目は r=1 の円を stroke-width 2 で描いた見え方＝半径 2 の塗り
        const float DOT_RADIUS = 2f;

        const int MIN_FACE = 1;
        const int MAX_FACE = 6;

        // Vue: iconRot += 90
        const float ROTATION_STEP = 90f;

        const float DISABLED_OPACITY = 0.4f;
        const float FOCUS_RING_WIDTH = 1f;

        // 1〜6 の目の座標（viewBox 32 基準）。Vue の SvgIcon の circle をそのまま写した
        static readonly Vector2[][] FACE_DOTS =
        {
            new[] { new Vector2(16f, 16f) },
            new[] { new Vector2(11f, 21f), new Vector2(21f, 11f) },
            new[] { new Vector2(16f, 16f), new Vector2(10f, 22f), new Vector2(22f, 10f) },
            new[]
            {
                new Vector2(10f, 22f), new Vector2(22f, 10f),
                new Vector2(10f, 10f), new Vector2(22f, 22f),
            },
            new[]
            {
                new Vector2(16f, 16f),
                new Vector2(10f, 22f), new Vector2(22f, 10f),
                new Vector2(10f, 10f), new Vector2(22f, 22f),
            },
            new[]
            {
                new Vector2(10f, 10f), new Vector2(10f, 16f), new Vector2(10f, 22f),
                new Vector2(22f, 10f), new Vector2(22f, 16f), new Vector2(22f, 22f),
            },
        };

        #endregion

        #region Fields

        readonly VisualElement _icon;
        readonly VisualElement _focusRing;

        TweeqTheme _theme = TweeqTheme.Dark();

        T _value;
        bool _disabled;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        float _iconRotation;

        // Vue の iconNum = ref(3)
        int _iconFace = 3;

        bool _hovered;
        bool _focused;
        int _pointerId = PointerId.invalidPointerId;

        #endregion

        #region Public API

        /// <summary>
        /// 現在値から次の値を作る。null の間はクリックしても何も起きない
        /// （Vue では必須 prop なので、未設定は「まだ配線されていない」状態と見なす）。
        /// </summary>
        public Func<T, T> Generate { get; set; }

        /// <summary>値が変わったときに発火する。</summary>
        public event Action<T> ValueChanged;

        /// <summary>1 クリック 1 回、<see cref="ValueChanged"/> と対で発火する。</summary>
        public event Action<T> Confirmed;

        /// <summary>現在値。次の <see cref="Generate"/> に渡る種でもある。</summary>
        public T value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                {
                    return;
                }

                SetValueWithoutNotify(value);
                ValueChanged?.Invoke(_value);
            }
        }

        /// <summary>操作不能状態。クリックもキー操作も通らない。</summary>
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

        /// <summary>サイコロの現在の回転角（度数）。演出専用。</summary>
        public float IconRotation => _iconRotation;

        /// <summary>サイコロの現在の出目（1〜6）。値とは無関係の演出。</summary>
        public int IconFace => _iconFace;

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

        /// <summary>
        /// プログラムからのクリック。Disabled か <see cref="Generate"/> 未設定なら何もしない。
        /// パネル非依存なのでテストからの発火にも使える。
        /// </summary>
        public void PerformClick()
        {
            if (_disabled)
            {
                return;
            }

            Func<T, T> generate = Generate;
            if (generate == null)
            {
                return;
            }

            RollIcon();

            T next = generate(_value);
            _value = next;

            ValueChanged?.Invoke(next);
            Confirmed?.Invoke(next);
        }

        /// <summary>通知を出さずに値を設定する。演出も動かさない。</summary>
        public void SetValueWithoutNotify(T newValue)
        {
            _value = newValue;
        }

        #endregion

        #region Construction

        public ShuffleInput()
        {
            this.AddToClassList("tweeq-shuffle-input");

            this.focusable = true;
            this.style.flexShrink = 0f;

            // フォーカスリングを 1px 外へ置くので Hidden にしてはいけない
            this.style.overflow = Overflow.Visible;

            _icon = new VisualElement
            {
                name = "tweeq-shuffle-icon",
                pickingMode = PickingMode.Ignore,
            };
            _icon.style.position = Position.Absolute;
            _icon.style.left = 0f;
            _icon.style.top = 0f;
            _icon.style.right = 0f;
            _icon.style.bottom = 0f;
            _icon.generateVisualContent += OnGenerateIcon;
            this.hierarchy.Add(_icon);

            // 塗りが淡い（Subtle 系）ので、フォーカスは外周リング 1 本だけにする（ButtonInput と同じ判断）
            _focusRing = new VisualElement
            {
                name = "tweeq-shuffle-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _focusRing.style.position = Position.Absolute;
            _focusRing.style.left = -FOCUS_RING_WIDTH;
            _focusRing.style.top = -FOCUS_RING_WIDTH;
            _focusRing.style.right = -FOCUS_RING_WIDTH;
            _focusRing.style.bottom = -FOCUS_RING_WIDTH;
            _focusRing.style.display = DisplayStyle.None;
            SetBorderWidth(_focusRing, FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusRing);

            ApplyStaticStyles();
            ApplyInteractivity();
            ApplyIconTransform();

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

        #endregion

        #region Styles

        void ApplyStaticStyles()
        {
            float size = _theme != null ? _theme.InputHeight : 0f;

            this.style.width = size;
            this.style.height = size;

            // InputGroup.ApplyStretch は flexBasis 未指定の子へ basis 0 を配る。
            // width より basis が勝つため、明示しないと 24px 正方形がゼロ幅まで潰れる
            this.style.flexGrow = 0f;
            this.style.flexBasis = size;

            ApplyCornerRadius();

            ApplyTransition(
                this,
                _theme != null ? _theme.HoverTransitionDuration : 0f,
                EasingMode.EaseInOutCubic,
                "background-color");

            // Vue: transition transform .3s cubic-bezier(0.19, 1.6, 0.42, 1)。
            // 跳ね返りのある曲線は EaseOutBack が最も近い。長さはテーマの hover 遷移に合わせる
            ApplyTransition(
                _icon,
                _theme != null ? _theme.HoverTransitionDuration : 0f,
                EasingMode.EaseOutBack,
                "rotate");

            if (_theme != null)
            {
                SetBorderColor(_focusRing, _theme.Accent);
            }
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

            // 外側リングは 1px 外に居るので、同じ見え方になるよう半径も 1px 太らせる
            SetCornerRadius(
                _focusRing,
                radius + FOCUS_RING_WIDTH,
                topLeft,
                topRight,
                bottomLeft,
                bottomRight);
        }

        #endregion

        #region Presentation

        // Vue は rest の背景を持たないが、こちらは InputGroup で隣と融合させる前提なので
        // ButtonInput の Subtle と同じ「Input 面 + Accent のアイコン」を rest にする
        Color CurrentBackground => _hovered && !_disabled
            ? (_theme != null ? _theme.AccentHover : Color.clear)
            : (_theme != null ? _theme.Input : Color.clear);

        Color CurrentIconColor
        {
            get
            {
                if (_theme == null)
                {
                    return Color.white;
                }

                return _hovered && !_disabled
                    ? TweeqTheme.ContrastText(_theme.AccentHover)
                    : _theme.Accent;
            }
        }

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.backgroundColor = CurrentBackground;
            _focusRing.style.display = _focused && !_disabled
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            _icon.MarkDirtyRepaint();
        }

        void RollIcon()
        {
            _iconRotation += ROTATION_STEP;

            // Vue: random(1, 6)（上限含む）
            _iconFace = UnityEngine.Random.Range(MIN_FACE, MAX_FACE + 1);

            ApplyIconTransform();
            _icon.MarkDirtyRepaint();
        }

        void ApplyIconTransform()
        {
            _icon.style.rotate = new Rotate(new Angle(_iconRotation, AngleUnit.Degree));
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

            // 押した指を外へ逃がして離した場合はクリック不成立
            Vector3 position = evt.position;
            bool inside = this.ContainsPoint(this.WorldToLocal(new Vector2(position.x, position.y)));

            // ポインタで得たフォーカスは離した時点で返す（ButtonInput と同じ判断）
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

        #region Painting

        void OnGenerateIcon(MeshGenerationContext context)
        {
            Painter2D painter = context?.painter2D;
            if (painter == null || _theme == null)
            {
                return;
            }

            Rect rect = _icon.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // アイコンフォント非依存（Unity 決定事項 1）。viewBox 32 の SVG を等倍で写す
            float scale = Mathf.Min(rect.width, rect.height) / VIEWBOX_SIZE;
            float originX = (rect.width - VIEWBOX_SIZE * scale) * 0.5f;
            float originY = (rect.height - VIEWBOX_SIZE * scale) * 0.5f;

            Color color = CurrentIconColor;
            painter.strokeColor = color;
            painter.fillColor = color;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            painter.lineWidth = STROKE_WIDTH * scale;

            Rect body = new Rect(
                originX + BODY_MIN * scale,
                originY + BODY_MIN * scale,
                (BODY_MAX - BODY_MIN) * scale,
                (BODY_MAX - BODY_MIN) * scale);

            TraceRoundedRect(painter, body, BODY_RADIUS * scale);
            painter.Stroke();

            int index = Mathf.Clamp(_iconFace, MIN_FACE, MAX_FACE) - 1;
            Vector2[] dots = FACE_DOTS[index];
            float dotRadius = DOT_RADIUS * scale;

            for (int i = 0; i < dots.Length; i++)
            {
                Vector2 center = new Vector2(
                    originX + dots[i].x * scale,
                    originY + dots[i].y * scale);

                painter.BeginPath();
                painter.Arc(
                    center,
                    dotRadius,
                    new Angle(0f, AngleUnit.Degree),
                    new Angle(360f, AngleUnit.Degree));
                painter.ClosePath();
                painter.Fill();
            }
        }

        // Painter2D に角丸矩形のプリミティブが無いので ArcTo で辿る
        static void TraceRoundedRect(Painter2D painter, Rect rect, float radius)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float limit = Mathf.Min(rect.width, rect.height) * 0.5f;
            float r = Mathf.Clamp(radius, 0f, limit);

            float x0 = rect.xMin;
            float y0 = rect.yMin;
            float x1 = rect.xMax;
            float y1 = rect.yMax;

            painter.BeginPath();
            painter.MoveTo(new Vector2(x0 + r, y0));
            painter.ArcTo(new Vector2(x1, y0), new Vector2(x1, y1), r);
            painter.ArcTo(new Vector2(x1, y1), new Vector2(x0, y1), r);
            painter.ArcTo(new Vector2(x0, y1), new Vector2(x0, y0), r);
            painter.ArcTo(new Vector2(x0, y0), new Vector2(x1, y0), r);
            painter.ClosePath();
        }

        #endregion

        #region Helpers

        static void ApplyTransition(
            VisualElement element, float duration, EasingMode easing, string property)
        {
            if (element == null)
            {
                return;
            }

            element.style.transitionProperty =
                new StyleList<StylePropertyName>(new List<StylePropertyName>
                {
                    new StylePropertyName(property),
                });
            element.style.transitionDuration =
                new StyleList<TimeValue>(new List<TimeValue>
                {
                    new TimeValue(duration, TimeUnit.Second),
                });
            element.style.transitionTimingFunction =
                new StyleList<EasingFunction>(new List<EasingFunction>
                {
                    new EasingFunction(easing),
                });
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
