using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>The chain icon drawn on the ratio-lock toggle, shared by <see cref="SizeInput"/> and <see cref="Size3Input"/>.</summary>
    internal static class SizeChainIcon
    {
        // Chain icon (a simplified version of the reference implementation's link-icon painter). Anchored to the center of the 24px box
        const float LINK_LOOP_OFFSET = 4.5f;
        const float LINK_LOOP_RADIUS = 3.2f;
        const float LINK_STROKE_WIDTH = 1.25f;
        const float LINK_BAR_WIDTH = 1f;

        internal static void Paint(MeshGenerationContext context, VisualElement icon, TweeqTheme theme, bool keepRatio)
        {
            if (context == null || theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Vector2 center = icon.contentRect.center;

            // The fill color is painted by the toggle side, so choose a color that reads well on top of it
            painter.strokeColor = keepRatio
                ? TweeqTheme.ContrastText(theme.Accent)
                : theme.TextSubtle;
            painter.lineWidth = LINK_STROKE_WIDTH;
            painter.lineCap = LineCap.Butt;

            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 loop = new Vector2(center.x + side * LINK_LOOP_OFFSET, center.y);
                painter.BeginPath();
                painter.Arc(
                    loop,
                    LINK_LOOP_RADIUS,
                    new Angle(0f, AngleUnit.Degree),
                    new Angle(360f, AngleUnit.Degree));
                painter.ClosePath();
                painter.Stroke();
            }

            if (!keepRatio)
            {
                return;
            }

            // Bridge the two loops only while connected (when broken, the gap itself reads as the "disconnected" symbol)
            painter.lineWidth = LINK_BAR_WIDTH;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - LINK_LOOP_OFFSET, center.y));
            painter.LineTo(new Vector2(center.x + LINK_LOOP_OFFSET, center.y));
            painter.Stroke();
        }
    }
}
