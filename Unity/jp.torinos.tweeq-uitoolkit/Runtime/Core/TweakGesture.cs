using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>ジェスチャに作用する修飾キーの状態。</summary>
    public struct GestureModifiers
    {
        /// <summary>微調整（×0.1）。Alt 相当。</summary>
        public bool Fine;

        /// <summary>加速（×fastMultiplier）。Shift 相当。</summary>
        public bool Fast;

        /// <summary>スナップ要求。Q 相当。</summary>
        public bool Snap;

        public GestureModifiers(bool fine, bool fast, bool snap)
        {
            Fine = fine;
            Fast = fast;
            Snap = snap;
        }
    }

    /// <summary>1 フレーム分のジェスチャ出力。</summary>
    public struct GestureUpdate
    {
        /// <summary>このフレームの値変化量。</summary>
        public double Delta;

        /// <summary>ドラッグ開始からの累積。値は「開始値 + これ」で求める。</summary>
        public double AccumulatedDelta;

        /// <summary>縦ドラッグで変化した感度倍率。</summary>
        public double Speed;

        /// <summary>入力された Snap 修飾キーのパススルー。</summary>
        public bool Snap;

        public GestureUpdate(double delta, double accumulatedDelta, double speed, bool snap)
        {
            Delta = delta;
            AccumulatedDelta = accumulatedDelta;
            Speed = speed;
            Snap = snap;
        }
    }

    #endregion

    /// <summary>
    /// 2 次元のポインタ移動をスカラーのデルタへ変換するステートフルなジェスチャ。
    /// 縦ドラッグで感度が連続的に変わり、方向 EMA の重みで値変更と感度変更が同時に起きないようブレンドする。
    /// </summary>
    public sealed class TweakGesture
    {
        #region Fields

        double _speed;
        double _accumulatedDelta;
        double _directionX;
        double _directionY;
        double _horizontalWeight;

        #endregion

        #region Properties

        /// <summary>ジェスチャ由来の感度倍率。Reset で 1。</summary>
        public double Speed
        {
            get { return _speed; }
        }

        /// <summary>ドラッグ開始からの累積デルタ。Reset で 0。フレーム毎にリセットしない。</summary>
        public double AccumulatedDelta
        {
            get { return _accumulatedDelta; }
        }

        /// <summary>直近の移動を「横＝値入力」とみなす強さ（0〜1）。</summary>
        public double HorizontalWeight
        {
            get { return _horizontalWeight; }
        }

        #endregion

        public TweakGesture()
        {
            Reset();
        }

        #region Public API

        /// <summary>累積と感度を初期状態へ戻す。</summary>
        public void Reset()
        {
            _speed = 1.0;
            _accumulatedDelta = 0.0;
            _directionX = 1.0;
            _directionY = 0.0;
            _horizontalWeight = 1.0;
        }

        /// <summary>ポインタ移動 1 サンプルを値のデルタへ変換する。</summary>
        /// <param name="dx">横移動量（px）。右が正。</param>
        /// <param name="dy">縦移動量（px）。下が正＝感度が下がる。</param>
        /// <param name="baseSpeed">px あたりの値変化量。バー有無や step から呼び出し側が決める。</param>
        /// <param name="fastMultiplier">Fast 修飾時の倍率。1 未満は 1 に切り上げる。</param>
        public GestureUpdate Update(
            double dx, double dy, double baseSpeed,
            GestureModifiers modifiers, double fastMultiplier,
            double minSpeed, double maxSpeed)
        {
            // 方向の指数移動平均。生の符号ではなく絶対値を混ぜるので「軸の傾き」だけが残る。
            double mixedX = _directionX * 0.9 + Math.Abs(dx) * 0.1;
            double mixedY = _directionY * 0.9 + Math.Abs(dy) * 0.1;
            double length = Math.Sqrt(mixedX * mixedX + mixedY * mixedY);
            if (length > TweeqMath.MACHINE_EPSILON)
            {
                _directionX = mixedX / length;
                _directionY = mixedY / length;
            }

            _horizontalWeight = TweeqMath.Smoothstep(0.4, 0.6, Math.Abs(_directionX));

            double verticallyAdjusted = _speed * Math.Pow(0.98, dy);
            _speed = TweeqMath.Clamp(
                TweeqMath.Lerp(verticallyAdjusted, _speed, _horizontalWeight),
                minSpeed, maxSpeed);

            double keySpeed = (modifiers.Fine ? 0.1 : 1.0)
                * (modifiers.Fast ? Math.Max(fastMultiplier, 1.0) : 1.0);

            double delta = dx * baseSpeed * _speed * keySpeed * _horizontalWeight;
            _accumulatedDelta += delta;

            return new GestureUpdate(delta, _accumulatedDelta, _speed, modifiers.Snap);
        }

        #endregion
    }
}
