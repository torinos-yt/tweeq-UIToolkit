using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CorePlacement = Tweeq.Core.PopoverPlacement;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// パネルごとに1つだけ存在するツールチップの実体。アンカーを差し替えて使い回すので、
    /// 要素の数だけポップオーバーが増えることはない（Vue 版 TooltipRoot と同じ構成）。
    /// 通常は <see cref="TweeqTooltip"/> 経由で使い、直接触るのはテーマ差し替えの時くらい。
    /// </summary>
    public sealed class TweeqTooltipRoot
    {
        #region Constants

        /// <summary>表示までの遅延（ms）。</summary>
        public const long SHOW_DELAY_MS = 200L;

        /// <summary>
        /// 非表示までの遅延（ms）。0 でも「次のフレーム」まで待つのが肝で、
        /// leave → enter が連続する乗り移りの時に閉じずに済む。
        /// </summary>
        public const long HIDE_DELAY_MS = 0L;

        // ピル形状（Tooltip.vue の .TqTooltip）
        const float PILL_PADDING_VERTICAL = 2f;
        const float PILL_PADDING_HORIZONTAL = 6f;
        const float PILL_RADIUS = 9999f;
        const float FONT_SIZE = 11f;

        // .plain の max-width 18em を 0.9em（=11px）基準で px 化したもの
        const float MAX_WIDTH = 198f;

        #endregion

        #region Fields

        static readonly Dictionary<IPanel, TweeqTooltipRoot> Roots =
            new Dictionary<IPanel, TweeqTooltipRoot>();

        readonly TweeqPopover _popover;
        readonly Label _label;
        readonly EventCallback<DetachFromPanelEvent> _onLayerDetached;

        TweeqTheme _theme = TweeqTheme.Dark();
        IPanel _panel;
        TweeqOverlayLayer _layer;

        // 表示中のアンカー。遅延中はまだ null のまま
        VisualElement _reference;
        VisualElement _pendingShow;
        string _pendingText;
        VisualElement _pendingHide;

        // 遅延は 2 件のスケジュール項目を使い回す（毎回 new するとホバーのたびにゴミが出る）
        IVisualElementScheduledItem _showTimer;
        IVisualElementScheduledItem _hideTimer;

        #endregion

        #region Public API

        /// <summary>
        /// context のパネルに紐づくインスタンスを取得する。無ければ作る。
        /// パネル未接続なら null を返すので、呼び出し側で必ず判定すること。
        /// </summary>
        public static TweeqTooltipRoot GetOrCreate(VisualElement context)
        {
            if (context == null || context.panel == null)
            {
                return null;
            }

            IPanel panel = context.panel;
            if (Roots.TryGetValue(panel, out TweeqTooltipRoot existing))
            {
                existing.EnsureLayer(context);
                return existing;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(context);
            if (layer == null)
            {
                return null;
            }

            TweeqTooltipRoot root = new TweeqTooltipRoot(panel, layer);
            Roots.Add(panel, root);
            return root;
        }

        /// <summary>
        /// パネルが分からない状態（要素がデタッチ済みなど）でも、その要素のツールチップを確実に消す。
        /// 生存しているルートは通常 1 個なので走査コストは無視できる。
        /// </summary>
        public static void CloseAnyFor(VisualElement reference)
        {
            if (reference == null)
            {
                return;
            }

            foreach (KeyValuePair<IPanel, TweeqTooltipRoot> entry in Roots)
            {
                entry.Value.CloseNow(reference);
            }
        }

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _popover.Theme = _theme;
                _label.style.color = _theme.Text;
            }
        }

        /// <summary>
        /// reference にツールチップを表示する。既に開いていれば遅延なしで乗り移り、
        /// 閉じていれば <see cref="SHOW_DELAY_MS"/> だけ待つ。
        /// </summary>
        public void Show(VisualElement reference, string text)
        {
            if (reference == null || string.IsNullOrEmpty(text) || _layer == null)
            {
                return;
            }

            _pendingShow = reference;
            _pendingText = text;
            _pendingHide = null;

            _hideTimer?.Pause();
            _showTimer?.Pause();

            if (_popover.IsOpen)
            {
                Apply();
                return;
            }

            EnsureTimers();
            _showTimer?.ExecuteLater(SHOW_DELAY_MS);
        }

        /// <summary>reference のツールチップを引っ込める（次フレームまで猶予を持つ）。</summary>
        public void Hide(VisualElement reference)
        {
            _showTimer?.Pause();

            if (_pendingShow == reference)
            {
                _pendingShow = null;
            }

            if (!_popover.IsOpen)
            {
                return;
            }

            _pendingHide = reference;

            EnsureTimers();
            if (_hideTimer == null)
            {
                // scheduler が使えないなら猶予を諦めて即閉じる
                HideNow();
                return;
            }

            _hideTimer.ExecuteLater(HIDE_DELAY_MS);
        }

        /// <summary>表示中の文言を差し替える（表示中でなければ何もしない）。</summary>
        public void SetText(VisualElement reference, string text)
        {
            if (reference == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_pendingShow == reference)
            {
                _pendingText = text;
            }

            if (_popover.IsOpen && _reference == reference)
            {
                SetLabelText(text);
            }
        }

        /// <summary>reference が持っているツールチップを猶予なく閉じる（要素の破棄時など）。</summary>
        public void CloseNow(VisualElement reference)
        {
            if (_pendingShow == reference)
            {
                _pendingShow = null;
                _showTimer?.Pause();
            }

            if (_reference != reference)
            {
                return;
            }

            _pendingHide = null;
            _hideTimer?.Pause();
            _reference = null;
            _popover.Close();
        }

        #endregion

        #region Construction

        TweeqTooltipRoot(IPanel panel, TweeqOverlayLayer layer)
        {
            _panel = panel;

            _popover = new TweeqPopover
            {
                name = "tweeq-tooltip",

                // Escape や外側クリックで消えるとフォーカス操作の邪魔になるだけなので切る
                LightDismiss = false,
                Placement = CorePlacement.Top,
                Theme = _theme,

                // ツールチップがポインタを奪うと、その下の要素が leave 扱いになって明滅する
                pickingMode = PickingMode.Ignore,
            };
            _popover.Balloon.pickingMode = PickingMode.Ignore;
            _popover.Balloon.Radius = PILL_RADIUS;
            _popover.Balloon.PaddingVertical = PILL_PADDING_VERTICAL;
            _popover.Balloon.PaddingHorizontal = PILL_PADDING_HORIZONTAL;
            _popover.Balloon.contentContainer.pickingMode = PickingMode.Ignore;

            _label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            _label.style.fontSize = FONT_SIZE;
            _label.style.color = _theme.Text;
            _label.style.maxWidth = MAX_WIDTH;
            _label.style.whiteSpace = WhiteSpace.Normal;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.marginLeft = 0f;
            _label.style.marginRight = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;
            _popover.Add(_label);

            _onLayerDetached = OnLayerDetached;
            BindLayer(layer);
        }

        #endregion

        #region Layer binding

        void BindLayer(TweeqOverlayLayer layer)
        {
            if (_layer == layer)
            {
                return;
            }

            _layer?.UnregisterCallback(_onLayerDetached);
            _layer = layer;

            // 層が入れ替わったらスケジュール項目も無効になるので、次回に作り直させる
            _showTimer = null;
            _hideTimer = null;

            _layer?.RegisterCallback(_onLayerDetached);
        }

        // 層が消えていたら取り直す。UI 側で root を組み替えた時に黙って死なないようにするため
        void EnsureLayer(VisualElement context)
        {
            if (_layer != null && _layer.panel != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(context);
            if (layer != null)
            {
                BindLayer(layer);
            }
        }

        void OnLayerDetached(DetachFromPanelEvent evt)
        {
            _popover.Close();
            _reference = null;
            _pendingShow = null;
            _pendingHide = null;
            _showTimer = null;
            _hideTimer = null;

            if (_panel != null)
            {
                Roots.Remove(_panel);
                _panel = null;
            }
        }

        void EnsureTimers()
        {
            if (_layer == null || _layer.panel == null)
            {
                return;
            }

            if (_showTimer == null)
            {
                _showTimer = _layer.schedule.Execute(Apply);
                _showTimer.Pause();
            }

            if (_hideTimer == null)
            {
                _hideTimer = _layer.schedule.Execute(HideNow);
                _hideTimer.Pause();
            }
        }

        #endregion

        #region Show / hide

        void Apply()
        {
            _showTimer?.Pause();

            VisualElement reference = _pendingShow;
            if (reference == null || reference.panel == null)
            {
                return;
            }

            SetLabelText(_pendingText);
            _reference = reference;

            // 既に開いていれば Open はアンカーの差し替えとして働き、フェードはやり直さない
            _popover.Open(reference);
        }

        void HideNow()
        {
            _hideTimer?.Pause();

            // 猶予の間に別の要素へ乗り移っていたら、そちらのツールチップを巻き添えにしない
            if (_pendingHide != null && _reference != _pendingHide)
            {
                _pendingHide = null;
                return;
            }

            _pendingHide = null;
            _reference = null;
            _popover.Close();
        }

        // 同じ文字列なら Label 側のテキスト再生成を避ける
        void SetLabelText(string text)
        {
            if (_label.text == text)
            {
                return;
            }

            _label.text = text;
        }

        #endregion
    }
}
