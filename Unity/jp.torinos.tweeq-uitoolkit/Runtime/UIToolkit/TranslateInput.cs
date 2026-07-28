using System;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

// クラス側に Label 相当のプロパティは無いが、Rotary と同じく型名の衝突を避けるため別名で参照する
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 2 軸のドラッグスクラバー（M6 第2波仕様 §C）。24×24 のボタンを掴んで動かした画素量が
    /// そのまま値の増分になり、ドラッグ中は原点中心のドットグリッドが背後に広がる。
    /// </summary>
    /// <remarks>
    /// 感度は px 1:1 × speed。Vue も egui も同じ 5 / 0.1 / 1 の 3 段で、修飾キーは毎イベント再評価する。
    /// 値の累積は「直前値 + Δ をクランプ」する Vue 方式（egui は開始値 + 総移動量）。
    /// クランプ端で押し続けても戻した瞬間に追従するので、こちらを採用している。
    /// </remarks>
    [UxmlElement]
    public partial class TranslateInput : VisualElement, INotifyValueChanged<Vector2>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float DEFAULT_SIZE = 24f;

        // RotaryInput と同じ裁定（m7-disabled-invalid-spec.md）。Vue は見た目不変だが、
        // 隣の数値欄が dim するのにボタンだけ生きて見えるのは公演現場で事故のもと
        const float DISABLED_OPACITY = 0.4f;

        // px→値の倍率。Vue computed speed / egui speed と同値
        const float SPEED_COARSE = 5f;
        const float SPEED_FINE = 0.1f;
        const float SPEED_NORMAL = 1f;

        // オーバーレイのグリッド倍率。Vue computed gridScale と同値（speed と逆向きに動く）
        const float GRID_SCALE_COARSE = 0.5f;
        const float GRID_SCALE_FINE = 4f;
        const float GRID_SCALE_NORMAL = 2f;

        // Vue useRafFn の 1 フレームぶんの補間係数
        const float GRID_SCALE_LERP = 0.4f;
        const long GRID_TICK_MS = 16;

        // これ以下の差は 1 フレームで詰めてしまう（毎フレームの再描画を止めるため）
        const float GRID_SCALE_EPSILON = 1e-3f;

        // Vue: .overlay-grid の inset calc(-150px + h/2) ＝ 直径 300 の箱。egui: radius 150
        const float OVERLAY_RADIUS = 150f;
        const float GRID_UNIT = 10f;
        const float DOT_RADIUS = 1f;

        // Vue の mask radial-gradient(closest-side, black 50%, transparent 100%)。
        // 半径の 50% までは不透明、そこから外周でゼロへ落ちる
        const float MASK_SOLID_RATIO = 0.5f;

        // 濃度を帯に量子化して、帯ごとに Fill を 1 回へ畳む（ドット 1 個ずつ Fill すると描画命令が爆発する）
        const int ALPHA_BANDS = 6;

        // grid scale が小さいほど点が増える。過密になる前に間隔で頭打ちにする
        const float MIN_DOT_SPACING = 4f;

        const float AXIS_LINE_WIDTH = 2f;
        const float RANGE_LINE_WIDTH = 1f;

        // ボタン面のドット 3×3（egui paint_grid_icon 実測）
        const float ICON_SPACING = 3.5f;
        const float ICON_DOT_RADIUS = 1f;

        const float FOCUS_RING_WIDTH = 1f;

        // Vue: translate(-50%, calc(-100% - h * .2))＝箱の上端からさらに h*0.2 上にラベル下端が来る
        const float LABEL_GAP_RATIO = 0.2f;
        const float LABEL_FONT_SIZE = 11f;
        const float LABEL_PADDING_X = 6f;
        const float LABEL_PADDING_Y = 4f;
        const float LABEL_RADIUS = 4f;
        const float LABEL_EDGE_MARGIN = 4f;
        const float LABEL_AXIS_GAP = 4f;
        const float LABEL_VALUE_MIN_WIDTH = 30f;

        #endregion

        #region Fields

        Vector2 _value;
        Vector2 _min = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        Vector2 _max = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        bool _showOverlayLabel = true;
        bool _disabled;
        TweeqTheme _theme = TweeqTheme.Dark();

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        readonly VisualElement _focusInner;
        readonly VisualElement _focusOuter;

        TranslateOverlay _overlay;
        readonly IVisualElementScheduledItem _gridItem;

        int _pointerId = PointerId.invalidPointerId;
        bool _dragging;
        Vector2 _previousPanelPosition;
        Vector2 _valueOnDragStart;
        bool _cursorHidden;

        bool _shiftHeld;
        bool _altHeld;
        bool _lockX;
        bool _lockY;

        float _gridScaleAnimated = GRID_SCALE_NORMAL;

        bool _hovered;
        bool _focused;

        #endregion

        #region Public API

        /// <summary>値が変わるたびに発火する（ポインタ移動 1 回につき最大 1 回）。</summary>
        public event Action<Vector2> ValueChanged;

        /// <summary>ドラッグ確定（ポインタを離した時）に 1 ジェスチャ 1 回だけ発火する。</summary>
        public event Action<Vector2> Confirmed;

        /// <summary>現在値。</summary>
        [UxmlAttribute]
        public Vector2 value
        {
            get => _value;
            set
            {
                if (_value.Equals(value))
                {
                    return;
                }

                Vector2 previous = _value;
                SetValueWithoutNotify(value);
                Notify(previous, _value);
            }
        }

        /// <summary>下限。既定は制限なし（負の無限大）。</summary>
        [UxmlAttribute]
        public Vector2 Min
        {
            get => _min;
            set
            {
                _min = value;
                UpdateOverlay();
            }
        }

        /// <summary>上限。既定は制限なし（正の無限大）。</summary>
        [UxmlAttribute]
        public Vector2 Max
        {
            get => _max;
            set
            {
                _max = value;
                UpdateOverlay();
            }
        }

        /// <summary>両軸に同じ下限を与える（仕様の「スカラー指定」）。</summary>
        public void SetMin(float uniform)
        {
            this.Min = new Vector2(uniform, uniform);
        }

        /// <summary>両軸に同じ上限を与える（仕様の「スカラー指定」）。</summary>
        public void SetMax(float uniform)
        {
            this.Max = new Vector2(uniform, uniform);
        }

        /// <summary>ドラッグ中のオーバーレイに現在値ラベルを出すか。既定 true（Vue と同じ）。</summary>
        [UxmlAttribute]
        public bool ShowOverlayLabel
        {
            get => _showOverlayLabel;
            set
            {
                if (_showOverlayLabel == value)
                {
                    return;
                }

                _showOverlayLabel = value;
                UpdateOverlay();
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

                if (_disabled && _dragging)
                {
                    // 無効化の瞬間にドラッグが生きていると、離す手段＝隠したカーソルを取り戻す手段が無くなる。
                    // 掴んだままのキャプチャも Escape と同じ手順で手放す
                    int pointerId = _pointerId;
                    _pointerId = PointerId.invalidPointerId;
                    CancelTranslateDrag();
                    ReleasePointerSafely(pointerId);
                }

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

        /// <summary>現在の感度（Shift=5 / Alt=0.1 / 既定 1）。</summary>
        public float Speed
        {
            get
            {
                if (_shiftHeld)
                {
                    return SPEED_COARSE;
                }

                return _altHeld ? SPEED_FINE : SPEED_NORMAL;
            }
        }

        /// <summary>現在のグリッド倍率の目標値（Shift=0.5 / Alt=4 / 既定 2）。</summary>
        public float GridScaleTarget
        {
            get
            {
                if (_shiftHeld)
                {
                    return GRID_SCALE_COARSE;
                }

                return _altHeld ? GRID_SCALE_FINE : GRID_SCALE_NORMAL;
            }
        }

        /// <summary>ドラッグセッション中か。</summary>
        public bool Dragging => _dragging;

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
        public void SetValueWithoutNotify(Vector2 newValue)
        {
            _value = newValue;
            UpdateOverlay();
        }

        /// <summary>
        /// 修飾キー相当の状態を差し替える（Shift=粗い / Alt=細かい）。
        /// ポインタ・キーイベントからも同じ経路で更新されるので、テストからはこれで代用できる。
        /// </summary>
        public void SetTweakModifiers(bool shift, bool alt)
        {
            if (_shiftHeld == shift && _altHeld == alt)
            {
                return;
            }

            _shiftHeld = shift;
            _altHeld = alt;
            UpdateOverlay();
        }

        /// <summary>X / Y キー相当の軸ロック。押している間だけ有効という契約なので、解除も呼び出し側の責務。</summary>
        public void SetAxisLocks(bool lockHorizontal, bool lockVertical)
        {
            if (_lockX == lockHorizontal && _lockY == lockVertical)
            {
                return;
            }

            _lockX = lockHorizontal;
            _lockY = lockVertical;
            UpdateOverlay();
        }

        /// <summary>ドラッグセッションを開始する（panel 非依存）。</summary>
        public void BeginTranslateDrag()
        {
            if (_disabled || _dragging)
            {
                return;
            }

            _dragging = true;
            _valueOnDragStart = _value;

            // Vue の raf は常時回っているのでドラッグ開始時点では既に目標値。
            // ここで合わせておかないと開始直後だけグリッドが伸び縮みして見える
            _gridScaleAnimated = GridScaleTarget;

            HideCursor();
            AcquireOverlay();
            _gridItem?.Resume();
            Refresh();
        }

        /// <summary>
        /// ドラッグ中の移動量（パネル座標 px・下が正）を適用する。
        /// 値の Y は上向きドラッグで増える（Vue は DOM 準拠で下=+Y だが、Unity の座標感覚に
        /// 合わせて反転する意図的逸脱。m6-wave2-spec.md「TranslateInput」参照）。
        /// </summary>
        public void UpdateTranslateDrag(Vector2 pixelDelta)
        {
            if (!_dragging)
            {
                return;
            }

            Vector2 delta = pixelDelta * this.Speed;
            delta.y = -delta.y;

            // 押している間だけのロック。X は「横のみ」＝縦成分を捨てる
            if (_lockX)
            {
                delta.y = 0f;
            }

            if (_lockY)
            {
                delta.x = 0f;
            }

            Vector2 next = new Vector2(
                ClampAxis(_value.x + delta.x, _min.x, _max.x),
                ClampAxis(_value.y + delta.y, _min.y, _max.y));

            if (next.Equals(_value))
            {
                UpdateOverlay();
                return;
            }

            Vector2 previous = _value;
            _value = next;
            UpdateOverlay();
            Notify(previous, next);
        }

        /// <summary>ドラッグを確定して終了する。Confirmed が 1 回だけ発火する。</summary>
        public void EndTranslateDrag()
        {
            if (!_dragging)
            {
                return;
            }

            StopDragSession();
            Confirmed?.Invoke(_value);
        }

        /// <summary>ドラッグを破棄して開始値へ戻す（Escape 相当）。Confirmed は発火しない。</summary>
        public void CancelTranslateDrag()
        {
            if (!_dragging)
            {
                return;
            }

            Vector2 restored = _valueOnDragStart;
            StopDragSession();

            // ドラッグ中に通知した値を巻き戻すので、こちらも通知する
            this.value = restored;
        }

        #endregion

        #region Construction

        public TranslateInput()
        {
            this.AddToClassList("tweeq-translate-input");

            this.focusable = true;
            this.style.width = DEFAULT_SIZE;
            this.style.height = DEFAULT_SIZE;
            this.style.flexShrink = 0f;

            // InputGroup.ApplyStretch は flexBasis 未指定の子へ basis 0 を配る。
            // width より basis が勝つため、明示しないと 24px 正方形がアイコンの内在幅まで潰れる
            this.style.flexGrow = 0f;
            this.style.flexBasis = DEFAULT_SIZE;

            // フォーカスリングを 1px 外に置くので Hidden にしてはいけない
            this.style.overflow = Overflow.Visible;

            _focusInner = CreateRing(0f);
            _focusOuter = CreateRing(-FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusInner);
            this.hierarchy.Add(_focusOuter);

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

            // scheduled item は 1 個だけ作って Resume/Pause で使い回す（毎ドラッグのクロージャ確保を避ける）
            _gridItem = this.schedule.Execute(OnGridTick).Every(GRID_TICK_MS);
            _gridItem.Pause();

            ApplyStaticStyles();
            Refresh();
        }

        VisualElement CreateRing(float inset)
        {
            VisualElement ring = new VisualElement
            {
                name = "tweeq-translate-focus-ring",
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
            this.style.width = _theme.InputHeight;
            this.style.height = _theme.InputHeight;
            ApplyCornerRadius();

            SetBorderColor(_focusInner, _theme.Input);
            SetBorderColor(_focusOuter, _theme.Accent);
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

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _dragging || _disabled)
            {
                return;
            }

            _pointerId = evt.pointerId;
            _previousPanelPosition = PanelPosition(evt);
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            // X / Y / Escape を受け取るためフォーカスを取る
            this.Focus();

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
            }

            // Vue の useDrag は dragDelaySeconds 0＝押した瞬間からドラッグ扱い（閾値なし）
            BeginTranslateDrag();
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_dragging || evt.pointerId != _pointerId)
            {
                return;
            }

            Vector2 position = PanelPosition(evt);
            Vector2 delta = position - _previousPanelPosition;
            _previousPanelPosition = position;

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            UpdateTranslateDrag(delta);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_dragging || evt.pointerId != _pointerId)
            {
                return;
            }

            int pointerId = _pointerId;
            _pointerId = PointerId.invalidPointerId;

            // 先に確定させる。順序を逆にすると ReleasePointer が投げる PointerCaptureOut が
            // セッションを畳んでしまい、Confirmed が出なくなる
            EndTranslateDrag();
            ReleasePointerSafely(pointerId);
            evt.StopPropagation();
        }

        // キャプチャを失った場合でもドラッグ状態（＝隠したカーソル・オーバーレイ）を残さない
        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _pointerId = PointerId.invalidPointerId;

            if (!_dragging)
            {
                return;
            }

            // 値は動いたところで確定させる。確定イベントは「離した」ときだけなのでここでは出さない
            StopDragSession();
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

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // パネルから外れてもカーソルとオーバーレイを取り残さない
            _pointerId = PointerId.invalidPointerId;
            _hovered = false;
            _focused = false;
            StopDragSession();
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

        #region Keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            SetTweakModifiers(
                (evt.modifiers & EventModifiers.Shift) != 0,
                (evt.modifiers & EventModifiers.Alt) != 0);

            switch (evt.keyCode)
            {
                case KeyCode.X:
                    SetAxisLocks(true, _lockY);
                    evt.StopPropagation();
                    break;

                case KeyCode.Y:
                    SetAxisLocks(_lockX, true);
                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    if (_dragging)
                    {
                        int pointerId = _pointerId;
                        _pointerId = PointerId.invalidPointerId;
                        CancelTranslateDrag();
                        ReleasePointerSafely(pointerId);
                        evt.StopPropagation();
                    }

                    break;
            }
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            SetTweakModifiers(
                (evt.modifiers & EventModifiers.Shift) != 0,
                (evt.modifiers & EventModifiers.Alt) != 0);

            switch (evt.keyCode)
            {
                case KeyCode.X:
                    SetAxisLocks(false, _lockY);
                    evt.StopPropagation();
                    break;

                case KeyCode.Y:
                    SetAxisLocks(_lockX, false);
                    evt.StopPropagation();
                    break;
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

            // 押しっぱなし扱いのキーはフォーカスを失った時点で解除する（KeyUp が来ないため）
            SetAxisLocks(false, false);
            SetTweakModifiers(false, false);
            Refresh();
        }

        #endregion

        #region Drag session

        void StopDragSession()
        {
            _dragging = false;
            _gridItem?.Pause();
            RestoreCursor();
            ReleaseOverlay();
            Refresh();
        }

        void Notify(Vector2 previous, Vector2 current)
        {
            if (this.panel != null)
            {
                using (ChangeEvent<Vector2> changeEvent = ChangeEvent<Vector2>.GetPooled(previous, current))
                {
                    changeEvent.target = this;
                    this.SendEvent(changeEvent);
                }
            }

            ValueChanged?.Invoke(current);
        }

        static float ClampAxis(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
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

        void OnGridTick()
        {
            if (!_dragging)
            {
                _gridItem?.Pause();
                return;
            }

            float target = GridScaleTarget;
            if (Mathf.Abs(_gridScaleAnimated - target) <= GRID_SCALE_EPSILON)
            {
                if (_gridScaleAnimated == target)
                {
                    return;
                }

                _gridScaleAnimated = target;
            }
            else
            {
                _gridScaleAnimated = Mathf.Lerp(_gridScaleAnimated, target, GRID_SCALE_LERP);
            }

            UpdateOverlay();
        }

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

            _overlay = new TranslateOverlay();
            layer.Add(_overlay);
            UpdateOverlay();
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

            TranslateOverlayState state = new TranslateOverlayState
            {
                Theme = _theme,
                Center = this.worldBound.center,
                Value = _value,
                Min = _min,
                Max = _max,
                GridScale = _gridScaleAnimated,
                LockX = _lockX,
                LockY = _lockY,
                ShowLabel = _showOverlayLabel,

                // Vue: precisionOf(speed)。0.1 のときだけ小数 1 桁になる
                Precision = TweeqMath.PrecisionOf(this.Speed),
            };

            _overlay.Sync(in state);
        }

        #endregion

        #region Presentation

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.focusable = !_disabled;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            if (!_disabled)
            {
                return;
            }

            // 減光した状態でホバー色・フォーカスリング・押しっぱなしキーが残らないようにする
            _hovered = false;
            _focused = false;
            SetAxisLocks(false, false);
            SetTweakModifiers(false, false);
        }

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.backgroundColor = _hovered || _dragging ? _theme.AccentHover : _theme.Accent;

            _focusInner.style.display = _focused ? DisplayStyle.Flex : DisplayStyle.None;
            _focusOuter.style.display = _focused ? DisplayStyle.Flex : DisplayStyle.None;

            this.MarkDirtyRepaint();
        }

        // ボタン面の 3×3 ドットアイコン（Vue の mingcute:dot-grid-fill 相当）
        void OnGenerateVisualContent(MeshGenerationContext context)
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

            Vector2 center = this.contentRect.center;
            painter.fillColor = TweeqTheme.ContrastText(_theme.Accent);

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector2 dot = new Vector2(center.x + x * ICON_SPACING, center.y + y * ICON_SPACING);
                    painter.BeginPath();
                    painter.Arc(
                        dot,
                        ICON_DOT_RADIUS,
                        new Angle(0f, AngleUnit.Degree),
                        new Angle(360f, AngleUnit.Degree));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        #endregion

        #region Helpers

        // オーバーレイはパネル座標で描くので、変換しない生の位置を持つ
        static Vector2 PanelPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return new Vector2(position.x, position.y);
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

        #region Overlay implementation

        /// <summary>ドラッグ中だけ生きるオーバーレイの描画パラメータ。座標はパネル座標。</summary>
        struct TranslateOverlayState
        {
            public TweeqTheme Theme;
            public Vector2 Center;
            public Vector2 Value;
            public Vector2 Min;
            public Vector2 Max;
            public float GridScale;
            public bool LockX;
            public bool LockY;
            public bool ShowLabel;
            public int Precision;
        }

        /// <summary>
        /// ドットグリッド・軸ロック線・レンジ枠・現在値ラベルを描く層。
        /// </summary>
        sealed class TranslateOverlay : VisualElement
        {
            #region Fields

            TranslateOverlayState _state;
            bool _hasState;

            VisualElement _labelRoot;
            UILabel _xAxis;
            UILabel _yAxis;
            ValueLabel _xValue;
            ValueLabel _yValue;

            // 直近にフォントを適用したテーマ。ドラッグ中は毎フレーム Sync が走るので、
            // managed 値（FontDefinition）の代入はテーマが変わった時だけに絞る
            TweeqTheme _fontTheme;

            #endregion

            #region Construction

            public TranslateOverlay()
            {
                this.name = "tweeq-translate-overlay";
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
                _labelRoot.style.paddingLeft = LABEL_PADDING_X;
                _labelRoot.style.paddingRight = LABEL_PADDING_X;
                _labelRoot.style.paddingTop = LABEL_PADDING_Y;
                _labelRoot.style.paddingBottom = LABEL_PADDING_Y;
                _labelRoot.style.display = DisplayStyle.None;
                SetBorderWidth(_labelRoot, 1f);
                SetCornerRadius(_labelRoot, LABEL_RADIUS, true, true, true, true);

                // 中心合わせは実解決サイズが要るので、確定した時点で置き直す
                _labelRoot.RegisterCallback<GeometryChangedEvent>(OnLabelGeometryChanged);

                _xAxis = CreateAxisLabel("X", 0f);
                _xValue = new ValueLabel();
                _labelRoot.Add(_xAxis);
                _labelRoot.Add(_xValue.Element);

                _yAxis = CreateAxisLabel("Y", LABEL_AXIS_GAP * 2f);
                _yValue = new ValueLabel();
                _labelRoot.Add(_yAxis);
                _labelRoot.Add(_yValue.Element);

                this.Add(_labelRoot);
            }

            static UILabel CreateAxisLabel(string text, float marginLeft)
            {
                UILabel label = new UILabel(text) { pickingMode = PickingMode.Ignore };
                label.style.fontSize = LABEL_FONT_SIZE;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.marginLeft = marginLeft;
                label.style.marginRight = LABEL_AXIS_GAP;
                label.style.marginTop = 0f;
                label.style.marginBottom = 0f;
                return label;
            }

            #endregion

            #region Sync

            public void Sync(in TranslateOverlayState state)
            {
                _state = state;
                _hasState = state.Theme != null;
                if (!_hasState)
                {
                    return;
                }

                SyncLabel();
                this.MarkDirtyRepaint();
            }

            void SyncLabel()
            {
                _labelRoot.style.display = _state.ShowLabel ? DisplayStyle.Flex : DisplayStyle.None;
                if (!_state.ShowLabel)
                {
                    return;
                }

                TweeqTheme theme = _state.Theme;
                _labelRoot.style.backgroundColor = theme.SurfaceOpaque;
                SetBorderColor(_labelRoot, theme.Border);

                // 軸名だけ弱い色にする（Vue の :deep(i) と同じ意図）
                _xAxis.style.color = theme.TextMuted;
                _yAxis.style.color = theme.TextMuted;
                _xValue.Element.style.color = theme.Text;
                _yValue.Element.style.color = theme.Text;

                if (!ReferenceEquals(_fontTheme, theme))
                {
                    _fontTheme = theme;

                    // 値そのものを読む欄なので数値フォント（X / Y の軸名は UI 既定のまま）
                    TweeqFonts.Apply(_xValue.Element, theme.FontNumeric);
                    TweeqFonts.Apply(_yValue.Element, theme.FontNumeric);
                }

                _xValue.Sync(_state.Value.x, _state.Precision);
                _yValue.Sync(_state.Value.y, _state.Precision);

                UpdateLabelPosition();
            }

            void OnLabelGeometryChanged(GeometryChangedEvent evt)
            {
                UpdateLabelPosition();
            }

            void UpdateLabelPosition()
            {
                if (_labelRoot == null || !_hasState)
                {
                    return;
                }

                float width = _labelRoot.resolvedStyle.width;
                float height = _labelRoot.resolvedStyle.height;
                float inputHeight = _state.Theme.InputHeight;

                float left = _state.Center.x - width * 0.5f;
                float top = _state.Center.y - inputHeight * (0.5f + LABEL_GAP_RATIO) - height;

                Rect bounds = this.contentRect;
                if (bounds.width > 0f && bounds.height > 0f)
                {
                    left = Mathf.Clamp(
                        left, bounds.xMin + LABEL_EDGE_MARGIN, Mathf.Max(bounds.xMax - width - LABEL_EDGE_MARGIN, bounds.xMin));
                    top = Mathf.Clamp(
                        top, bounds.yMin + LABEL_EDGE_MARGIN, Mathf.Max(bounds.yMax - height - LABEL_EDGE_MARGIN, bounds.yMin));
                }

                _labelRoot.style.left = left;
                _labelRoot.style.top = top;
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

                PaintGrid(painter, theme);
                PaintAxisLocks(painter, theme);
                PaintRange(painter, theme);
            }

            void PaintGrid(Painter2D painter, TweeqTheme theme)
            {
                float scale = _state.GridScale;
                if (!(scale > 0f))
                {
                    return;
                }

                float spacing = Mathf.Max(GRID_UNIT * scale, MIN_DOT_SPACING);
                Vector2 center = _state.Center;

                // 値の逆方向へグリッドを流す（Vue の background-position、egui の rem_euclid と同じ）。
                // Y は値空間が上向き正（Unity 合わせの逸脱）でパネルは下向き正なので符号がそのまま
                float offsetX = Repeat(-_state.Value.x * scale, spacing);
                float offsetY = Repeat(_state.Value.y * scale, spacing);

                float left = center.x - OVERLAY_RADIUS + offsetX;
                float top = center.y - OVERLAY_RADIUS + offsetY;
                float right = center.x + OVERLAY_RADIUS;
                float bottom = center.y + OVERLAY_RADIUS;

                Color baseColor = theme.TextSubtle;
                float solidRadius = OVERLAY_RADIUS * MASK_SOLID_RATIO;

                for (int band = 0; band < ALPHA_BANDS; band++)
                {
                    // 濃度 a のとき距離は R*(1 - a/2)。帯の内外半径はその逆算
                    float alphaHigh = 1f - band / (float)ALPHA_BANDS;
                    float alphaLow = 1f - (band + 1) / (float)ALPHA_BANDS;

                    float inner = band == 0 ? 0f : OVERLAY_RADIUS * (1f - alphaHigh * 0.5f);
                    float outer = OVERLAY_RADIUS * (1f - alphaLow * 0.5f);

                    // 内周の帯は「マスクが 1 で頭打ちの円」を丸ごと含む
                    if (band == 0)
                    {
                        outer = Mathf.Max(outer, solidRadius);
                    }

                    float innerSqr = inner * inner;
                    float outerSqr = outer * outer;

                    Color color = baseColor;
                    color.a = baseColor.a * (band == 0 ? 1f : (alphaHigh + alphaLow) * 0.5f);
                    painter.fillColor = color;
                    painter.BeginPath();

                    bool any = false;
                    for (float y = top; y <= bottom; y += spacing)
                    {
                        float dy = y - center.y;
                        for (float x = left; x <= right; x += spacing)
                        {
                            float dx = x - center.x;
                            float distanceSqr = dx * dx + dy * dy;
                            if (distanceSqr < innerSqr || distanceSqr >= outerSqr)
                            {
                                continue;
                            }

                            // 半径 1px の点は矩形と見分けが付かないので、帯ごとに Fill 1 回へ畳める矩形で描く
                            painter.MoveTo(new Vector2(x - DOT_RADIUS, y - DOT_RADIUS));
                            painter.LineTo(new Vector2(x + DOT_RADIUS, y - DOT_RADIUS));
                            painter.LineTo(new Vector2(x + DOT_RADIUS, y + DOT_RADIUS));
                            painter.LineTo(new Vector2(x - DOT_RADIUS, y + DOT_RADIUS));
                            painter.ClosePath();
                            any = true;
                        }
                    }

                    if (any)
                    {
                        painter.Fill();
                    }
                }
            }

            void PaintAxisLocks(Painter2D painter, TweeqTheme theme)
            {
                if (!_state.LockX && !_state.LockY)
                {
                    return;
                }

                Vector2 center = _state.Center;
                painter.strokeColor = theme.Accent;
                painter.lineWidth = AXIS_LINE_WIDTH;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();

                if (_state.LockX)
                {
                    painter.MoveTo(new Vector2(center.x - OVERLAY_RADIUS, center.y));
                    painter.LineTo(new Vector2(center.x + OVERLAY_RADIUS, center.y));
                }

                if (_state.LockY)
                {
                    painter.MoveTo(new Vector2(center.x, center.y - OVERLAY_RADIUS));
                    painter.LineTo(new Vector2(center.x, center.y + OVERLAY_RADIUS));
                }

                painter.Stroke();
            }

            // Vue の .zero / egui の range_rect。可動域が有限なときだけ枠で見せる
            void PaintRange(Painter2D painter, TweeqTheme theme)
            {
                Vector2 min = _state.Min;
                Vector2 max = _state.Max;
                if (!IsFinite(min.x) || !IsFinite(min.y) || !IsFinite(max.x) || !IsFinite(max.y))
                {
                    return;
                }

                float scale = _state.GridScale;
                Vector2 center = _state.Center;
                float x0 = center.x + (min.x - _state.Value.x) * scale;
                float x1 = center.x + (max.x - _state.Value.x) * scale;

                // 値空間は上向き正（Unity 合わせの逸脱）なので、min.y（下端）はパネルでは center より下
                float y0 = center.y + (_state.Value.y - min.y) * scale;
                float y1 = center.y + (_state.Value.y - max.y) * scale;

                painter.strokeColor = theme.Accent;
                painter.lineWidth = RANGE_LINE_WIDTH;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x0, y0));
                painter.LineTo(new Vector2(x1, y0));
                painter.LineTo(new Vector2(x1, y1));
                painter.LineTo(new Vector2(x0, y1));
                painter.ClosePath();
                painter.Stroke();
            }

            static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }

            // 負の値でも [0, length) に収める（Rust の rem_euclid 相当）
            static float Repeat(float value, float length)
            {
                if (length <= 0f)
                {
                    return 0f;
                }

                float result = value - Mathf.Floor(value / length) * length;
                return result < 0f ? 0f : result;
            }

            #endregion

            #region Value label

            /// <summary>
            /// 表示が変わったときだけ文字列を作り直す値ラベル。
            /// ドラッグ中は毎フレーム Sync が走るので、ここでケチらないと GC が回る。
            /// </summary>
            sealed class ValueLabel
            {
                readonly UILabel _label;

                double _key;
                int _precision = -1;
                bool _hasKey;

                public ValueLabel()
                {
                    _label = new UILabel(string.Empty) { pickingMode = PickingMode.Ignore };
                    _label.style.fontSize = LABEL_FONT_SIZE;
                    _label.style.unityTextAlign = TextAnchor.MiddleRight;
                    _label.style.minWidth = LABEL_VALUE_MIN_WIDTH;
                    _label.style.marginLeft = 0f;
                    _label.style.marginRight = 0f;
                    _label.style.marginTop = 0f;
                    _label.style.marginBottom = 0f;
                }

                public UILabel Element => _label;

                public void Sync(double value, int precision)
                {
                    bool cacheable = TryGetKey(value, precision, out double key);
                    if (cacheable && _hasKey && _precision == precision
                        && TweeqFormat.SameValueBits(_key, key))
                    {
                        return;
                    }

                    _label.text = TweeqFormat.Format(value, precision, true);

                    // 丸め境界付近や非有限値はキー化できないので、次フレームも作り直させる
                    _hasKey = cacheable;
                    _key = key;
                    _precision = precision;
                }

                // 表示は precision 桁で丸まるので、その粒度で一致していれば文字列は同じになる
                static bool TryGetKey(double value, int precision, out double key)
                {
                    key = 0.0;
                    if (!TweeqMath.IsFinite(value))
                    {
                        return false;
                    }

                    double scale = Math.Pow(10.0, TweeqFormat.ClampDigits(precision));
                    double scaled = value * scale;
                    key = Math.Round(scaled, MidpointRounding.AwayFromZero);
                    return Math.Abs(scaled - key) < 0.5 - 1e-6;
                }
            }

            #endregion
        }

        #endregion
    }
}
