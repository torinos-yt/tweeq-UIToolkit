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
    ///
    ///     /* Font tokens. Either a legacy Font or a TextCore FontAsset.
    ///        Asset references must use url(); resource() does not resolve for these. */
    ///     --tq-font-ui: url("project://database/Assets/Fonts/MyFont.ttf");
    ///     --tq-font-numeric: url("/Assets/Fonts/MyMono.ttf");
    ///     --tq-font-heading: url("MyFont-SemiBold.ttf"); /* relative to this .uss */
    ///     --tq-font-code: url("project://database/Packages/my.package/Fonts/MyMono.ttf");
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

        /// <summary>Custom property name that supplies <see cref="TweeqTheme.FontUi"/>.</summary>
        public const string FONT_UI_PROPERTY_NAME = "--tq-font-ui";

        /// <summary>Custom property name that supplies <see cref="TweeqTheme.FontNumeric"/>.</summary>
        public const string FONT_NUMERIC_PROPERTY_NAME = "--tq-font-numeric";

        /// <summary>Custom property name that supplies <see cref="TweeqTheme.FontHeading"/>.</summary>
        public const string FONT_HEADING_PROPERTY_NAME = "--tq-font-heading";

        /// <summary>Custom property name that supplies <see cref="TweeqTheme.FontCode"/>.</summary>
        public const string FONT_CODE_PROPERTY_NAME = "--tq-font-code";

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

        // Font seeds arrive as an asset reference (USS resource() / url()), so the property type is
        // Object rather than a font type: which of Font / FontAsset it turned out to be is decided at resolve time
        static readonly CustomStyleProperty<UnityEngine.Object> FontUiProperty =
            new CustomStyleProperty<UnityEngine.Object>(FONT_UI_PROPERTY_NAME);

        static readonly CustomStyleProperty<UnityEngine.Object> FontNumericProperty =
            new CustomStyleProperty<UnityEngine.Object>(FONT_NUMERIC_PROPERTY_NAME);

        static readonly CustomStyleProperty<UnityEngine.Object> FontHeadingProperty =
            new CustomStyleProperty<UnityEngine.Object>(FONT_HEADING_PROPERTY_NAME);

        static readonly CustomStyleProperty<UnityEngine.Object> FontCodeProperty =
            new CustomStyleProperty<UnityEngine.Object>(FONT_CODE_PROPERTY_NAME);

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
        UnityEngine.Object _resolvedFontUi;
        UnityEngine.Object _resolvedFontNumeric;
        UnityEngine.Object _resolvedFontHeading;
        UnityEngine.Object _resolvedFontCode;

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

        #region Font seeds

        /// <summary>
        /// Reads the four font custom properties off <paramref name="style"/> and overrides the
        /// matching tokens on <paramref name="theme"/>.
        /// </summary>
        public static void ApplyFontSeeds(ICustomStyle style, TweeqTheme theme)
        {
            if (style == null || theme == null)
            {
                return;
            }

            ApplyFontSeeds(
                theme,
                ResolveFontSeed(style, FontUiProperty),
                ResolveFontSeed(style, FontNumericProperty),
                ResolveFontSeed(style, FontHeadingProperty),
                ResolveFontSeed(style, FontCodeProperty));
        }

        /// <summary>
        /// Overrides a theme's font tokens with already-resolved seed assets. A null entry leaves
        /// that token untouched, which is what makes "specify only the ones you care about" work —
        /// an unspecified token keeps the default (FontUi empty, the rest bundled Geist).
        /// </summary>
        public static void ApplyFontSeeds(
            TweeqTheme theme,
            UnityEngine.Object ui,
            UnityEngine.Object numeric,
            UnityEngine.Object heading,
            UnityEngine.Object code)
        {
            if (theme == null)
            {
                return;
            }

            if (TryCreateFontDefinition(ui, out FontDefinition uiFont))
            {
                theme.FontUi = uiFont;
            }

            if (TryCreateFontDefinition(numeric, out FontDefinition numericFont))
            {
                theme.FontNumeric = numericFont;
            }

            if (TryCreateFontDefinition(heading, out FontDefinition headingFont))
            {
                theme.FontHeading = headingFont;
            }

            if (TryCreateFontDefinition(code, out FontDefinition codeFont))
            {
                theme.FontCode = codeFont;
            }
        }

        /// <summary>
        /// Turns a seed asset into a <see cref="FontDefinition"/>. Both a TextCore
        /// <see cref="UnityEngine.TextCore.Text.FontAsset"/> and a legacy <see cref="Font"/> are
        /// accepted, since which one a USS <c>url()</c> yields depends on how the font was
        /// imported. Returns false for anything else.
        /// </summary>
        public static bool TryCreateFontDefinition(UnityEngine.Object asset, out FontDefinition definition)
        {
            definition = default;

            // Unity's lifetime-aware == also catches an asset destroyed after the style was resolved
            if (asset == null)
            {
                return false;
            }

            if (asset is UnityEngine.TextCore.Text.FontAsset fontAsset)
            {
                definition = FontDefinition.FromSDFFont(fontAsset);
                return true;
            }

            if (asset is Font font)
            {
                definition = FontDefinition.FromFont(font);
                return true;
            }

            // A wrong asset type would silently leave the default font in place and be hard to diagnose,
            // so warn (but don't throw — this library has to survive a bad asset at runtime)
            Debug.LogWarning(
                $"[TweeqRoot] a font seed must be a Font or a FontAsset, got "
                + $"'{asset.GetType().Name}' ({asset.name}).");
            return false;
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

            UnityEngine.Object fontUi = ResolveFontSeed(style, FontUiProperty);
            UnityEngine.Object fontNumeric = ResolveFontSeed(style, FontNumericProperty);
            UnityEngine.Object fontHeading = ResolveFontSeed(style, FontHeadingProperty);
            UnityEngine.Object fontCode = ResolveFontSeed(style, FontCodeProperty);

            if (_seedsResolved
                && _resolvedMode == mode
                && _resolvedBackground == background
                && _resolvedAccent == accent
                && _resolvedGray == gray
                && ReferenceEquals(_resolvedFontUi, fontUi)
                && ReferenceEquals(_resolvedFontNumeric, fontNumeric)
                && ReferenceEquals(_resolvedFontHeading, fontHeading)
                && ReferenceEquals(_resolvedFontCode, fontCode))
            {
                return;
            }

            _seedsResolved = true;
            _resolvedMode = mode;
            _resolvedBackground = background;
            _resolvedAccent = accent;
            _resolvedGray = gray;
            _resolvedFontUi = fontUi;
            _resolvedFontNumeric = fontNumeric;
            _resolvedFontHeading = fontHeading;
            _resolvedFontCode = fontCode;

            _theme = TweeqTheme.FromSeeds(mode, background, accent, gray);
            ApplyFontSeeds(_theme, fontUi, fontNumeric, fontHeading, fontCode);
            Redistribute();
        }

        static UnityEngine.Object ResolveFontSeed(
            ICustomStyle style, CustomStyleProperty<UnityEngine.Object> property)
        {
            return style.TryGetValue(property, out UnityEngine.Object value) ? value : null;
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
