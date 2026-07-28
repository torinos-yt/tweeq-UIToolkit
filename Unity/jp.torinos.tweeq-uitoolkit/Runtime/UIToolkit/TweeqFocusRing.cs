using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 入力欄のフォーカスリング。host に重ねる絶対配置レイヤとして描く。
    /// </summary>
    /// <remarks>
    /// ホスト自身の border で描かないのは、border を足すと絶対配置の子（バー・ハンドル・
    /// TextField）が 1px 内側へずれてしまうため。picking は無効なので、重ねてもポインタは
    /// 下の要素へ抜ける。
    /// </remarks>
    public sealed class TweeqFocusRing : VisualElement
    {
        #region Constants

        /// <summary>リングの線幅（px）。</summary>
        public const float RING_WIDTH = 1f;

        /// <summary>既定の要素名。</summary>
        public const string DEFAULT_NAME = "tweeq-focus-ring";

        #endregion

        #region Construction

        public TweeqFocusRing()
        {
            this.name = DEFAULT_NAME;
            this.AddToClassList("tweeq-focus-ring");
            this.pickingMode = PickingMode.Ignore;

            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.right = 0f;
            this.style.bottom = 0f;
            this.style.display = DisplayStyle.None;

            TweeqInputBoxStyles.SetBorderWidth(this, RING_WIDTH);
        }

        /// <summary>
        /// リングを生成して host の最前面（子の末尾）へ重ねる。
        /// </summary>
        /// <remarks>
        /// host が null でもリング自体は返す。呼び出し側の参照を null にしないことで、
        /// 以降の <see cref="Apply" /> / <see cref="Visible" /> が素通りできる。
        /// </remarks>
        public static TweeqFocusRing Attach(VisualElement host)
        {
            TweeqFocusRing ring = new TweeqFocusRing();

            if (host != null)
            {
                host.hierarchy.Add(ring);
            }

            return ring;
        }

        #endregion

        #region Public API

        /// <summary>リングを表示するか。</summary>
        public bool Visible
        {
            get { return this.style.display.value == DisplayStyle.Flex; }
            set { this.style.display = value ? DisplayStyle.Flex : DisplayStyle.None; }
        }

        /// <summary>
        /// 色と角丸をテーマ・グループ位置へ追従させる。箱と同じ引数で呼ぶこと。
        /// </summary>
        public void Apply(
            TweeqTheme theme, TweeqBoxPosition inlinePosition, TweeqBoxPosition blockPosition)
        {
            if (theme == null)
            {
                return;
            }

            TweeqInputBoxStyles.SetBorderColor(this, theme.Accent);
            TweeqInputBoxStyles.ApplyCornerRadius(this, theme, inlinePosition, blockPosition);
        }

        #endregion
    }
}
