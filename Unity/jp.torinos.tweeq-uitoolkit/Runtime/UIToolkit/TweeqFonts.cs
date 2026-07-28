using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// パッケージ同梱 Geist フォントへのアクセス。
    /// 本家 tweeq のフォントトークンは fontUi=system-ui / fontNumeric=Geist /
    /// fontHeading=Geist / fontCode=Geist Mono。ここではそれを UI Toolkit の
    /// <see cref="FontDefinition"/> として提供する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ロードに失敗したら例外ではなく <c>default(FontDefinition)</c>（＝空）を返す。
    /// 空は「インラインで上書きしない」を意味し、USS ／ PanelSettings の既定フォントが
    /// そのまま使われる。フォールバックが機能の一部なので、呼び出し側は null 判定を
    /// 書かずに <see cref="Apply"/> か <see cref="IsEmpty"/> を使えばよい。
    /// </para>
    /// <para>適用サンプル:</para>
    /// <code>
    /// // 1) 直接インラインスタイルへ
    /// FontDefinition numeric = TweeqFonts.NumericFont;
    /// if (!TweeqFonts.IsEmpty(numeric))
    /// {
    ///     label.style.unityFontDefinition = new StyleFontDefinition(numeric);
    /// }
    ///
    /// // 2) フォールバック込みのヘルパ（空なら上書きを外して既定へ戻す）
    /// TweeqFonts.Apply(label, TweeqFonts.CodeFont);
    ///
    /// // 3) テーマトークン経由（統合後）
    /// TweeqFonts.Apply(label, theme.FontNumeric);
    ///
    /// // 4) UI 全体を Geist にしたい場合は UiFont ではなく素の Regular を割り当てる
    /// theme.FontUi = TweeqFonts.GeistRegular;
    /// </code>
    /// <para>
    /// <see cref="HeadingFont"/> は SemiBold の実ウェイトなので、併せて
    /// <c>style.unityFontStyleAndWeight = FontStyle.Bold</c> を掛けると
    /// レガシー <see cref="Font"/> の擬似ボールドが二重に乗る。見出しに本フォントを
    /// 使うなら FontStyle は Normal に戻すこと。
    /// </para>
    /// </remarks>
    public static class TweeqFonts
    {
        #region Constants

        /// <summary>Resources 配下の格納フォルダ名。</summary>
        public const string RESOURCE_FOLDER = "Tweeq";

        /// <summary>Geist Regular の Resources パス。</summary>
        public const string GEIST_REGULAR_PATH = RESOURCE_FOLDER + "/Geist-Regular";

        /// <summary>Geist SemiBold の Resources パス。</summary>
        public const string GEIST_SEMIBOLD_PATH = RESOURCE_FOLDER + "/Geist-SemiBold";

        /// <summary>Geist Mono Regular の Resources パス。</summary>
        public const string GEIST_MONO_REGULAR_PATH = RESOURCE_FOLDER + "/GeistMono-Regular";

        #endregion

        #region Fields

        // ロード結果は「解決済みフラグ + 値」で持つ。失敗（null）もキャッシュしないと
        // 未同梱プロジェクトで毎フレーム Resources.Load を叩いてしまう
        static FontDefinition RegularDefinition;
        static bool RegularResolved;

        static FontDefinition SemiBoldDefinition;
        static bool SemiBoldResolved;

        static FontDefinition MonoDefinition;
        static bool MonoResolved;

        #endregion

        #region Raw fonts

        /// <summary>Geist Regular。同梱されていなければ空。</summary>
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

        /// <summary>Geist SemiBold。同梱されていなければ空。</summary>
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

        /// <summary>Geist Mono Regular。同梱されていなければ空。</summary>
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
        /// UI 一般（ラベル・ボタン）。本家は system-ui なので、UI Toolkit 側の対応物は
        /// 「指定しない」＝ PanelSettings / USS の既定フォント。ゆえに常に空を返す。
        /// </summary>
        public static FontDefinition UiFont => default;

        /// <summary>数値表示。本家 fontNumeric=Geist に対応。</summary>
        public static FontDefinition NumericFont => GeistRegular;

        /// <summary>見出し。本家 fontHeading=Geist（bold 表示）に対応する SemiBold。</summary>
        public static FontDefinition HeadingFont => GeistSemiBold;

        /// <summary>コード・HEX 欄など等幅が要る箇所。本家 fontCode=Geist Mono に対応。</summary>
        public static FontDefinition CodeFont => GeistMonoRegular;

        #endregion

        #region Availability

        /// <summary>同梱 3 ウェイトすべてがロードできたか。</summary>
        public static bool IsAvailable =>
            !IsEmpty(GeistRegular) && !IsEmpty(GeistSemiBold) && !IsEmpty(GeistMonoRegular);

        /// <summary><see cref="NumericFont"/> が使えるか。</summary>
        public static bool IsNumericFontAvailable => !IsEmpty(NumericFont);

        /// <summary><see cref="HeadingFont"/> が使えるか。</summary>
        public static bool IsHeadingFontAvailable => !IsEmpty(HeadingFont);

        /// <summary><see cref="CodeFont"/> が使えるか。</summary>
        public static bool IsCodeFontAvailable => !IsEmpty(CodeFont);

        /// <summary>フォント未指定（＝既定へフォールバックする状態）か。</summary>
        public static bool IsEmpty(in FontDefinition definition) =>
            definition.font == null && definition.fontAsset == null;

        #endregion

        #region Loading

        /// <summary>
        /// 全ウェイトを先読みする。初回描画のヒッチを避けたい場合に起動時に呼ぶ。
        /// 失敗しても例外は出ない。
        /// </summary>
        public static void Preload()
        {
            _ = GeistRegular;
            _ = GeistSemiBold;
            _ = GeistMonoRegular;
        }

        /// <summary>
        /// キャッシュを破棄する。フォントアセットを差し替えた直後や、テストで
        /// ロード経路をやり直したいときに使う。
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
        /// Resources パスから <see cref="FontDefinition"/> を作る。見つからなければ空。
        /// </summary>
        public static FontDefinition LoadFontDefinition(string resourcePath)
        {
            Font font = LoadFont(resourcePath);

            // FromFont(null) は例外を投げるので、必ず手前で弾く
            return font == null ? default : FontDefinition.FromFont(font);
        }

        /// <summary>
        /// Resources パスから <see cref="Font"/> をロードする。見つからなければ null。
        /// </summary>
        public static Font LoadFont(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            // 公演現場で使う前提のライブラリなので、フォント欠落でランタイム例外は出さない。
            // Resources.Load は通常 null を返すだけだが、インポート不整合で投げる場合に備える
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
        /// インラインスタイルへフォントを適用する。空の定義を渡すとインライン指定を
        /// 外すので、USS / PanelSettings の既定フォントへ戻る（テーマ再適用でも
        /// 前回の指定が残らない）。
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
