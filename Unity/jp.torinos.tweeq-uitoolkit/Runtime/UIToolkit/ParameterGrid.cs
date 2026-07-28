using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// UI Toolkit には CSS の gap が無いため、子のマージンで隙間を作る。
    /// ParameterGrid ファミリー共通の実装なのでここに集約する。
    /// </summary>
    internal static class TweeqGap
    {
        /// <summary>先頭以外の子へ主軸方向のマージンを配る。</summary>
        public static void Apply(VisualElement container, float gap, FlexDirection direction)
        {
            if (container == null)
            {
                return;
            }

            bool horizontal = direction == FlexDirection.Row || direction == FlexDirection.RowReverse;
            int count = container.childCount;

            for (int i = 0; i < count; i++)
            {
                VisualElement child = container.ElementAt(i);
                if (child == null)
                {
                    continue;
                }

                // 同値の代入はインラインスタイル側で弾かれるので、レイアウトは汚れない
                float value = i == 0 ? 0f : gap;
                if (horizontal)
                {
                    child.style.marginLeft = value;
                }
                else
                {
                    child.style.marginTop = value;
                }
            }
        }
    }

    /// <summary>
    /// ラベル列｜入力列の 2 カラムレイアウト（仕様 §3）。
    ///
    /// 本家は `grid-template-columns: minmax(60px, max-content) minmax(0, 1fr)` だが
    /// UI Toolkit に CSS grid は無い。そこで「配下の Parameter のラベル希望幅を実測し、
    /// max(60, 最大値) を全行へ配る」ことで max-content 共有列を再現する（仕様 §5-5）。
    /// ParameterGroup の中の Parameter も同じ Grid から幅をもらう（仕様 §5-6）。
    /// </summary>
    [UxmlElement]
    public partial class ParameterGrid : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>ラベル列の下限幅（本家 minmax の 60px）。</summary>
        public const float MIN_LABEL_WIDTH = 60f;

        // 本家 .TqParameterGrid の padding-left
        const float PADDING_LEFT = 3f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        // Refresh のたびに作り直さないよう使い回す
        readonly List<Parameter> _parameters = new List<Parameter>();

        IVisualElementScheduledItem _pendingRefresh;

        #endregion

        #region Public API

        /// <summary>
        /// 配色テーマ。Refresh のたびに配下の Parameter / ParameterHeading /
        /// ParameterGroup へ伝播するので、個別に設定し直す必要はない。
        /// </summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // 同一インスタンスでも打ち切らない。テーマ設定後に足された行へ届ける
                // 再配布の入り口はこの setter しか無い（M7 転送契約の取りこぼし修正）
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                RequestRefresh();
                TweeqThemeDistribution.Distribute(this, _theme);
            }
        }

        /// <summary>行間の再配置とラベル列幅の再配布を即座に行う。</summary>
        public void Refresh()
        {
            ApplyRowGaps();
            DistributeLabelWidths();
        }

        #endregion

        #region Construction

        public ParameterGrid()
        {
            this.AddToClassList("tweeq-parameter-grid");
            this.style.flexDirection = FlexDirection.Column;
            this.style.alignItems = Align.Stretch;

            ApplyStaticStyles();

            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        void ApplyStaticStyles()
        {
            this.style.paddingLeft = PADDING_LEFT;
        }

        #endregion

        #region Refresh scheduling

        /// <summary>
        /// 子（Parameter / Group）側から呼ぶ再計算要求。同一フレーム内の複数要求は 1 回に畳む。
        /// </summary>
        internal void RequestRefresh()
        {
            if (this.panel == null)
            {
                // パネル外ではスケジューラが回らないので即時に済ませる
                Refresh();
                return;
            }

            if (_pendingRefresh != null)
            {
                return;
            }

            _pendingRefresh = this.schedule.Execute(() =>
            {
                _pendingRefresh = null;
                Refresh();
            }).StartingIn(0);
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // GeometryChangedEvent はバブルするので、入力欄のアニメーション中も
            // 際限なく飛んでくる。自分自身のレイアウト変化（＝行の増減・パネル幅変更）
            // だけを再計算のきっかけにする。行の追加は Parameter 側の
            // AttachToPanelEvent からも通知される
            if (evt == null || !ReferenceEquals(evt.target, this))
            {
                return;
            }

            // 幅を書き戻すと再びここへ来るが、値が変わらなければ ApplyLabelWidth が
            // 何も書かないのでループは 1 往復で止まる
            RequestRefresh();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            RequestRefresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // 剥がしたパネルのスケジュール項目を掴んだままだと、次に載せたとき
            // 「予約済み」と誤認して二度と再計算されなくなる
            _pendingRefresh?.Pause();
            _pendingRefresh = null;
        }

        #endregion

        #region Layout

        void ApplyRowGaps()
        {
            TweeqGap.Apply(this.contentContainer, _theme.GapControl, FlexDirection.Column);
        }

        void DistributeLabelWidths()
        {
            _parameters.Clear();
            Collect(this.contentContainer);

            float target = MIN_LABEL_WIDTH;
            for (int i = 0; i < _parameters.Count; i++)
            {
                float measured = _parameters[i].MeasureLabelWidth();
                if (measured > target)
                {
                    target = measured;
                }
            }

            for (int i = 0; i < _parameters.Count; i++)
            {
                _parameters[i].ApplyLabelWidth(target);
            }
        }

        // この Grid が面倒を見る Parameter を集めつつ、テーマも配る。
        // ネストした ParameterGrid は自前で幅を配るので踏み込まない（仕様 §5-6）。
        void Collect(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            int count = element.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                VisualElement child = element.hierarchy.ElementAt(i);
                if (child == null || child is ParameterGrid)
                {
                    continue;
                }

                if (child is Parameter parameter)
                {
                    parameter.Theme = _theme;
                    _parameters.Add(parameter);

                    // Parameter の中身は入力欄なので、これ以上降りる意味は無い
                    continue;
                }

                if (child is ParameterHeading heading)
                {
                    heading.Theme = _theme;
                    continue;
                }

                if (child is ParameterGroup group)
                {
                    group.Theme = _theme;
                    Collect(group.Content);
                    continue;
                }

                Collect(child);
            }
        }

        #endregion

        #region Helpers

        /// <summary>自身を含めず祖先方向へ辿り、幅を配ってくれる Grid を探す（仕様 §5-6）。</summary>
        internal static ParameterGrid Find(VisualElement element)
        {
            VisualElement current = element?.hierarchy.parent;
            while (current != null)
            {
                if (current is ParameterGrid grid)
                {
                    return grid;
                }

                current = current.hierarchy.parent;
            }

            return null;
        }

        #endregion
    }
}
