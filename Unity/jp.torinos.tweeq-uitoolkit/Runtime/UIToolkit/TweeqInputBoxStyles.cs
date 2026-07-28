using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 入力欄の「クローム」（枠・角丸・背景）を組み立てるヘルパ。
    /// </summary>
    /// <remarks>
    /// 実装は NumberInput を正典として抽出したもので、見た目はビット単位で同一。
    /// 外部 asmdef のカスタムウィジェットが tweeq の入力欄と同じ外装を持てるように
    /// public 化した（ext-custom-widgets-spec.md EXT-01-A）。
    /// </remarks>
    public static class TweeqInputBoxStyles
    {
        #region Edge helpers

        /// <summary>4 辺の border 幅を一括で設定する。</summary>
        public static void SetBorderWidth(VisualElement element, float width)
        {
            if (element == null)
            {
                return;
            }

            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
        }

        /// <summary>4 辺の border 色を一括で設定する。</summary>
        public static void SetBorderColor(VisualElement element, Color color)
        {
            if (element == null)
            {
                return;
            }

            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        /// <summary>4 隅の角丸半径を一括で設定する。</summary>
        public static void SetCornerRadius(VisualElement element, float radius)
        {
            SetCornerRadius(element, radius, true, true, true, true);
        }

        #endregion

        #region Chrome

        /// <summary>
        /// グループ内での位置に応じて角丸を潰す（仕様 §1 の角丸表）。
        /// </summary>
        /// <remarks>
        /// 両軸の指定は OR で合成する（片方でも「潰す」なら潰す）。
        /// フォーカスリングのように別レイヤで枠を描く要素にも同じ引数で掛けること。
        /// </remarks>
        public static void ApplyCornerRadius(
            VisualElement element,
            TweeqTheme theme,
            TweeqBoxPosition inlinePosition,
            TweeqBoxPosition blockPosition)
        {
            if (element == null)
            {
                return;
            }

            float radius = theme != null ? theme.InputRadius : 0f;

            bool topLeft = true;
            bool topRight = true;
            bool bottomLeft = true;
            bool bottomRight = true;

            switch (inlinePosition)
            {
                case TweeqBoxPosition.Start:
                    topRight = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    bottomLeft = false;
                    break;
            }

            switch (blockPosition)
            {
                case TweeqBoxPosition.Start:
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    topRight = false;
                    break;
            }

            SetCornerRadius(element, radius, topLeft, topRight, bottomLeft, bottomRight);
        }

        /// <summary>
        /// 背景色だけをトランジションさせる（仕様 §5: 0.15s / cubic-bezier(0.4,0,0.2,1)）。
        /// </summary>
        /// <remarks>
        /// UI Toolkit に同一カーブが無いので EaseInOutCubic で近似する
        /// （NumberInput / RotaryInput と同じ判断）。
        /// </remarks>
        public static void ApplyBackgroundTransition(VisualElement element, TweeqTheme theme)
        {
            if (element == null || theme == null)
            {
                return;
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("background-color") });
            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(theme.HoverTransitionDuration, TimeUnit.Second) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
        }

        /// <summary>ホバー状態に応じた入力欄の背景色を返す。</summary>
        /// <remarks>
        /// disabled は「背景透明 + 1px Border のインセット枠」で色ではなく構成が変わるため、
        /// ここでは扱わない（呼び出し側が分岐して <see cref="SetBorderWidth"/> を掛ける）。
        /// </remarks>
        public static Color ResolveBackground(TweeqTheme theme, bool hovered)
        {
            if (theme == null)
            {
                return Color.clear;
            }

            return hovered ? theme.InputHover : theme.Input;
        }

        #endregion

        #region Internals

        static void SetCornerRadius(
            VisualElement element,
            float radius,
            bool topLeft,
            bool topRight,
            bool bottomLeft,
            bool bottomRight)
        {
            if (element == null)
            {
                return;
            }

            element.style.borderTopLeftRadius = topLeft ? radius : 0f;
            element.style.borderTopRightRadius = topRight ? radius : 0f;
            element.style.borderBottomLeftRadius = bottomLeft ? radius : 0f;
            element.style.borderBottomRightRadius = bottomRight ? radius : 0f;
        }

        #endregion
    }
}
