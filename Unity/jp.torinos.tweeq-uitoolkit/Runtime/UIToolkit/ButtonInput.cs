using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// クラス側に string Label プロパティがあるため、Label 型は別名で参照する
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// アクション用ボタン（仕様 §3）。値を持たず <see cref="Clicked"/> だけを発火する。
    /// 角丸融合に参加するため <see cref="ITweeqInputBox"/> を実装する。
    /// </summary>
    [UxmlElement]
    public partial class ButtonInput : VisualElement, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Vue の padding-inline .75em / padding-right .6em を rem12 換算した実寸
        const float LABEL_PADDING = 9f;
        const float CHEVRON_PADDING = 7.2f;

        // narrow は min-width を捨てて「グリフにほぼ密着」させる（仕様 §3）。
        // CSS では .narrow が :has(.label) より後に来るのでラベル有りでも 1px が勝つ
        const float NARROW_PADDING = 1f;

        // mdi:chevron-down は 18px アイコン枠の中央半分しか埋めないので、
        // 枠ではなく実幅（18 * 0.5）を占有域にする
        const float CHEVRON_ZONE = 9f;
        const float CHEVRON_OPACITY = 0.6f;
        const float CHEVRON_HALF_WIDTH = 4.5f;
        const float CHEVRON_HALF_HEIGHT = 2.5f;

        const float DISABLED_OPACITY = 0.4f;
        const float FOCUS_RING_WIDTH = 1f;

        // Vue: animation blink .5s infinite alternate → 往復で 1.0s 周期
        const long BLINK_PERIOD_MS = 1000;

        // Vue: animation tq-input-button-flash .6s ease-in-out 2
        const long FLASH_CYCLE_MS = 600;
        const long FLASH_DURATION_MS = FLASH_CYCLE_MS * 2;
        const float FLASH_SCALE = 1.06f;
        const float FLASH_RING_WIDTH = 2f;
        const float FLASH_GLOW_WIDTH = 4f;
        const float FLASH_GLOW_ALPHA = 0.35f;

        // box-shadow の glow（0 0 10px 1px）が収まるだけルート外へはみ出させる
        const float FLASH_MARGIN = 8f;

        // schedule の最小刻み。60fps 相当で十分滑らかに見える
        const long TICK_MS = 16;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        string _labelText = string.Empty;
        bool _chevron;
        bool _blink;
        bool _subtle;
        bool _narrow;
        bool _disabled;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        readonly UILabel _label;
        readonly VisualElement _chevronElement;
        readonly VisualElement _flashLayer;
        readonly VisualElement _focusOuter;
        readonly VisualElement _focusInner;

        // 状態ごとの配色。毎フレーム引き直すのは無駄なので Theme / Subtle 変更時にだけ作る
        Color _restBackground;
        Color _hoverBackground;
        Color _restText;
        Color _hoverText;
        Color _blinkFrom;
        Color _blinkTo;

        bool _hovered;
        bool _focused;
        int _pointerId = PointerId.invalidPointerId;

        IVisualElementScheduledItem _blinkItem;
        IVisualElementScheduledItem _flashItem;
        long _flashStartMs = -1L;
        float _flashIntensity;

        #endregion

        #region Public API

        /// <summary>クリック・Enter・Space で発火する。</summary>
        public event Action Clicked;

        /// <summary>ボタン内に表示する文字列。溢れた分は ellipsis で畳む。</summary>
        // UXML 側は Vue の prop 名（text）に合わせる。C# 側は他 Input と揃えた Label のまま
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

        /// <summary>右端に下向き三角を出す。付けると中身は左詰めになる（仕様 §3）。</summary>
        [UxmlAttribute("chevron")]
        public bool Chevron
        {
            get => _chevron;
            set
            {
                if (_chevron == value)
                {
                    return;
                }

                _chevron = value;
                ApplyContentLayout();
            }
        }

        /// <summary>背景を 0.5s 交互に点滅させる。</summary>
        [UxmlAttribute("blink")]
        public bool Blink
        {
            get => _blink;
            set
            {
                if (_blink == value)
                {
                    return;
                }

                _blink = value;
                RefreshBlink();
            }
        }

        /// <summary>控えめな塗り。rest は Neutral 相当だが hover は Accent 側へ跳ねる（仕様 §3）。</summary>
        [UxmlAttribute("subtle")]
        public bool Subtle
        {
            get => _subtle;
            set
            {
                if (_subtle == value)
                {
                    return;
                }

                _subtle = value;
                RefreshPalette();
                Refresh();
                RefreshBlink();
            }
        }

        /// <summary>正方形の最小幅を捨てて横方向に詰める。</summary>
        [UxmlAttribute("narrow")]
        public bool Narrow
        {
            get => _narrow;
            set
            {
                if (_narrow == value)
                {
                    return;
                }

                _narrow = value;
                ApplyContentLayout();
            }
        }

        /// <summary>操作不能状態。イベントは発火せず Blink も止まる（仕様 §3）。</summary>
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
                RefreshBlink();
            }
        }

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                RefreshPalette();
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

        /// <summary>
        /// 注意を引くための一発アニメーション（仕様 §3）。0.6s ease-in-out ×2 回。
        /// 再生中に呼び直しても頭から掛け直す。
        /// </summary>
        public void Flash()
        {
            StopFlash();

            if (this.panel == null)
            {
                // スケジューラが回らないので視覚効果は出せない。状態だけ素直に戻しておく
                ApplyFlashVisual(0f);
                return;
            }

            _flashStartMs = -1L;
            _flashItem = this.schedule.Execute(OnFlashTick).Every(TICK_MS);
        }

        /// <summary>
        /// プログラムからのクリック。Disabled のときは何もしない。
        /// パネル非依存なのでテストからの発火にも使える。
        /// </summary>
        public void PerformClick()
        {
            if (_disabled)
            {
                return;
            }

            Clicked?.Invoke();
        }

        #endregion

        #region Construction

        public ButtonInput()
        {
            this.AddToClassList("tweeq-button-input");

            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.justifyContent = Justify.Center;
            this.style.flexShrink = 0f;

            // Flash のリング／グローはルートの外側へ描くので、ここを Hidden にしてはいけない
            this.style.overflow = Overflow.Visible;

            _flashLayer = new VisualElement
            {
                name = "tweeq-button-flash",
                pickingMode = PickingMode.Ignore,
            };
            _flashLayer.style.position = Position.Absolute;
            _flashLayer.style.left = -FLASH_MARGIN;
            _flashLayer.style.top = -FLASH_MARGIN;
            _flashLayer.style.right = -FLASH_MARGIN;
            _flashLayer.style.bottom = -FLASH_MARGIN;
            _flashLayer.style.display = DisplayStyle.None;
            _flashLayer.generateVisualContent += OnGenerateFlash;
            this.hierarchy.Add(_flashLayer);

            _label = new UILabel(string.Empty) { pickingMode = PickingMode.Ignore };
            _label.style.marginLeft = 0f;
            _label.style.marginRight = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;
            _label.style.paddingLeft = 0f;
            _label.style.paddingRight = 0f;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;

            // 溢れたら ellipsis。UI Toolkit は 3 点セットで指定しないと省略記号が出ない
            _label.style.whiteSpace = WhiteSpace.NoWrap;
            _label.style.overflow = Overflow.Hidden;
            _label.style.textOverflow = TextOverflow.Ellipsis;
            _label.style.minWidth = 0f;
            _label.style.flexShrink = 1f;
            this.hierarchy.Add(_label);

            _chevronElement = new VisualElement
            {
                name = "tweeq-button-chevron",
                pickingMode = PickingMode.Ignore,
            };
            _chevronElement.style.width = CHEVRON_ZONE;
            _chevronElement.style.flexShrink = 0f;
            _chevronElement.style.alignSelf = Align.Stretch;

            // margin-left auto で右端へ寄せる（＝残りの中身が左詰めになる）
            _chevronElement.style.marginLeft = StyleKeyword.Auto;
            _chevronElement.style.display = DisplayStyle.None;
            _chevronElement.generateVisualContent += OnGenerateChevron;
            this.hierarchy.Add(_chevronElement);

            // フォーカスリングはルートの border を使うと中身が 1px ずれるので別レイヤに分ける。
            // 塗り潰しボタンは「内側 1px Input + 外側 1px Accent」の二重（Vue の fill-focus-style）
            _focusInner = CreateRing(0f);
            _focusOuter = CreateRing(-FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusInner);
            this.hierarchy.Add(_focusOuter);

            RefreshPalette();
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
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        public ButtonInput(string label)
            : this()
        {
            this.Label = label;
        }

        VisualElement CreateRing(float inset)
        {
            VisualElement ring = new VisualElement
            {
                name = "tweeq-button-focus-ring",
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
            ApplyCornerRadius();
            ApplyMinWidth();

            // 仕様 §3: hover 系 0.15s。UI Toolkit に cubic-bezier(0.4,0,0.2,1) が無いので
            // EaseInOutCubic で近似する（NumberInput と同じ判断）。
            // transition は継承されないので、文字色はラベル側へ個別に掛ける
            ApplyTransition(
                this, _theme.HoverTransitionDuration, EasingMode.EaseInOutCubic, "background-color");
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
            _chevronElement.style.display = _chevron ? DisplayStyle.Flex : DisplayStyle.None;

            // シェブロンは右端に固定されるので、残りの中身は左詰めになる
            this.style.justifyContent = _chevron ? Justify.FlexStart : Justify.Center;

            float left = hasLabel ? LABEL_PADDING : 0f;
            float right = _chevron ? CHEVRON_PADDING : left;

            if (_narrow)
            {
                left = NARROW_PADDING;
                right = NARROW_PADDING;
            }

            this.style.paddingLeft = left;
            this.style.paddingRight = right;
            ApplyMinWidth();
        }

        void ApplyMinWidth()
        {
            this.style.minWidth = _narrow ? 0f : _theme.InputHeight;
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

        #region Palette

        void RefreshPalette()
        {
            if (_theme == null)
            {
                return;
            }

            // Neutral トークンが無いため Subtle の rest は Input で近似する（Unity 決定事項 5）。
            // Vue の --tq-color-neutral は input より一段「存在感のある」無彩色だが、
            // 現行トークンで最も近いのが Input なのでそのまま採用している
            _restBackground = _subtle ? _theme.Input : _theme.Accent;

            // Subtle でも hover は Neutral hover ではなく AccentHover（仕様 §3 の明示事項）
            _hoverBackground = _theme.AccentHover;

            _restText = TweeqTheme.ContrastText(_restBackground);
            _hoverText = TweeqTheme.ContrastText(_hoverBackground);

            // Vue の --bg / --bg-blink。Subtle は neutral↔neutral-hover なので Input↔InputHover で近似する
            _blinkFrom = _restBackground;
            _blinkTo = _subtle ? _theme.InputHover : _theme.AccentHover;
        }

        Color CurrentBackground => _hovered && !_disabled ? _hoverBackground : _restBackground;

        Color CurrentText => _hovered && !_disabled ? _hoverText : _restText;

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            // 点滅中の背景はスケジューラが毎フレーム書くので、ここでは触らない
            if (_blinkItem == null)
            {
                this.style.backgroundColor = CurrentBackground;
            }

            Color text = _blinkItem != null ? _restText : CurrentText;
            _label.style.color = text;

            bool ringVisible = _focused && !_disabled;

            // Subtle は塗りが淡いので外周リングだけ（Vue の --focus-ring 上書き）
            _focusInner.style.display = ringVisible && !_subtle
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _focusOuter.style.display = ringVisible ? DisplayStyle.Flex : DisplayStyle.None;

            _chevronElement.MarkDirtyRepaint();
        }

        #endregion

        #region Blink

        void RefreshBlink()
        {
            bool active = _blink && !_disabled;

            if (!active)
            {
                StopBlink();
                Refresh();
                return;
            }

            if (_blinkItem != null || this.panel == null)
            {
                return;
            }

            // 毎フレーム背景を書き換えるので、0.15s の遷移が残っていると追従が鈍る
            ApplyTransition(this, 0f, EasingMode.EaseInOutCubic, "background-color");
            _blinkItem = this.schedule.Execute(OnBlinkTick).Every(TICK_MS);
        }

        void StopBlink()
        {
            if (_blinkItem == null)
            {
                return;
            }

            _blinkItem.Pause();
            _blinkItem = null;

            ApplyTransition(
                this,
                _theme != null ? _theme.HoverTransitionDuration : 0f,
                EasingMode.EaseInOutCubic,
                "background-color");
        }

        void OnBlinkTick(TimerState state)
        {
            if (!_blink || _disabled)
            {
                StopBlink();
                Refresh();
                return;
            }

            // CSS の alternate は往復。三角波を smoothstep に通して ease 相当の折り返しにする
            float phase = (state.now % BLINK_PERIOD_MS) / (float)BLINK_PERIOD_MS;
            float triangle = 1f - Mathf.Abs(phase * 2f - 1f);
            float weight = triangle * triangle * (3f - 2f * triangle);

            this.style.backgroundColor = Color.Lerp(_blinkFrom, _blinkTo, weight);
        }

        #endregion

        #region Flash

        void StopFlash()
        {
            if (_flashItem != null)
            {
                _flashItem.Pause();
                _flashItem = null;
            }

            _flashStartMs = -1L;
            ApplyFlashVisual(0f);
        }

        void OnFlashTick(TimerState state)
        {
            if (_flashStartMs < 0L)
            {
                // TimerState.start はティックごとに進むので、開始時刻は自前で覚える
                _flashStartMs = state.now;
            }

            long elapsed = state.now - _flashStartMs;
            if (elapsed >= FLASH_DURATION_MS)
            {
                StopFlash();
                return;
            }

            // 0% / 100% で無効、50% で最大。ease-in-out 相当に smoothstep を掛ける
            float phase = (elapsed % FLASH_CYCLE_MS) / (float)FLASH_CYCLE_MS;
            float triangle = 1f - Mathf.Abs(phase * 2f - 1f);
            ApplyFlashVisual(triangle * triangle * (3f - 2f * triangle));
        }

        void ApplyFlashVisual(float intensity)
        {
            _flashIntensity = Mathf.Clamp01(intensity);

            float scale = Mathf.Lerp(1f, FLASH_SCALE, _flashIntensity);

            // Scale のコンストラクタは Vector3 を取る。Vector2 を渡すと z が 0 に潰れる
            this.style.scale = new Scale(new Vector3(scale, scale, 1f));

            _flashLayer.style.display = _flashIntensity > 0f
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _flashLayer.MarkDirtyRepaint();
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

            // ポインタで得たフォーカスは離した時点で返す。残すと後の Enter/Space が誤爆する
            // （Vue の @mousedown.prevent と同じ意図）。UI Toolkit のフォーカス移動は
            // PreDispatch で済んでいるため「押す前に持っていたか」は handler からは判別できず、
            // 一律で外している。Tab フォーカス中にクリックした場合だけ Vue と挙動が異なる
            if (_focused)
            {
                this.Blur();
            }

            if (inside)
            {
                Clicked?.Invoke();
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

            Clicked?.Invoke();
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

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // パネル外で Blink を立てられていた場合、ここで初めてスケジューラが回せる
            RefreshBlink();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            StopBlink();
            StopFlash();
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

        void OnGenerateChevron(MeshGenerationContext context)
        {
            Painter2D painter = context?.painter2D;
            if (painter == null || _theme == null)
            {
                return;
            }

            Rect rect = _chevronElement.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // アイコンフォント非依存（Unity 決定事項 1）。下向き三角を図形で描く
            Color color = _blinkItem != null ? _restText : CurrentText;
            color.a *= CHEVRON_OPACITY;

            float centerX = rect.width * 0.5f;
            float centerY = rect.height * 0.5f;

            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX - CHEVRON_HALF_WIDTH, centerY - CHEVRON_HALF_HEIGHT));
            painter.LineTo(new Vector2(centerX + CHEVRON_HALF_WIDTH, centerY - CHEVRON_HALF_HEIGHT));
            painter.LineTo(new Vector2(centerX, centerY + CHEVRON_HALF_HEIGHT));
            painter.ClosePath();
            painter.Fill();
        }

        void OnGenerateFlash(MeshGenerationContext context)
        {
            Painter2D painter = context?.painter2D;
            if (painter == null || _theme == null || _flashIntensity <= 0f)
            {
                return;
            }

            Rect rect = _flashLayer.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // UI Toolkit に box-shadow が無いので、ルート外周へのリング 2 本で近似する
            // （仕様 §3 の「2px accent + glow」）。内側＝実線リング、外側＝薄いグロー
            float radius = _theme.InputRadius;
            Rect ring = new Rect(
                FLASH_MARGIN - FLASH_RING_WIDTH * 0.5f,
                FLASH_MARGIN - FLASH_RING_WIDTH * 0.5f,
                rect.width - (FLASH_MARGIN - FLASH_RING_WIDTH * 0.5f) * 2f,
                rect.height - (FLASH_MARGIN - FLASH_RING_WIDTH * 0.5f) * 2f);

            Color glow = _theme.Accent;
            glow.a *= FLASH_GLOW_ALPHA * _flashIntensity;
            painter.strokeColor = glow;
            painter.lineWidth = FLASH_GLOW_WIDTH;
            TraceRoundedRect(painter, Inflate(ring, FLASH_GLOW_WIDTH * 0.5f), radius + FLASH_GLOW_WIDTH);
            painter.Stroke();

            Color solid = _theme.Accent;
            solid.a *= _flashIntensity;
            painter.strokeColor = solid;
            painter.lineWidth = FLASH_RING_WIDTH;
            TraceRoundedRect(painter, ring, radius + FLASH_RING_WIDTH * 0.5f);
            painter.Stroke();
        }

        static Rect Inflate(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
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
