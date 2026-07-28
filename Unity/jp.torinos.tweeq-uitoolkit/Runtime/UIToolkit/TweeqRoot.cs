using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Builds a <see cref="TweeqTheme"/> from USS custom properties and distributes it to
    /// descendant <see cref="ITweeqThemed"/> elements. The single entry point for propagating
    /// a theme through UI assembled purely from UXML.
    /// </summary>
    /// <remarks>
    /// <para>USS custom properties read (all optional):</para>
    /// <code>
    /// .my-panel {
    ///     --tq-accent: #0000ff;
    ///     --tq-gray: #8b8d98;
    ///     --tq-background: #111111;
    ///     --tq-color-mode: "dark"; /* or "light" */
    /// }
    /// </code>
    /// <para>
    /// Tokens with no value specified fall back to <see cref="TweeqTheme"/>'s default seeds, so
    /// writing no USS at all produces the same theme as <see cref="TweeqTheme.Dark"/>. If
    /// <see cref="Theme"/> is assigned from C#, that assignment takes priority and subsequent
    /// USS resolution results are ignored (so USS never overrides intent expressed in code).
    /// </para>
    /// <para>
    /// Distribution happens only when attached to a panel, when USS is resolved, or when
    /// <see cref="Theme"/> is assigned. Call <see cref="Redistribute"/> if children are added afterward.
    /// </para>
    /// <para>
    /// Traversal stops at two points: when it hits an <see cref="ITweeqThemed"/>, it stops below it
    /// (forwarding within a composite part is that part's own responsibility); when it hits a nested
    /// <see cref="TweeqRoot"/>, it stops below it (a nested root is a theme boundary that keeps the
    /// theme determined by its own USS).
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqRoot : VisualElement
    {
        #region Constants

        /// <summary>USS class applied to the root itself.</summary>
        public const string USS_CLASS_NAME = "tweeq-root";

        /// <summary>Custom property name that supplies the accent seed color.</summary>
        public const string ACCENT_PROPERTY_NAME = "--tq-accent";

        /// <summary>Custom property name that supplies the gray seed color.</summary>
        public const string GRAY_PROPERTY_NAME = "--tq-gray";

        /// <summary>Custom property name that supplies the background seed color.</summary>
        public const string BACKGROUND_PROPERTY_NAME = "--tq-background";

        /// <summary>Custom property name that supplies the appearance mode ("dark" / "light").</summary>
        public const string COLOR_MODE_PROPERTY_NAME = "--tq-color-mode";

        /// <summary>The dark value to specify for <see cref="COLOR_MODE_PROPERTY_NAME"/>.</summary>
        public const string COLOR_MODE_DARK = "dark";

        /// <summary>The light value to specify for <see cref="COLOR_MODE_PROPERTY_NAME"/>.</summary>
        public const string COLOR_MODE_LIGHT = "light";

        #endregion

        #region Custom style properties

        static readonly CustomStyleProperty<Color> AccentProperty =
            new CustomStyleProperty<Color>(ACCENT_PROPERTY_NAME);

        static readonly CustomStyleProperty<Color> GrayProperty =
            new CustomStyleProperty<Color>(GRAY_PROPERTY_NAME);

        static readonly CustomStyleProperty<Color> BackgroundProperty =
            new CustomStyleProperty<Color>(BACKGROUND_PROPERTY_NAME);

        static readonly CustomStyleProperty<string> ColorModeProperty =
            new CustomStyleProperty<string>(COLOR_MODE_PROPERTY_NAME);

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        // Latch that lets a C# assignment take priority over USS. Once set, it is never cleared
        bool _themeAssignedFromCode;

        bool _paintBackground = true;

        // CustomStyleResolvedEvent fires on every layout/style update, so theme
        // generation and distribution are skipped unless the seeds have actually changed
        bool _seedsResolved;
        ColorMode _resolvedMode;
        Color _resolvedBackground;
        Color _resolvedAccent;
        Color _resolvedGray;

        #endregion

        #region Public API

        /// <summary>
        /// The theme distributed to descendants. Assigning it redistributes immediately, and
        /// USS-side settings are ignored from then on. Passing null falls back to <see cref="TweeqTheme.Dark"/>.
        /// </summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _themeAssignedFromCode = true;
                Redistribute();
            }
        }

        /// <summary>
        /// Whether to paint the theme's <see cref="TweeqTheme.Background"/> as this element's own
        /// background color (default true). The generated background color cannot be written from
        /// USS, so without this the panel would be left unstyled.
        /// </summary>
        [UxmlAttribute]
        public bool PaintBackground
        {
            get => _paintBackground;
            set
            {
                _paintBackground = value;
                ApplyBackground();
            }
        }

        /// <summary>
        /// Redistributes the current theme to descendants. Call after dynamically adding children.
        /// Distribution is a setup-time operation and is not intended to be called every frame.
        /// </summary>
        public void Redistribute()
        {
            ApplyBackground();
            Distribute(this);
        }

        #endregion

        #region Construction

        public TweeqRoot()
        {
            this.AddToClassList(USS_CLASS_NAME);

            // A tree built from UXML gets its children in place before it is attached to the panel,
            // so this is the hook that misses the fewest elements for the bulk distribution
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);

            ApplyBackground();
        }

        #endregion

        #region Internals

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            Redistribute();
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            // Contract: a C# assignment wins. Don't let a USS-derived theme overwrite it
            if (_themeAssignedFromCode)
            {
                return;
            }

            ICustomStyle style = evt?.customStyle;
            if (style == null)
            {
                return;
            }

            ColorMode mode = ResolveColorMode(style);

            // The background is based on the mode's default. This prevents light mode from being
            // left with a black background when only --tq-color-mode is specified (same idea as TweeqTheme.WithColorMode)
            Color background = mode == ColorMode.Light
                ? TweeqTheme.DEFAULT_LIGHT_BACKGROUND
                : TweeqTheme.DEFAULT_DARK_BACKGROUND;
            if (style.TryGetValue(BackgroundProperty, out Color backgroundValue))
            {
                background = backgroundValue;
            }

            Color accent = TweeqTheme.DEFAULT_ACCENT;
            if (style.TryGetValue(AccentProperty, out Color accentValue))
            {
                accent = accentValue;
            }

            Color gray = TweeqTheme.DEFAULT_GRAY;
            if (style.TryGetValue(GrayProperty, out Color grayValue))
            {
                gray = grayValue;
            }

            if (_seedsResolved
                && _resolvedMode == mode
                && _resolvedBackground == background
                && _resolvedAccent == accent
                && _resolvedGray == gray)
            {
                return;
            }

            _seedsResolved = true;
            _resolvedMode = mode;
            _resolvedBackground = background;
            _resolvedAccent = accent;
            _resolvedGray = gray;

            _theme = TweeqTheme.FromSeeds(mode, background, accent, gray);
            Redistribute();
        }

        static ColorMode ResolveColorMode(ICustomStyle style)
        {
            if (!style.TryGetValue(ColorModeProperty, out string text) || string.IsNullOrEmpty(text))
            {
                return ColorMode.Dark;
            }

            string trimmed = text.Trim();
            if (string.Equals(trimmed, COLOR_MODE_LIGHT, StringComparison.OrdinalIgnoreCase))
            {
                return ColorMode.Light;
            }

            if (string.Equals(trimmed, COLOR_MODE_DARK, StringComparison.OrdinalIgnoreCase))
            {
                return ColorMode.Dark;
            }

            // A misspelling would silently fall back to dark and be hard to diagnose, so just log a warning (don't throw)
            Debug.LogWarning(
                $"[TweeqRoot] unknown {COLOR_MODE_PROPERTY_NAME} value '{trimmed}'. "
                + $"use \"{COLOR_MODE_DARK}\" or \"{COLOR_MODE_LIGHT}\".");
            return ColorMode.Dark;
        }

        void ApplyBackground()
        {
            if (!_paintBackground)
            {
                this.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                return;
            }

            this.style.backgroundColor = _theme != null
                ? new StyleColor(_theme.Background)
                : new StyleColor(StyleKeyword.Null);
        }

        // UQuery can't target an interface due to its T : VisualElement constraint, so hierarchy is walked manually.
        // hierarchy is used (rather than the logical tree) so composite parts that swap out contentContainer aren't missed
        void Distribute(VisualElement parent)
        {
            if (parent == null)
            {
                return;
            }

            int childCount = parent.hierarchy.childCount;
            for (int index = 0; index < childCount; index++)
            {
                VisualElement child = parent.hierarchy.ElementAt(index);
                if (child == null)
                {
                    continue;
                }

                // A nested root is its own theme boundary. Leave everything below it to that root
                if (child is TweeqRoot)
                {
                    continue;
                }

                if (child is ITweeqThemed themed)
                {
                    themed.Theme = _theme;
                    continue;
                }

                Distribute(child);
            }
        }

        #endregion
    }
}
