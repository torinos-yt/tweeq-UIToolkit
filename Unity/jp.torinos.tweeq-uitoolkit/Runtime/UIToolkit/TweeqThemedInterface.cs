using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// An element into which <see cref="TweeqTheme"/> can be injected from outside. A marker that
    /// <see cref="TweeqRoot"/> uses when walking its descendants to distribute the theme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A component that already has <c>public TweeqTheme Theme { get; set; }</c> satisfies the
    /// implementation just by adding <c>, ITweeqThemed</c> to its declaration (M7 second-wave
    /// common contract).
    /// </para>
    /// <para>
    /// Distribution flows one way, "downward from a level above yourself"; the implementer
    /// <b>bears the responsibility of distributing the theme it receives to its own children</b>.
    /// TweeqRoot stops its search the moment it finds an ITweeqThemed, so unless a composite
    /// component (e.g. AngleInput) forwards the theme to its internal children, it won't reach
    /// further down.
    /// </para>
    /// <para>
    /// The setter must not fail even if passed null (a <c>?? TweeqTheme.Dark()</c> fallback is
    /// assumed, same as existing implementations).
    /// </para>
    /// </remarks>
    public interface ITweeqThemed
    {
        /// <summary>The color theme this element uses.</summary>
        TweeqTheme Theme { get; set; }
    }

    /// <summary>
    /// Common implementation of "the composite part's responsibility to forward to its children".
    /// Unifies what TweeqRoot / TweeqModal / TweeqTabs / the Parameter family each used to
    /// implement as the same traversal individually (unified during M8 integration).
    /// </summary>
    public static class TweeqThemeDistribution
    {
        /// <summary>
        /// Distributes the theme to <see cref="ITweeqThemed"/> instances under <paramref name="parent"/>.
        /// Once an ITweeqThemed is hit, its subtree is left to that instance's own forwarding
        /// responsibility and the search stops there; a nested <see cref="TweeqRoot"/> is skipped
        /// entirely as its own independent theme boundary.
        /// </summary>
        public static void Distribute(VisualElement parent, TweeqTheme theme)
        {
            if (parent == null)
            {
                return;
            }

            int childCount = parent.hierarchy.childCount;
            for (int index = 0; index < childCount; index++)
            {
                VisualElement child = parent.hierarchy.ElementAt(index);
                if (child == null || child is TweeqRoot)
                {
                    continue;
                }

                if (child is ITweeqThemed themed)
                {
                    themed.Theme = theme;
                    continue;
                }

                Distribute(child, theme);
            }
        }
    }
}
