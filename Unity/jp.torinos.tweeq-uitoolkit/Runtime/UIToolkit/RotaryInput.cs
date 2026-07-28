using System;
using System.Collections.Generic;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 円形の角度スクラバー。値は度数で、多回転により ±360 を超える値も保持する。
    /// 相対モード（既定）と絶対モード（針側ホバー or A キー）を持つ。
    /// </summary>
    [UxmlElement]
    public partial class RotaryInput : VisualElement, INotifyValueChanged<float>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float DEFAULT_SIZE = 24f;

        // Vue / React は「機能だけ無効・見た目不変」だが、隣の NumberInput が dim するのにノブだけ
        // 生きて見えるのは公演現場で事故のもと。ButtonInput 方式の減光を意図的逸脱として採る
        // （m7-disabled-invalid-spec.md の裁定）
        const float DISABLED_OPACITY = 0.4f;
        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // ホバー／ドラッグ中は 1.8 倍に膨らむ（Vue の transform: scale(1.8)）
        const float HOVER_SCALE = 1.8f;

        // フォーカスリングは 24px 箱の inset -3px ＝ 直径 30px
        const float FOCUS_RING_INSET = 3f;
        const float FOCUS_RING_WIDTH = 1f;

        // スナップリング帯域（egui 版 SNAP_INNER_RADIUS_FACTOR / SNAP_OUTER_RADIUS と同値）
        const float SNAP_RING_INNER_FACTOR = 4f;
        const float SNAP_RING_OUTER_RADIUS = 160f;

        // 1 回転ごとに同心円をずらす量（24 * 0.25 = 6px）
        const float ARC_RADIUS_STEP_FACTOR = 0.25f;
        const float MIN_ARC_RADIUS = 8f;

        // 値が壊れていても描画ループが爆発しないための上限
        const int MAX_METER_LINES = 720;
        const int MAX_TURN_CIRCLES = 64;

        const double FINE_SCALE = 0.1;

        // Vue 版は angleOffset の既定が -90（値 0 が真上）。API 契約では AngleOffset の既定を 0 と
        // したため、「0°=真上」の基準はここで吸収し、描画と絶対モードの双方に同じ値を掛ける。
        const double UP_ANGLE_OFFSET = -90.0;

        // Vue 版 tip パス（viewBox 32、中心 16）＝半径比 4/16 と 14/16
        const float INDICATOR_INNER_RATIO = 0.25f;
        const float INDICATOR_OUTER_RATIO = 0.875f;
        const float INDICATOR_WIDTH = 3f;

        // 中心付近は針の左右どちらに居るかが不安定になるので、絶対モード判定を無効にする
        const float ABSOLUTE_DEAD_ZONE_RATIO = 0.4375f;

        // 中心付近では方向ベクトルが暴れるので、この長さ（二乗）未満のベクトルからは角度を取らない
        const float MIN_VECTOR_SQR_LENGTH = 1f;

        #endregion

        #region Fields

        float _value;

        // スナップ前の生の累積角度。スナップは出力側にのみ掛け、ここには残さない
        double _local;

        double _snap = 45.0;
        double _step;
        double _angleOffset;
        bool _disabled;
        TweeqTheme _theme = TweeqTheme.Dark();

        // スケールする層。フォーカスリングを巻き込まないため描画を分けている
        VisualElement _knob;
        TweakOverlay _overlay;

        int _pointerId = PointerId.invalidPointerId;
        bool _pointerDown;
        bool _dragging;
        Vector2 _pressPosition;
        Vector2 _previousPosition;
        Vector2 _originPanelPosition;
        Vector2 _pointerPanelPosition;
        float _valueOnDragStart;
        float _dragThreshold = MOUSE_DRAG_THRESHOLD;
        float _pointerDistance;

        bool _absoluteKeyHeld;
        bool _relativeKeyHeld;
        bool _absoluteKeyWasLast;
        bool _snapKeyHeld;
        bool _shiftHeld;
        bool _altHeld;

        bool _modeByPointer;
        bool _cursorHidden;

        bool _hovered;
        bool _focused;

        #endregion

        #region Public API

        /// <summary>ドラッグ確定時（ポインタを離した時）に発火する。</summary>
        public event Action<float> Confirmed;

        /// <summary>現在の角度（度数）。</summary>
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

        /// <summary>スナップ角度（度数）。既定 45。</summary>
        [UxmlAttribute]
        public double Snap
        {
            get => _snap;
            set
            {
                _snap = value;
                Refresh();
            }
        }

        /// <summary>出力の量子化ステップ。0 以下で無効。</summary>
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

        /// <summary>インジケータの角度オフセット（度数）。既定 0（0°が真上）。</summary>
        [UxmlAttribute]
        public double AngleOffset
        {
            get => _angleOffset;
            set
            {
                _angleOffset = value;
                Refresh();
            }
        }

        /// <summary>
        /// 操作不能状態。ドラッグ中に立てた場合はジェスチャを破棄して開始値へ戻す。
        /// </summary>
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

                if (_disabled && (_pointerDown || _dragging))
                {
                    // 無効化の瞬間にドラッグが生きていると、離す手段＝隠したカーソルを取り戻す手段が無くなる
                    CancelDrag();
                }

                ApplyInteractivity();
                UpdateVisualState();
            }
        }

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyKnobTransition();
                Refresh();
            }
        }

        /// <summary>
        /// 横方向グループでの位置。ノブは円形で潰す角を持たないため、保持するだけの no-op（仕様 §5-1）。
        /// </summary>
        public TweeqBoxPosition InlinePosition { get; set; } = TweeqBoxPosition.None;

        /// <summary>縦方向グループでの位置。InlinePosition と同じく no-op。</summary>
        public TweeqBoxPosition BlockPosition { get; set; } = TweeqBoxPosition.None;

        /// <summary>ドラッグセッション中か。</summary>
        public bool Dragging => _dragging;

        /// <summary>
        /// ドラッグセッションを開始する（panel 非依存）。実操作はポインタイベント経由だが、
        /// 外部ドライバとテストのために口を開けてある（TranslateInput と同じ構成）。
        /// </summary>
        /// <remarks>
        /// ポインタ座標を伴わないので絶対モードの引き寄せは起きない（相対モード相当）。
        /// </remarks>
        public void BeginRotaryDrag()
        {
            if (_disabled || _dragging)
            {
                return;
            }

            _dragging = true;
            _valueOnDragStart = _value;
            _local = _value;

            HideCursor();
            AcquireOverlay();
            UpdateVisualState();
        }

        /// <summary>ドラッグ中の角度増分（度数）を適用する。</summary>
        public void UpdateRotaryDrag(double deltaDegrees)
        {
            if (!_dragging)
            {
                return;
            }

            ApplyDelta(deltaDegrees);
        }

        /// <summary>ドラッグを確定して終了する。<see cref="Confirmed"/> が 1 回だけ発火する。</summary>
        public void EndRotaryDrag()
        {
            if (!_dragging)
            {
                return;
            }

            int pointerId = _pointerId;
            ResetDragState();
            ReleasePointerSafely(pointerId);
            UpdateVisualState();
            Confirmed?.Invoke(_value);
        }

        /// <summary>ドラッグを破棄して開始値へ戻す（Escape 相当）。<see cref="Confirmed"/> は発火しない。</summary>
        public void CancelRotaryDrag()
        {
            if (!_dragging)
            {
                return;
            }

            CancelDrag();
        }

        /// <summary>ChangeEvent を発火せずに値を設定する。累積角度も同期される。</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = newValue;

            // 外部からの設定はドラッグセッションの外にあるので、生の累積器も揃えておく
            _local = newValue;
            Refresh();
        }

        #endregion

        #region Construction

        public RotaryInput()
        {
            this.focusable = true;
            this.style.width = DEFAULT_SIZE;
            this.style.height = DEFAULT_SIZE;
            this.style.flexShrink = 0f;

            // 1.8 倍に膨らんだノブとフォーカスリングを切らない
            this.style.overflow = Overflow.Visible;

            _knob = new VisualElement
            {
                name = "tweeq-rotary-knob",

                // ヒット判定は外側（非スケール層）に集約する
                pickingMode = PickingMode.Ignore,
            };
            _knob.style.position = Position.Absolute;
            _knob.style.left = 0f;
            _knob.style.top = 0f;
            _knob.style.right = 0f;
            _knob.style.bottom = 0f;
            _knob.style.overflow = Overflow.Visible;
            _knob.generateVisualContent += OnGenerateKnobContent;
            this.hierarchy.Add(_knob);

            ApplyKnobTransition();
            ApplyKnobScale();

            this.generateVisualContent += OnGenerateVisualContent;

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<KeyUpEvent>(OnKeyUp);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _pointerDown || _disabled)
            {
                return;
            }

            _pointerDown = true;
            _dragging = false;
            _pointerId = evt.pointerId;
            _dragThreshold = evt.pointerType == UnityEngine.UIElements.PointerType.mouse
                ? MOUSE_DRAG_THRESHOLD
                : TOUCH_DRAG_THRESHOLD;

            _pressPosition = LocalPosition(evt);
            _previousPosition = _pressPosition;
            _originPanelPosition = PanelPosition(evt);
            _pointerPanelPosition = _originPanelPosition;
            _pointerDistance = Vector2.Distance(_pressPosition, Center());
            _valueOnDragStart = _value;
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            // ドラッグ開始時のモードで固定するので、押した瞬間の位置で一度だけ決める
            UpdateModeByPointer(_pressPosition);

            // KeyDown/KeyUp（Q/A/R/Escape）を受け取るためフォーカスを取る
            this.Focus();

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
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

            if (!_pointerDown)
            {
                // ホバー中のみモード判定を更新する（ドラッグ中は凍結）
                UpdateModeByPointer(LocalPosition(evt));
                return;
            }

            if (evt.pointerId != _pointerId)
            {
                return;
            }

            Vector2 position = LocalPosition(evt);
            Vector2 center = Center();
            _pointerPanelPosition = PanelPosition(evt);
            _pointerDistance = Vector2.Distance(position, center);
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            if (!_dragging)
            {
                UpdateModeByPointer(position);

                if (Vector2.Distance(position, _pressPosition) < _dragThreshold)
                {
                    return;
                }

                BeginDrag(position);
                evt.StopPropagation();
                return;
            }

            double delta = ComputeDelta(_previousPosition, position, center);
            _previousPosition = position;
            ApplyDelta(delta);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            bool wasDragging = _dragging;
            int pointerId = _pointerId;
            ResetDragState();
            ReleasePointerSafely(pointerId);

            if (wasDragging)
            {
                Confirmed?.Invoke(_value);
            }

            evt.StopPropagation();
            UpdateVisualState();
        }

        // キャプチャを失った場合でもドラッグ状態（＝隠したカーソル・オーバーレイ）を残さない
        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_pointerDown && !_dragging)
            {
                return;
            }

            ResetDragState();
            UpdateVisualState();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            UpdateVisualState();

            if (evt != null)
            {
                UpdateModeByPointer(LocalPosition(evt));
            }
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;

            if (!_dragging)
            {
                _modeByPointer = false;
            }

            UpdateVisualState();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // パネルから外れてもカーソルとオーバーレイを取り残さない
            ResetDragState();
        }

        #endregion

        #region Keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            switch (evt.keyCode)
            {
                case KeyCode.A:
                    _absoluteKeyHeld = true;
                    _absoluteKeyWasLast = true;
                    evt.StopPropagation();
                    break;
                case KeyCode.R:
                    _relativeKeyHeld = true;
                    _absoluteKeyWasLast = false;
                    evt.StopPropagation();
                    break;
                case KeyCode.Q:
                    _snapKeyHeld = true;
                    evt.StopPropagation();
                    break;
                case KeyCode.Escape:
                    if (_pointerDown || _dragging)
                    {
                        CancelDrag();
                        evt.StopPropagation();
                    }

                    break;
            }

            if (_dragging)
            {
                // スナップ／モードの切り替えは出力へ即座に反映する（累積角度は動かさない）
                ApplyDelta(0.0);
            }

            Refresh();
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            switch (evt.keyCode)
            {
                case KeyCode.A:
                    _absoluteKeyHeld = false;
                    evt.StopPropagation();
                    break;
                case KeyCode.R:
                    _relativeKeyHeld = false;
                    evt.StopPropagation();
                    break;
                case KeyCode.Q:
                    _snapKeyHeld = false;
                    evt.StopPropagation();
                    break;
            }

            if (_dragging)
            {
                ApplyDelta(0.0);
            }

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
            _absoluteKeyHeld = false;
            _relativeKeyHeld = false;
            _snapKeyHeld = false;
            Refresh();
        }

        #endregion

        #region Drag session

        void BeginDrag(Vector2 position)
        {
            _dragging = true;
            _previousPosition = position;
            _valueOnDragStart = _value;
            _local = _value;

            HideCursor();
            AcquireOverlay();

            // 絶対モードで掴んだ場合は、その場でポインタ角度へ引き寄せる（Vue 版 onDragStart 相当）
            if (AbsoluteMode)
            {
                ApplyDelta(AbsoluteDelta(position, Center()));
            }
            else
            {
                ApplyDelta(0.0);
            }

            UpdateVisualState();
        }

        void CancelDrag()
        {
            int pointerId = _pointerId;
            float restored = _valueOnDragStart;
            ResetDragState();
            ReleasePointerSafely(pointerId);

            // ドラッグ中に通知した値を巻き戻すので、こちらも通知する
            this.value = restored;
            UpdateVisualState();
        }

        void ResetDragState()
        {
            _pointerDown = false;
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            RestoreCursor();
            ReleaseOverlay();
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

        double ComputeDelta(Vector2 previous, Vector2 current, Vector2 center)
        {
            if (AbsoluteMode)
            {
                return AbsoluteDelta(current, center);
            }

            Vector2 previousVector = previous - center;
            Vector2 currentVector = current - center;
            if (previousVector.sqrMagnitude <= MIN_VECTOR_SQR_LENGTH
                || currentVector.sqrMagnitude <= MIN_VECTOR_SQR_LENGTH)
            {
                return 0.0;
            }

            // 前フレームと現フレームのベクトル間の符号付き角度を積むので多回転が自然に扱える
            return TweeqMath.SignedAngleBetween(ScreenAngle(currentVector), ScreenAngle(previousVector));
        }

        double AbsoluteDelta(Vector2 position, Vector2 center)
        {
            Vector2 vector = position - center;
            if (vector.sqrMagnitude <= MIN_VECTOR_SQR_LENGTH)
            {
                return 0.0;
            }

            double target = ScreenAngle(vector) - _angleOffset - UP_ANGLE_OFFSET;

            // スナップ済みの出力ではなく生の累積値を基準にすることで、スナップが累積器へ漏れない
            return TweeqMath.SignedAngleBetween(target, _local);
        }

        void ApplyDelta(double delta)
        {
            if (_altHeld)
            {
                delta *= FINE_SCALE;
            }

            var result = RotaryLogic.GetDragValue(_local, delta, _snap, ShouldSnap);
            _local = result.local;

            float next = (float)TweeqMath.Quantize(result.output, _step, 0.0);
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

        #region Mode

        /// <summary>ホバー／ドラッグで「膨らんでいる」状態か。</summary>
        bool Active => _hovered || _dragging;

        bool AbsoluteMode
        {
            get
            {
                // A/R を押している間はキーが勝つ（両押しは後から押した方）。
                // 押していなければポインタ位置由来のモードに委ねる。
                if (_absoluteKeyHeld && _relativeKeyHeld)
                {
                    return _absoluteKeyWasLast;
                }

                if (_absoluteKeyHeld || _relativeKeyHeld)
                {
                    return _absoluteKeyHeld;
                }

                return _modeByPointer;
            }
        }

        bool ShouldSnap
        {
            get
            {
                if (_shiftHeld || _snapKeyHeld)
                {
                    return true;
                }

                float inner = _theme != null ? _theme.InputHeight * SNAP_RING_INNER_FACTOR : 0f;
                return _dragging
                    && inner <= _pointerDistance
                    && _pointerDistance <= SNAP_RING_OUTER_RADIUS;
            }
        }

        // 針が向く側の半円ウェッジ（デッドゾーン外）に入ったら絶対モード。
        // ドラッグ中は開始時のモードを保つため一切更新しない。
        void UpdateModeByPointer(Vector2 localPosition)
        {
            if (_dragging)
            {
                return;
            }

            bool absolute = false;
            Vector2 offset = localPosition - Center();
            float distance = offset.magnitude;
            float radius = KnobVisualRadius();

            if (radius > 0f && distance <= radius && distance > radius * ABSOLUTE_DEAD_ZONE_RATIO)
            {
                Vector2 tipDirection = AngleDirection(DisplayAngle());
                absolute = Vector2.Dot(offset, tipDirection) > 0f;
            }

            if (absolute == _modeByPointer)
            {
                return;
            }

            _modeByPointer = absolute;
            Refresh();
        }

        #endregion

        #region Cursor

        void HideCursor()
        {
            if (_cursorHidden)
            {
                return;
            }

            _cursorHidden = true;
            UnityEngine.Cursor.visible = false;
        }

        void RestoreCursor()
        {
            if (!_cursorHidden)
            {
                return;
            }

            _cursorHidden = false;
            UnityEngine.Cursor.visible = true;
        }

        #endregion

        #region Overlay

        void AcquireOverlay()
        {
            if (_overlay != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // パネル未接続ならガイドは諦める（操作自体は成立させる）
                return;
            }

            _overlay = new TweakOverlay();
            layer.Add(_overlay);
        }

        void ReleaseOverlay()
        {
            if (_overlay == null)
            {
                return;
            }

            _overlay.RemoveFromHierarchy();
            _overlay = null;
        }

        void UpdateOverlay()
        {
            if (_overlay == null)
            {
                return;
            }

            if (!_dragging || _theme == null)
            {
                ReleaseOverlay();
                return;
            }

            double offset = _angleOffset + UP_ANGLE_OFFSET;
            TweakOverlayState state = new TweakOverlayState
            {
                Theme = _theme,
                Center = this.worldBound.center,
                Origin = _originPanelPosition,
                Pointer = _pointerPanelPosition,
                StartAngle = _valueOnDragStart + offset,

                // Vue は弧の終端に model.value（スナップ・量子化後の出力）を使う。
                // 生の累積角度 _local を渡すとスナップ中もグルグルが滑ってしまう
                CurrentAngle = _value + offset,
                ValueAngle = _value + offset,
                Value = _value,
                Snap = _snap,
                AngleOffset = offset,
                Absolute = AbsoluteMode,
                DoSnap = ShouldSnap,
            };

            _overlay.Sync(in state);
        }

        #endregion

        #region Geometry

        Vector2 Center()
        {
            Rect rect = this.contentRect;
            return rect.center;
        }

        float KnobRadius()
        {
            Rect rect = this.contentRect;
            return Mathf.Min(rect.width, rect.height) * 0.5f;
        }

        // 見た目の半径。判定はスケール後の円で行う
        float KnobVisualRadius()
        {
            // 目標スケール（1.8）ではなくアニメーション中の補間値を使う。
            // 目標値で判定するとホバー開始直後（まだ小さい）にノブの外周ギリギリでも
            // 絶対モード扱いになり、暗い accentSoft でスケールが始まって見える
            float scale = Active ? HOVER_SCALE : 1f;
            if (_knob != null)
            {
                Vector3 resolved = _knob.resolvedStyle.scale.value;
                if (!float.IsNaN(resolved.x) && resolved.x > 0f)
                {
                    scale = resolved.x;
                }
            }

            return KnobRadius() * scale;
        }

        // キャプチャ中も座標系がぶれないよう、パネル座標からローカルへ変換する
        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return this.WorldToLocal(new Vector2(position.x, position.y));
        }

        // オーバーレイはパネル座標で描くので、変換しない生の位置も持っておく
        static Vector2 PanelPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return new Vector2(position.x, position.y);
        }

        // スクリーン座標は y 下向きなので、時計回りが正になる
        static double ScreenAngle(Vector2 vector)
        {
            return Mathf.Rad2Deg * Mathf.Atan2(vector.y, vector.x);
        }

        static Vector2 AngleDirection(double degrees)
        {
            float radians = Mathf.Deg2Rad * (float)degrees;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        double DisplayAngle()
        {
            return _value + _angleOffset + UP_ANGLE_OFFSET;
        }

        // float の丸め誤差で「スナップ角ちょうど」を取りこぼさないための許容幅
        static bool NearlyMultiple(double value, double step)
        {
            if (!TweeqMath.IsFinite(value) || !TweeqMath.IsFinite(step) || step == 0.0)
            {
                return false;
            }

            double snapped = Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
            double tolerance = Math.Max(1e-3, Math.Abs(value) * 1e-5);
            return Math.Abs(snapped - value) <= tolerance;
        }

        #endregion

        #region Knob presentation

        void ApplyKnobTransition()
        {
            if (_knob == null)
            {
                return;
            }

            float duration = _theme != null ? _theme.HoverTransitionDuration : 0.15f;

            // Vue は cubic-bezier(0.4, 0, 0.2, 1)（Material standard）。UI Toolkit の EasingMode に
            // 同一カーブが無いため、立ち上がりと収束が最も近い EaseInOutCubic で近似する。
            _knob.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("scale") });
            _knob.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });
            _knob.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
        }

        void ApplyKnobScale()
        {
            if (_knob == null)
            {
                return;
            }

            float scale = Active ? HOVER_SCALE : 1f;
            _knob.style.scale = new StyleScale(new Scale(new Vector3(scale, scale, 1f)));
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.focusable = !_disabled;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            if (!_disabled)
            {
                return;
            }

            // 減光した状態で膨らんだまま／リングが残ったままにならないよう、見た目の状態も落とす
            _focused = false;
            _hovered = false;
            _modeByPointer = false;
            _absoluteKeyHeld = false;
            _relativeKeyHeld = false;
            _snapKeyHeld = false;
        }

        void UpdateVisualState()
        {
            ApplyKnobScale();
            Refresh();
        }

        // 外側（フォーカスリング）とノブは別レイヤなので、必ず両方を汚す
        void Refresh()
        {
            this.MarkDirtyRepaint();

            if (_knob != null)
            {
                _knob.MarkDirtyRepaint();
            }

            UpdateOverlay();
        }

        #endregion

        #region Painting

        // 外側はスケールしない層。仕様 §1 のフォーカスリングだけを描く
        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || !_focused)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            float radius = KnobRadius() + FOCUS_RING_INSET;
            if (radius <= 0f)
            {
                return;
            }

            painter.strokeColor = _theme.AccentHover;
            painter.lineWidth = FOCUS_RING_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.Arc(Center(), radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        // ノブ層。scale はこの要素に掛かるので、描画は常に等倍の 24px 箱で行う
        void OnGenerateKnobContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _knob == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _knob.contentRect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f)
            {
                return;
            }

            Vector2 center = rect.center;
            bool absoluteHover = Active && AbsoluteMode;

            Color disc = _theme.Accent;
            if (absoluteHover)
            {
                disc = _theme.AccentSoft;
            }
            else if (Active || _focused)
            {
                disc = _theme.AccentHover;
            }

            painter.fillColor = disc;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();

            Vector2 direction = AngleDirection(DisplayAngle());
            painter.strokeColor = absoluteHover ? _theme.AccentHover : _theme.Input;
            painter.lineWidth = INDICATOR_WIDTH;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(center + direction * (radius * INDICATOR_INNER_RATIO));
            painter.LineTo(center + direction * (radius * INDICATOR_OUTER_RATIO));
            painter.Stroke();
        }

        #endregion

        #region Tweak overlay

        /// <summary>ドラッグ中だけ生きるオーバーレイの描画パラメータ。角度は描画オフセット込み。</summary>
        struct TweakOverlayState
        {
            public TweeqTheme Theme;
            public Vector2 Center;
            public Vector2 Origin;
            public Vector2 Pointer;
            public double StartAngle;
            public double CurrentAngle;
            public double ValueAngle;
            public double Value;
            public double Snap;
            public double AngleOffset;
            public bool Absolute;
            public bool DoSnap;
        }

        /// <summary>
        /// スナップメーター・多回転サークル・弧＋矢印・絶対ガイド線・値ラベルを描く層。
        /// 座標は全てパネル座標（＝この要素のローカル座標）。
        /// </summary>
        sealed class TweakOverlay : VisualElement
        {
            #region Constants

            const float PILL_HEIGHT = 20f;
            const float PILL_PADDING = 8f;
            const float PILL_FONT_SIZE = 11f;
            const float CHEVRON_FONT_SIZE = 14f;
            const float CHEVRON_GAP = 4f;
            const float LABEL_EDGE_MARGIN = 40f;

            const float GUIDE_WIDTH = 2f;
            const float METER_WIDTH = 1f;
            const float METER_SNAP_WIDTH = 2f;

            // これ未満の掃き角は「まだ動いていない」とみなす（度）
            const double MIN_ARC_SWEEP = 1e-4;

            // 全長 6px（先端 4 + 尾 2）、幅 6px の塗り三角
            const float ARROW_TIP_OFFSET = 4f;
            const float ARROW_TAIL_OFFSET = 2f;
            const float ARROW_HALF_WIDTH = 3f;

            #endregion

            #region Fields

            TweakOverlayState _state;
            bool _hasState;

            VisualElement _labelRoot;
            VisualElement _arrowsRoot;
            VisualElement _pill;
            Label _valueLabel;
            Label _leftChevron;
            Label _rightChevron;
            Vector2 _labelPoint;

            // 直近にフォントを適用したテーマ。ドラッグ中は毎フレーム Sync が走るので、
            // managed 値（FontDefinition）の代入はテーマが変わった時だけに絞る
            TweeqTheme _fontTheme;

            // 角度表示は 0.1° 単位なので、同じ表示に落ちるフレームでは文字列を作り直さない
            long _angleKeyRevolutions;
            double _angleKeyTenths;
            bool _hasAngleKey;

            #endregion

            #region Construction

            public TweakOverlay()
            {
                this.name = "tweeq-rotary-tweak-overlay";
                this.pickingMode = PickingMode.Ignore;
                this.style.position = Position.Absolute;
                this.style.left = 0f;
                this.style.top = 0f;
                this.style.right = 0f;
                this.style.bottom = 0f;
                this.style.overflow = Overflow.Visible;

                this.generateVisualContent += OnGenerateVisualContent;
                BuildLabel();
            }

            void BuildLabel()
            {
                _labelRoot = new VisualElement { pickingMode = PickingMode.Ignore };
                _labelRoot.style.position = Position.Absolute;
                _labelRoot.style.flexDirection = FlexDirection.Row;
                _labelRoot.style.alignItems = Align.Center;

                // 中心合わせは実解決サイズが要るので、確定した時点で置き直す
                _labelRoot.RegisterCallback<GeometryChangedEvent>(OnLabelGeometryChanged);

                _leftChevron = CreateChevron("<");
                _rightChevron = CreateChevron(">");

                _pill = new VisualElement { pickingMode = PickingMode.Ignore };
                _pill.style.height = PILL_HEIGHT;
                _pill.style.minWidth = PILL_HEIGHT;
                _pill.style.flexDirection = FlexDirection.Row;
                _pill.style.alignItems = Align.Center;
                _pill.style.justifyContent = Justify.Center;
                _pill.style.paddingLeft = PILL_PADDING;
                _pill.style.paddingRight = PILL_PADDING;
                _pill.style.flexShrink = 0f;
                SetBorderWidth(_pill, 1f);

                // 高さ固定なので「完全ピル」は半径 = 高さ/2 で計算できる
                SetBorderRadius(_pill, PILL_HEIGHT * 0.5f);

                _valueLabel = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
                _valueLabel.style.fontSize = PILL_FONT_SIZE;
                _valueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                ClearMargin(_valueLabel);
                _pill.Add(_valueLabel);

                // Vue の .arrows 相当: ピル本体は水平のまま、この層だけがノブ→ポインタ方向に回転する
                _arrowsRoot = new VisualElement { pickingMode = PickingMode.Ignore };
                _arrowsRoot.style.position = Position.Absolute;
                _arrowsRoot.style.left = 0f;
                _arrowsRoot.style.top = 0f;
                _arrowsRoot.style.right = 0f;
                _arrowsRoot.style.bottom = 0f;
                _arrowsRoot.style.overflow = Overflow.Visible;
                _arrowsRoot.Add(_leftChevron);
                _arrowsRoot.Add(_rightChevron);

                _labelRoot.Add(_pill);
                _labelRoot.Add(_arrowsRoot);
                this.Add(_labelRoot);
            }

            static Label CreateChevron(string text)
            {
                Label label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.style.fontSize = CHEVRON_FONT_SIZE;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                ClearMargin(label);

                // ピルの外側（左右）に張り出す。Vue の right:100% / left:100% 相当
                label.style.position = Position.Absolute;
                label.style.top = Length.Percent(50);
                label.style.translate = new StyleTranslate(new Translate(0f, Length.Percent(-50)));
                if (text == "<")
                {
                    label.style.right = Length.Percent(100);
                    label.style.marginRight = CHEVRON_GAP;
                }
                else
                {
                    label.style.left = Length.Percent(100);
                    label.style.marginLeft = CHEVRON_GAP;
                }

                return label;
            }

            #endregion

            #region Sync

            public void Sync(in TweakOverlayState state)
            {
                _state = state;
                _hasState = state.Theme != null;
                if (!_hasState)
                {
                    return;
                }

                ApplyLabelStyle(state.Theme);
                SyncValueLabel(state.Value);
                UpdateLabelTransform();
                this.MarkDirtyRepaint();
            }

            // Sync はポインタが動かないフレームでも走るので、表示が変わるときだけ文字列を作る
            void SyncValueLabel(double value)
            {
                bool cacheable = TweeqFormat.TryGetAngleDisplayKey(
                    value, out long revolutions, out double tenths);

                if (cacheable
                    && _hasAngleKey
                    && _angleKeyRevolutions == revolutions
                    && TweeqFormat.SameValueBits(_angleKeyTenths, tenths))
                {
                    return;
                }

                _valueLabel.text = TweeqFormat.FormatAngle(value);

                // 丸め境界付近や非有限値はキー化できないので、次フレームも作り直させる
                _hasAngleKey = cacheable;
                _angleKeyRevolutions = revolutions;
                _angleKeyTenths = tenths;
            }

            void ApplyLabelStyle(TweeqTheme theme)
            {
                _pill.style.backgroundColor = theme.SurfaceOpaque;
                SetBorderColor(_pill, theme.Border);
                _valueLabel.style.color = theme.Text;
                _leftChevron.style.color = theme.Accent;
                _rightChevron.style.color = theme.Accent;

                if (!ReferenceEquals(_fontTheme, theme))
                {
                    _fontTheme = theme;

                    // 角度そのものを読む欄なので数値フォント（シェブロンは記号なので UI 既定のまま）
                    TweeqFonts.Apply(_valueLabel, theme.FontNumeric);
                }
            }

            void UpdateLabelTransform()
            {
                Rect bounds = this.contentRect;
                Vector2 target = _state.Pointer;

                if (bounds.width > LABEL_EDGE_MARGIN * 2f && bounds.height > LABEL_EDGE_MARGIN * 2f)
                {
                    Rect inner = new Rect(
                        bounds.xMin + LABEL_EDGE_MARGIN,
                        bounds.yMin + LABEL_EDGE_MARGIN,
                        bounds.width - LABEL_EDGE_MARGIN * 2f,
                        bounds.height - LABEL_EDGE_MARGIN * 2f);
                    target = ClampAlongRay(_state.Origin, target, inner);
                }

                _labelPoint = target;

                Vector2 pointerVector = _state.Pointer - _state.Center;
                if (pointerVector.sqrMagnitude > MIN_VECTOR_SQR_LENGTH)
                {
                    // ノブ→ポインタの向きに直交させると、ドラッグ方向に沿って読める。
                    // 回転はシェブロン層のみ（Vue と同じくピル本体は水平を保つ）
                    float degrees = (float)(ScreenAngle(pointerVector) + 90.0);
                    _arrowsRoot.style.rotate = new StyleRotate(new Rotate(new Angle(degrees, AngleUnit.Degree)));
                }

                UpdateLabelPosition();
            }

            void OnLabelGeometryChanged(GeometryChangedEvent evt)
            {
                UpdateLabelPosition();
            }

            void UpdateLabelPosition()
            {
                if (_labelRoot == null)
                {
                    return;
                }

                float width = _labelRoot.resolvedStyle.width;
                float height = _labelRoot.resolvedStyle.height;
                _labelRoot.style.left = _labelPoint.x - width * 0.5f;
                _labelRoot.style.top = _labelPoint.y - height * 0.5f;
            }

            #endregion

            #region Painting

            void OnGenerateVisualContent(MeshGenerationContext context)
            {
                if (!_hasState || context == null)
                {
                    return;
                }

                TweeqTheme theme = _state.Theme;
                if (theme == null)
                {
                    return;
                }

                Painter2D painter = context.painter2D;
                if (painter == null)
                {
                    return;
                }

                PaintSnapMeter(painter, theme);

                if (_state.Absolute)
                {
                    PaintAbsoluteGuide(painter, theme);
                }
                else
                {
                    PaintRelativePath(painter, theme);
                }

                PaintActiveTick(painter, theme);
            }

            void PaintSnapMeter(Painter2D painter, TweeqTheme theme)
            {
                double snap = Math.Abs(_state.Snap);
                if (!TweeqMath.IsFinite(snap) || snap <= 0.0)
                {
                    return;
                }

                float inner = theme.InputHeight * SNAP_RING_INNER_FACTOR;
                float outer = SNAP_RING_OUTER_RADIUS;
                if (outer <= inner)
                {
                    return;
                }

                int count = (int)TweeqMath.Clamp(Math.Ceiling(360.0 / snap), 1.0, MAX_METER_LINES);

                painter.lineCap = LineCap.Butt;
                painter.lineWidth = _state.DoSnap ? METER_SNAP_WIDTH : METER_WIDTH;
                painter.strokeColor = _state.DoSnap ? theme.AccentSoftHover : theme.Border;
                painter.BeginPath();

                for (int index = 0; index < count; index++)
                {
                    Vector2 direction = AngleDirection(index * snap + _state.AngleOffset);
                    painter.MoveTo(_state.Center + direction * inner);
                    painter.LineTo(_state.Center + direction * outer);
                }

                painter.Stroke();
            }

            void PaintActiveTick(Painter2D painter, TweeqTheme theme)
            {
                if (!_state.DoSnap || !NearlyMultiple(_state.Value, _state.Snap))
                {
                    return;
                }

                float inner = theme.InputHeight * SNAP_RING_INNER_FACTOR;
                float outer = SNAP_RING_OUTER_RADIUS;
                if (outer <= inner)
                {
                    return;
                }

                Vector2 direction = AngleDirection(_state.ValueAngle);
                painter.lineCap = LineCap.Butt;
                painter.lineWidth = METER_SNAP_WIDTH;
                painter.strokeColor = theme.Accent;
                painter.BeginPath();
                painter.MoveTo(_state.Center + direction * inner);
                painter.LineTo(_state.Center + direction * outer);
                painter.Stroke();
            }

            void PaintAbsoluteGuide(Painter2D painter, TweeqTheme theme)
            {
                // カーソルを消しているので、この線がそのままポインタの代わりになる
                float innerRadius = theme.InputHeight;
                float distance = Mathf.Max(Vector2.Distance(_state.Pointer, _state.Center), innerRadius);
                Vector2 direction = AngleDirection(_state.ValueAngle);

                painter.lineCap = LineCap.Butt;
                painter.lineWidth = GUIDE_WIDTH;
                painter.strokeColor = theme.Accent;
                painter.BeginPath();
                painter.MoveTo(_state.Center + direction * innerRadius);
                painter.LineTo(_state.Center + direction * distance);
                painter.Stroke();
            }

            void PaintRelativePath(Painter2D painter, TweeqTheme theme)
            {
                double total = _state.CurrentAngle - _state.StartAngle;
                if (!TweeqMath.IsFinite(total))
                {
                    return;
                }

                float baseRadius = theme.InputHeight * SNAP_RING_INNER_FACTOR;
                float step = theme.InputHeight * ARC_RADIUS_STEP_FACTOR;
                float sign = total < 0.0 ? -1f : 1f;
                int turns = Mathf.Clamp((int)Math.Floor(Math.Abs(total) / 360.0), 0, MAX_TURN_CIRCLES);

                painter.lineCap = LineCap.Butt;
                painter.lineWidth = GUIDE_WIDTH;
                painter.strokeColor = theme.Accent;

                for (int index = 0; index < turns; index++)
                {
                    float radius = Mathf.Max(MIN_ARC_RADIUS, baseRadius + sign * index * step);
                    painter.BeginPath();
                    painter.Arc(
                        _state.Center,
                        radius,
                        new Angle(0f, AngleUnit.Degree),
                        new Angle(360f, AngleUnit.Degree));
                    painter.ClosePath();
                    painter.Stroke();
                }

                double remainder = total - turns * (double)sign * 360.0;
                float arcRadius = Mathf.Max(MIN_ARC_RADIUS, baseRadius + sign * turns * step);

                // 多回転すると開始角が数千度になる。弧の形は 360 の剰余でしか決まらないので畳んでおく
                double startAngle = TweeqMath.UnsignedMod(_state.StartAngle, 360.0);
                double endAngle = startAngle + remainder;
                bool forward = remainder >= 0.0;

                // 掃き角 0 を Arc に渡すと全周と区別できないので、動き出すまで弧は描かない
                if (Math.Abs(remainder) > MIN_ARC_SWEEP)
                {
                    // UI Toolkit は y 下向きなので、角度が増える向き＝画面上の時計回り
                    painter.BeginPath();
                    painter.Arc(
                        _state.Center,
                        arcRadius,
                        new Angle((float)startAngle, AngleUnit.Degree),
                        new Angle((float)endAngle, AngleUnit.Degree),
                        forward ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
                    painter.Stroke();
                }

                PaintArrowHead(painter, theme, endAngle, arcRadius, forward);
            }

            void PaintArrowHead(Painter2D painter, TweeqTheme theme, double endAngle, float radius, bool forward)
            {
                Vector2 direction = AngleDirection(endAngle);
                Vector2 endPoint = _state.Center + direction * radius;

                // 弧の接線＝半径ベクトルの直交。逆回転なら向きを反転する（egui 版と同じ構成）
                Vector2 tangent = forward
                    ? new Vector2(-direction.y, direction.x)
                    : new Vector2(direction.y, -direction.x);
                Vector2 normal = new Vector2(-tangent.y, tangent.x);

                Vector2 tip = endPoint + tangent * ARROW_TIP_OFFSET;
                Vector2 left = endPoint - tangent * ARROW_TAIL_OFFSET + normal * ARROW_HALF_WIDTH;
                Vector2 right = endPoint - tangent * ARROW_TAIL_OFFSET - normal * ARROW_HALF_WIDTH;

                painter.fillColor = theme.Accent;
                painter.BeginPath();
                painter.MoveTo(tip);
                painter.LineTo(left);
                painter.LineTo(right);
                painter.ClosePath();
                painter.Fill();
            }

            #endregion

            #region Helpers

            // ラベルは「開始点→ポインタ」のレイ上に居たまま、内側へ引き戻す
            static Vector2 ClampAlongRay(Vector2 origin, Vector2 target, Rect bounds)
            {
                if (bounds.Contains(target))
                {
                    return target;
                }

                Vector2 direction = target - origin;
                if (direction.sqrMagnitude <= Mathf.Epsilon)
                {
                    return ClampToRect(target, bounds);
                }

                float amount = 1f;
                if (direction.x > 0f)
                {
                    amount = Mathf.Min(amount, (bounds.xMax - origin.x) / direction.x);
                }
                else if (direction.x < 0f)
                {
                    amount = Mathf.Min(amount, (bounds.xMin - origin.x) / direction.x);
                }

                if (direction.y > 0f)
                {
                    amount = Mathf.Min(amount, (bounds.yMax - origin.y) / direction.y);
                }
                else if (direction.y < 0f)
                {
                    amount = Mathf.Min(amount, (bounds.yMin - origin.y) / direction.y);
                }

                return ClampToRect(origin + direction * Mathf.Clamp01(amount), bounds);
            }

            static Vector2 ClampToRect(Vector2 point, Rect bounds)
            {
                return new Vector2(
                    Mathf.Clamp(point.x, bounds.xMin, bounds.xMax),
                    Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));
            }

            static void ClearMargin(VisualElement element)
            {
                element.style.marginLeft = 0f;
                element.style.marginRight = 0f;
                element.style.marginTop = 0f;
                element.style.marginBottom = 0f;
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

            static void SetBorderRadius(VisualElement element, float radius)
            {
                element.style.borderTopLeftRadius = radius;
                element.style.borderTopRightRadius = radius;
                element.style.borderBottomLeftRadius = radius;
                element.style.borderBottomRightRadius = radius;
            }

            #endregion
        }

        #endregion
    }
}
