using System;
using UnityEngine;
using UnityEngine.UIElements;

// The class has its own string Title property, so the Label type is referenced under an alias (same reason as ButtonInput)
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A boilerplate modal with title + scrollable body + Cancel/confirm footer
    /// (m8-modal-tabs-spec.md §B; equivalent to the shell of the Vue original's PaneModalComplex / PaneModalTabs).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original assumes a singleton built around a schema-driven form (InputComplex), but this port
    /// <b>generalizes only the shell</b> and leaves the content to the caller (an intentional deviation, grounded in the spec).
    /// </para>
    /// <para>
    /// <b>Rolling back values is the caller's responsibility.</b> Since there's no schema, if you want Cancel
    /// to revert values, capture the current value in <see cref="TweeqModal.Opened"/> and write it back in
    /// <see cref="Cancelled"/>. This class never touches the content's values itself.
    /// </para>
    /// <para>
    /// Keys are only picked up during the panel root's <b>bubble phase</b> while open. If an inner widget
    /// (TextField editing, Escape-restore during a drag, a LightDismiss popover) handles the key first and calls
    /// StopPropagation, it never reaches here — that's the intended priority order.
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqModalDialog : TweeqModal
    {
        #region Constants

        /// <summary>The title's font size (px).</summary>
        public const float TITLE_FONT_SIZE = 14f;

        /// <summary>The default value for <see cref="ConfirmLabel"/>.</summary>
        public const string DEFAULT_CONFIRM_LABEL = "Save";

        /// <summary>The default value for <see cref="CancelLabel"/>.</summary>
        public const string DEFAULT_CANCEL_LABEL = "Cancel";

        // A safety margin kept inside the body ScrollView's clip boundary.
        // Prevents out-of-bounds drawing (e.g. the focus ring at inset −3px) from being clipped by the viewport
        const float CLIP_SAFE_PADDING = 4f;

        #endregion

        #region Fields

        readonly UILabel _title;
        readonly ScrollView _body;
        readonly VisualElement _footer;
        readonly ButtonInput _cancel;
        readonly ButtonInput _confirm;

        string _titleText = string.Empty;
        bool _footerStretch = true;

        // A method-group conversion allocates a delegate every time, so keep a single instance to reuse across register/unregister
        readonly EventCallback<KeyDownEvent> _onRootKeyDown;

        // Remember the registration target, so we always unregister from the same object even if the layer gets swapped
        VisualElement _keyRoot;

        #endregion

        #region Public API

        /// <summary>Fires on the confirm button (or Enter). After firing, <see cref="TweeqModal.Open"/> becomes false.</summary>
        public event Action Confirmed;

        /// <summary>Fires on the cancel button (or Escape). After firing, <see cref="TweeqModal.Open"/> becomes false.</summary>
        public event Action Cancelled;

        /// <summary>The heading. If empty, the whole row disappears.</summary>
        [UxmlAttribute("title")]
        public string Title
        {
            get => _titleText;
            set
            {
                string text = value ?? string.Empty;
                if (_titleText == text)
                {
                    return;
                }

                _titleText = text;
                ApplyTitle();
            }
        }

        /// <summary>The confirm button's label text.</summary>
        [UxmlAttribute("confirm-label")]
        public string ConfirmLabel
        {
            get => _confirm.Label;
            set => _confirm.Label = string.IsNullOrEmpty(value) ? DEFAULT_CONFIRM_LABEL : value;
        }

        /// <summary>The cancel button's label text.</summary>
        [UxmlAttribute("cancel-label")]
        public string CancelLabel
        {
            get => _cancel.Label;
            set => _cancel.Label = string.IsNullOrEmpty(value) ? DEFAULT_CANCEL_LABEL : value;
        }

        /// <summary>
        /// Whether to distribute the footer buttons evenly (default true, matching the Vue original's PaneModalComplex).
        /// false right-aligns them instead (PaneModalTabs).
        /// </summary>
        [UxmlAttribute("footer-stretch")]
        public bool FooterStretch
        {
            get => _footerStretch;
            set
            {
                if (_footerStretch == value)
                {
                    return;
                }

                _footerStretch = value;
                ApplyFooterLayout();
            }
        }

        /// <summary>The body's scroll view. Content added normally via Add goes in here.</summary>
        public ScrollView Body => _body;

        /// <summary>The cancel button. Use this when you need to touch something other than the label.</summary>
        public ButtonInput CancelButton => _cancel;

        /// <summary>The confirm button. Use this when you need to touch something other than the label.</summary>
        public ButtonInput ConfirmButton => _confirm;

        /// <summary>Content goes into the body's scroll view.</summary>
        public override VisualElement contentContainer
            => _body != null ? _body.contentContainer : base.contentContainer;

        /// <summary>Fires cancel and closes. The shared path for the button and Escape.</summary>
        public void PerformCancel()
        {
            Cancelled?.Invoke();
            this.Open = false;
        }

        /// <summary>Fires confirm and closes. The shared path for the button and Enter.</summary>
        public void PerformConfirm()
        {
            Confirmed?.Invoke();
            this.Open = false;
        }

        /// <summary>
        /// Key handling while open. Called from the panel root's bubble phase.
        /// A return value of true means "consumed" — the caller should call StopPropagation.
        /// </summary>
        /// <param name="keyCode">The key that was pressed.</param>
        /// <param name="source">The origin of the key event (i.e. the focused element). May be null.</param>
        public bool PerformKey(KeyCode keyCode, VisualElement source)
        {
            if (!this.Open)
            {
                return false;
            }

            // Equivalent to the Vue original's "do nothing if more than one :popover-open".
            // While a popover is open in this layer, that popover owns the key (e.g. a nested dropdown)
            if (HasOpenPopover())
            {
                return false;
            }

            if (keyCode == KeyCode.Escape)
            {
                PerformCancel();
                return true;
            }

            if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
            {
                // Inside a multiline TextField, prioritize the newline (confirm only via the explicit button)
                if (IsInsideMultilineText(source))
                {
                    return false;
                }

                PerformConfirm();
                return true;
            }

            return false;
        }

        #endregion

        #region Construction

        public TweeqModalDialog()
        {
            this.name = "tweeq-modal-dialog";

            // It gets added to the layer after construction, but reserve the handler instance up front (reused across register/unregister)
            _onRootKeyDown = OnRootKeyDown;

            TweeqTheme theme = this.Theme;

            // The balloon's content stacks vertically. To make only the body stretch and scroll internally,
            // apply a shrink-allowing setting from this level down (UI Toolkit's default is flex-shrink: 0)
            VisualElement content = this.Pane.contentContainer;
            content.style.flexDirection = FlexDirection.Column;
            content.style.flexShrink = 1f;
            content.style.minHeight = 0f;

            _title = new UILabel(string.Empty) { name = "tweeq-modal-dialog-title" };
            _title.style.fontSize = TITLE_FONT_SIZE;
            _title.style.flexShrink = 0f;
            _title.style.marginLeft = 0f;
            _title.style.marginRight = 0f;
            _title.style.marginTop = 0f;
            _title.style.marginBottom = 0f;
            _title.style.display = DisplayStyle.None;
            this.Pane.Add(_title);

            _body = new ScrollView(ScrollViewMode.Vertical) { name = "tweeq-modal-dialog-body" };
            _body.style.flexGrow = 1f;
            _body.style.flexShrink = 1f;
            _body.style.minHeight = 0f;

            // The viewport clips out-of-bounds drawing (e.g. the focus ring), so keep a safety margin
            // inside the clip boundary (same reason as TweeqTabs's CLIP_SAFE_PADDING)
            VisualElement bodyContent = _body.contentContainer;
            if (bodyContent != null)
            {
                bodyContent.style.paddingTop = CLIP_SAFE_PADDING;
                bodyContent.style.paddingBottom = CLIP_SAFE_PADDING;
                bodyContent.style.paddingLeft = CLIP_SAFE_PADDING;
                bodyContent.style.paddingRight = CLIP_SAFE_PADDING;
            }

            this.Pane.Add(_body);

            _footer = new VisualElement { name = "tweeq-modal-dialog-footer" };
            _footer.style.flexDirection = FlexDirection.Row;
            _footer.style.flexShrink = 0f;

            _cancel = new ButtonInput(DEFAULT_CANCEL_LABEL)
            {
                name = "tweeq-modal-dialog-cancel",
                Theme = theme,
                Subtle = true,
            };
            _confirm = new ButtonInput(DEFAULT_CONFIRM_LABEL)
            {
                name = "tweeq-modal-dialog-confirm",
                Theme = theme,
            };

            _cancel.Clicked += PerformCancel;
            _confirm.Clicked += PerformConfirm;

            _footer.Add(_cancel);
            _footer.Add(_confirm);
            this.Pane.Add(_footer);

            ApplyFooterLayout();
            ApplyDialogTheme();
        }

        #endregion

        #region Key routing

        protected override void OnMounted(TweeqOverlayLayer layer)
        {
            base.OnMounted(layer);

            if (_keyRoot != null || layer == null)
            {
                return;
            }

            VisualElement root = layer.hierarchy.parent;
            if (root == null)
            {
                return;
            }

            // Bubble phase (not TrickleDown). If an inner widget handles it first and stops it, this never gets called
            _keyRoot = root;
            _keyRoot.RegisterCallback(_onRootKeyDown);
        }

        protected override void OnUnmounted()
        {
            if (_keyRoot != null)
            {
                _keyRoot.UnregisterCallback(_onRootKeyDown);
                _keyRoot = null;
            }

            base.OnUnmounted();
        }

        void OnRootKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // The key event's target is the focused element. The multiline check walks up starting from here
            if (PerformKey(evt.keyCode, evt.target as VisualElement))
            {
                evt.StopPropagation();
            }
        }

        bool HasOpenPopover()
        {
            return ContainsPopover(this.Layer);
        }

        static bool ContainsPopover(VisualElement parent)
        {
            if (parent == null)
            {
                return false;
            }

            int childCount = parent.hierarchy.childCount;
            for (int index = 0; index < childCount; index++)
            {
                VisualElement child = parent.hierarchy.ElementAt(index);
                if (child == null)
                {
                    continue;
                }

                if (child is TweeqPopover || ContainsPopover(child))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsInsideMultilineText(VisualElement source)
        {
            // A TextField's actual focus moves to its internal input element, so walk up the ancestors to find the owner
            for (VisualElement node = source; node != null; node = node.hierarchy.parent)
            {
                if (node is TextField field && field.multiline)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Presentation

        protected override void OnThemeApplied()
        {
            ApplyDialogTheme();
        }

        void ApplyDialogTheme()
        {
            TweeqTheme theme = this.Theme;
            if (theme == null || _title == null)
            {
                return;
            }

            _title.style.color = theme.Text;

            // The heading font (Geist SemiBold) has a real weight, so we don't combine it with FontStyle.Bold.
            // Only when it fails to load do we keep Bold, since faux-bold is the only way left to make it look bold
            FontDefinition heading = theme.FontHeading;
            TweeqFonts.Apply(_title, heading);
            _title.style.unityFontStyleAndWeight = TweeqFonts.IsEmpty(heading)
                ? FontStyle.Bold
                : FontStyle.Normal;

            _cancel.Theme = theme;
            _confirm.Theme = theme;

            ApplyGaps();
        }

        void ApplyTitle()
        {
            _title.text = _titleText;
            _title.style.display = string.IsNullOrEmpty(_titleText)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            ApplyGaps();
        }

        // UI Toolkit has no CSS gap, so this is built with margins. To avoid leaving space above the body
        // when the title is hidden, distribute margins manually based on visibility
        void ApplyGaps()
        {
            TweeqTheme theme = this.Theme;
            float section = theme != null ? theme.GapSection : 18f;
            bool hasTitle = !string.IsNullOrEmpty(_titleText);

            _title.style.marginTop = 0f;
            _body.style.marginTop = hasTitle ? section : 0f;
            _footer.style.marginTop = section;

            TweeqGap.Apply(_footer, theme != null ? theme.GapControl : 9f, FlexDirection.Row);
        }

        void ApplyFooterLayout()
        {
            // Even distribution matches the Vue original's ModalComplex (making a 2-choice look equal); right-align matches ModalTabs
            _footer.style.justifyContent = _footerStretch ? Justify.FlexStart : Justify.FlexEnd;

            ApplyFooterButton(_cancel);
            ApplyFooterButton(_confirm);
        }

        void ApplyFooterButton(ButtonInput button)
        {
            button.style.flexGrow = _footerStretch ? 1f : 0f;
            button.style.flexShrink = _footerStretch ? 1f : 0f;
        }

        #endregion
    }
}
