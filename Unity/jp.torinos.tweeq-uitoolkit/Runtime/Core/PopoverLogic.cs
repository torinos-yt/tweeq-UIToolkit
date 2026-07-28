namespace Tweeq.Core
{
    #region Data

    /// <summary>Desired placement relative to the anchor. `Start`/`End` align along the cross axis; no suffix means centering.</summary>
    public enum PopoverPlacement
    {
        Top, TopStart, TopEnd,
        Bottom, BottomStart, BottomEnd,
        Left, LeftStart, LeftEnd,
        Right, RightStart, RightEnd,
    }

    /// <summary>
    /// Core is noEngineReferences, so UnityEngine.Vector2 isn't available. A double version, with the assumption that it becomes float at the rendering boundary.
    /// </summary>
    public readonly struct TweeqVec2
    {
        public readonly double X;
        public readonly double Y;

        public TweeqVec2(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// A double version of UnityEngine.Rect. Handled as "top-left origin, Y pointing down", same as UI Toolkit.
    /// </summary>
    public readonly struct TweeqRect
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Width;
        public readonly double Height;

        public TweeqRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double Left => X;

        public double Top => Y;

        public double Right => X + Width;

        public double Bottom => Y + Height;

        public double CenterX => X + Width * 0.5;

        public double CenterY => Y + Height * 0.5;

        /// <summary>For converting from a "four edges" representation like worldBound.</summary>
        public static TweeqRect FromEdges(double left, double top, double right, double bottom)
        {
            return new TweeqRect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>Result of the placement computation. All coordinates are in panel (root) space.</summary>
    public readonly struct PopoverResult
    {
        /// <summary>X of the popover's top-left corner.</summary>
        public readonly double X;

        /// <summary>Y of the popover's top-left corner.</summary>
        public readonly double Y;

        /// <summary>Effective placement after flipping.</summary>
        public readonly PopoverPlacement Effective;

        /// <summary>The edge the arrow is attached to. 0=Top 1=Bottom 2=Left 3=Right (see <see cref="PopoverLogic.ARROW_SIDE_TOP"/> etc.).</summary>
        public readonly int ArrowSide;

        /// <summary>Arrow's center position along the edge (distance from the popover's top-left corner).</summary>
        public readonly double ArrowOffset;

        public PopoverResult(double x, double y, PopoverPlacement effective, int arrowSide, double arrowOffset)
        {
            X = x;
            Y = y;
            Effective = effective;
            ArrowSide = arrowSide;
            ArrowOffset = arrowOffset;
        }
    }

    #endregion

    /// <summary>
    /// Pure function for anchor placement + edge-of-screen avoidance (flip / shift) + arrow direction. No UnityEngine dependency; all double.
    /// Reproduces by hand what the Vue original leaves to CSS Anchor Positioning.
    /// </summary>
    public static class PopoverLogic
    {
        #region Constants

        /// <summary>Margin reserved at the viewport edge (Popover.vue's VIEWPORT_MARGIN).</summary>
        public const double DEFAULT_VIEWPORT_MARGIN = 8.0;

        public const int ARROW_SIDE_TOP = 0;
        public const int ARROW_SIDE_BOTTOM = 1;
        public const int ARROW_SIDE_LEFT = 2;
        public const int ARROW_SIDE_RIGHT = 3;

        // Kept in Core purely to determine ArrowOffset's clamp range. Must always match the value on the TweeqBalloon side
        // (a mismatch would make the arrow dig into the rounded corner).
        public const double ARROW_WIDTH = 14.0;
        public const double CORNER_RADIUS = 13.0;

        // Tolerance so that a candidate that "exactly touches the edge" isn't rejected due to rounding error.
        const double FIT_EPSILON = 1e-6;

        // The internal side representation reuses the same numbers as ARROW_SIDE_* (deriving the opposite edge is then a single subtraction).
        const int SIDE_TOP = 0;
        const int SIDE_BOTTOM = 1;
        const int SIDE_LEFT = 2;
        const int SIDE_RIGHT = 3;

        const int ALIGN_CENTER = 0;
        const int ALIGN_START = 1;
        const int ALIGN_END = 2;

        #endregion

        #region Public API

        /// <summary>
        /// Computes the popover's top-left coordinate, effective placement, and arrow from the desired placement.
        /// Steps: 1) base placement 2) flip (equivalent to CSS position-try-fallbacks) 3) shift + clamp 4) arrow.
        /// </summary>
        public static PopoverResult Resolve(
            TweeqRect anchor, TweeqVec2 size, TweeqVec2 viewport,
            PopoverPlacement placement = PopoverPlacement.BottomStart,
            double offsetMain = 0.0, double offsetCross = 0.0,
            double viewportMargin = DEFAULT_VIEWPORT_MARGIN)
        {
            PopoverPlacement effective = placement;
            TweeqVec2 position = Place(anchor, size, placement, offsetMain, offsetCross);

            if (!Fits(position, size, viewport, viewportMargin))
            {
                // Same order as CSS's position-try-fallbacks: flip-block, flip-inline, flip-block flip-inline, and the
                // "take the first candidate that fits" rule. If none fit, the original placement is kept and left to the next-stage clamp.
                PopoverPlacement blockFlipped = FlipBlock(placement);
                PopoverPlacement inlineFlipped = FlipInline(placement);
                PopoverPlacement bothFlipped = FlipInline(blockFlipped);

                if (TryPlace(anchor, size, viewport, blockFlipped, offsetMain, offsetCross, viewportMargin,
                        out TweeqVec2 candidate))
                {
                    effective = blockFlipped;
                    position = candidate;
                }
                else if (TryPlace(anchor, size, viewport, inlineFlipped, offsetMain, offsetCross, viewportMargin,
                        out candidate))
                {
                    effective = inlineFlipped;
                    position = candidate;
                }
                else if (TryPlace(anchor, size, viewport, bothFlipped, offsetMain, offsetCross, viewportMargin,
                        out candidate))
                {
                    effective = bothFlipped;
                    position = candidate;
                }
            }

            // The Vue original shifts only the cross axis, but when every flip fails the main axis is left off-screen,
            // so the same formula is applied to both axes. Preferring the start-side edge when both edges overflow
            // (the popover is bigger than the viewport) also matches the original.
            double x = ShiftIntoViewport(position.X, size.X, viewport.X, viewportMargin);
            double y = ShiftIntoViewport(position.Y, size.Y, viewport.Y, viewportMargin);

            int arrowSide = ResolveArrowSide(anchor, x, y, size, placement);
            double arrowOffset = ResolveArrowOffset(anchor, x, y, size, arrowSide);

            return new PopoverResult(x, y, effective, arrowSide, arrowOffset);
        }

        #endregion

        #region Placement

        static bool TryPlace(
            TweeqRect anchor, TweeqVec2 size, TweeqVec2 viewport, PopoverPlacement placement,
            double offsetMain, double offsetCross, double viewportMargin, out TweeqVec2 position)
        {
            position = Place(anchor, size, placement, offsetMain, offsetCross);
            return Fits(position, size, viewport, viewportMargin);
        }

        /// <summary>Raw placement before flipping. The main axis is the anchor's opposite edge + offsetMain; the cross axis follows align.</summary>
        static TweeqVec2 Place(
            TweeqRect anchor, TweeqVec2 size, PopoverPlacement placement, double offsetMain, double offsetCross)
        {
            int side = SideOf(placement);
            int align = AlignOf(placement);

            switch (side)
            {
                case SIDE_TOP:
                    return new TweeqVec2(
                        CrossPosition(anchor.Left, anchor.Right, size.X, align, offsetCross),
                        anchor.Top - size.Y - offsetMain);
                case SIDE_BOTTOM:
                    return new TweeqVec2(
                        CrossPosition(anchor.Left, anchor.Right, size.X, align, offsetCross),
                        anchor.Bottom + offsetMain);
                case SIDE_LEFT:
                    return new TweeqVec2(
                        anchor.Left - size.X - offsetMain,
                        CrossPosition(anchor.Top, anchor.Bottom, size.Y, align, offsetCross));
                default:
                    return new TweeqVec2(
                        anchor.Right + offsetMain,
                        CrossPosition(anchor.Top, anchor.Bottom, size.Y, align, offsetCross));
            }
        }

        static double CrossPosition(double anchorMin, double anchorMax, double size, int align, double offsetCross)
        {
            if (align == ALIGN_START)
            {
                return anchorMin + offsetCross;
            }

            if (align == ALIGN_END)
            {
                // In CSS the end-side edge is pinned to the anchor's end edge, so offsetCross acts inward (toward the start direction).
                return anchorMax - size - offsetCross;
            }

            return (anchorMin + anchorMax) * 0.5 - size * 0.5;
        }

        /// <summary>
        /// "Fits" is judged inclusive of viewportMargin. Unless only candidates that need no margin-worth of shift are allowed to pass,
        /// a further shift would run right after the flip and pull the arrow away from the anchor.
        /// </summary>
        static bool Fits(TweeqVec2 position, TweeqVec2 size, TweeqVec2 viewport, double viewportMargin)
        {
            return position.X >= viewportMargin - FIT_EPSILON
                && position.Y >= viewportMargin - FIT_EPSILON
                && position.X + size.X <= viewport.X - viewportMargin + FIT_EPSILON
                && position.Y + size.Y <= viewport.Y - viewportMargin + FIT_EPSILON;
        }

        static double ShiftIntoViewport(double position, double size, double viewport, double viewportMargin)
        {
            if (position + size > viewport - viewportMargin)
            {
                position = viewport - viewportMargin - size;
            }

            return position < viewportMargin ? viewportMargin : position;
        }

        #endregion

        #region Flip

        /// <summary>Flips the block axis (top/bottom, assuming non-vertical writing mode). For left/right placements this is the cross axis, so start/end swap.</summary>
        static PopoverPlacement FlipBlock(PopoverPlacement placement)
        {
            switch (placement)
            {
                case PopoverPlacement.Top: return PopoverPlacement.Bottom;
                case PopoverPlacement.TopStart: return PopoverPlacement.BottomStart;
                case PopoverPlacement.TopEnd: return PopoverPlacement.BottomEnd;
                case PopoverPlacement.Bottom: return PopoverPlacement.Top;
                case PopoverPlacement.BottomStart: return PopoverPlacement.TopStart;
                case PopoverPlacement.BottomEnd: return PopoverPlacement.TopEnd;
                case PopoverPlacement.LeftStart: return PopoverPlacement.LeftEnd;
                case PopoverPlacement.LeftEnd: return PopoverPlacement.LeftStart;
                case PopoverPlacement.RightStart: return PopoverPlacement.RightEnd;
                case PopoverPlacement.RightEnd: return PopoverPlacement.RightStart;
                // Left / Right (centered) have no asymmetry along the block axis, so they're unchanged.
                default: return placement;
            }
        }

        /// <summary>Flips the inline axis (left/right). For top/bottom placements this is the cross axis, so start/end swap.</summary>
        static PopoverPlacement FlipInline(PopoverPlacement placement)
        {
            switch (placement)
            {
                case PopoverPlacement.Left: return PopoverPlacement.Right;
                case PopoverPlacement.LeftStart: return PopoverPlacement.RightStart;
                case PopoverPlacement.LeftEnd: return PopoverPlacement.RightEnd;
                case PopoverPlacement.Right: return PopoverPlacement.Left;
                case PopoverPlacement.RightStart: return PopoverPlacement.LeftStart;
                case PopoverPlacement.RightEnd: return PopoverPlacement.LeftEnd;
                case PopoverPlacement.TopStart: return PopoverPlacement.TopEnd;
                case PopoverPlacement.TopEnd: return PopoverPlacement.TopStart;
                case PopoverPlacement.BottomStart: return PopoverPlacement.BottomEnd;
                case PopoverPlacement.BottomEnd: return PopoverPlacement.BottomStart;
                default: return placement;
            }
        }

        #endregion

        #region Arrow

        /// <summary>
        /// The arrow is derived from the actually landed position (so it automatically follows flips).
        /// Falls back to the opposite side of the requested placement only when it overlaps the anchor and no edge can be determined.
        /// </summary>
        static int ResolveArrowSide(TweeqRect anchor, double x, double y, TweeqVec2 size, PopoverPlacement requested)
        {
            // ±1px tolerance so a side that exactly touches isn't missed (matches the value used by another reference implementation).
            if (y >= anchor.Bottom - 1.0)
            {
                return ARROW_SIDE_TOP;
            }

            if (y + size.Y <= anchor.Top + 1.0)
            {
                return ARROW_SIDE_BOTTOM;
            }

            if (x >= anchor.Right - 1.0)
            {
                return ARROW_SIDE_LEFT;
            }

            // Another reference implementation lacks this branch and implicitly falls through to the fallback
            // for "the anchor's left side" too, but that would attach the arrow to the wrong side for a Left placement.
            // Made explicit here for completeness.
            if (x + size.X <= anchor.Left + 1.0)
            {
                return ARROW_SIDE_RIGHT;
            }

            // The Vue original's `else arrow = 'right'` lacks completeness, so it's not adopted here (as recorded in the porting-notes deviation log).
            switch (SideOf(requested))
            {
                case SIDE_BOTTOM: return ARROW_SIDE_TOP;
                case SIDE_TOP: return ARROW_SIDE_BOTTOM;
                case SIDE_RIGHT: return ARROW_SIDE_LEFT;
                default: return ARROW_SIDE_RIGHT;
            }
        }

        static double ResolveArrowOffset(TweeqRect anchor, double x, double y, TweeqVec2 size, int arrowSide)
        {
            bool horizontal = arrowSide == ARROW_SIDE_TOP || arrowSide == ARROW_SIDE_BOTTOM;
            double center = horizontal ? anchor.CenterX - x : anchor.CenterY - y;
            double edge = horizontal ? size.X : size.Y;

            // Digging into the rounded corner would break the outline, so the range is kept inset by half the arrow's base width.
            double limit = CORNER_RADIUS + ARROW_WIDTH * 0.5;
            if (edge <= limit * 2.0)
            {
                // When the edge is too short and the clamp range would invert, fix it to the center (same idea as the Balloon side's r = min(radius, w/2, h/2)).
                return edge * 0.5;
            }

            return TweeqMath.Clamp(center, limit, edge - limit);
        }

        #endregion

        #region Placement decomposition

        static int SideOf(PopoverPlacement placement)
        {
            switch (placement)
            {
                case PopoverPlacement.Top:
                case PopoverPlacement.TopStart:
                case PopoverPlacement.TopEnd:
                    return SIDE_TOP;
                case PopoverPlacement.Bottom:
                case PopoverPlacement.BottomStart:
                case PopoverPlacement.BottomEnd:
                    return SIDE_BOTTOM;
                case PopoverPlacement.Left:
                case PopoverPlacement.LeftStart:
                case PopoverPlacement.LeftEnd:
                    return SIDE_LEFT;
                default:
                    return SIDE_RIGHT;
            }
        }

        static int AlignOf(PopoverPlacement placement)
        {
            switch (placement)
            {
                case PopoverPlacement.TopStart:
                case PopoverPlacement.BottomStart:
                case PopoverPlacement.LeftStart:
                case PopoverPlacement.RightStart:
                    return ALIGN_START;
                case PopoverPlacement.TopEnd:
                case PopoverPlacement.BottomEnd:
                case PopoverPlacement.LeftEnd:
                case PopoverPlacement.RightEnd:
                    return ALIGN_END;
                default:
                    return ALIGN_CENTER;
            }
        }

        #endregion
    }
}
