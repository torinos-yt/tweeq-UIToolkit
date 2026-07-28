using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Focus ring for an input field. Drawn as an absolutely-positioned layer overlaid on the host.
    /// </summary>
    /// <remarks>
    /// Not drawn via the host's own border, because adding a border would shift the absolutely-
    /// positioned children (bar, handle, TextField) 1px inward. Picking is disabled, so even when
    /// overlaid, the pointer passes through to the element below.
    /// </remarks>
    public sealed class TweeqFocusRing : VisualElement
    {
        #region Constants

        /// <summary>Line width of the ring (px).</summary>
        public const float RING_WIDTH = 1f;

        /// <summary>Default element name.</summary>
        public const string DEFAULT_NAME = "tweeq-focus-ring";

        #endregion

        #region Construction

        public TweeqFocusRing()
        {
            this.name = DEFAULT_NAME;
            this.AddToClassList("tweeq-focus-ring");
            this.pickingMode = PickingMode.Ignore;

            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.right = 0f;
            this.style.bottom = 0f;
            this.style.display = DisplayStyle.None;

            TweeqInputBoxStyles.SetBorderWidth(this, RING_WIDTH);
        }

        /// <summary>
        /// Creates a ring and overlays it on top of host (as the last child).
        /// </summary>
        /// <remarks>
        /// Returns a ring even if host is null. Keeping the caller's reference non-null lets
        /// subsequent calls to <see cref="Apply" /> / <see cref="Visible" /> pass through harmlessly.
        /// </remarks>
        public static TweeqFocusRing Attach(VisualElement host)
        {
            TweeqFocusRing ring = new TweeqFocusRing();

            if (host != null)
            {
                host.hierarchy.Add(ring);
            }

            return ring;
        }

        #endregion

        #region Public API

        /// <summary>Whether to show the ring.</summary>
        public bool Visible
        {
            get { return this.style.display.value == DisplayStyle.Flex; }
            set { this.style.display = value ? DisplayStyle.Flex : DisplayStyle.None; }
        }

        /// <summary>
        /// Follows the color and corner rounding of the theme and group position. Call with the
        /// same arguments as the box.
        /// </summary>
        public void Apply(
            TweeqTheme theme, TweeqBoxPosition inlinePosition, TweeqBoxPosition blockPosition)
        {
            if (theme == null)
            {
                return;
            }

            TweeqInputBoxStyles.SetBorderColor(this, theme.Accent);
            TweeqInputBoxStyles.ApplyCornerRadius(this, theme, inlinePosition, blockPosition);
        }

        #endregion
    }
}
