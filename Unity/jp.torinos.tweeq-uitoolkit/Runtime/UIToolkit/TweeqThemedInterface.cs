using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// <see cref="TweeqTheme"/> を外から差し込める要素。<see cref="TweeqRoot"/> が
    /// 配下を辿ってテーマを配るための目印。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既に <c>public TweeqTheme Theme { get; set; }</c> を持つコンポーネントは、宣言に
    /// <c>, ITweeqThemed</c> を足すだけで実装を満たす（M7 第2波の共通契約）。
    /// </para>
    /// <para>
    /// 配布は「自分より上の階層から降ってくる」片方向で、実装側は受け取ったテーマを
    /// <b>自分の子へ自分で配る責務を持つ</b>。TweeqRoot は ITweeqThemed を見つけたら
    /// そこで探索を打ち切るので、複合コンポーネント（AngleInput 等）が内部の子へ
    /// 転送していないと配下まで届かない。
    /// </para>
    /// <para>
    /// setter は null を渡されても落ちないこと（既存実装と同じく <c>?? TweeqTheme.Dark()</c>
    /// のフォールバックを想定）。
    /// </para>
    /// </remarks>
    public interface ITweeqThemed
    {
        /// <summary>この要素が使う配色テーマ。</summary>
        TweeqTheme Theme { get; set; }
    }

    /// <summary>
    /// 「複合部品が子へ転送する責務」の共通実装。TweeqRoot / TweeqModal / TweeqTabs /
    /// Parameter 系がそれぞれ同じ走査を持っていたのを一本化した（M8 統合時）。
    /// </summary>
    public static class TweeqThemeDistribution
    {
        /// <summary>
        /// <paramref name="parent"/> 配下の <see cref="ITweeqThemed"/> へテーマを配る。
        /// ITweeqThemed に当たったらその配下は相手の転送責務として打ち切り、
        /// 入れ子の <see cref="TweeqRoot"/> は独自のテーマ境界としてまるごと飛ばす。
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
