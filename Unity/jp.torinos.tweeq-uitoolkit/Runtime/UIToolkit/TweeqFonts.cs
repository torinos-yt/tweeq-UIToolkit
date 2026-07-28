using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Access to the Geist fonts bundled with the package.
    /// The Vue original tweeq's font tokens are fontUi=system-ui / fontNumeric=Geist /
    /// fontHeading=Geist / fontCode=Geist Mono. Here we provide those as UI Toolkit's
    /// <see cref="FontDefinition"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If loading fails, this returns <c>default(FontDefinition)</c> (i.e. empty) rather than
    /// throwing. Empty means "do not override inline", so the default font from
    /// USS / PanelSettings is used as-is. Since the fallback is part of the intended behavior,
    /// callers can use <see cref="Apply"/> or <see cref="IsEmpty"/> instead of writing a null check.
    /// </para>
    /// <para>Usage examples:</para>
    /// <code>
    /// // 1) Directly into an inline style
    /// FontDefinition numeric = TweeqFonts.NumericFont;
    /// if (!TweeqFonts.IsEmpty(numeric))
    /// {
    ///     label.style.unityFontDefinition = new StyleFontDefinition(numeric);
    /// }
    ///
    /// // 2) Helper with fallback included (if empty, removes the override and reverts to default)
    /// TweeqFonts.Apply(label, TweeqFonts.CodeFont);
    ///
    /// // 3) Via theme token (after integration)
    /// TweeqFonts.Apply(label, theme.FontNumeric);
    ///
    /// // 4) To make the whole UI use Geist, assign the plain Regular instead of UiFont
    /// theme.FontUi = TweeqFonts.GeistRegular;
    /// </code>
    /// <para>
    /// <see cref="HeadingFont"/> is actually the SemiBold weight, so also applying
    /// <c>style.unityFontStyleAndWeight = FontStyle.Bold</c> stacks the legacy
    /// <see cref="Font"/>'s synthetic bold on top of it, doubling the effect. When using this
    /// font for headings, set FontStyle back to Normal.
    /// </para>
    /// </remarks>
    public static class TweeqFonts
    {
        #region Constants

        /// <summary>Storage folder name under Resources.</summary>
        public const string RESOURCE_FOLDER = "Tweeq";

        /// <summary>Resources path for Geist Regular.</summary>
        public const string GEIST_REGULAR_PATH = RESOURCE_FOLDER + "/Geist-Regular";

        /// <summary>Resources path for Geist SemiBold.</summary>
        public const string GEIST_SEMIBOLD_PATH = RESOURCE_FOLDER + "/Geist-SemiBold";

        /// <summary>Resources path for Geist Mono Regular.</summary>
        public const string GEIST_MONO_REGULAR_PATH = RESOURCE_FOLDER + "/GeistMono-Regular";

        #endregion

        #region Fields

        // Load results are stored as a "resolved flag + value" pair. Failures (null) must also
        // be cached, otherwise projects without the bundled fonts would hit Resources.Load every frame.
        static FontDefinition RegularDefinition;
        static bool RegularResolved;

        static FontDefinition SemiBoldDefinition;
        static bool SemiBoldResolved;

        static FontDefinition MonoDefinition;
        static bool MonoResolved;

        #endregion

        #region Raw fonts

        /// <summary>Geist Regular. Empty if not bundled.</summary>
        public static FontDefinition GeistRegular
        {
            get
            {
                if (!RegularResolved)
                {
                    RegularResolved = true;
                    RegularDefinition = LoadFontDefinition(GEIST_REGULAR_PATH);
                }

                return RegularDefinition;
            }
        }

        /// <summary>Geist SemiBold. Empty if not bundled.</summary>
        public static FontDefinition GeistSemiBold
        {
            get
            {
                if (!SemiBoldResolved)
                {
                    SemiBoldResolved = true;
                    SemiBoldDefinition = LoadFontDefinition(GEIST_SEMIBOLD_PATH);
                }

                return SemiBoldDefinition;
            }
        }

        /// <summary>Geist Mono Regular. Empty if not bundled.</summary>
        public static FontDefinition GeistMonoRegular
        {
            get
            {
                if (!MonoResolved)
                {
                    MonoResolved = true;
                    MonoDefinition = LoadFontDefinition(GEIST_MONO_REGULAR_PATH);
                }

                return MonoDefinition;
            }
        }

        #endregion

        #region Semantic fonts

        /// <summary>
        /// General UI (labels, buttons). The Vue original uses system-ui, so the UI Toolkit
        /// equivalent is "unspecified" — i.e. the default font from PanelSettings / USS.
        /// Hence this always returns empty.
        /// </summary>
        public static FontDefinition UiFont => default;

        /// <summary>Numeric display. Corresponds to the Vue original's fontNumeric=Geist.</summary>
        public static FontDefinition NumericFont => GeistRegular;

        /// <summary>Headings. SemiBold, corresponding to the Vue original's fontHeading=Geist (shown bold).</summary>
        public static FontDefinition HeadingFont => GeistSemiBold;

        /// <summary>Places requiring a monospace font, such as code or HEX fields. Corresponds to the Vue original's fontCode=Geist Mono.</summary>
        public static FontDefinition CodeFont => GeistMonoRegular;

        #endregion

        #region Availability

        /// <summary>Whether all 3 bundled weights could be loaded.</summary>
        public static bool IsAvailable =>
            !IsEmpty(GeistRegular) && !IsEmpty(GeistSemiBold) && !IsEmpty(GeistMonoRegular);

        /// <summary>Whether <see cref="NumericFont"/> is available.</summary>
        public static bool IsNumericFontAvailable => !IsEmpty(NumericFont);

        /// <summary>Whether <see cref="HeadingFont"/> is available.</summary>
        public static bool IsHeadingFontAvailable => !IsEmpty(HeadingFont);

        /// <summary>Whether <see cref="CodeFont"/> is available.</summary>
        public static bool IsCodeFontAvailable => !IsEmpty(CodeFont);

        /// <summary>Whether no font is specified (i.e. falling back to the default).</summary>
        public static bool IsEmpty(in FontDefinition definition) =>
            definition.font == null && definition.fontAsset == null;

        #endregion

        #region Loading

        /// <summary>
        /// Preloads all weights. Call this at startup if you want to avoid a hitch on first draw.
        /// No exception is thrown even on failure.
        /// </summary>
        public static void Preload()
        {
            _ = GeistRegular;
            _ = GeistSemiBold;
            _ = GeistMonoRegular;
        }

        /// <summary>
        /// Discards the cache. Use this right after swapping font assets, or when you want to
        /// redo the load path in tests.
        /// </summary>
        public static void ResetCache()
        {
            RegularResolved = false;
            SemiBoldResolved = false;
            MonoResolved = false;
            RegularDefinition = default;
            SemiBoldDefinition = default;
            MonoDefinition = default;
        }

        /// <summary>
        /// Builds a <see cref="FontDefinition"/> from a Resources path. Empty if not found.
        /// </summary>
        public static FontDefinition LoadFontDefinition(string resourcePath)
        {
            Font font = LoadFont(resourcePath);

            // FromFont(null) throws, so this must be guarded against beforehand
            return font == null ? default : FontDefinition.FromFont(font);
        }

        /// <summary>
        /// Loads a <see cref="Font"/> from a Resources path. Null if not found.
        /// </summary>
        public static Font LoadFont(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            // This library is meant for use in live-performance settings, so a missing font
            // must not throw a runtime exception. Resources.Load normally just returns null,
            // but this guards against it throwing due to import inconsistencies.
            try
            {
                return Resources.Load<Font>(resourcePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TweeqFonts] failed to load font '{resourcePath}': {e.Message}");
                return null;
            }
        }

        #endregion

        #region Apply

        /// <summary>
        /// Applies a font to the inline style. Passing an empty definition removes the inline
        /// override, reverting to the default font from USS / PanelSettings (so a previous
        /// setting doesn't linger even when the theme is reapplied).
        /// </summary>
        public static void Apply(VisualElement element, in FontDefinition definition)
        {
            if (element == null)
            {
                return;
            }

            element.style.unityFontDefinition = IsEmpty(definition)
                ? new StyleFontDefinition(StyleKeyword.Null)
                : new StyleFontDefinition(definition);
        }

        #endregion
    }
}
