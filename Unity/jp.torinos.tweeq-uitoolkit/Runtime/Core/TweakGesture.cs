using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>The state of the modifier keys acting on a gesture.</summary>
    public struct GestureModifiers
    {
        /// <summary>Fine adjustment (x0.1). Equivalent to Alt.</summary>
        public bool Fine;

        /// <summary>Acceleration (x fastMultiplier). Equivalent to Shift.</summary>
        public bool Fast;

        /// <summary>Snap request. Equivalent to Q.</summary>
        public bool Snap;

        public GestureModifiers(bool fine, bool fast, bool snap)
        {
            Fine = fine;
            Fast = fast;
            Snap = snap;
        }
    }

    /// <summary>The gesture output for a single frame.</summary>
    public struct GestureUpdate
    {
        /// <summary>The change in value for this frame.</summary>
        public double Delta;

        /// <summary>The accumulation since drag start. The value is obtained as "start value + this".</summary>
        public double AccumulatedDelta;

        /// <summary>The sensitivity multiplier changed by vertical dragging.</summary>
        public double Speed;

        /// <summary>Pass-through of the input Snap modifier key.</summary>
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
    /// A stateful gesture that converts 2D pointer movement into a scalar delta.
    /// Vertical dragging continuously changes the sensitivity, and the direction EMA's weight blends things
    /// so that value changes and sensitivity changes don't happen at the same time.
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

        /// <summary>The sensitivity multiplier coming from the gesture. 1 after Reset.</summary>
        public double Speed
        {
            get { return _speed; }
        }

        /// <summary>The accumulated delta since drag start. 0 after Reset. Not reset every frame.</summary>
        public double AccumulatedDelta
        {
            get { return _accumulatedDelta; }
        }

        /// <summary>How strongly the most recent movement is treated as "horizontal = value input" (0-1).</summary>
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

        /// <summary>Resets the accumulation and sensitivity to their initial state.</summary>
        public void Reset()
        {
            _speed = 1.0;
            _accumulatedDelta = 0.0;
            _directionX = 1.0;
            _directionY = 0.0;
            _horizontalWeight = 1.0;
        }

        /// <summary>Converts a single sample of pointer movement into a value delta.</summary>
        /// <param name="dx">The horizontal movement amount (px). Right is positive.</param>
        /// <param name="dy">The vertical movement amount (px). Down is positive = sensitivity decreases.</param>
        /// <param name="baseSpeed">The value change per px. Decided by the caller from things like bar presence or step.</param>
        /// <param name="fastMultiplier">The multiplier applied when the Fast modifier is active. Values under 1 are raised to 1.</param>
        public GestureUpdate Update(
            double dx, double dy, double baseSpeed,
            GestureModifiers modifiers, double fastMultiplier,
            double minSpeed, double maxSpeed)
        {
            // Exponential moving average of direction. Since absolute values are blended in rather than raw signs, only the "axis tilt" remains.
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
