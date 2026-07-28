using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Helper that adjusts keyboard navigation on a per-panel basis (feedback-fixes-01.md C-3).
    /// Since tweeq's controls use arrow keys for value manipulation, it is often desirable to
    /// stop focus movement across the whole panel. However, the library does not force this by
    /// default — it is opt-in for the caller.
    /// </summary>
    public static class TweeqNavigation
    {
        /// <summary>
        /// Stops focus movement via ↑↓←→ under root. Tab (Next / Previous) passes through
        /// untouched. Calling this again on an already-disabled root does not double-register.
        /// </summary>
        /// <param name="root">Target root element. Does nothing if null.</param>
        public static void DisableArrowFocusNavigation(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            // TrickleDown so this intercepts before individual controls (NumberInput / RadioInput).
            // Registration itself is possible even if panel is not yet attached, so panel is not required here.
            root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Undoes <see cref="DisableArrowFocusNavigation" /> and restores default behavior.
        /// Harmless to call on a root that was never registered.
        /// </summary>
        /// <param name="root">Target root element. Does nothing if null.</param>
        public static void EnableArrowFocusNavigation(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            root.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        // The callback is a static method, so no root → delegate mapping table
        // (ConditionalWeakTable or static Dictionary) is kept at all.
        // Keeping such a table would let the static field strongly reference root and leak the
        // whole tree (a ConditionalWeakTable would avoid that, but it's unnecessary to begin with).
        // A delegate derived from a static method has a null target and the same method, so
        // Register / Unregister always match under UI Toolkit's equality check.
        // In other words, "holding no state" is the simplest solution and does not leak.
        static void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
                    break;

                default:
                    // Next / Previous (Tab) is left untouched since its whole purpose is to move focus.
                    return;
            }

            evt.StopPropagation();

            // In Unity 6, IgnoreEvent is what actually stops focus movement (PreventDefault is deprecated).
            // panel may not exist yet at registration time, so focusController is fetched from currentTarget every time.
            VisualElement target = evt.currentTarget as VisualElement;
            target?.focusController?.IgnoreEvent(evt);
        }
    }
}
