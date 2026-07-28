using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>
    /// 状態を表す色。ref/tweeq/src/theme/palette.ts の buildSemanticColors に対応する。
    /// </summary>
    public struct SemanticColors
    {
        /// <summary>エラー（不正な入力値・失敗）。</summary>
        public Rgba32 Error;

        /// <summary>エラーの淡い面色。</summary>
        public Rgba32 ErrorSoft;

        /// <summary>警告。</summary>
        public Rgba32 Warning;

        /// <summary>警告の淡い面色。</summary>
        public Rgba32 WarningSoft;

        /// <summary>成功。</summary>
        public Rgba32 Success;

        /// <summary>成功の淡い面色。</summary>
        public Rgba32 SuccessSoft;

        /// <summary>情報。</summary>
        public Rgba32 Info;

        /// <summary>情報の淡い面色。</summary>
        public Rgba32 InfoSoft;

        /// <summary>録画インジケータ。原典の指定どおり <see cref="Error"/> と同値。</summary>
        public Rgba32 Rec;
    }

    #endregion

    /// <summary>
    /// 曲線的に選ばれた base16 風の色相パレットから、アクセントに寄せたセマンティック色を作る。
    /// ref/tweeq/src/theme/palette.ts の移植。
    /// </summary>
    /// <remarks>
    /// 「代表色」は Radix スケールを通さず、アクセントの明度・彩度をそのまま流用して色相だけ
    /// 差し替える。UI 全体が同じ鮮やかさで揃うのが狙いで、色相のナッジには上限を掛けて
    /// 赤が赤に見えなくなるのを防いでいる。
    /// </remarks>
    public static class TweeqSemanticColors
    {
        #region Constants

        /// <summary>赤のシード色相（Radix red step9 #e5484d）。</summary>
        public static readonly Rgba SeedRed = FromHex(0xE5, 0x48, 0x4D);

        /// <summary>黄のシード色相（Radix amber step9 #ffc53d）。</summary>
        public static readonly Rgba SeedYellow = FromHex(0xFF, 0xC5, 0x3D);

        /// <summary>緑のシード色相（Radix grass step9 #46a758）。</summary>
        public static readonly Rgba SeedGreen = FromHex(0x46, 0xA7, 0x58);

        /// <summary>青のシード色相（Radix blue step9 #3e63dd）。</summary>
        public static readonly Rgba SeedBlue = FromHex(0x3E, 0x63, 0xDD);

        // アクセント色相へ寄せる割合と、シードから離れられる上限。
        // 上限があるからこそ、アクセントが色相環の反対側にあっても赤は赤に見える
        const double NUDGE_T = 0.3;
        const double NUDGE_MAX_DEG = 24.0;

        // 淡い面色を作るときの、背景から代表色へ向かう混合率
        const double SOFT_TINT_T = 0.15;

        #endregion

        #region Public

        /// <summary>背景色とアクセント色からセマンティック色一式を作る。</summary>
        public static SemanticColors Build(Rgba background, Rgba accent)
        {
            Rgba32 red = RepresentativeColor(SeedRed, accent);
            Rgba32 yellow = RepresentativeColor(SeedYellow, accent);
            Rgba32 green = RepresentativeColor(SeedGreen, accent);
            Rgba32 blue = RepresentativeColor(SeedBlue, accent);

            return new SemanticColors
            {
                Error = red,
                ErrorSoft = SoftTint(background, red),
                Warning = yellow,
                WarningSoft = SoftTint(background, yellow),
                Success = green,
                SuccessSoft = SoftTint(background, green),
                Info = blue,
                InfoSoft = SoftTint(background, blue),
                Rec = red,
            };
        }

        /// <summary>
        /// シード色相の代表色。アクセントの明度・彩度をそのまま使い、色相だけをシードから
        /// アクセント方向へ最大 <see cref="NUDGE_MAX_DEG"/> 度だけ寄せる。
        /// </summary>
        public static Rgba32 RepresentativeColor(Rgba seed, Rgba accent)
        {
            Oklch seedColor = TweeqOklch.SrgbToOklch(seed.R, seed.G, seed.B);
            Oklch accentColor = TweeqOklch.SrgbToOklch(accent.R, accent.G, accent.B);

            double hue = NudgedHue(seedColor.H, accentColor.H);
            if (double.IsNaN(hue))
            {
                hue = seedColor.H;
            }

            return TweeqOklch.OklchToBytes(new Oklch(accentColor.L, accentColor.C, hue));
        }

        /// <summary>代表色を背景側へ寄せた淡い面色。OKLCH 上で 15% 混合する。</summary>
        public static Rgba32 SoftTint(Rgba background, Rgba32 color)
        {
            Oklch backgroundColor = TweeqOklch.SrgbToOklch(background.R, background.G, background.B);
            Oklch target = TweeqOklch.SrgbToOklch(color.R / 255.0, color.G / 255.0, color.B / 255.0);

            // 色相は短いほうの弧で補間する（原典 colorjs.io の hue: 'shorter'）
            double h1 = backgroundColor.H;
            double h2 = target.H;
            double delta = h2 - h1;
            if (delta > 180.0)
            {
                h1 += 360.0;
            }
            else if (delta < -180.0)
            {
                h2 += 360.0;
            }

            return TweeqOklch.OklchToBytes(new Oklch(
                Interpolate(backgroundColor.L, target.L),
                Interpolate(backgroundColor.C, target.C),
                Interpolate(h1, h2)));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// シード色相をアクセント色相へ短い弧で寄せる。どちらかが無彩色ならシードのまま返す。
        /// </summary>
        public static double NudgedHue(double seedHue, double accentHue)
        {
            if (double.IsNaN(seedHue) || double.IsNaN(accentHue))
            {
                return seedHue;
            }

            double delta = ((accentHue - seedHue + 540.0) % 360.0) - 180.0;
            double shifted = Math.Max(
                seedHue - NUDGE_MAX_DEG,
                Math.Min(seedHue + NUDGE_MAX_DEG, seedHue + delta * NUDGE_T));

            return (shifted % 360.0 + 360.0) % 360.0;
        }

        // 片側が未定義（NaN）なら、もう片側の値をそのまま採る（原典の補間規則）
        static double Interpolate(double from, double to)
        {
            if (double.IsNaN(from))
            {
                return to;
            }

            if (double.IsNaN(to))
            {
                return from;
            }

            return from + (to - from) * SOFT_TINT_T;
        }

        static Rgba FromHex(int r, int g, int b)
        {
            return new Rgba(r / 255.0, g / 255.0, b / 255.0, 1.0);
        }

        #endregion
    }
}
