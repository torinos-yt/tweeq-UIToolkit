using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CorePlacement = Tweeq.Core.PopoverPlacement;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// The single tooltip instance that exists per panel. It swaps and reuses its anchor, so the number
    /// of popovers doesn't grow with the number of elements (the same structure as the Vue original's
    /// TooltipRoot). Normally used via <see cref="TweeqTooltip"/>; touched directly only for theme swaps.
    /// </summary>
    public sealed class TweeqTooltipRoot
    {
        #region Constants

        /// <summary>The delay before showing (ms).</summary>
        public const long SHOW_DELAY_MS = 200L;

        /// <summary>
        /// The delay before hiding (ms). The key point is that even at 0 it still waits for "the next frame",
        /// which avoids closing when a leave → enter transfer happens back-to-back.
        /// </summary>
        public const long HIDE_DELAY_MS = 0L;

        // Pill shape (Tooltip.vue's .TqTooltip)
        const float PILL_PADDING_VERTICAL = 2f;
        const float PILL_PADDING_HORIZONTAL = 6f;
        const float PILL_RADIUS = 9999f;
        const float FONT_SIZE = 11f;

        // .plain's max-width of 18em converted to px based on 0.9em (=11px)
        const float MAX_WIDTH = 198f;

        #endregion

        #region Fields

        static readonly Dictionary<IPanel, TweeqTooltipRoot> Roots =
            new Dictionary<IPanel, TweeqTooltipRoot>();

        readonly TweeqPopover _popover;
        readonly Label _label;
        readonly EventCallback<DetachFromPanelEvent> _onLayerDetached;

        TweeqTheme _theme = TweeqTheme.Dark();
        IPanel _panel;
        TweeqOverlayLayer _layer;

        // The anchor currently shown. Still null while the delay is pending
        VisualElement _reference;
        VisualElement _pendingShow;
        string _pendingText;
        VisualElement _pendingHide;

        // The delays reuse 2 scheduled items (allocating new ones every hover would produce garbage)
        IVisualElementScheduledItem _showTimer;
        IVisualElementScheduledItem _hideTimer;

        #endregion

        #region Public API

        /// <summary>
        /// Gets the instance tied to context's panel, creating one if none exists.
        /// Returns null if not attached to a panel, so the caller must always check.
        /// </summary>
        public static TweeqTooltipRoot GetOrCreate(VisualElement context)
        {
            if (context == null || context.panel == null)
            {
                return null;
            }

            IPanel panel = context.panel;
            if (Roots.TryGetValue(panel, out TweeqTooltipRoot existing))
            {
                existing.EnsureLayer(context);
                return existing;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(context);
            if (layer == null)
            {
                return null;
            }

            TweeqTooltipRoot root = new TweeqTooltipRoot(panel, layer);
            Roots.Add(panel, root);
            return root;
        }

        /// <summary>
        /// Reliably closes the tooltip for an element even when its panel is unknown (e.g. already detached).
        /// The number of live roots is normally 1, so the scan cost is negligible.
        /// </summary>
        public static void CloseAnyFor(VisualElement reference)
        {
            if (reference == null)
            {
                return;
            }

            foreach (KeyValuePair<IPanel, TweeqTooltipRoot> entry in Roots)
            {
                entry.Value.CloseNow(reference);
            }
        }

        /// <summary>The color theme. Falls back to Dark() if null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _popover.Theme = _theme;
                _label.style.color = _theme.Text;
                TweeqFonts.Apply(_label, _theme.FontUi);
            }
        }

        /// <summary>
        /// Shows the tooltip at reference. If already open, transfers with no delay;
        /// if closed, waits for <see cref="SHOW_DELAY_MS"/> first.
        /// </summary>
        public void Show(VisualElement reference, string text)
        {
            if (reference == null || string.IsNullOrEmpty(text) || _layer == null)
            {
                return;
            }

            _pendingShow = reference;
            _pendingText = text;
            _pendingHide = null;

            _hideTimer?.Pause();
            _showTimer?.Pause();

            if (_popover.IsOpen)
            {
                Apply();
                return;
            }

            EnsureTimers();
            _showTimer?.ExecuteLater(SHOW_DELAY_MS);
        }

        /// <summary>Retracts reference's tooltip (with a grace period until the next frame).</summary>
        public void Hide(VisualElement reference)
        {
            _showTimer?.Pause();

            if (_pendingShow == reference)
            {
                _pendingShow = null;
            }

            if (!_popover.IsOpen)
            {
                return;
            }

            _pendingHide = reference;

            EnsureTimers();
            if (_hideTimer == null)
            {
                // If the scheduler isn't available, give up the grace period and close immediately
                HideNow();
                return;
            }

            _hideTimer.ExecuteLater(HIDE_DELAY_MS);
        }

        /// <summary>Replaces the displayed text (does nothing if not currently shown).</summary>
        public void SetText(VisualElement reference, string text)
        {
            if (reference == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_pendingShow == reference)
            {
                _pendingText = text;
            }

            if (_popover.IsOpen && _reference == reference)
            {
                SetLabelText(text);
            }
        }

        /// <summary>Closes reference's tooltip with no grace period (e.g. when the element is destroyed).</summary>
        public void CloseNow(VisualElement reference)
        {
            if (_pendingShow == reference)
            {
                _pendingShow = null;
                _showTimer?.Pause();
            }

            if (_reference != reference)
            {
                return;
            }

            _pendingHide = null;
            _hideTimer?.Pause();
            _reference = null;
            _popover.Close();
        }

        #endregion

        #region Construction

        TweeqTooltipRoot(IPanel panel, TweeqOverlayLayer layer)
        {
            _panel = panel;

            _popover = new TweeqPopover
            {
                name = "tweeq-tooltip",

                // Dismissing on Escape or an outside click would only get in the way of focus operations, so turn it off
                LightDismiss = false,
                Placement = CorePlacement.Top,
                Theme = _theme,

                // If the tooltip steals the pointer, the element beneath it gets treated as "leave" and flickers
                pickingMode = PickingMode.Ignore,
            };
            _popover.Balloon.pickingMode = PickingMode.Ignore;
            _popover.Balloon.Radius = PILL_RADIUS;
            _popover.Balloon.PaddingVertical = PILL_PADDING_VERTICAL;
            _popover.Balloon.PaddingHorizontal = PILL_PADDING_HORIZONTAL;
            _popover.Balloon.contentContainer.pickingMode = PickingMode.Ignore;

            _label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            _label.style.fontSize = FONT_SIZE;
            _label.style.color = _theme.Text;
            _label.style.maxWidth = MAX_WIDTH;
            _label.style.whiteSpace = WhiteSpace.Normal;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.marginLeft = 0f;
            _label.style.marginRight = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;
            _popover.Add(_label);

            _onLayerDetached = OnLayerDetached;
            BindLayer(layer);
        }

        #endregion

        #region Layer binding

        void BindLayer(TweeqOverlayLayer layer)
        {
            if (_layer == layer)
            {
                return;
            }

            _layer?.UnregisterCallback(_onLayerDetached);
            _layer = layer;

            // When the layer is swapped, the scheduled items become invalid too, so let them be recreated next time
            _showTimer = null;
            _hideTimer = null;

            _layer?.RegisterCallback(_onLayerDetached);
        }

        // Reacquire the layer if it's gone, so this doesn't silently die when the UI side rewires its root
        void EnsureLayer(VisualElement context)
        {
            if (_layer != null && _layer.panel != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(context);
            if (layer != null)
            {
                BindLayer(layer);
            }
        }

        void OnLayerDetached(DetachFromPanelEvent evt)
        {
            _popover.Close();
            _reference = null;
            _pendingShow = null;
            _pendingHide = null;
            _showTimer = null;
            _hideTimer = null;

            if (_panel != null)
            {
                Roots.Remove(_panel);
                _panel = null;
            }
        }

        void EnsureTimers()
        {
            if (_layer == null || _layer.panel == null)
            {
                return;
            }

            if (_showTimer == null)
            {
                _showTimer = _layer.schedule.Execute(Apply);
                _showTimer.Pause();
            }

            if (_hideTimer == null)
            {
                _hideTimer = _layer.schedule.Execute(HideNow);
                _hideTimer.Pause();
            }
        }

        #endregion

        #region Show / hide

        void Apply()
        {
            _showTimer?.Pause();

            VisualElement reference = _pendingShow;
            if (reference == null || reference.panel == null)
            {
                return;
            }

            SetLabelText(_pendingText);
            _reference = reference;

            // If already open, Open acts as an anchor swap and doesn't redo the fade
            _popover.Open(reference);
        }

        void HideNow()
        {
            _hideTimer?.Pause();

            // If a transfer to a different element happened during the grace period, don't take that one's tooltip down with it
            if (_pendingHide != null && _reference != _pendingHide)
            {
                _pendingHide = null;
                return;
            }

            _pendingHide = null;
            _reference = null;
            _popover.Close();
        }

        // Avoid regenerating the Label's text when the string is unchanged
        void SetLabelText(string text)
        {
            if (_label.text == text)
            {
                return;
            }

            _label.text = text;
        }

        #endregion
    }
}
