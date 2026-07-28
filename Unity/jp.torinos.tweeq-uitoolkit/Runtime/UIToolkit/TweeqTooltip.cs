using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Entry point for attaching a tooltip to an arbitrary element (equivalent to the Vue
    /// version's v-tooltip directive). The actual implementation is a single
    /// <see cref="TweeqTooltipRoot"/> per panel, shared by everyone.
    /// </summary>
    public static class TweeqTooltip
    {
        #region Fields

        // Map from element → subscription. Detach reliably removes entries, so this is the sole holder.
        static readonly Dictionary<VisualElement, TooltipBinding> Bindings =
            new Dictionary<VisualElement, TooltipBinding>();

        #endregion

        #region Public API

        /// <summary>
        /// Attaches a tooltip to target. If one is already attached, this acts as a text
        /// replacement (reflected immediately if currently shown).
        /// </summary>
        public static void Attach(VisualElement target, string text)
        {
            if (target == null)
            {
                return;
            }

            if (Bindings.TryGetValue(target, out TooltipBinding existing))
            {
                existing.SetText(text);
                return;
            }

            Bindings.Add(target, new TooltipBinding(target, text));
        }

        /// <summary>
        /// Removes the tooltip from target. If currently shown, it closes immediately, and
        /// all subscriptions are unregistered too, so no reference lingers.
        /// </summary>
        public static void Detach(VisualElement target)
        {
            if (target == null)
            {
                return;
            }

            if (!Bindings.TryGetValue(target, out TooltipBinding binding))
            {
                return;
            }

            Bindings.Remove(target);
            binding.Dispose();
        }

        /// <summary>Whether a tooltip is attached to target.</summary>
        public static bool IsAttached(VisualElement target)
        {
            return target != null && Bindings.ContainsKey(target);
        }

        /// <summary>
        /// Replaces the color scheme of the tooltip shared within context's panel.
        /// Since there's only a single instance, calling this once at app startup affects
        /// all elements.
        /// </summary>
        public static void SetTheme(VisualElement context, TweeqTheme theme)
        {
            TweeqTooltipRoot root = TweeqTooltipRoot.GetOrCreate(context);
            if (root == null)
            {
                return;
            }

            root.Theme = theme;
        }

        #endregion

        #region Binding

        /// <summary>The subscription for a single element. Delegates are allocated at construction and reused for register/unregister.</summary>
        sealed class TooltipBinding
        {
            #region Fields

            readonly VisualElement _target;
            readonly EventCallback<PointerEnterEvent> _onPointerEnter;
            readonly EventCallback<PointerLeaveEvent> _onPointerLeave;
            readonly EventCallback<FocusInEvent> _onFocusIn;
            readonly EventCallback<FocusOutEvent> _onFocusOut;
            readonly EventCallback<DetachFromPanelEvent> _onDetachFromPanel;

            string _text;

            #endregion

            #region Construction

            public TooltipBinding(VisualElement target, string text)
            {
                _target = target;
                _text = text;

                _onPointerEnter = OnPointerEnter;
                _onPointerLeave = OnPointerLeave;
                _onFocusIn = OnFocusIn;
                _onFocusOut = OnFocusOut;
                _onDetachFromPanel = OnDetachFromPanel;

                _target.RegisterCallback(_onPointerEnter);
                _target.RegisterCallback(_onPointerLeave);
                _target.RegisterCallback(_onFocusIn);
                _target.RegisterCallback(_onFocusOut);
                _target.RegisterCallback(_onDetachFromPanel);
            }

            #endregion

            #region API

            public void SetText(string text)
            {
                _text = text;
                TweeqTooltipRoot.GetOrCreate(_target)?.SetText(_target, _text);
            }

            public void Dispose()
            {
                _target.UnregisterCallback(_onPointerEnter);
                _target.UnregisterCallback(_onPointerLeave);
                _target.UnregisterCallback(_onFocusIn);
                _target.UnregisterCallback(_onFocusOut);
                _target.UnregisterCallback(_onDetachFromPanel);

                TweeqTooltipRoot.CloseAnyFor(_target);
            }

            #endregion

            #region Events

            void OnPointerEnter(PointerEnterEvent evt)
            {
                RequestShow();
            }

            void OnPointerLeave(PointerLeaveEvent evt)
            {
                RequestHide();
            }

            void OnFocusIn(FocusInEvent evt)
            {
                RequestShow();
            }

            void OnFocusOut(FocusOutEvent evt)
            {
                RequestHide();
            }

            // An element removed from the panel can't receive a leave event, so this cuts off any leftover state here
            void OnDetachFromPanel(DetachFromPanelEvent evt)
            {
                TweeqTooltipRoot.CloseAnyFor(_target);
            }

            void RequestShow()
            {
                if (string.IsNullOrEmpty(_text))
                {
                    return;
                }

                TweeqTooltipRoot.GetOrCreate(_target)?.Show(_target, _text);
            }

            void RequestHide()
            {
                TweeqTooltipRoot.GetOrCreate(_target)?.Hide(_target);
            }

            #endregion
        }

        #endregion
    }
}
