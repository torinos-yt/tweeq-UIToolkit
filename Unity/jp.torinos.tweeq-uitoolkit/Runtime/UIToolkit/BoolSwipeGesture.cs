using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// CheckboxInput / SwitchInput が共有するスワイプトグルの状態機械（仕様「共通: スワイプトグル」）。
    ///
    /// 継承ではなくコンポジションを選んだ理由: 2 つのウィジェットで共通なのは
    /// 「ポインタ／キーをどう bool の変化へ翻訳するか」だけで、見た目（角丸の箱とピル）と
    /// 子要素の構成は全く別物。共通の基底 VisualElement を作ると、レイアウトを持たない
    /// 抽象クラスに描画フックだけが並ぶことになり、どちらのウィジェットも読みにくくなる。
    /// </summary>
    sealed class BoolSwipeGesture
    {
        #region Constants

        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // 閾値に届かなくても 0.2s 長押しでドラッグへ入る（仕様「共通」）
        const long HOLD_DRAG_DELAY_MS = 200;

        // プレビュー値のデッドゾーン。Vue の tweakThreshold と同値で、ポインタ種別によらず 3px
        const float PREVIEW_DEAD_ZONE = 3f;

        #endregion

        #region Fields

        readonly VisualElement _owner;

        int _pointerId = PointerId.invalidPointerId;
        bool _pointerDown;
        bool _dragging;
        float _dragThreshold = MOUSE_DRAG_THRESHOLD;
        Vector2 _pressPosition;
        Vector2 _pointerPosition;
        bool _valueOnDragStart;
        bool _previewValue;
        IVisualElementScheduledItem _holdItem;

        #endregion

        #region Public API

        /// <summary>現在値の取得元。未設定なら false 扱い。</summary>
        public Func<bool> ValueGetter { get; set; }

        /// <summary>プレビュー値が変わるたびに呼ばれる（＝ドラッグ中も即反映する。仕様「共通」）。</summary>
        public Action<bool> ValueChanged { get; set; }

        /// <summary>クリック／リリース／キー入力ごとに 1 回だけ呼ばれる。</summary>
        public Action<bool> Confirmed { get; set; }

        /// <summary>押下・ドラッグ状態が変わったときの再描画フック。</summary>
        public Action StateChanged { get; set; }

        /// <summary>操作を受け付けないか。SwitchInput は常に false（仕様 §2: disabled 無し）。</summary>
        public bool Disabled { get; set; }

        /// <summary>閾値を越えてドラッグ中か。</summary>
        public bool Dragging => _dragging;

        /// <summary>押下中か（閾値未満を含む）。</summary>
        public bool Pressed => _pointerDown;

        /// <summary>ドラッグ中のプレビュー値。<see cref="Dragging"/> が false のときは意味を持たない。</summary>
        public bool PreviewValue => _previewValue;

        public BoolSwipeGesture(VisualElement owner)
        {
            _owner = owner;
            if (_owner == null)
            {
                return;
            }

            _owner.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _owner.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _owner.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _owner.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            _owner.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _owner.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        /// <summary>
        /// 進行中の操作を打ち切る。Confirmed は発火しない。
        /// Disabled 化のように「離す手段が無くなる」状況で呼ぶ。
        /// </summary>
        public void Cancel()
        {
            if (!_pointerDown && !_dragging)
            {
                return;
            }

            int pointerId = _pointerId;
            ResetState();
            ReleasePointerSafely(pointerId);
            StateChanged?.Invoke();
        }

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _pointerDown || Disabled || _owner == null)
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
            _pointerPosition = _pressPosition;

            // キーボードショートカット（T/F/Space...）を受け取るためにフォーカスを取る。
            // Vue も onClick / onDragEnd で input.focus() している
            _owner.Focus();

            if (_owner.panel != null)
            {
                _owner.CapturePointer(_pointerId);
                _holdItem = _owner.schedule.Execute(OnHoldElapsed).StartingIn(HOLD_DRAG_DELAY_MS);
            }

            evt.StopPropagation();
            StateChanged?.Invoke();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            _pointerPosition = LocalPosition(evt);

            if (!_dragging)
            {
                if (Vector2.Distance(_pointerPosition, _pressPosition) < _dragThreshold)
                {
                    return;
                }

                BeginDrag();
                evt.StopPropagation();
                return;
            }

            UpdatePreview();
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

            // 先に状態を落としてから解放する。解放で飛んでくる PointerCaptureOutEvent を空振りさせるため
            ResetState();
            ReleasePointerSafely(pointerId);
            StateChanged?.Invoke();

            if (wasDragging)
            {
                // ドラッグ中の値はプレビューで反映済みなので、ここは確定だけ
                Confirmed?.Invoke(CurrentValue);
            }
            else
            {
                // 閾値未満で離した＝クリック → 値を反転
                bool next = !CurrentValue;
                ValueChanged?.Invoke(next);
                Confirmed?.Invoke(next);
            }

            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_pointerDown && !_dragging)
            {
                return;
            }

            bool wasDragging = _dragging;
            ResetState();
            StateChanged?.Invoke();

            // キャプチャを奪われてもドラッグ中の値は既に外へ通知済み。
            // 確定を飛ばすと「変わったのにコミットされない」状態が残るので、ここでも確定させる
            if (wasDragging)
            {
                Confirmed?.Invoke(CurrentValue);
            }
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // 要素ごと消えるので聞き手も居ない。状態だけ落とす（Confirmed は発火しない）
            ResetState();
        }

        void OnHoldElapsed()
        {
            if (!_pointerDown || _dragging || Disabled)
            {
                return;
            }

            BeginDrag();
        }

        #endregion

        #region Keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || Disabled)
            {
                return;
            }

            if (!TryResolveKey(evt.keyCode, CurrentValue, out bool next))
            {
                return;
            }

            // 同じキーに割り当てられたアプリ側ショートカットを二重に叩かない（Vue と同じ判断）
            evt.StopPropagation();

            ValueChanged?.Invoke(next);
            Confirmed?.Invoke(next);
        }

        // 仕様「共通」: T/Y/1/P→true、F/N/0/M→false、Space→トグル
        static bool TryResolveKey(KeyCode keyCode, bool current, out bool next)
        {
            switch (keyCode)
            {
                case KeyCode.T:
                case KeyCode.Y:
                case KeyCode.P:
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    next = true;
                    return true;

                case KeyCode.F:
                case KeyCode.N:
                case KeyCode.M:
                case KeyCode.Alpha0:
                case KeyCode.Keypad0:
                    next = false;
                    return true;

                case KeyCode.Space:
                    next = !current;
                    return true;
            }

            next = current;
            return false;
        }

        #endregion

        #region Drag session

        void BeginDrag()
        {
            _dragging = true;
            _valueOnDragStart = CurrentValue;
            StopHoldTimer();

            // 開始直後は dx≈0 ＝ デッドゾーン内なので、プレビューは必ず開始値の反転になる。
            // Vue の tweakingValue（null → 値）の watch と同じタイミングで最初の変更通知が出る
            UpdatePreview();
            StateChanged?.Invoke();
        }

        void UpdatePreview()
        {
            float dx = _pointerPosition.x - _pressPosition.x;

            bool preview = Mathf.Abs(dx) <= PREVIEW_DEAD_ZONE
                ? !_valueOnDragStart
                : dx > 0f;

            _previewValue = preview;

            // 変化したときだけ通知する（Vue は tweakingValue の watch なので同じ挙動）。
            // 前回のプレビューではなく実値と比べるのは、外部が値を拒否した場合にも
            // 「見えている値＝通知した値」を保つため
            if (preview == CurrentValue)
            {
                StateChanged?.Invoke();
                return;
            }

            ValueChanged?.Invoke(preview);
            StateChanged?.Invoke();
        }

        void ResetState()
        {
            _pointerDown = false;
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            StopHoldTimer();
        }

        void StopHoldTimer()
        {
            if (_holdItem == null)
            {
                return;
            }

            _holdItem.Pause();
            _holdItem = null;
        }

        void ReleasePointerSafely(int pointerId)
        {
            if (_owner == null || _owner.panel == null || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (_owner.HasPointerCapture(pointerId))
            {
                _owner.ReleasePointer(pointerId);
            }
        }

        bool CurrentValue => ValueGetter != null && ValueGetter();

        // キャプチャ中も座標系がぶれないよう、パネル座標からローカルへ変換する
        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            Vector2 panelPosition = new Vector2(position.x, position.y);
            return _owner == null ? panelPosition : _owner.WorldToLocal(panelPosition);
        }

        #endregion
    }
}
