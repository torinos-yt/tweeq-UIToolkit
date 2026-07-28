using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>Appearance mode.</summary>
    public enum ColorMode
    {
        /// <summary>Light surfaces + dark text.</summary>
        Light,

        /// <summary>Dark surfaces + light text.</summary>
        Dark,
    }

    /// <summary>
    /// Semantic colors / metrics shared across Tweeq widgets.
    /// </summary>
    /// <remarks>
    /// Color tokens are generated from 4 inputs — appearance, background, accent, and gray — via
    /// <see cref="RadixThemeEngine"/> (the same mapping as the Vue original's stores/theme.ts).
    /// Individual tokens can also be overwritten directly, but going through <see cref="WithAccent"/>
    /// and similar methods regenerates all of them from the 4 inputs at once.
    /// Numeric metrics (padding, radius, duration) are not part of that generation, so they are
    /// preserved across copies.
    /// </remarks>
    public class TweeqTheme
    {
        #region Constants

        /// <summary>Same default accent as the Vue original's store.</summary>
        public static readonly Color DEFAULT_ACCENT = Rgb(0x00, 0x00, 0xFF);

        /// <summary>Same default gray as the Vue original's store (a neutral leaning toward Radix slate).</summary>
        public static readonly Color DEFAULT_GRAY = Rgb(0x8B, 0x8D, 0x98);

        /// <summary>Default light background.</summary>
        public static readonly Color DEFAULT_LIGHT_BACKGROUND = Rgb(0xFF, 0xFF, 0xFF);

        /// <summary>Default dark background.</summary>
        public static readonly Color DEFAULT_DARK_BACKGROUND = Rgb(0x11, 0x11, 0x11);

        // Vue: colorSurface is grayScale[0] laid down at 80% opacity (a panel surface that lets the background show through)
        const float SURFACE_ALPHA = 0.8f;

        // Vue: light's colorShadow is grayScale[11] at 20%. Dark is fixed at #000000aa
        const float LIGHT_SHADOW_ALPHA = 0.2f;

        #endregion

        #region Seeds

        // The generation source for the color tokens. The Radix scale depends on all 4 inputs, so
        // swapping just one via a With* method always regenerates every token
        ColorMode _mode = ColorMode.Dark;

        Color _backgroundSeed = DEFAULT_DARK_BACKGROUND;

        Color _accentSeed = DEFAULT_ACCENT;

        Color _graySeed = DEFAULT_GRAY;

        /// <summary>The accent seed color (the generation source; <see cref="Accent"/> is the resulting Radix step9).</summary>
        public Color AccentSeed
        {
            get { return _accentSeed; }
        }

        /// <summary>The gray seed color.</summary>
        public Color GraySeed
        {
            get { return _graySeed; }
        }

        /// <summary>The background seed color.</summary>
        public Color BackgroundSeed
        {
            get { return _backgroundSeed; }
        }

        #endregion

        #region Tokens

        /// <summary>Which appearance — light or dark.</summary>
        public ColorMode Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        /// <summary>The application background.</summary>
        public Color Background { get; set; }

        /// <summary>The background for elevated surfaces (panels, popups).</summary>
        public Color Surface { get; set; }

        /// <summary>
        /// An opaque color made by compositing <see cref="Surface"/> onto <see cref="Background"/>.
        /// The Vue original's translucent Surface relies on a backdrop-filter blur to work, but
        /// since UI Toolkit has no blur and the background would show through legibly as-is,
        /// elevated shells like popups and modals use this instead (deliberate deviation; see m8-modal-tabs-spec.md).
        /// </summary>
        public Color SurfaceOpaque
        {
            get
            {
                float alpha = Mathf.Clamp01(Surface.a);
                float inverse = 1f - alpha;
                return new Color(
                    Surface.r * alpha + Background.r * inverse,
                    Surface.g * alpha + Background.g * inverse,
                    Surface.b * alpha + Background.b * inverse,
                    1f);
            }
        }

        /// <summary>Primary text.</summary>
        public Color Text { get; set; }

        /// <summary>Secondary text.</summary>
        public Color TextMuted { get; set; }

        /// <summary>Accent.</summary>
        public Color Accent { get; set; }

        /// <summary>Accent on hover.</summary>
        public Color AccentHover { get; set; }

        /// <summary>A soft accent surface (equivalent to Radix accentScale[4]).</summary>
        public Color AccentSoft { get; set; }

        /// <summary>Hover for the soft accent surface (equivalent to Radix accentScale[5]).</summary>
        public Color AccentSoftHover { get; set; }

        /// <summary>
        /// Text color to place on an accent-filled surface (equivalent to Radix accentContrast).
        /// This is the result of judging "is white legible" via APCA, so it is more faithful to
        /// the original than <see cref="ContrastText"/>, which only looks at luminance.
        /// </summary>
        public Color OnAccent { get; set; }

        /// <summary>
        /// The fill color for a button that doesn't borrow the accent (equivalent to Radix
        /// grayScale[4]). It sits one step forward of Input, so it reads as a "pressable surface".
        /// </summary>
        public Color Neutral { get; set; }

        /// <summary>Hover for Neutral (equivalent to Radix grayScale[5]).</summary>
        public Color NeutralHover { get; set; }

        /// <summary>The input field background.</summary>
        public Color Input { get; set; }

        /// <summary>Input field background on hover.</summary>
        public Color InputHover { get; set; }

        /// <summary>A subdued border.</summary>
        public Color Border { get; set; }

        /// <summary>An even more subdued border (for tick/grid lines; equivalent to Radix grayScaleAlpha[2]).</summary>
        public Color BorderSubtle { get; set; }

        /// <summary>A text color weaker than TextMuted (equivalent to Radix grayScale[9]).</summary>
        public Color TextSubtle { get; set; }

        /// <summary>
        /// Error display (invalid input values). As in the Vue original, a representative color
        /// that pulls a red seed hue toward the accent.
        /// </summary>
        public Color Error { get; set; } = Rgb(0xEE, 0x4F, 0x57);

        /// <summary>
        /// The popup shadow color (`--tq-color-shadow`). Since UI Toolkit has no box-shadow, this
        /// is referenced by the side that approximates one by layering a translucent outline via Painter2D.
        /// </summary>
        public Color Shadow { get; set; } = RgbaBytes(0x00, 0x00, 0x00, 0xAA);

        /// <summary>Standard input field height (px).</summary>
        public float InputHeight { get; set; } = 24f;

        /// <summary>Standard input field corner radius (px).</summary>
        public float InputRadius { get; set; } = 4f;

        /// <summary>
        /// Corner radius of a popup (popover / balloon) (px). `--tq-radius-popup`.
        /// Designed as concentric circles with InputRadius(4) + PopupPadding(9), so changing either one should keep the other in sync.
        /// </summary>
        public float RadiusPopup { get; set; } = 13f;

        /// <summary>Inner padding of a popup (px). `--tq-popup-padding`.</summary>
        public float PopupPadding { get; set; } = 9f;

        /// <summary>
        /// Outer width of a fixed-width popup (px). `--tq-popup-width`.
        /// Used by panels like ColorInput's picker, where the width is decided by on-screen
        /// appearance rather than by the content's needs.
        /// This is the outer dimension including PopupPadding, so the content width becomes PopupWidth - PopupPadding*2.
        /// </summary>
        public float PopupWidth { get; set; } = 240f;

        /// <summary>Gap between adjacent input boxes within a group (px). Spec §4 gapGroup.</summary>
        public float GapGroup { get; set; } = 2f;

        /// <summary>Gap between related elements (px). Spec §4 gapRelated.</summary>
        public float RelatedGap { get; set; } = 6f;

        /// <summary>Gap between controls (rows/columns) (px). Spec §4 gapControl.</summary>
        public float GapControl { get; set; } = 9f;

        /// <summary>Gap between sections (px). Spec §4 gapSection.</summary>
        public float GapSection { get; set; } = 18f;

        /// <summary>Duration of hover-related transitions (seconds).</summary>
        public float HoverTransitionDuration { get; set; } = 0.15f;

        /// <summary>
        /// Duration of transitions directly tied to an operation, such as press or appearance
        /// (seconds). `--tq-active-transition-duration`.
        /// Shorter than hover so the result of an operation doesn't appear delayed.
        /// </summary>
        public float ActiveTransitionDuration { get; set; } = 0.064f;

        #endregion

        #region Font tokens

        // Corresponds to the Vue original tweeq's font tokens (fontUi / fontNumeric / fontHeading / fontCode).
        // Unlike the color tokens, these are fields rather than properties so the applying side
        // can pass them with in, as in TweeqFonts.Apply(element, in theme.FontNumeric)
        // (a property can't be passed directly as an in argument).
        // They are given field initializers so the bundled Geist is applied even for a bare
        // new TweeqTheme(), not just FromSeeds / Dark / Light. TweeqFonts caches its load
        // results and returns empty on failure, so the Resources reference here is cheap

        /// <summary>
        /// The general UI font, i.e. every piece of text not claimed by
        /// <see cref="FontNumeric"/> / <see cref="FontHeading"/> / <see cref="FontCode"/>.
        /// Corresponds to the Vue original's fontUi=system-ui, so the default is empty
        /// (unspecified) and the panel's own font applies.
        /// </summary>
        public FontDefinition FontUi = default;

        /// <summary>Font for numeric display (the Vue original's fontNumeric=Geist).</summary>
        public FontDefinition FontNumeric = TweeqFonts.NumericFont;

        /// <summary>Font for headings (the Vue original's fontHeading=Geist bold, actually weighted SemiBold).</summary>
        public FontDefinition FontHeading = TweeqFonts.HeadingFont;

        /// <summary>Font for places that need monospace (e.g. HEX fields) (the Vue original's fontCode=Geist Mono).</summary>
        public FontDefinition FontCode = TweeqFonts.CodeFont;

        #endregion

        #region Presets

        /// <summary>Light theme with the default inputs (accent #0000ff / gray #8B8D98 / background #ffffff).</summary>
        public static TweeqTheme Light()
        {
            return FromSeeds(ColorMode.Light, DEFAULT_LIGHT_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);
        }

        /// <summary>Dark theme with the default inputs (accent #0000ff / gray #8B8D98 / background #111111).</summary>
        public static TweeqTheme Dark()
        {
            return FromSeeds(ColorMode.Dark, DEFAULT_DARK_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);
        }

        /// <summary>
        /// Generates color tokens from the 4 inputs (appearance, background, accent, gray).
        /// The single entry point that corresponds 1:1 to the settings the Vue original's theme store holds.
        /// </summary>
        public static TweeqTheme FromSeeds(ColorMode mode, Color background, Color accent, Color gray)
        {
            TweeqTheme theme = new TweeqTheme
            {
                _mode = mode,
                _backgroundSeed = background,
                _accentSeed = accent,
                _graySeed = gray,
            };
            theme.ApplyRadixColors();
            return theme;
        }

        #endregion

        #region Helpers

        /// <summary>Returns a copy of this theme.</summary>
        public TweeqTheme Copy()
        {
            return (TweeqTheme)this.MemberwiseClone();
        }

        /// <summary>Returns a copy with only the accent color swapped. Color tokens are regenerated via Radix.</summary>
        public TweeqTheme WithAccent(Color accent)
        {
            TweeqTheme copy = this.Copy();
            copy._accentSeed = accent;
            copy.ApplyRadixColors();
            return copy;
        }

        /// <summary>Returns a copy with only the gray color swapped.</summary>
        public TweeqTheme WithGray(Color gray)
        {
            TweeqTheme copy = this.Copy();
            copy._graySeed = gray;
            copy.ApplyRadixColors();
            return copy;
        }

        /// <summary>Returns a copy with only the background color swapped.</summary>
        public TweeqTheme WithBackground(Color background)
        {
            TweeqTheme copy = this.Copy();
            copy._backgroundSeed = background;
            copy.ApplyRadixColors();
            return copy;
        }

        /// <summary>
        /// Returns a copy with only the appearance mode swapped. Assuming the background hasn't
        /// been explicitly specified, it snaps to that mode's default (the same behavior as the
        /// watch in the Vue original's store).
        /// </summary>
        public TweeqTheme WithColorMode(ColorMode mode)
        {
            TweeqTheme copy = this.Copy();
            copy._mode = mode;
            copy._backgroundSeed = mode == ColorMode.Light
                ? DEFAULT_LIGHT_BACKGROUND
                : DEFAULT_DARK_BACKGROUND;
            copy.ApplyRadixColors();
            return copy;
        }

        /// <summary>Returns a legible text color (black or white) based on the background color's luminance.</summary>
        /// <remarks>
        /// For text on an accent-filled surface, using <see cref="OnAccent"/> (Radix's APCA
        /// judgment result) is faithful to the original. This simple check is meant for the ad
        /// hoc case of placing text on an arbitrary surface color.
        /// </remarks>
        public static Color ContrastText(Color background)
        {
            float luminance =
                (0.299f * background.r + 0.587f * background.g + 0.114f * background.b) * 255f;
            return luminance > 150f ? Color.black : Color.white;
        }

        // Regenerates the full set of color tokens from the 4 seeds. Doesn't touch numeric metrics
        void ApplyRadixColors()
        {
            RadixAppearance appearance = _mode == ColorMode.Light
                ? RadixAppearance.Light
                : RadixAppearance.Dark;

            RadixThemeColors radix = RadixThemeEngine.GenerateThemeColors(
                appearance,
                ToRgba(_backgroundSeed),
                ToRgba(_accentSeed),
                ToRgba(_graySeed));

            SemanticColors semantic = TweeqSemanticColors.Build(
                ToRgba(_backgroundSeed), ToRgba(_accentSeed));

            Background = ToColor(radix.Background);

            Accent = ToColor(radix.AccentScale[8]);
            AccentHover = ToColor(radix.AccentScale[10]);
            AccentSoft = ToColor(radix.AccentScale[4]);
            AccentSoftHover = ToColor(radix.AccentScale[5]);
            OnAccent = ToColor(radix.AccentContrast);

            Text = ToColor(radix.GrayScale[11]);
            TextMuted = ToColor(radix.GrayScale[10]);
            TextSubtle = ToColor(radix.GrayScale[9]);

            Surface = WithAlpha(ToColor(radix.GrayScale[0]), SURFACE_ALPHA);
            Border = ToColor(radix.GrayScaleAlpha[3]);
            BorderSubtle = ToColor(radix.GrayScaleAlpha[2]);

            Input = ToColor(radix.GrayScale[2]);
            InputHover = ToColor(radix.GrayScale[3]);
            Neutral = ToColor(radix.GrayScale[4]);
            NeutralHover = ToColor(radix.GrayScale[5]);

            // Dark uses a pure black shadow to recede; light thins out the darkest text color instead (same as the Vue original)
            Shadow = _mode == ColorMode.Dark
                ? RgbaBytes(0x00, 0x00, 0x00, 0xAA)
                : WithAlpha(ToColor(radix.GrayScale[11]), LIGHT_SHADOW_ALPHA);

            Error = ToColor(semantic.Error);
        }

        static Rgba ToRgba(Color color)
        {
            return new Rgba(color.r, color.g, color.b, color.a);
        }

        static Color ToColor(Rgba32 color)
        {
            return new Color32((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        static Color Rgb(byte r, byte g, byte b)
        {
            return new Color32(r, g, b, 255);
        }

        static Color RgbaBytes(byte r, byte g, byte b, byte a)
        {
            return new Color32(r, g, b, a);
        }

        #endregion
    }
}
