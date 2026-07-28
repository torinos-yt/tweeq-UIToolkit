namespace Tweeq.UIToolkit
{
    /// <summary>
    /// グループ内での位置。隣とくっつく側の角を潰すために使う（仕様 §1）。
    /// </summary>
    public enum TweeqBoxPosition
    {
        /// <summary>単独。角丸は全周そのまま。</summary>
        None,

        /// <summary>先頭。進行方向側の 2 角を潰す。</summary>
        Start,

        /// <summary>中間。4 角すべて潰す。</summary>
        Middle,

        /// <summary>末尾。進行方向と逆側の 2 角を潰す。</summary>
        End,
    }

    /// <summary>
    /// InputGroup が位置を割り当てられる入力ボックス。
    /// 角丸の適用は各ボックス側の責務（グループは仕切り線もボーダー結合も持たない）。
    /// </summary>
    public interface ITweeqInputBox
    {
        /// <summary>横方向（FlexDirection.Row）グループでの位置。</summary>
        TweeqBoxPosition InlinePosition { get; set; }

        /// <summary>縦方向（FlexDirection.Column）グループでの位置。</summary>
        TweeqBoxPosition BlockPosition { get; set; }
    }
}
