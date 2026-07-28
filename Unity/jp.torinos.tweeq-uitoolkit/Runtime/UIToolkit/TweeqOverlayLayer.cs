using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Shared overlay layer laid over the frontmost part of the panel. A place to put things that
    /// need to draw across the whole area without disturbing layout, such as popovers, tooltips,
    /// or drag-time guide drawing.
    /// Child element coordinates are handled in this layer's local space (i.e. panel coordinates).
    /// </summary>
    public sealed class TweeqOverlayLayer : VisualElement
    {
        #region Constants

        /// <summary>Name used to identify this layer within the hierarchy.</summary>
        public const string LAYER_NAME = "tweeq-overlay-layer";

        #endregion

        #region Construction

        public TweeqOverlayLayer()
        {
            this.name = LAYER_NAME;

            // Covers the whole area but never steals hit-testing
            this.pickingMode = PickingMode.Ignore;
            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.right = 0f;
            this.style.bottom = 0f;
            this.style.overflow = Overflow.Visible;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Gets the layer hanging off the topmost element of the panel that context belongs to.
        /// Creates one if it doesn't exist.
        /// Returns null if not attached to a panel (panel == null), so the caller must always
        /// check for that.
        /// </summary>
        public static TweeqOverlayLayer GetOrCreate(VisualElement context)
        {
            if (context == null || context.panel == null)
            {
                return null;
            }

            VisualElement root = context;
            while (root.hierarchy.parent != null)
            {
                root = root.hierarchy.parent;
            }

            int childCount = root.hierarchy.childCount;
            for (int index = 0; index < childCount; index++)
            {
                if (!(root.hierarchy.ElementAt(index) is TweeqOverlayLayer existing))
                {
                    continue;
                }

                // UI Toolkit draws in hierarchy order, so unless this is the last child, it gets hidden behind UI added afterward
                if (index != childCount - 1)
                {
                    root.hierarchy.Remove(existing);
                    root.hierarchy.Add(existing);
                }

                return existing;
            }

            TweeqOverlayLayer layer = new TweeqOverlayLayer();
            root.hierarchy.Add(layer);
            return layer;
        }

        #endregion
    }
}
