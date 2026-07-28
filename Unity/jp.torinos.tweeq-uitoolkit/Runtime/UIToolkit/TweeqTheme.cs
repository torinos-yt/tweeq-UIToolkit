using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>外観モード。</summary>
    public enum ColorMode
    {
        /// <summary>明るい面 + 暗い文字。</summary>
        Light,

        /// <summary>暗い面 + 明るい文字。</summary>
        Dark,
    }

    /// <summary>
    /// Tweeq ウィジェット共通のセマンティックカラー／メトリクス。
    /// </summary>
    /// <remarks>
    /// 色トークンは「外観・背景・アクセント・グレー」の 4 入力から <see cref="RadixThemeEngine"/>
    /// で生成する（Vue 版 stores/theme.ts と同じ写像）。個々のトークンを直接書き換えることもできるが、
    /// <see cref="WithAccent"/> などを通すと 4 入力から一括で作り直される。
    /// 数値メトリクス（余白・角丸・時間）は生成対象ではないので、複製しても保たれる。
    /// </remarks>
    public class TweeqTheme
    {
        #region Constants

        /// <summary>Vue 版ストアと同じ既定アクセント。</summary>
        public static readonly Color DEFAULT_ACCENT = Rgb(0x00, 0x00, 0xFF);

        /// <summary>Vue 版ストアと同じ既定グレー（Radix slate 寄りのニュートラル）。</summary>
        public static readonly Color DEFAULT_GRAY = Rgb(0x8B, 0x8D, 0x98);

        /// <summary>ライトの既定背景。</summary>
        public static readonly Color DEFAULT_LIGHT_BACKGROUND = Rgb(0xFF, 0xFF, 0xFF);

        /// <summary>ダークの既定背景。</summary>
        public static readonly Color DEFAULT_DARK_BACKGROUND = Rgb(0x11, 0x11, 0x11);

        // Vue: colorSurface は grayScale[0] を 80% 不透明で敷いたもの（背景が透けるパネル面）
        const float SURFACE_ALPHA = 0.8f;

        // Vue: ライトの colorShadow は grayScale[11] の 20%。ダークは #000000aa 固定
        const float LIGHT_SHADOW_ALPHA = 0.2f;

        #endregion

        #region Seeds

        // 色トークンの生成元。Radix スケールは 4 入力すべてに依存するので、
        // With* で 1 つ差し替えたら必ず全トークンを作り直す
        ColorMode _mode = ColorMode.Dark;

        Color _backgroundSeed = DEFAULT_DARK_BACKGROUND;

        Color _accentSeed = DEFAULT_ACCENT;

        Color _graySeed = DEFAULT_GRAY;

        /// <summary>アクセントのシード色（生成元。<see cref="Accent"/> は Radix step9 の結果）。</summary>
        public Color AccentSeed
        {
            get { return _accentSeed; }
        }

        /// <summary>グレーのシード色。</summary>
        public Color GraySeed
        {
            get { return _graySeed; }
        }

        /// <summary>背景のシード色。</summary>
        public Color BackgroundSeed
        {
            get { return _backgroundSeed; }
        }

        #endregion

        #region Tokens

        /// <summary>明暗どちらの外観か。</summary>
        public ColorMode Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        /// <summary>アプリケーション背景。</summary>
        public Color Background { get; set; }

        /// <summary>浮いた面（パネル・ポップアップ）の背景。</summary>
        public Color Surface { get; set; }

        /// <summary>
        /// <see cref="Surface"/> を <see cref="Background"/> に合成した不透明色。
        /// Vue の半透明 Surface は backdrop-filter のブラー前提で成立しているが、
        /// UI Toolkit にブラーが無く背面がそのまま透けて読めてしまうため、
        /// ポップアップ・モーダル等の浮いた外装はこちらを使う（意図的逸脱・m8-modal-tabs-spec.md）。
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

        /// <summary>主要テキスト。</summary>
        public Color Text { get; set; }

        /// <summary>副次テキスト。</summary>
        public Color TextMuted { get; set; }

        /// <summary>アクセント。</summary>
        public Color Accent { get; set; }

        /// <summary>ホバー時のアクセント。</summary>
        public Color AccentHover { get; set; }

        /// <summary>淡いアクセント面（Radix accentScale[4] 相当）。</summary>
        public Color AccentSoft { get; set; }

        /// <summary>淡いアクセント面のホバー（Radix accentScale[5] 相当）。</summary>
        public Color AccentSoftHover { get; set; }

        /// <summary>
        /// アクセント塗りの上に載せる文字色（Radix accentContrast 相当）。
        /// APCA で「白が読めるか」を判定した結果なので、輝度だけを見る
        /// <see cref="ContrastText"/> より原典に忠実。
        /// </summary>
        public Color OnAccent { get; set; }

        /// <summary>
        /// アクセントを借りない塗りボタンの面色（Radix grayScale[4] 相当）。
        /// Input より一段前に出るので「押せる面」として読める。
        /// </summary>
        public Color Neutral { get; set; }

        /// <summary>Neutral のホバー（Radix grayScale[5] 相当）。</summary>
        public Color NeutralHover { get; set; }

        /// <summary>入力欄の背景。</summary>
        public Color Input { get; set; }

        /// <summary>ホバー時の入力欄背景。</summary>
        public Color InputHover { get; set; }

        /// <summary>控えめな境界線。</summary>
        public Color Border { get; set; }

        /// <summary>さらに控えめな境界線（目盛りグリッド用。Radix grayScaleAlpha[2] 相当）。</summary>
        public Color BorderSubtle { get; set; }

        /// <summary>TextMuted より弱い文字色（Radix grayScale[9] 相当）。</summary>
        public Color TextSubtle { get; set; }

        /// <summary>
        /// エラー表示（不正な入力値）。Vue 版と同じく、赤のシード色相をアクセントへ寄せた代表色。
        /// </summary>
        public Color Error { get; set; } = Rgb(0xEE, 0x4F, 0x57);

        /// <summary>
        /// ポップアップの影色（`--tq-color-shadow`）。UI Toolkit に box-shadow が無いため、
        /// Painter2D で半透明の輪郭を重ねて近似する側が参照する。
        /// </summary>
        public Color Shadow { get; set; } = RgbaBytes(0x00, 0x00, 0x00, 0xAA);

        /// <summary>標準的な入力欄の高さ（px）。</summary>
        public float InputHeight { get; set; } = 24f;

        /// <summary>標準的な入力欄の角丸半径（px）。</summary>
        public float InputRadius { get; set; } = 4f;

        /// <summary>
        /// ポップアップ（ポップオーバー／バルーン）の角丸半径（px）。`--tq-radius-popup`。
        /// InputRadius(4) + PopupPadding(9) の同心円設計なので、どちらかを変えたら追随させる。
        /// </summary>
        public float RadiusPopup { get; set; } = 13f;

        /// <summary>ポップアップ内側の余白（px）。`--tq-popup-padding`。</summary>
        public float PopupPadding { get; set; } = 9f;

        /// <summary>
        /// 固定幅ポップアップの外形幅（px）。`--tq-popup-width`。
        /// ColorInput のピッカーのように「中身の都合ではなく画面上の見た目で幅を決める」パネルが使う。
        /// PopupPadding を含んだ外側の寸法なので、中身の幅は PopupWidth - PopupPadding*2 になる。
        /// </summary>
        public float PopupWidth { get; set; } = 240f;

        /// <summary>グループ内で隣接する入力ボックス間の隙間（px）。仕様 §4 gapGroup。</summary>
        public float GapGroup { get; set; } = 2f;

        /// <summary>関連要素間の隙間（px）。仕様 §4 gapRelated。</summary>
        public float RelatedGap { get; set; } = 6f;

        /// <summary>コントロール（行・列）間の隙間（px）。仕様 §4 gapControl。</summary>
        public float GapControl { get; set; } = 9f;

        /// <summary>セクション間の隙間（px）。仕様 §4 gapSection。</summary>
        public float GapSection { get; set; } = 18f;

        /// <summary>ホバー系トランジションの時間（秒）。</summary>
        public float HoverTransitionDuration { get; set; } = 0.15f;

        /// <summary>
        /// 押下・出現など「操作に直結する」トランジションの時間（秒）。`--tq-active-transition-duration`。
        /// ホバーより短いのは、操作の結果が遅れて見えないようにするため。
        /// </summary>
        public float ActiveTransitionDuration { get; set; } = 0.064f;

        #endregion

        #region Font tokens

        // 本家 tweeq のフォントトークン（fontUi / fontNumeric / fontHeading / fontCode）に対応。
        // 色トークンと違いプロパティではなくフィールドなのは、適用側が
        // TweeqFonts.Apply(element, in theme.FontNumeric) と in 付きで渡せるようにするため
        // （プロパティは in 引数に直接渡せない）。
        // 初期化子で持たせているのは FromSeeds / Dark / Light に限らず素の new TweeqTheme() でも
        // 同梱 Geist が乗るようにするため。TweeqFonts 側がロード結果をキャッシュし、
        // 失敗しても空を返すので、ここでの Resources 参照は安い

        /// <summary>
        /// UI 一般のフォント。本家 fontUi=system-ui に相当するので既定は空（＝指定しない）。
        /// 適用コードは持たない、将来の一括上書き用の予約トークン。
        /// </summary>
        public FontDefinition FontUi = default;

        /// <summary>数値表示のフォント（本家 fontNumeric=Geist）。</summary>
        public FontDefinition FontNumeric = TweeqFonts.NumericFont;

        /// <summary>見出しのフォント（本家 fontHeading=Geist の bold 相当＝SemiBold 実ウェイト）。</summary>
        public FontDefinition FontHeading = TweeqFonts.HeadingFont;

        /// <summary>等幅が要る箇所（HEX 欄など）のフォント（本家 fontCode=Geist Mono）。</summary>
        public FontDefinition FontCode = TweeqFonts.CodeFont;

        #endregion

        #region Presets

        /// <summary>既定入力（accent #0000ff / gray #8B8D98 / 背景 #ffffff）のライトテーマ。</summary>
        public static TweeqTheme Light()
        {
            return FromSeeds(ColorMode.Light, DEFAULT_LIGHT_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);
        }

        /// <summary>既定入力（accent #0000ff / gray #8B8D98 / 背景 #111111）のダークテーマ。</summary>
        public static TweeqTheme Dark()
        {
            return FromSeeds(ColorMode.Dark, DEFAULT_DARK_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);
        }

        /// <summary>
        /// 4 つの入力（外観・背景・アクセント・グレー）から色トークンを生成する。
        /// Vue 版 theme ストアが持つ設定項目と 1 対 1 に対応する唯一の入口。
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

        /// <summary>このテーマの複製を返す。</summary>
        public TweeqTheme Copy()
        {
            return (TweeqTheme)this.MemberwiseClone();
        }

        /// <summary>アクセント色だけを差し替えた複製を返す。色トークンは Radix で作り直す。</summary>
        public TweeqTheme WithAccent(Color accent)
        {
            TweeqTheme copy = this.Copy();
            copy._accentSeed = accent;
            copy.ApplyRadixColors();
            return copy;
        }

        /// <summary>グレー色だけを差し替えた複製を返す。</summary>
        public TweeqTheme WithGray(Color gray)
        {
            TweeqTheme copy = this.Copy();
            copy._graySeed = gray;
            copy.ApplyRadixColors();
            return copy;
        }

        /// <summary>背景色だけを差し替えた複製を返す。</summary>
        public TweeqTheme WithBackground(Color background)
        {
            TweeqTheme copy = this.Copy();
            copy._backgroundSeed = background;
            copy.ApplyRadixColors();
            return copy;
        }

        /// <summary>
        /// 外観モードだけを差し替えた複製を返す。背景は明示的に指定されていない前提で
        /// そのモードの既定へスナップする（Vue 版ストアの watch と同じ振る舞い）。
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

        /// <summary>背景色の輝度から読みやすい文字色（黒 or 白）を返す。</summary>
        /// <remarks>
        /// アクセント塗りの上の文字は <see cref="OnAccent"/>（Radix の APCA 判定結果）を使うのが
        /// 原典に忠実。この簡易判定は「任意の面色の上に文字を置く」その場限りの用途向け。
        /// </remarks>
        public static Color ContrastText(Color background)
        {
            float luminance =
                (0.299f * background.r + 0.587f * background.g + 0.114f * background.b) * 255f;
            return luminance > 150f ? Color.black : Color.white;
        }

        // 4 つのシードから色トークン一式を作り直す。数値メトリクスには触らない
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

            // ダークは真っ黒の影で沈ませ、ライトは最も濃い文字色を薄めて使う（Vue と同じ）
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
