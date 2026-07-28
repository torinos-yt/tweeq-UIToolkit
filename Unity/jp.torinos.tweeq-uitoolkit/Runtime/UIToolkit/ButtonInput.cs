using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// The class has a string Label property, so the Label type is referenced under an alias
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A button for actions (spec §3). It holds no value and only fires <see cref="Clicked"/>.
    /// It implements <see cref="ITweeqInputBox"/> to participate in corner-radius fusion.
    /// </summary>
    [UxmlElement]
    public partial class ButtonInput : VisualElement, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Actual dimensions converted from the Vue original's padding-inline .75em / padding-right .6em at rem12
        const float LABEL_PADDING = 9f;
        const float CHEVRON_PADDING = 7.2f;

        // narrow discards min-width to make it "hug the glyph closely" (spec §3).
        // In CSS, .narrow comes after :has(.label), so the 1px value wins even when a label is present
        const float NARROW_PADDING = 1f;

        // mdi:chevron-down only fills the center half of its 18px icon box, so
        // the occupied area is the actual width (18 * 0.5) rather than the full box
        const float CHEVRON_ZONE = 9f;
        const float CHEVRON_OPACITY = 0.6f;
        const float CHEVRON_HALF_WIDTH = 4.5f;
        const float CHEVRON_HALF_HEIGHT = 2.5f;

        const float DISABLED_OPACITY = 0.4f;
        const float FOCUS_RING_WIDTH = 1f;

        // Vue: animation blink .5s infinite alternate → a 1.0s period for a round trip
        const long BLINK_PERIOD_MS = 1000;

        // Vue: animation tq-input-button-flash .6s ease-in-out 2
        const long FLASH_CYCLE_MS = 600;
        const long FLASH_DURATION_MS = FLASH_CYCLE_MS * 2;
        const float FLASH_SCALE = 1.06f;
        const float FLASH_RING_WIDTH = 2f;
        const float FLASH_GLOW_WIDTH = 4f;
        const float FLASH_GLOW_ALPHA = 0.35f;

        // Extends beyond the root by just enough to fit the box-shadow glow (0 0 10px 1px)
        const float FLASH_MARGIN = 8f;

        // The minimum tick for schedule. Equivalent to 60fps, which looks smooth enough
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

        // Colors per state. Redrawing every frame would be wasteful, so these are only built when Theme / Subtle changes
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

        /// <summary>Fires on click, Enter, or Space.</summary>
        public event Action Clicked;

        /// <summary>The string displayed inside the button. Overflow is collapsed with an ellipsis.</summary>
        // The UXML side matches the Vue original's prop name (text). The C# side stays as Label, in line with the other Inputs
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

        /// <summary>Shows a downward-pointing triangle on the right edge. Enabling it left-aligns the content (spec §3).</summary>
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

        /// <summary>Blinks the background back and forth every 0.5s.</summary>
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

        /// <summary>A subdued fill. Rest is equivalent to Neutral, but hover jumps over to the Accent side (spec §3).</summary>
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

        /// <summary>Discards the square minimum width and packs it tighter horizontally.</summary>
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

        /// <summary>Non-interactive state. Events don't fire and Blink also stops (spec §3).</summary>
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

        /// <summary>The color theme. Falls back to Dark() if null is passed.</summary>
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

        /// <summary>Position within a horizontal group.</summary>
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

        /// <summary>Position within a vertical group.</summary>
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
        /// A one-shot attention-grabbing animation (spec §3). 0.6s ease-in-out ×2 cycles.
        /// Calling it again while it's playing restarts it from the beginning.
        /// </summary>
        public void Flash()
        {
            StopFlash();

            if (this.panel == null)
            {
                // The scheduler won't run, so no visual effect can be shown. Just reset the state plainly
                ApplyFlashVisual(0f);
                return;
            }

            _flashStartMs = -1L;
            _flashItem = this.schedule.Execute(OnFlashTick).Every(TICK_MS);
        }

        /// <summary>
        /// A programmatic click. Does nothing when Disabled.
        /// Panel-independent, so it can also be used to fire from tests.
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

            // The Flash ring/glow is drawn outside the root, so this must not be set to Hidden
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

            // Ellipsis on overflow. UI Toolkit only shows the ellipsis when all three properties are set together
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

            // margin-left auto pushes it to the right edge (= the remaining content becomes left-aligned)
            _chevronElement.style.marginLeft = StyleKeyword.Auto;
            _chevronElement.style.display = DisplayStyle.None;
            _chevronElement.generateVisualContent += OnGenerateChevron;
            this.hierarchy.Add(_chevronElement);

            // Using the root's border for the focus ring would shift the content by 1px, so it's split into a separate layer.
            // A filled button gets a double ring of "inner 1px Input + outer 1px Accent" (the Vue original's fill-focus-style)
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

            // Spec §3: hover-related 0.15s. UI Toolkit has no cubic-bezier(0.4,0,0.2,1), so
            // it's approximated with EaseInOutCubic (the same call made for NumberInput).
            // transition isn't inherited, so the text color transition is applied individually to the label
            ApplyTransition(
                this, _theme.HoverTransitionDuration, EasingMode.EaseInOutCubic, "background-color");
            ApplyTransition(
                _label, _theme.HoverTransitionDuration, EasingMode.EaseInOutCubic, "color");

            SetBorderColor(_focusInner, _theme.Input);
            SetBorderColor(_focusOuter, _theme.Accent);

            TweeqFonts.Apply(_label, _theme.FontUi);
        }

        void ApplyContentLayout()
        {
            bool hasLabel = !string.IsNullOrEmpty(_labelText);

            _label.text = _labelText;
            _label.style.display = hasLabel ? DisplayStyle.Flex : DisplayStyle.None;
            _chevronElement.style.display = _chevron ? DisplayStyle.Flex : DisplayStyle.None;

            // The chevron is fixed to the right edge, so the remaining content becomes left-aligned
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

        // The corner-radius table from spec §1. Values for both axes are combined with OR (if either says "flatten", it's flattened)
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

            // The outer ring sits 1px further out, so its radius is also grown by 1px to keep the same appearance
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

            // There's no Neutral token, so Subtle's rest state is approximated with Input (Unity decision item 5).
            // The Vue original's --tq-color-neutral is an achromatic color with a bit more "presence" than input,
            // but Input is the closest match among the current tokens, so it's adopted as-is
            _restBackground = _subtle ? _theme.Input : _theme.Accent;

            // Even when Subtle, hover uses AccentHover rather than Neutral hover (an explicit item from spec §3)
            _hoverBackground = _theme.AccentHover;

            _restText = TweeqTheme.ContrastText(_restBackground);
            _hoverText = TweeqTheme.ContrastText(_hoverBackground);

            // The Vue original's --bg / --bg-blink. Subtle is neutral↔neutral-hover, so it's approximated with Input↔InputHover
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

            // While blinking, the scheduler writes the background every frame, so it isn't touched here
            if (_blinkItem == null)
            {
                this.style.backgroundColor = CurrentBackground;
            }

            Color text = _blinkItem != null ? _restText : CurrentText;
            _label.style.color = text;

            bool ringVisible = _focused && !_disabled;

            // Subtle's fill is pale, so only the outer ring is shown (an override of the Vue original's --focus-ring)
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

            // The background is rewritten every frame, so leaving the 0.15s transition active would make it lag behind
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

            // CSS's alternate goes back and forth. The triangle wave is passed through smoothstep to make the turnaround ease-like
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
                // TimerState.start advances on every tick, so the start time is tracked manually here
                _flashStartMs = state.now;
            }

            long elapsed = state.now - _flashStartMs;
            if (elapsed >= FLASH_DURATION_MS)
            {
                StopFlash();
                return;
            }

            // Off at 0% / 100%, maximum at 50%. smoothstep is applied for an ease-in-out-like feel
            float phase = (elapsed % FLASH_CYCLE_MS) / (float)FLASH_CYCLE_MS;
            float triangle = 1f - Mathf.Abs(phase * 2f - 1f);
            ApplyFlashVisual(triangle * triangle * (3f - 2f * triangle));
        }

        void ApplyFlashVisual(float intensity)
        {
            _flashIntensity = Mathf.Clamp01(intensity);

            float scale = Mathf.Lerp(1f, FLASH_SCALE, _flashIntensity);

            // Scale's constructor takes a Vector3. Passing a Vector2 would collapse z to 0
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

            // If the pressed finger slides outside and is released there, the click doesn't count
            Vector3 position = evt.position;
            bool inside = this.ContainsPoint(this.WorldToLocal(new Vector2(position.x, position.y)));

            // Focus gained via pointer is released the moment the pointer is lifted. Leaving it would let a later
            // Enter/Space misfire (the same intent as the Vue original's @mousedown.prevent). Because UI Toolkit's
            // focus change already happens during PreDispatch, the handler can't tell "did it have focus before the
            // press," so it's unconditionally cleared here. This only diverges from the Vue original's behavior
            // when clicking while already Tab-focused
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
            // If Blink was turned on while outside a panel, the scheduler can only start running now
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

            // Independent of any icon font (Unity decision item 1). The downward triangle is drawn as a shape
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

            // UI Toolkit has no box-shadow, so it's approximated with two rings around the root's perimeter
            // (spec §3's "2px accent + glow"). Inner = solid ring, outer = faint glow
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

        // Painter2D has no rounded-rect primitive, so it's traced using ArcTo
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
