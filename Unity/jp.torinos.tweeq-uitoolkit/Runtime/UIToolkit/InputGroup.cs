using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 入力ボックスを隣接させて並べるコンテナ（仕様 §1）。
    /// 仕切り線もボーダー結合も持たず、gap 2px と各子の角丸だけで「つながり」を表現する。
    /// </summary>
    [UxmlElement]
    public partial class InputGroup : VisualElement, ITweeqThemed
    {
        #region Fields

        FlexDirection _direction = FlexDirection.Row;
        TweeqTheme _theme = TweeqTheme.Dark();

        // GeometryChangedEvent は毎レイアウトで飛んでくるので、
        // 子構成が変わった時だけ再割り当てするためのガード
        int _positionedChildCount = -1;

        #endregion

        #region Public API

        /// <summary>並びの方向。Row（既定）なら InlinePosition、Column なら BlockPosition を割り当てる。</summary>
        [UxmlAttribute("direction")]
        public FlexDirection Direction
        {
            get => _direction;
            set
            {
                if (_direction == value)
                {
                    return;
                }

                _direction = value;
                this.style.flexDirection = _direction;
                RefreshPositions();
            }
        }

        /// <summary>gap の値を取る配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                RefreshPositions();
            }
        }

        /// <summary>子を追加して位置を再割り当てする。</summary>
        public new void Add(VisualElement child)
        {
            if (child == null)
            {
                return;
            }

            base.Add(child);
            RefreshPositions();
        }

        /// <summary>子を取り外して位置を再割り当てする。</summary>
        public new void Remove(VisualElement child)
        {
            if (child == null || child.parent != this)
            {
                return;
            }

            base.Remove(child);
            RefreshPositions();
        }

        /// <summary>子をすべて取り外す。</summary>
        public new void Clear()
        {
            base.Clear();
            RefreshPositions();
        }

        /// <summary>
        /// 子の位置と gap を割り当て直す。位置の対象は <see cref="ITweeqInputBox"/> 実装子のみ。
        /// 子構成を直接いじった場合は手動で呼ぶ。
        /// </summary>
        public void RefreshPositions()
        {
            int childCount = this.childCount;
            int boxCount = 0;

            for (int i = 0; i < childCount; i++)
            {
                if (this.ElementAt(i) is ITweeqInputBox)
                {
                    boxCount++;
                }
            }

            _positionedChildCount = childCount;

            float gap = _theme != null ? _theme.GapGroup : 0f;
            bool row = _direction == FlexDirection.Row || _direction == FlexDirection.RowReverse;
            int boxIndex = 0;

            for (int i = 0; i < childCount; i++)
            {
                VisualElement child = this.ElementAt(i);
                if (child == null)
                {
                    continue;
                }

                ApplyGap(child, gap, row, i == childCount - 1);

                if (!(child is ITweeqInputBox box))
                {
                    continue;
                }

                // 2 個未満なら「つながり」が存在しないので割り当てない（仕様 §1）
                TweeqBoxPosition position = boxCount < 2
                    ? TweeqBoxPosition.None
                    : Resolve(boxIndex, boxCount);

                if (row)
                {
                    box.InlinePosition = position;
                    box.BlockPosition = TweeqBoxPosition.None;
                }
                else
                {
                    box.BlockPosition = position;
                    box.InlinePosition = TweeqBoxPosition.None;
                }

                ApplyStretch(child);
                boxIndex++;
            }
        }

        #endregion

        #region Construction

        public InputGroup()
        {
            this.AddToClassList("tweeq-input-group");
            this.style.flexDirection = _direction;
            this.style.flexGrow = 1f;

            // 子の追加は InputGroup.Add 経由が正規ルートだが、hierarchy 直操作や
            // VisualElement 型経由の Add を取りこぼさないための保険を 2 段掛ける
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel, TrickleDown.TrickleDown);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        #endregion

        #region Internals

        static TweeqBoxPosition Resolve(int index, int count)
        {
            if (index == 0)
            {
                return TweeqBoxPosition.Start;
            }

            return index == count - 1 ? TweeqBoxPosition.End : TweeqBoxPosition.Middle;
        }

        // UI Toolkit 6000.3 のインラインスタイルには flex gap が無いので、子のマージンで代替する。
        // 末尾の 1 個だけ外すのは「間隔」であって「余白」ではないため。
        // 判定は ITweeqInputBox 実装子ではなく全子で行う（末尾がラベル等でも間隔は要る）
        static void ApplyGap(VisualElement child, float gap, bool row, bool last)
        {
            float value = last ? 0f : gap;
            child.style.marginRight = row ? value : 0f;
            child.style.marginBottom = row ? 0f : value;
        }

        // 幅（高さ）を等分する。NumberInput は子がすべて絶対配置でコンテンツ幅を持たないため、
        // これが無いと minWidth まで潰れる。呼び出し側が明示指定済みなら尊重する
        static void ApplyStretch(VisualElement child)
        {
            if (child.style.flexGrow.keyword == StyleKeyword.Null)
            {
                child.style.flexGrow = 1f;
            }

            if (child.style.flexBasis.keyword == StyleKeyword.Null)
            {
                child.style.flexBasis = 0f;
            }
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            RefreshPositions();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // スタイル更新でこのコールバックが再入しても、子数が同じなら何もしない
            if (_positionedChildCount == this.childCount)
            {
                return;
            }

            RefreshPositions();
        }

        #endregion
    }
}
