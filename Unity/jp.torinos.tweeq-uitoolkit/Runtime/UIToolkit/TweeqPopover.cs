using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Tweeq.Core は noEngineReferences なので Rect / Vector2 を自前の double 構造体として持つ。
// `using Tweeq.Core;` すると UnityEngine 側の同名型と全面衝突するため、別名でだけ引き込む
using CorePlacement = Tweeq.Core.PopoverPlacement;
using CoreRect = Tweeq.Core.TweeqRect;
using CoreVector2 = Tweeq.Core.TweeqVec2;
using PopoverLogic = Tweeq.Core.PopoverLogic;
using PopoverResult = Tweeq.Core.PopoverResult;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// アンカーに追従して <see cref="TweeqOverlayLayer"/> 上に浮かぶ表示専用のポップオーバー。
    /// トリガーは持たない（Vue 版と同じく open は外部制御）。中身は普通に Add すればよく、
    /// 内部の <see cref="TweeqBalloon"/> に入る。
    /// </summary>
    [UxmlElement]
    public partial class TweeqPopover : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>ビューポート端に確保する余白（px）。Popover.vue の VIEWPORT_MARGIN。</summary>
        public const float DEFAULT_VIEWPORT_MARGIN = 8f;

        // 矢印なしポップアップ（Dropdown 等）の影。common.styl の box-shadow 0 0 20px 相当
        const float POPUP_SHADOW_BLUR = 20f;
        const float POPUP_SHADOW_OFFSET_Y = 0f;

        #endregion

        #region Fields

        // トランジション定義は不変なので 1 個だけ作って全インスタンスで共有する
        // （style.transition* は毎回 List を要求するため、都度 new すると開くたびにゴミが出る）
        static readonly StyleList<StylePropertyName> OpacityProperty =
            new StyleList<StylePropertyName>(new List<StylePropertyName> { new StylePropertyName("opacity") });

        static readonly StyleList<EasingFunction> EaseOut =
            new StyleList<EasingFunction>(new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) });

        static readonly StyleList<TimeValue> InstantDuration =
            new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) });

        TweeqTheme _theme = TweeqTheme.Dark();
        TweeqBalloon _balloon;

        // テーマ由来の秒数。テーマ差し替え時にだけ作り直す
        StyleList<TimeValue> _fadeDuration;

        TweeqOverlayLayer _layer;
        VisualElement _root;
        VisualElement _anchor;

        bool _isOpen;
        bool _useFixedPosition;
        Vector2 _fixedPosition;
        bool _arrow = true;
        bool _chrome = true;

        CorePlacement _placement = CorePlacement.BottomStart;
        double _offsetMain;
        double _offsetCross;
        float _viewportMargin = DEFAULT_VIEWPORT_MARGIN;

        // 毎回のメソッドグループ変換はデリゲートを確保するので、登録／解除で使い回す実体を持つ
        // アンカーと層で同じハンドラを使い回す（どちらが動いても要求は「再配置」だけ）
        readonly EventCallback<GeometryChangedEvent> _onWatchedGeometryChanged;
        readonly EventCallback<DetachFromPanelEvent> _onAnchorDetached;
        readonly EventCallback<PointerDownEvent> _onRootPointerDown;
        readonly EventCallback<KeyDownEvent> _onRootKeyDown;

        // 監視を掛けた層。閉じる時に確実に外すため、_layer とは別に持つ
        TweeqOverlayLayer _watchedLayer;

        // 「1フレーム後にサイズ確定を待って再配置＋フェードイン」の1件だけを使い回す
        IVisualElementScheduledItem _settleItem;

        #endregion

        #region Public API

        /// <summary>Close() が実際に閉じた時に一度だけ発火する。</summary>
        public event Action Closed;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _isOpen;

        /// <summary>吹き出し本体。半径・パディング・影を個別に詰めたい時に触る。</summary>
        public TweeqBalloon Balloon => _balloon;

        /// <summary>
        /// パネル解決に使う所有者要素。<see cref="Open(Vector2)"/> はアンカーを持たないため、
        /// これか直前のアンカーからオーバーレイ層を辿る。
        /// </summary>
        public VisualElement Context { get; set; }

        /// <summary>
        /// バルーン外装（Surface・border・padding・影）を popover 側で描くか（既定 true）。
        /// false は「オーバーレイ層へのホストと開閉だけ」の素通しモードで、外装は中身の責務になる
        /// （Dropdown は行幅とフィールドの位置合わせのため自前で外装を描く）。中身の親が変わるので
        /// Add より前に設定すること。
        /// </summary>
        [UxmlAttribute("chrome")]
        public bool Chrome
        {
            get => _chrome;
            set
            {
                if (_chrome == value)
                {
                    return;
                }

                _chrome = value;

                if (_balloon != null)
                {
                    _balloon.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        /// <summary>矢印を出すか。false だと角丸矩形のポップアップ（Dropdown 用）になる。</summary>
        [UxmlAttribute("arrow")]
        public bool Arrow
        {
            get => _arrow;
            set
            {
                if (_arrow == value)
                {
                    return;
                }

                _arrow = value;
                ApplyShadowStyle();

                if (!_arrow && _balloon != null)
                {
                    _balloon.ArrowSide = TweeqArrowSide.None;
                }

                Reposition();
            }
        }

        /// <summary>希望する配置。既定は BottomStart。画面端では自動で flip / shift する。</summary>
        [UxmlAttribute("placement")]
        public CorePlacement Placement
        {
            get => _placement;
            set
            {
                _placement = value;
                Reposition();
            }
        }

        /// <summary>メイン軸（アンカーから離れる方向）の追加オフセット。</summary>
        public double OffsetMain
        {
            get => _offsetMain;
            set
            {
                _offsetMain = value;
                Reposition();
            }
        }

        /// <summary>クロス軸（辺に沿う方向）の追加オフセット。</summary>
        public double OffsetCross
        {
            get => _offsetCross;
            set
            {
                _offsetCross = value;
                Reposition();
            }
        }

        /// <summary>ビューポート端に確保する余白（px）。</summary>
        public float ViewportMargin
        {
            get => _viewportMargin;
            set
            {
                _viewportMargin = value;
                Reposition();
            }
        }

        /// <summary>
        /// 外側クリック／Escape で自動的に閉じるか（既定 true）。
        /// false のときは閉じるのが所有者の責務になる（ネスト・Dropdown 用）。
        /// </summary>
        [UxmlAttribute("light-dismiss")]
        public bool LightDismiss { get; set; } = true;

        /// <summary>配色テーマ。null を渡した場合は Dark() にフォールバックする。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();

                if (_balloon != null)
                {
                    _balloon.Theme = _theme;
                }

                ApplyFadeTransition();
            }
        }

        /// <summary>中身はバルーンのコンテンツ層へ入る（Chrome=false のときは popover 直下）。</summary>
        public override VisualElement contentContainer
            => _chrome && _balloon != null ? _balloon.contentContainer : this;

        /// <summary>アンカー要素に追従させて開く。</summary>
        public void Open(VisualElement anchor)
        {
            if (anchor == null)
            {
                return;
            }

            UnwatchAnchor();
            _anchor = anchor;
            _useFixedPosition = false;

            if (Context == null)
            {
                Context = anchor;
            }

            OpenInternal(anchor);
        }

        /// <summary>パネル座標を直接指定して開く（Dropdown の macOS 風配置など）。</summary>
        public void Open(Vector2 position)
        {
            Open(position, Context ?? _anchor);
        }

        /// <summary>パネル座標を直接指定して開く。context はオーバーレイ層を辿るためだけに使う。</summary>
        public void Open(Vector2 position, VisualElement context)
        {
            if (context == null)
            {
                return;
            }

            UnwatchAnchor();
            _anchor = null;
            _useFixedPosition = true;
            _fixedPosition = position;
            Context = context;

            OpenInternal(context);
        }

        /// <summary>閉じる。開いていなければ何もしない（Closed も発火しない）。</summary>
        public void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            // RemoveFromHierarchy が DetachFromPanel を呼ぶので、先に降ろして再入を防ぐ
            _isOpen = false;

            _settleItem?.Pause();
            UnwatchRoot();
            UnwatchAnchor();
            this.RemoveFromHierarchy();

            _layer = null;
            _anchor = null;

            Closed?.Invoke();
        }

        /// <summary>
        /// その要素へのポインタ操作を「外側クリック」（＝ light dismiss で閉じる対象）と見なすか。
        /// </summary>
        /// <remarks>
        /// 自分の中身と、<b>入れ子のポップオーバー</b>（ピッカー内 Dropdown のリスト等。
        /// オーバーレイ層に兄弟として開く）だけが外側ではない。層の中でも
        /// <see cref="TweeqModal"/> の backdrop / pane はポップオーバーではないので外側扱いになり、
        /// モーダル内のクリックでネストしたポップオーバーが正しく閉じる。
        /// </remarks>
        public bool IsOutsideClick(VisualElement target)
        {
            if (target == null)
            {
                return true;
            }

            if (target == this || this.Contains(target))
            {
                return false;
            }

            // target から層まで遡る間に別の TweeqPopover があるときだけ免除する
            // （層の中でも backdrop / pane しか無い経路は外側扱い＝閉じる）。
            // 層の外に居る target はそのまま根まで辿るが、ポップオーバーは開いている間しか
            // 層に載らないので誤検出しない
            for (VisualElement node = target; node != null && node != _layer; node = node.hierarchy.parent)
            {
                if (node is TweeqPopover)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>アンカーやサイズが変わった時に呼ぶ再配置。開いていなければ何もしない。</summary>
        public void Reposition()
        {
            if (!_isOpen || _layer == null || _balloon == null)
            {
                return;
            }

            float width = this.layout.width;
            float height = this.layout.height;
            if (!IsUsableSize(width) || !IsUsableSize(height))
            {
                // まだレイアウトが降りていない。GeometryChanged で呼び直される
                return;
            }

            if (_useFixedPosition)
            {
                _balloon.ArrowSide = TweeqArrowSide.None;
                this.style.left = _fixedPosition.x;
                this.style.top = _fixedPosition.y;
                return;
            }

            // アンカーが破棄／デタッチされた後に開きっぱなしにしない
            if (_anchor == null || _anchor.panel == null)
            {
                return;
            }

            Rect anchorRect = _anchor.worldBound;
            Rect viewport = _layer.layout;
            if (!IsUsableSize(viewport.width) || !IsUsableSize(viewport.height))
            {
                return;
            }

            PopoverResult result = PopoverLogic.Resolve(
                new CoreRect(anchorRect.x, anchorRect.y, anchorRect.width, anchorRect.height),
                new CoreVector2(width, height),
                new CoreVector2(viewport.width, viewport.height),
                _placement,
                _offsetMain,
                _offsetCross,
                _viewportMargin);

            this.style.left = (float)result.X;
            this.style.top = (float)result.Y;

            _balloon.ArrowSide = _arrow ? ToArrowSide(result.ArrowSide) : TweeqArrowSide.None;
            _balloon.ArrowOffset = (float)result.ArrowOffset;
        }

        #endregion

        #region Construction

        public TweeqPopover()
        {
            this.name = "tweeq-popover";
            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.overflow = Overflow.Visible;

            // 影と矢印が切れないよう、幅はコンテンツなり
            this.style.alignItems = Align.FlexStart;

            _balloon = new TweeqBalloon { Theme = _theme };
            this.hierarchy.Add(_balloon);

            _onWatchedGeometryChanged = OnWatchedGeometryChanged;
            _onAnchorDetached = OnAnchorDetached;
            _onRootPointerDown = OnRootPointerDown;
            _onRootKeyDown = OnRootKeyDown;

            this.RegisterCallback<GeometryChangedEvent>(OnSelfGeometryChanged);
            this.RegisterCallback<DetachFromPanelEvent>(OnSelfDetached);

            ApplyShadowStyle();
            ApplyFadeTransition();
        }

        #endregion

        #region Open / Close internals

        void OpenInternal(VisualElement context)
        {
            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(context);
            if (layer == null)
            {
                // パネル未接続では置き場所が無い。例外は投げず「開かなかった」で済ませる
                return;
            }

            // 既に開いている状態での開き直しは、Closed を発火せずにアンカーを載せ替えるだけにする。
            // ツールチップの「乗り移り」がここを通るので、出現アニメを再生し直すと明滅する
            bool wasOpen = _isOpen;

            _layer = layer;
            if (this.hierarchy.parent != layer)
            {
                // 付け替えの Detach で OnSelfDetached → Close() が走らないよう、先に閉状態へ倒す
                _isOpen = false;
                this.RemoveFromHierarchy();
                layer.Add(this);
            }

            _isOpen = true;

            WatchAnchor();
            WatchRoot();

            // Vue と同じく、開いた瞬間に一度解決してから 1 フレーム後に確定サイズで詰め直す。
            // 初回フレームで矢印の位置が決まっていないと、バルーンの scale 原点がずれる
            if (!wasOpen)
            {
                // 2 回目以降は opacity が 1 のまま残っているので、そのまま 0 を入れると
                // 「消えるアニメ」が先に走る。開始値は duration 0 で当てる
                this.style.transitionDuration = InstantDuration;
                this.style.opacity = 0f;
            }

            Reposition();

            if (!wasOpen && _chrome)
            {
                _balloon.PlayIn();
            }

            if (_settleItem == null)
            {
                _settleItem = this.schedule.Execute(Settle);
            }

            _settleItem.ExecuteLater(0L);
        }

        void Settle()
        {
            if (!_isOpen)
            {
                return;
            }

            Reposition();

            this.style.transitionDuration = _fadeDuration;
            this.style.opacity = 1f;
        }

        void WatchAnchor()
        {
            if (_anchor == null)
            {
                return;
            }

            _anchor.RegisterCallback(_onWatchedGeometryChanged);
            _anchor.RegisterCallback(_onAnchorDetached);
        }

        void UnwatchAnchor()
        {
            if (_anchor == null)
            {
                return;
            }

            _anchor.UnregisterCallback(_onWatchedGeometryChanged);
            _anchor.UnregisterCallback(_onAnchorDetached);
        }

        // light dismiss は panel root の TrickleDown で拾う。popover 自身は
        // オーバーレイ層に居るので、通常のバブリングでは外側のクリックが届かない。
        // 層のリサイズ（＝ビューポート変化）はアンカーを動かさないことがあるので別途監視する
        void WatchRoot()
        {
            if (_layer == null)
            {
                return;
            }

            if (_watchedLayer != _layer)
            {
                _watchedLayer?.UnregisterCallback(_onWatchedGeometryChanged);
                _watchedLayer = _layer;
                _watchedLayer.RegisterCallback(_onWatchedGeometryChanged);
            }

            if (_root != null)
            {
                return;
            }

            VisualElement root = _layer.hierarchy.parent;
            if (root == null)
            {
                return;
            }

            _root = root;
            _root.RegisterCallback(_onRootPointerDown, TrickleDown.TrickleDown);
            _root.RegisterCallback(_onRootKeyDown, TrickleDown.TrickleDown);
        }

        void UnwatchRoot()
        {
            if (_watchedLayer != null)
            {
                _watchedLayer.UnregisterCallback(_onWatchedGeometryChanged);
                _watchedLayer = null;
            }

            if (_root == null)
            {
                return;
            }

            _root.UnregisterCallback(_onRootPointerDown, TrickleDown.TrickleDown);
            _root.UnregisterCallback(_onRootKeyDown, TrickleDown.TrickleDown);
            _root = null;
        }

        #endregion

        #region Events

        void OnSelfGeometryChanged(GeometryChangedEvent evt)
        {
            // left/top を書き換えるとこのイベントがもう一度来るが、次回は同値になって収束する
            Reposition();
        }

        void OnSelfDetached(DetachFromPanelEvent evt)
        {
            // 外部からツリーごと外された場合でも監視を残さない
            if (!_isOpen)
            {
                return;
            }

            Close();
        }

        void OnWatchedGeometryChanged(GeometryChangedEvent evt)
        {
            Reposition();
        }

        void OnAnchorDetached(DetachFromPanelEvent evt)
        {
            Close();
        }

        void OnRootPointerDown(PointerDownEvent evt)
        {
            if (!_isOpen || !LightDismiss || evt == null)
            {
                return;
            }

            // 「層の中なら閉じない」だとモーダルが層に載った時に、モーダル内のクリックで
            // ネストしたポップオーバーが閉じなくなる。判定は IsOutsideClick に集約する
            if (evt.target is VisualElement target && !IsOutsideClick(target))
            {
                return;
            }

            Close();
        }

        void OnRootKeyDown(KeyDownEvent evt)
        {
            if (!_isOpen || !LightDismiss || evt == null || evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            Close();
            evt.StopPropagation();
        }

        #endregion

        #region Presentation

        void ApplyShadowStyle()
        {
            if (_balloon == null)
            {
                return;
            }

            // 矢印付きは「指している」ので浅く近い影、矢印なしのパネルは広く柔らかい影
            _balloon.ShadowBlur = _arrow ? TweeqBalloon.DEFAULT_SHADOW_BLUR : POPUP_SHADOW_BLUR;
            _balloon.ShadowOffsetY = _arrow ? TweeqBalloon.DEFAULT_SHADOW_OFFSET_Y : POPUP_SHADOW_OFFSET_Y;
        }

        // トランジションは初期化時（とテーマ差し替え時）に一度だけ。毎フレーム触ると StyleList を確保する
        void ApplyFadeTransition()
        {
            float duration = _theme != null ? _theme.ActiveTransitionDuration : 0.064f;

            _fadeDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });

            this.style.transitionProperty = OpacityProperty;
            this.style.transitionTimingFunction = EaseOut;
            this.style.transitionDuration = _fadeDuration;
        }

        #endregion

        #region Helpers

        // PopoverResult.ArrowSide: 0=Top 1=Bottom 2=Left 3=Right
        static TweeqArrowSide ToArrowSide(int side)
        {
            switch (side)
            {
                case 0:
                    return TweeqArrowSide.Top;
                case 1:
                    return TweeqArrowSide.Bottom;
                case 2:
                    return TweeqArrowSide.Left;
                case 3:
                    return TweeqArrowSide.Right;
                default:
                    return TweeqArrowSide.None;
            }
        }

        static bool IsUsableSize(float value)
        {
            return !float.IsNaN(value) && value > 0f;
        }

        #endregion
    }
}
