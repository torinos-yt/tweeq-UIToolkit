using System;

namespace Tweeq.Core
{
    /// <summary>比率ロック適用後のサイズと、適用後のロック状態。</summary>
    public readonly struct SizeApplyResult
    {
        /// <summary>適用後の 0 軸（幅）。</summary>
        public readonly double X;

        /// <summary>適用後の 1 軸（高さ）。</summary>
        public readonly double Y;

        /// <summary>適用後の比率ロック状態。自動解除された場合のみ入力と異なる。</summary>
        public readonly bool KeepRatio;

        public SizeApplyResult(double x, double y, bool keepRatio)
        {
            X = x;
            Y = y;
            KeepRatio = keepRatio;
        }
    }

    /// <summary>
    /// InputSize の比率ロック（Vue InputSize.vue onUpdate）。
    /// ロック中に片軸だけ動かしたときの他軸の追従と、両軸同時変更による自動解除を担う。
    /// </summary>
    public static class SizeLogic
    {
        #region Constants

        // 比率は 100 倍・0.01 倍と桁が大きく振れるので、絶対誤差ではなく相対誤差で見る。
        // linearly の scalar.approx は絶対 1e-6 固定だが、それだと大きな比率で誤検知して
        // ロックが勝手に外れる（意図的逸脱）
        const double RATIO_TOLERANCE = 1e-6;

        #endregion

        #region Public API

        /// <summary>
        /// 直前値 <paramref name="previousX"/>/<paramref name="previousY"/> から
        /// <paramref name="nextX"/>/<paramref name="nextY"/> への変更を比率ロックに通す。
        /// 基準値はジェスチャ開始時の値ではなく直前値になるので、
        /// ドラッグ中の連続適用では <see cref="Apply(double,double,double,double,double,double,bool)"/> を使うこと。
        /// </summary>
        public static SizeApplyResult Apply(
            double previousX, double previousY, double nextX, double nextY, bool keepRatio)
        {
            return Apply(previousX, previousY, nextX, nextY, previousX, previousY, keepRatio);
        }

        /// <summary>
        /// 比率ロックを適用する。<paramref name="baselineX"/>/<paramref name="baselineY"/> は
        /// 編集開始時に記録した値（Vue の valueOnEdit）。
        /// ドラッグ中に直前値を基準にすると倍率が積み上がって誤差が溜まるため、基準は固定して渡す。
        /// </summary>
        public static SizeApplyResult Apply(
            double previousX,
            double previousY,
            double nextX,
            double nextY,
            double baselineX,
            double baselineY,
            bool keepRatio)
        {
            bool changedX = previousX != nextX;
            bool changedY = previousY != nextY;

            // 両軸が同時に動いて比率まで変わったのは「ユーザーが比率を崩しに来た」入力。
            // ここでロックを外さないと入力を打ち消し続けてしまう（Vue onUpdate 準拠）
            if (keepRatio && changedX && changedY
                && !ApproximatelySameRatio(previousX / previousY, nextX / nextY))
            {
                keepRatio = false;
            }

            if (!keepRatio)
            {
                return new SizeApplyResult(nextX, nextY, false);
            }

            // Vue は「0 軸が変わっていなければ 1 軸が動いた」とみなす（両軸変化時は 0 軸が主）
            bool primaryIsX = changedX;
            double primaryBaseline = primaryIsX ? baselineX : baselineY;
            double primaryNext = primaryIsX ? nextX : nextY;

            double ratio = primaryNext / primaryBaseline;
            if (!TweeqMath.IsFinite(ratio))
            {
                // 基準が 0（0 除算）なら比率を作れないので倍率 1 = 他軸据え置きで素通しする
                ratio = 1.0;
            }

            return primaryIsX
                ? new SizeApplyResult(primaryNext, baselineY * ratio, true)
                : new SizeApplyResult(baselineX * ratio, primaryNext, true);
        }

        #endregion

        #region Internals

        static bool ApproximatelySameRatio(double left, double right)
        {
            // 0 幅・0 高さでは比率が ±∞ / NaN になる。同じ非有限値どうしは「変わっていない」扱いにして、
            // 0 を跨いだときだけ解除する
            if (left == right)
            {
                return true;
            }

            if (!TweeqMath.IsFinite(left) || !TweeqMath.IsFinite(right))
            {
                return double.IsNaN(left) && double.IsNaN(right);
            }

            double scale = Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= RATIO_TOLERANCE * scale;
        }

        #endregion
    }
}
