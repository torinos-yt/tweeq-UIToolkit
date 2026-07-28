using System;

namespace Tweeq.Core
{
    /// <summary>
    /// Rotary のドラッグ累積。スナップ結果をドラッグ状態に還元しないための分離。
    /// </summary>
    public static class RotaryLogic
    {
        /// <summary>
        /// ポインタ由来の delta を累積する。
        /// local は常に生の累積値（スナップしない）で、スナップは output にのみ適用する。
        /// こうしないとスナップ解除時に値が飛ぶ。
        /// </summary>
        public static (double local, double output) GetDragValue(
            double local, double delta, double snap, bool shouldSnap)
        {
            double nextLocal = local + delta;
            if (!shouldSnap || !TweeqMath.IsFinite(snap) || snap == 0.0)
            {
                return (nextLocal, nextLocal);
            }

            // Rust 側の quantize と丸め方向を揃える（C# 既定の銀行家丸めを避ける）。
            double output = Math.Round(nextLocal / snap, MidpointRounding.AwayFromZero) * snap;
            return (nextLocal, TweeqMath.NormalizeZero(output));
        }
    }
}
