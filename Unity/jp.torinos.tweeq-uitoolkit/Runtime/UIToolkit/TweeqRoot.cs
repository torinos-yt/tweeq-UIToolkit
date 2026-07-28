using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// USS カスタムプロパティから <see cref="TweeqTheme"/> を作り、配下の
    /// <see cref="ITweeqThemed"/> へ配るコンテナ。UXML だけで組んだ UI に
    /// テーマを行き渡らせるための唯一の入口。
    /// </summary>
    /// <remarks>
    /// <para>読み取る USS カスタムプロパティ（すべて任意）:</para>
    /// <code>
    /// .my-panel {
    ///     --tq-accent: #0000ff;
    ///     --tq-gray: #8b8d98;
    ///     --tq-background: #111111;
    ///     --tq-color-mode: "dark"; /* or "light" */
    /// }
    /// </code>
    /// <para>
    /// 指定が無いトークンは <see cref="TweeqTheme"/> の既定シードを使うので、USS を一切書かなければ
    /// <see cref="TweeqTheme.Dark"/> と同じテーマになる。C# から <see cref="Theme"/> を代入した場合は
    /// そちらが優先され、以降 USS の解決結果は無視する（コードの意図を USS が上書きしないため）。
    /// </para>
    /// <para>
    /// 配布のタイミングはパネル接続時・USS 解決時・<see cref="Theme"/> 代入時のみ。
    /// あとから子を足した場合は <see cref="Redistribute"/> を呼ぶ。
    /// </para>
    /// <para>
    /// 探索は 2 か所で打ち切る: <see cref="ITweeqThemed"/> に当たったらその配下（複合部品の内部は
    /// 部品自身が転送する責務）、入れ子の <see cref="TweeqRoot"/> に当たったらその配下
    /// （入れ子はテーマ境界として自分の USS で決めたテーマを保つ）。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqRoot : VisualElement
    {
        #region Constants

        /// <summary>ルート自身に付く USS クラス。</summary>
        public const string USS_CLASS_NAME = "tweeq-root";

        /// <summary>アクセントのシード色を渡すカスタムプロパティ名。</summary>
        public const string ACCENT_PROPERTY_NAME = "--tq-accent";

        /// <summary>グレーのシード色を渡すカスタムプロパティ名。</summary>
        public const string GRAY_PROPERTY_NAME = "--tq-gray";

        /// <summary>背景のシード色を渡すカスタムプロパティ名。</summary>
        public const string BACKGROUND_PROPERTY_NAME = "--tq-background";

        /// <summary>外観モード（"dark" / "light"）を渡すカスタムプロパティ名。</summary>
        public const string COLOR_MODE_PROPERTY_NAME = "--tq-color-mode";

        /// <summary><see cref="COLOR_MODE_PROPERTY_NAME"/> に指定するダークの値。</summary>
        public const string COLOR_MODE_DARK = "dark";

        /// <summary><see cref="COLOR_MODE_PROPERTY_NAME"/> に指定するライトの値。</summary>
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

        // C# 代入を USS より優先させるためのラッチ。一度立てたら下げない
        bool _themeAssignedFromCode;

        bool _paintBackground = true;

        // CustomStyleResolvedEvent はレイアウト・スタイル更新の度に飛んでくるので、
        // シードが変わっていない限りテーマ生成と配布をやり直さない
        bool _seedsResolved;
        ColorMode _resolvedMode;
        Color _resolvedBackground;
        Color _resolvedAccent;
        Color _resolvedGray;

        #endregion

        #region Public API

        /// <summary>
        /// 配下へ配るテーマ。代入すると即座に再配布し、以降 USS 側の指定は無視する。
        /// null を渡した場合は <see cref="TweeqTheme.Dark"/> にフォールバックする。
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
        /// テーマの <see cref="TweeqTheme.Background"/> を自分の背景色として塗るか（既定 true）。
        /// 生成された背景色は USS からは書けないので、ここで面倒を見ないとパネルが素のままになる。
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
        /// 現在のテーマを配下へ配り直す。子を動的に足した後に呼ぶ。
        /// 配布はセットアップ時の操作なので、毎フレーム呼ぶ想定はしていない。
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

            // UXML から組まれた木は「子が揃ってからパネルに載る」ので、
            // 一括配布のフックはここが最も取りこぼしが少ない
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
            // C# 代入が勝つ契約。USS 由来のテーマで踏み潰さない
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

            // 背景は「モードの既定」を土台にする。--tq-color-mode だけ指定した場合に
            // ライトが黒背景のまま残るのを防ぐため（TweeqTheme.WithColorMode と同じ考え方）
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

            // 綴り間違いは黙って dark になると原因が分からないので警告だけ出す（例外は投げない）
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

        // UQuery は T : VisualElement 制約でインターフェースを取れないので、hierarchy を自前で辿る。
        // hierarchy 側なのは contentContainer を差し替えている複合部品を取りこぼさないため
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

                // 入れ子ルートは独自のテーマ境界。配下ごと相手に任せる
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
