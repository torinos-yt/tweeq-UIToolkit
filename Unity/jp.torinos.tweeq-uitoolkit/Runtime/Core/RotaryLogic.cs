using System;

namespace Tweeq.Core
{
    /// <summary>
    /// Rotary's drag accumulation. Kept separate so the snapped result is never fed back into the drag state.
    /// </summary>
    public static class RotaryLogic
    {
        /// <summary>
        /// Accumulates a delta originating from the pointer.
        /// local is always the raw accumulated value (never snapped); snapping is applied only to output.
        /// Otherwise the value would jump when snapping is turned off.
        /// </summary>
        public static (double local, double output) GetDragValue(
            double local, double delta, double snap, bool shouldSnap)
        {
            double nextLocal = local + delta;
            if (!shouldSnap || !TweeqMath.IsFinite(snap) || snap == 0.0)
            {
                return (nextLocal, nextLocal);
            }

            // Aligns the rounding direction with the reference implementation's quantize (avoiding C#'s default banker's rounding).
            double output = Math.Round(nextLocal / snap, MidpointRounding.AwayFromZero) * snap;
            return (nextLocal, TweeqMath.NormalizeZero(output));
        }
    }
}
