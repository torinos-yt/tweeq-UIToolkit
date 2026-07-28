using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// スクラブ 1 フレーム分の移動量と修飾キー。
    /// </summary>
    /// <remarks>
    /// 移動量は前回配信時からの差分（target のローカル座標）。
    /// 修飾キーは Tweeq.Core の <c>GestureModifiers</c> へそのまま渡せる粒度で持つ。
    /// </remarks>
    public readonly struct ScrubUpdate
    {
        /// <summary>前回からの横移動量（px）。</summary>
        public readonly float DeltaX;

        /// <summary>前回からの縦移動量（px）。</summary>
        public readonly float DeltaY;

        /// <summary>Shift（fast）が押されているか。</summary>
        public readonly bool Shift;

        /// <summary>Alt（fine）が押されているか。</summary>
        public readonly bool Alt;

        /// <summary>全項目を指定して生成する。</summary>
        public ScrubUpdate(float deltaX, float deltaY, bool shift, bool alt)
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
            Shift = shift;
            Alt = alt;
        }
    }

    /// <summary>
    /// 「押す → 閾値を越えたらスクラブ、越えなければクリック」のポインタ配線だけを持つ Manipulator。
    /// </summary>
    /// <remarks>
    /// <para>
    /// NumberInput のポインタ配線を外部 asmdef から再利用できる形へ抽出したもの
    /// （ext-custom-widgets-spec.md EXT-01-B）。値の数学は一切持たないので、
    /// <see cref="ScrubUpdated"/> の移動量を <c>TweakGesture</c> 等へ利用者が渡して組み立てる。
    /// </para>
    /// <para>
    /// キャンセルの受け口は 2 つ（target の KeyDown(Escape) と PointerCaptureOut）。
    /// Escape を拾うには target 自身がフォーカスを持っている必要があるので、
    /// フォーカス移動は利用者側の責務。
    /// </para>
    /// </remarks>
    public sealed class TweeqScrubManipulator : PointerManipulator
    {
        #region Constants

        /// <summary>マウスでドラッグとみなす移動量（px）。</summary>
        public const float MOUSE_DRAG_THRESHOLD = 3f;

        /// <summary>タッチ・ペンでドラッグとみなす移動量（px）。指は素の手ぶれが大きいので緩い。</summary>
        public const float TOUCH_DRAG_THRESHOLD = 5f;

        #endregion

        #region Fields

        int _pointerId = PointerId.invalidPointerId;
        bool _pointerDown;
        bool _scrubbing;
        bool _cursorHidden;
        float _dragThreshold = MOUSE_DRAG_THRESHOLD;
        Vector2 _pressPosition;
        Vector2 _previousPosition;
        bool _shiftHeld;
        bool _altHeld;

        #endregion

        #region Public API

        /// <summary>スクラブ中に OS カーソルを隠すか（既定 false）。</summary>
        public bool HideCursorWhileScrubbing { get; set; }

        /// <summary>スクラブ中か。描画側が「掴んでいる」表現へ切り替えるために読む。</summary>
        public bool IsScrubbing
        {
            get { return _scrubbing; }
        }

        /// <summary>閾値を越えてスクラブに入った瞬間に 1 回。</summary>
        public event Action ScrubBegan;

        /// <summary>スクラブ中の移動ごと。<see cref="ScrubBegan"/> の直後は移動量 0 では飛ばない。</summary>
        public event Action<ScrubUpdate> ScrubUpdated;

        /// <summary>スクラブしたままボタンを離した（＝コミット）。</summary>
        public event Action ScrubEnded;

        /// <summary>Escape または PointerCaptureOut でスクラブが中断された。</summary>
        public event Action ScrubCancelled;

        /// <summary>閾値未満のまま離した（＝クリック）。</summary>
        public event Action Clicked;

        #endregion

        #region Manipulator

        protected override void RegisterCallbacksOnTarget()
        {
            this.target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.target.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.target.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            this.target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            this.target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            this.target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            this.target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            this.target.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            ResetSession();
        }

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _pointerDown || this.target == null)
            {
                return;
            }

            _pointerDown = true;
            _scrubbing = false;
            _pointerId = evt.pointerId;
            _dragThreshold = evt.pointerType == UnityEngine.UIElements.PointerType.mouse
                ? MOUSE_DRAG_THRESHOLD
                : TOUCH_DRAG_THRESHOLD;
            _pressPosition = LocalPosition(evt);
            _previousPosition = _pressPosition;
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            if (this.target.panel != null)
            {
                this.target.CapturePointer(_pointerId);
            }

            // ここでは StopPropagation しない。まだクリックかスクラブか決まっておらず、
            // 押下を潰すと利用者側のフォーカス・キャレット処理まで巻き添えにするため
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            Vector2 position = LocalPosition(evt);

            if (!_scrubbing)
            {
                if (Vector2.Distance(position, _pressPosition) < _dragThreshold)
                {
                    return;
                }

                BeginScrub(position);
                evt.StopPropagation();
                return;
            }

            Vector2 delta = position - _previousPosition;
            _previousPosition = position;

            Action<ScrubUpdate> updated = ScrubUpdated;
            if (updated != null)
            {
                updated(new ScrubUpdate(delta.x, delta.y, _shiftHeld, _altHeld));
            }

            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            int pointerId = _pointerId;

            // 解放より先に状態を畳む。ReleasePointer は PointerCaptureOut を呼び戻すので、
            // 畳んでおかないと Commit と Cancel が二重に飛ぶ
            ResetSession();
            ReleasePointerSafely(pointerId);

            if (wasScrubbing)
            {
                Action ended = ScrubEnded;
                if (ended != null)
                {
                    ended();
                }

                evt.StopPropagation();
                return;
            }

            Action clicked = Clicked;
            if (clicked != null)
            {
                clicked();
            }
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_pointerDown && !_scrubbing)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            ResetSession();

            if (!wasScrubbing)
            {
                return;
            }

            Action cancelled = ScrubCancelled;
            if (cancelled != null)
            {
                cancelled();
            }
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            if (!_pointerDown && !_scrubbing)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            int pointerId = _pointerId;

            ResetSession();
            ReleasePointerSafely(pointerId);

            if (wasScrubbing)
            {
                Action cancelled = ScrubCancelled;
                if (cancelled != null)
                {
                    cancelled();
                }
            }

            evt.StopPropagation();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            ResetSession();
        }

        #endregion

        #region Session

        void BeginScrub(Vector2 position)
        {
            _scrubbing = true;

            // 閾値を越えた地点を原点にする。越えるまでの移動量は値に乗せない
            _previousPosition = position;

            if (HideCursorWhileScrubbing)
            {
                HideCursor();
            }

            Action began = ScrubBegan;
            if (began != null)
            {
                began();
            }
        }

        void ResetSession()
        {
            _pointerDown = false;
            _scrubbing = false;
            _pointerId = PointerId.invalidPointerId;
            RestoreCursor();
        }

        void ReleasePointerSafely(int pointerId)
        {
            if (this.target == null || this.target.panel == null
                || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (this.target.HasPointerCapture(pointerId))
            {
                this.target.ReleasePointer(pointerId);
            }
        }

        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            Vector2 world = new Vector2(position.x, position.y);
            return this.target != null ? this.target.WorldToLocal(world) : world;
        }

        void HideCursor()
        {
            // panel が無い＝EditMode テストなどの論理層だけの実行。OS カーソルには触らない
            if (_cursorHidden || this.target == null || this.target.panel == null)
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
    }
}
