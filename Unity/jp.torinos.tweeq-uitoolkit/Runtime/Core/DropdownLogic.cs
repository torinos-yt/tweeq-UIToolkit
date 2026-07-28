using System;

namespace Tweeq.Core
{
    /// <summary>
    /// macOS 風ドロップダウンの縦位置。選択中の option がフィールドに重なる top を逆算する。
    /// UnityEngine 非依存・すべて double。
    /// </summary>
    public static class DropdownLogic
    {
        #region Constants

        /// <summary>viewport 端に確保する余白（InputDropdown.vue の VIEWPORT_MARGIN）。</summary>
        public const double DEFAULT_VIEWPORT_MARGIN = 6.0;

        /// <summary>popup の枠が option 行の外側に持つ厚み（padding + border 相当。Vue の SELECT_CHROME）。</summary>
        public const double DEFAULT_SELECT_CHROME = 2.0;

        /// <summary>
        /// フィールド側の border 1px + focus outline 1px（Vue/React の DOM 箱モデル由来の 2px）。
        /// UIToolkit 版のフィールドはこのインセットを持たない（focus ring は別レイヤー）ため、
        /// 呼び出し側が実測値（通常 0）を渡す。既定値は React 版との数値互換のため 2 のまま。
        /// </summary>
        public const double DEFAULT_FIELD_INSET = 2.0;

        #endregion

        #region Public API

        /// <summary>
        /// popup の top（panel 座標）。currentIndex 番目の option がフィールドに重なる位置を理想値とし、
        /// viewport に viewportMargin を確保できる範囲へクランプする。収まらない分は内部スクロールで見せる。
        /// listHeight は実測済みのリスト全高（未実測なら 0 以下）。
        /// </summary>
        public static double GetDropdownTop(
            double fieldWorldY, int currentIndex, double itemHeight, double viewportHeight,
            double viewportMargin = DEFAULT_VIEWPORT_MARGIN,
            double selectChrome = DEFAULT_SELECT_CHROME,
            double listHeight = 0.0,
            double fieldInset = DEFAULT_FIELD_INSET)
        {
            int index = currentIndex < 0 ? 0 : currentIndex;
            double idealTop = fieldWorldY - fieldInset - selectChrome - index * itemHeight;

            double available = viewportHeight - viewportMargin * 2.0;

            // 未実測（0 以下）は「収まらない側」に倒す。実測前に全高を楽観視すると、初回フレームだけ
            // 下端がはみ出してスクロール矢印ごと画面外に出てしまう。
            double measured = listHeight > 0.0 ? listHeight : double.PositiveInfinity;

            // 全部収まるならリスト全体が画面内に留まる位置で頭打ち。収まらないなら「最低 1 行は見える」位置まで下げてよい
            // （下端はスクロール矢印込みで viewport 下端まで伸びる前提）。
            double maxTop = measured <= available
                ? viewportHeight - viewportMargin - listHeight
                : viewportHeight - viewportMargin - itemHeight;

            // maxTop が margin を下回る（viewport が極端に低い）ケースでも上端の margin を守る。
            return Math.Max(viewportMargin, Math.Min(Math.Max(viewportMargin, maxTop), idealTop));
        }

        /// <summary>
        /// popup の最大高さ。viewport 下端まで伸ばすが、リスト自体より高くはしない
        /// （listHeight &lt;= 0 は「未実測」とみなして下端までいっぱいに取る）。
        /// </summary>
        public static double GetDropdownMaxHeight(
            double top, double listHeight, double viewportHeight,
            double viewportMargin = DEFAULT_VIEWPORT_MARGIN)
        {
            double available = viewportHeight - top - viewportMargin;
            if (available < 0.0)
            {
                available = 0.0;
            }

            return listHeight > 0.0 ? Math.Min(listHeight, available) : available;
        }

        #endregion
    }
}
