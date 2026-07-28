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
        #region Constants

        /// <summary>入力欄のテキストサイズ（px）。</summary>
        public const float TEXT_FONT_SIZE = 12f;

        /// <summary>disabled 時のインセット枠の太さ（px）。</summary>
        public const float DISABLED_BORDER_WIDTH = 1f;

        // TextField の内側要素。背景・枠を消して 24px の高さを使い切るために触る
        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

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

        /// <summary>
        /// disabled 表現の付け外し（仕様 §5: 背景透明 + 1px Border のインセット枠）。
        /// </summary>
        /// <remarks>
        /// 解除側で通常の背景色を塗り直さないのは、hover 状態を知っているのが呼び出し側だから。
        /// 解除後は <see cref="ResolveBackground"/> の結果を背景へ入れること。
        /// </remarks>
        public static void ApplyDisabledChrome(VisualElement element, TweeqTheme theme, bool disabled)
        {
            if (element == null)
            {
                return;
            }

            if (!disabled)
            {
                SetBorderWidth(element, 0f);
                return;
            }

            element.style.backgroundColor = Color.clear;
            SetBorderWidth(element, DISABLED_BORDER_WIDTH);

            if (theme != null)
            {
                SetBorderColor(element, theme.Border);
            }
        }

        #endregion

        #region Text field

        /// <summary>
        /// 常時表示の <see cref="TextField" /> を入力欄の 24px 枠に収める正規化一式。
        /// </summary>
        /// <remarks>
        /// <para>
        /// UI Toolkit 既定の USS は上下 padding と auto 高さを入れてくるので、
        /// そのままだと 24px の枠内で行が潰れて読めなくなる（feedback-fixes-01.md A-6）。
        /// 高さ・余白・文字サイズを明示し、背景と枠は外側の箱に任せる。
        /// </para>
        /// <para>
        /// 左右 padding は 0 に倒す。値の中央寄せ幅は widget ごとに違うので、
        /// 必要な側が呼び出し後に上書きする（NumberInput / StringInput は 0.5em ぶん入れる）。
        /// </para>
        /// </remarks>
        public static void ApplyTextField(TextField field, TweeqTheme theme)
        {
            if (field == null)
            {
                return;
            }

            field.style.fontSize = TEXT_FONT_SIZE;
            field.style.paddingLeft = 0f;
            field.style.paddingRight = 0f;
            field.style.paddingTop = 0f;
            field.style.paddingBottom = 0f;
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            field.style.marginTop = 0f;
            field.style.marginBottom = 0f;
            field.style.minHeight = 0f;
            field.style.alignItems = Align.Stretch;

            ApplyTextSelectionColors(field, theme);

            VisualElement textInput = field.Q(TEXT_INPUT_NAME);
            if (textInput != null)
            {
                textInput.style.backgroundColor = Color.clear;
                SetBorderWidth(textInput, 0f);
                SetBorderColor(textInput, Color.clear);
                textInput.style.paddingLeft = 0f;
                textInput.style.paddingRight = 0f;
                textInput.style.paddingTop = 0f;
                textInput.style.paddingBottom = 0f;
                textInput.style.marginLeft = 0f;
                textInput.style.marginRight = 0f;
                textInput.style.marginTop = 0f;
                textInput.style.marginBottom = 0f;
                textInput.style.height = Length.Percent(100f);
                textInput.style.minHeight = 0f;
                textInput.style.fontSize = TEXT_FONT_SIZE;
                textInput.style.whiteSpace = WhiteSpace.NoWrap;
            }

            // 実際に字を描くのは unity-text-input の中の TextElement。
            // 縦潰れは input 側だけ直しても残るのでこちらにも同じ指定を掛ける
            TextElement textElement = textInput != null ? textInput.Q<TextElement>() : null;
            if (textElement != null)
            {
                textElement.style.height = Length.Percent(100f);
                textElement.style.minHeight = 0f;
                textElement.style.paddingTop = 0f;
                textElement.style.paddingBottom = 0f;
                textElement.style.marginTop = 0f;
                textElement.style.marginBottom = 0f;
                textElement.style.fontSize = TEXT_FONT_SIZE;
            }
        }

        #endregion

        #region Internals

        // キャレット・選択色は USS 既定（黒）のままだと暗背景で見えない。
        // selectionColor は obsolete だが、推奨の --unity-selection-color は C# から
        // インスタンス単位で設定できない（テーマは TweeqTheme 駆動）ため使い続ける。
        // 警告の抑止をこの 1 メソッドに閉じ込めるのが公開 API 化の目的の一つ
        static void ApplyTextSelectionColors(TextField field, TweeqTheme theme)
        {
            if (theme == null)
            {
                return;
            }

#pragma warning disable 618
            field.textSelection.cursorColor = theme.Text;
            field.textSelection.selectionColor = theme.AccentSoft;
#pragma warning restore 618
        }

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
