using System;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 「1 編集セッションにつき 1 回だけ確定を通知する」ウィジェットの共通契約。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 各ウィジェットが個別に持っていた <c>Confirmed</c> イベントを、外部 asmdef から
    /// 型で束ねられるように宣言だけ切り出したもの（ext-custom-widgets-spec.md EXT-01-C）。
    /// </para>
    /// <para>
    /// 既存ウィジェットへの後付け実装はしていない。連続的に変わる値は
    /// <c>INotifyValueChanged&lt;T&gt;</c> 側で流し、Undo 1 単位にしたい確定だけを
    /// ここへ載せる、という使い分けを意図している。
    /// </para>
    /// </remarks>
    public interface ITweeqConfirmable<T>
    {
        /// <summary>編集セッションの終了時に、値が変わっていた場合だけ 1 回発火する。</summary>
        event Action<T> Confirmed;
    }
}
