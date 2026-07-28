namespace Tweeq.Core
{
    #region Data

    /// <summary>アンカーに対する希望配置。`Start`/`End` はクロス軸の寄せ、無印はセンタリング。</summary>
    public enum PopoverPlacement
    {
        Top, TopStart, TopEnd,
        Bottom, BottomStart, BottomEnd,
        Left, LeftStart, LeftEnd,
        Right, RightStart, RightEnd,
    }

    /// <summary>
    /// Core は noEngineReferences なので UnityEngine.Vector2 が使えない。描画境界で float 化する前提の double 版。
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
    /// UnityEngine.Rect の double 版。UI Toolkit と同じく「左上原点・Y 下向き」で扱う。
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

        /// <summary>worldBound のような「4 辺」表現からの変換用。</summary>
        public static TweeqRect FromEdges(double left, double top, double right, double bottom)
        {
            return new TweeqRect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>配置計算の結果。座標はすべて panel（ルート）座標系。</summary>
    public readonly struct PopoverResult
    {
        /// <summary>popover 左上の X。</summary>
        public readonly double X;

        /// <summary>popover 左上の Y。</summary>
        public readonly double Y;

        /// <summary>flip 後の実効 placement。</summary>
        public readonly PopoverPlacement Effective;

        /// <summary>矢印が付く辺。0=Top 1=Bottom 2=Left 3=Right（<see cref="PopoverLogic.ARROW_SIDE_TOP"/> 等）。</summary>
        public readonly int ArrowSide;

        /// <summary>辺に沿った矢印中心位置（popover 左上からの距離）。</summary>
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
    /// アンカー配置 + 画面端回避（flip / shift）+ 矢印方向の純関数。UnityEngine 非依存・すべて double。
    /// Vue 原典が CSS Anchor Positioning に任せている部分を手計算で再現する。
    /// </summary>
    public static class PopoverLogic
    {
        #region Constants

        /// <summary>viewport 端に確保する余白（Popover.vue の VIEWPORT_MARGIN）。</summary>
        public const double DEFAULT_VIEWPORT_MARGIN = 8.0;

        public const int ARROW_SIDE_TOP = 0;
        public const int ARROW_SIDE_BOTTOM = 1;
        public const int ARROW_SIDE_LEFT = 2;
        public const int ARROW_SIDE_RIGHT = 3;

        // ArrowOffset のクランプ域を決めるためだけに Core にも持つ。TweeqBalloon 側と必ず同じ値にすること
        // （ズレると矢印が角丸に食い込む）。
        public const double ARROW_WIDTH = 14.0;
        public const double CORNER_RADIUS = 13.0;

        // 「ちょうど端に接する」候補を丸め誤差で不合格にしないための許容差。
        const double FIT_EPSILON = 1e-6;

        // 内部の side 表現は ARROW_SIDE_* と同じ番号を使い回す（対辺の導出が引き算 1 本で済む）。
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
        /// 希望 placement から popover 左上座標・実効 placement・矢印を求める。
        /// 手順は 1) 基本配置 2) flip（CSS position-try-fallbacks 相当）3) shift + クランプ 4) 矢印。
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
                // CSS の position-try-fallbacks: flip-block, flip-inline, flip-block flip-inline と同じ順序・
                // 「最初に収まった候補を採る」規則。どれも収まらなければ元の placement のまま次段のクランプに委ねる。
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

            // Vue 原典はクロス軸だけを shift するが、flip が全滅した時にメイン軸が画面外に残るので両軸に同じ式を適用する。
            // 端が両方はみ出す（popover が viewport より大きい）場合は開始側の端を優先するのも原典と同じ。
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

        /// <summary>flip 前の素の配置。メイン軸は anchor の対辺 + offsetMain、クロス軸は align に従う。</summary>
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
                // CSS では end 側の辺を anchor の end 辺に留めるので、offsetCross は内側（開始方向）へ効く。
                return anchorMax - size - offsetCross;
            }

            return (anchorMin + anchorMax) * 0.5 - size * 0.5;
        }

        /// <summary>
        /// 「収まる」は viewportMargin 込みで判定する。margin ぶんの shift が要らない候補だけを合格にしないと、
        /// flip 直後にさらに shift が走って矢印が anchor から離れてしまう。
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

        /// <summary>ブロック軸（縦書きでない前提で上下）の反転。左右配置ではクロス軸なので start/end が入れ替わる。</summary>
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
                // Left / Right（センタリング）はブロック軸に非対称性が無いので不変。
                default: return placement;
            }
        }

        /// <summary>インライン軸（左右）の反転。上下配置ではクロス軸なので start/end が入れ替わる。</summary>
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
        /// 矢印は「実際に landed した位置」から導く（flip に自動追従させるため）。
        /// anchor と重なっていてどの辺とも判定できない時だけ、希望 placement の対辺へフォールバックする。
        /// </summary>
        static int ResolveArrowSide(TweeqRect anchor, double x, double y, TweeqVec2 size, PopoverPlacement requested)
        {
            // ±1px は「辺がぴったり接する」ケースを取りこぼさないための許容（React core と同値）。
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

            // React core にはこの分岐が無く「anchor の左側」も暗黙にフォールバックへ落ちるが、
            // それだと Left 配置で矢印が逆側に付く。網羅性のため明示する。
            if (x + size.X <= anchor.Left + 1.0)
            {
                return ARROW_SIDE_RIGHT;
            }

            // Vue 原典の `else arrow = 'right'` は網羅性を欠くので不採用（porting-notes の逸脱記録どおり）。
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

            // 角丸に食い込むと輪郭が破綻するので、矢印の底辺半分ぶんだけ内側に寄せた範囲へ収める。
            double limit = CORNER_RADIUS + ARROW_WIDTH * 0.5;
            if (edge <= limit * 2.0)
            {
                // 辺が短すぎてクランプ域が反転する場合は中央固定（Balloon 側の r = min(radius, w/2, h/2) と同じ発想）。
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
