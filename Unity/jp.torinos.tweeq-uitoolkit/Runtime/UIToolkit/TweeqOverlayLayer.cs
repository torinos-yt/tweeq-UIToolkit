using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// パネル最前面に敷く共有オーバーレイ層。ポップオーバー／ツールチップ／ドラッグ中の
    /// ガイド描画など、レイアウトを乱さずに全面へ描きたいものの置き場所。
    /// 子要素の座標はこの層のローカル空間（＝パネル座標）で扱う。
    /// </summary>
    public sealed class TweeqOverlayLayer : VisualElement
    {
        #region Constants

        /// <summary>階層内でこの層を特定するための名前。</summary>
        public const string LAYER_NAME = "tweeq-overlay-layer";

        #endregion

        #region Construction

        public TweeqOverlayLayer()
        {
            this.name = LAYER_NAME;

            // 全面を覆うがヒットは一切奪わない
            this.pickingMode = PickingMode.Ignore;
            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.right = 0f;
            this.style.bottom = 0f;
            this.style.overflow = Overflow.Visible;
        }

        #endregion

        #region Public API

        /// <summary>
        /// context が属するパネルの最上位要素にぶら下がる層を取得する。無ければ作る。
        /// パネル未接続（panel == null）なら null を返すので、呼び出し側で必ず判定すること。
        /// </summary>
        public static TweeqOverlayLayer GetOrCreate(VisualElement context)
        {
            if (context == null || context.panel == null)
            {
                return null;
            }

            VisualElement root = context;
            while (root.hierarchy.parent != null)
            {
                root = root.hierarchy.parent;
            }

            int childCount = root.hierarchy.childCount;
            for (int index = 0; index < childCount; index++)
            {
                if (!(root.hierarchy.ElementAt(index) is TweeqOverlayLayer existing))
                {
                    continue;
                }

                // UI Toolkit は階層順に描くので、最後の子でないと後から追加された UI に隠れる
                if (index != childCount - 1)
                {
                    root.hierarchy.Remove(existing);
                    root.hierarchy.Add(existing);
                }

                return existing;
            }

            TweeqOverlayLayer layer = new TweeqOverlayLayer();
            root.hierarchy.Add(layer);
            return layer;
        }

        #endregion
    }
}
