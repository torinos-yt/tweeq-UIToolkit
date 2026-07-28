using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 画面全体を覆うモーダル（m8-modal-tabs-spec.md §A・Vue 版 PaneModal 相当）。
    /// 利用者ツリーに置くが自分では何も描かず（サイズ 0）、<see cref="Open"/> の間だけ
    /// 内部の backdrop を <see cref="TweeqOverlayLayer"/> へ載せる。
    /// 中身は普通に Add すればよく、内部の <see cref="TweeqBalloon"/> に常駐するので
    /// 開閉で破棄されない。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 閉じる責務は所有者にある。基底の TweeqModal は<b>キーを一切扱わない</b>（Vue PaneModal 準拠）。
    /// backdrop クリックでも閉じず、<see cref="Emphasize"/> のバウンスと
    /// <see cref="OutsideClicked"/> だけを返す（「閉じないモーダル」）。
    /// </para>
    /// <para>
    /// Vue からの意図的逸脱が 2 件ある（仕様書が根拠）:
    /// backdrop-filter が無いので <see cref="TweeqTheme.Background"/> の 50% アルファで暗転すること、
    /// および背面 UI の誤操作＝事故なので backdrop でポインタを遮断すること。
    /// </para>
    /// <para>
    /// <see cref="Open"/> は Vue と同じ純リフレクタで、この要素からは書き戻さない。
    /// パネル未接続で開いた場合は例外を投げずに「載せられなかった」で済ませ、
    /// パネルへ接続された時点で載せ直す（UXML の open="true" がこの経路を通る）。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqModal : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>層の端に確保する余白（px）。Vue の pane-margin。最大サイズはこの 2 倍を引いた値。</summary>
        public const float PANE_MARGIN = 48f;

        /// <summary>外装の角丸半径（px）。Vue の radius-popup。</summary>
        public const float PANE_RADIUS = 13f;

        /// <summary>外装の内側余白（px）。Vue の pane-padding（バルーン既定の 9 を上書き）。</summary>
        public const float PANE_PADDING = 12f;

        // 矢印なしポップアップと同じ「広く柔らかい」影
        const float PANE_SHADOW_BLUR = 20f;
        const float PANE_SHADOW_OFFSET_Y = 0f;

        // 暗転の濃さ。UI Toolkit に backdrop-filter が無いための代替（意図的逸脱）
        const float BACKDROP_ALPHA = 0.5f;

        // 出現時に下から持ち上げる量（px）。React 版 style.styl の translateY(-6px) 相当
        const float ENTER_TRANSLATE_Y = -6f;

        // emphasize: scale 1 → 1.03(35%) → 1 を 0.2s
        const long EMPHASIZE_DURATION_MS = 200L;
        const float EMPHASIZE_PEAK_SCALE = 1.03f;
        const float EMPHASIZE_PEAK_PHASE = 0.35f;

        // schedule の最小刻み。60fps 相当で十分滑らかに見える
        const long TICK_MS = 16L;

        #endregion

        #region Fields

        // トランジション定義は不変なので 1 個だけ作って全インスタンスで共有する
        // （style.transition* は毎回 List を要求するため、都度 new すると開くたびにゴミが出る）
        static readonly StyleList<StylePropertyName> PaneProperties =
            new StyleList<StylePropertyName>(new List<StylePropertyName>
            {
                new StylePropertyName("opacity"),
                new StylePropertyName("translate"),
            });

        static readonly StyleList<StylePropertyName> BackdropProperties =
            new StyleList<StylePropertyName>(new List<StylePropertyName>
            {
                new StylePropertyName("background-color"),
            });

        // 本数はプロパティ側と揃える（CSS の循環補完に頼らず、読んで分かる形にしておく）
        static readonly StyleList<EasingFunction> PaneEase =
            new StyleList<EasingFunction>(new List<EasingFunction>
            {
                new EasingFunction(EasingMode.EaseOutCubic),
                new EasingFunction(EasingMode.EaseOutCubic),
            });

        static readonly StyleList<EasingFunction> BackdropEase =
            new StyleList<EasingFunction>(new List<EasingFunction>
            {
                new EasingFunction(EasingMode.EaseOutCubic),
            });

        static readonly StyleList<TimeValue> PaneInstant =
            new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(0f, TimeUnit.Second),
                new TimeValue(0f, TimeUnit.Second),
            });

        static readonly StyleList<TimeValue> BackdropInstant =
            new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(0f, TimeUnit.Second),
            });

        TweeqTheme _theme = TweeqTheme.Dark();

        readonly VisualElement _backdrop;
        readonly TweeqBalloon _pane;

        // テーマ由来の秒数。テーマ差し替え時にだけ作り直す
        StyleList<TimeValue> _paneDuration;
        StyleList<TimeValue> _backdropDuration;

        bool _open;

        // 「層に載っているか」。_open は要求、_mounted は実際の設置状態（パネル未接続だと乖離する）
        bool _mounted;

        TweeqOverlayLayer _layer;

        // 毎回のメソッドグループ変換はデリゲートを確保するので、登録／解除で使い回す実体を持つ
        readonly EventCallback<GeometryChangedEvent> _onLayerGeometryChanged;

        // 「1フレーム後に開始値から目標値へ遷移させる」1件だけを使い回す
        IVisualElementScheduledItem _settleItem;

        IVisualElementScheduledItem _emphasizeItem;
        long _emphasizeStartMs = -1L;
        bool _emphasizing;

        #endregion

        #region Public API

        /// <summary><see cref="Open"/> が false→true になった時に一度だけ発火する。</summary>
        public event Action Opened;

        /// <summary><see cref="Open"/> が true→false になった時に一度だけ発火する。</summary>
        public event Action Closed;

        /// <summary>backdrop（＝モーダルの外側）が押された時に発火する。閉じるかは所有者の判断。</summary>
        public event Action OutsideClicked;

        /// <summary>
        /// 開閉。Vue と同じ純リフレクタで、この要素からは書き戻さない
        /// （backdrop クリックや Escape では false にならない）。
        /// </summary>
        [UxmlAttribute("open")]
        public bool Open
        {
            get => _open;
            set
            {
                if (_open == value)
                {
                    return;
                }

                _open = value;

                if (_open)
                {
                    Mount();
                    Opened?.Invoke();
                }
                else
                {
                    Unmount();
                    Closed?.Invoke();
                }
            }
        }

        /// <summary>全面を覆う背景層。暗転とポインタ遮断を担う。</summary>
        public VisualElement Backdrop => _backdrop;

        /// <summary>外装のバルーン。半径・パディング・影を個別に詰めたい時に触る。</summary>
        public TweeqBalloon Pane => _pane;

        /// <summary><see cref="Emphasize"/> のバウンスを再生中か。</summary>
        public bool IsEmphasizing => _emphasizing;

        /// <summary>配色テーマ。backdrop / バルーン / 中身の <see cref="ITweeqThemed"/> 子孫へ配る。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();

                _pane.Theme = _theme;

                // バルーンは Theme 代入のたびに自分の transition（scale）を張り直すので、
                // モーダル側の opacity / translate 指定はその後で上書きし直す必要がある
                ApplyTransitions();
                ApplyBackdropColor(_mounted ? 1f : 0f);
                DistributeTheme(_pane.contentContainer);
                OnThemeApplied();
            }
        }

        /// <summary>中身はバルーンのコンテンツ層へ入る（開閉で親が変わらないので破棄されない）。</summary>
        public override VisualElement contentContainer => _pane != null ? _pane.contentContainer : this;

        /// <summary>
        /// 注意を引くためのバウンス（scale 1 → 1.03 → 1 を 0.2s）。
        /// 再生中に呼び直すと先頭から掛け直す。パネル未接続では scheduler が回らないので何もしない。
        /// </summary>
        public void Emphasize()
        {
            // 先頭から。TimerState.now は毎ティック進むので開始時刻は自前で覚える
            _emphasizeStartMs = -1L;
            ApplyEmphasizeScale(0f);

            if (_pane.panel == null)
            {
                _emphasizing = false;
                return;
            }

            _emphasizing = true;

            if (_emphasizeItem == null)
            {
                _emphasizeItem = _pane.schedule.Execute(OnEmphasizeTick).Every(TICK_MS);
                return;
            }

            _emphasizeItem.Resume();
        }

        /// <summary>
        /// backdrop クリック相当の処理（バウンス → <see cref="OutsideClicked"/>）を発火する。
        /// パネル無しではポインタイベントを合成できないため、テストと外部ドライバのために口を開けてある。
        /// </summary>
        public void PerformOutsideClick()
        {
            Emphasize();
            OutsideClicked?.Invoke();
        }

        #endregion

        #region Construction

        public TweeqModal()
        {
            this.name = "tweeq-modal";

            // 利用者ツリーでは場所を取らない。実体はオーバーレイ層側にある
            this.style.display = DisplayStyle.None;
            this.pickingMode = PickingMode.Ignore;

            _backdrop = new VisualElement
            {
                name = "tweeq-modal-backdrop",

                // Vue の popover="manual" は背面を操作可能なままにするが、
                // 公演現場での誤操作＝事故なのでポインタを遮断する（意図的逸脱）
                pickingMode = PickingMode.Position,
            };
            _backdrop.style.position = Position.Absolute;
            _backdrop.style.left = 0f;
            _backdrop.style.top = 0f;
            _backdrop.style.right = 0f;
            _backdrop.style.bottom = 0f;
            _backdrop.style.justifyContent = Justify.Center;
            _backdrop.style.alignItems = Align.Center;

            _pane = new TweeqBalloon
            {
                name = "tweeq-modal-pane",
                Theme = _theme,
                ArrowSide = TweeqArrowSide.None,
                Radius = PANE_RADIUS,
                PaddingVertical = PANE_PADDING,
                PaddingHorizontal = PANE_PADDING,
                ShadowBlur = PANE_SHADOW_BLUR,
                ShadowOffsetY = PANE_SHADOW_OFFSET_Y,
            };

            // バルーンは既定で alignSelf: FlexStart（吹き出しは内容なり幅で左に付く）。
            // モーダルは backdrop の中央寄せに従わせたいので、親の alignItems へ戻す
            _pane.style.alignSelf = Align.Auto;
            _backdrop.hierarchy.Add(_pane);

            _onLayerGeometryChanged = OnLayerGeometryChanged;

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            _backdrop.RegisterCallback<PointerDownEvent>(OnBackdropPointerDown);

            ApplyTransitions();
            ApplyBackdropColor(0f);
        }

        #endregion

        #region Mounting

        /// <summary>層に載っている間だけ非 null。派生クラスがキー配線などに使う。</summary>
        protected TweeqOverlayLayer Layer => _layer;

        /// <summary>層へ載った直後に呼ばれる。既定では何もしない。</summary>
        protected virtual void OnMounted(TweeqOverlayLayer layer)
        {
        }

        /// <summary>層から降ろす直前に呼ばれる。登録したハンドラはここで必ず外すこと。</summary>
        protected virtual void OnUnmounted()
        {
        }

        /// <summary><see cref="Theme"/> 代入の最後に呼ばれる。派生の自前パーツへ配り直すためのフック。</summary>
        protected virtual void OnThemeApplied()
        {
        }

        void Mount()
        {
            if (_mounted)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // パネル未接続では置き場所が無い。例外は投げず、接続時に載せ直す
                return;
            }

            _layer = layer;

            if (_backdrop.hierarchy.parent != layer)
            {
                _backdrop.RemoveFromHierarchy();
                layer.Add(_backdrop);
            }

            _mounted = true;

            // 層のサイズ変化（＝ビューポート変化）は中身を動かさないことがあるので別途監視する
            _layer.RegisterCallback(_onLayerGeometryChanged);

            ApplyMaxSize();
            BeginEnterAnimation();

            OnMounted(_layer);
        }

        void Unmount()
        {
            StopEmphasize();

            if (_mounted)
            {
                _mounted = false;
                _settleItem?.Pause();

                if (_layer != null)
                {
                    _layer.UnregisterCallback(_onLayerGeometryChanged);
                }

                // 派生のハンドラ解除はツリーから外す前に済ませる（リーク禁止）
                OnUnmounted();
            }

            _layer = null;

            // 閉じている間 backdrop は親無しで保持する。中身はバルーンに残るので壊れない。
            // 閉じたのに 1 フレームでもポインタを吸うと事故なので、フェードアウトは待たずに即座に降ろす
            // （テスト契約「Close で除去」もこの即時性を要求している）
            _backdrop.RemoveFromHierarchy();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // UXML の open="true" は属性適用（パネル接続前）で立つので、ここで拾い直す
            if (_open && !_mounted)
            {
                Mount();
            }
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // 所有者ごとツリーから外されたら層に置き去りにしない。Open の要求自体は保つので、
            // 載せ直せばまた開く
            Unmount();
        }

        #endregion

        #region Presentation

        void OnLayerGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyMaxSize();
        }

        // 中身が層に収まらない時だけ効く上限。これ以下なら内容なりの中央寄せになる
        void ApplyMaxSize()
        {
            if (_layer == null)
            {
                return;
            }

            float width = _layer.layout.width;
            float height = _layer.layout.height;
            if (!IsUsableSize(width) || !IsUsableSize(height))
            {
                // まだレイアウトが降りていない。GeometryChanged で呼び直される
                return;
            }

            _pane.style.maxWidth = Mathf.Max(0f, width - PANE_MARGIN * 2f);
            _pane.style.maxHeight = Mathf.Max(0f, height - PANE_MARGIN * 2f);
        }

        // トランジションは初期化時（とテーマ差し替え時）に一度だけ。毎フレーム触ると StyleList を確保する
        void ApplyTransitions()
        {
            float duration = _theme != null ? _theme.ActiveTransitionDuration : 0.064f;

            _paneDuration = new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(duration, TimeUnit.Second),
                new TimeValue(duration, TimeUnit.Second),
            });

            _backdropDuration = new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(duration, TimeUnit.Second),
            });

            // scale をトランジション対象に含めない。emphasize は schedule で毎フレーム書くので、
            // 遷移が乗っていると狙った波形にならない
            _pane.style.transitionProperty = PaneProperties;
            _pane.style.transitionTimingFunction = PaneEase;
            _pane.style.transitionDuration = _paneDuration;

            _backdrop.style.transitionProperty = BackdropProperties;
            _backdrop.style.transitionTimingFunction = BackdropEase;
            _backdrop.style.transitionDuration = _backdropDuration;
        }

        void ApplyBackdropColor(float weight)
        {
            Color color = _theme != null ? _theme.Background : Color.black;
            color.a = BACKDROP_ALPHA * Mathf.Clamp01(weight);
            _backdrop.style.backgroundColor = color;
        }

        void BeginEnterAnimation()
        {
            // 2 回目以降は前回の終了値（opacity 1）が残っているので、そのまま 0 を入れると
            // 「消えるアニメ」が先に走る。開始値は duration 0 で当てる
            // （Vue の @starting-style が担っていた役目。Popover / Balloon と同じ手口）
            _pane.style.transitionDuration = PaneInstant;
            _pane.style.opacity = 0f;
            _pane.style.translate = new StyleTranslate(
                new Translate(new Length(0f), new Length(ENTER_TRANSLATE_Y), 0f));

            _backdrop.style.transitionDuration = BackdropInstant;
            ApplyBackdropColor(0f);

            if (_backdrop.panel == null)
            {
                // scheduler が回らないので、透明のまま固まらないよう即座に終了値へ飛ばす
                Settle();
                return;
            }

            if (_settleItem == null)
            {
                _settleItem = _backdrop.schedule.Execute(Settle);
            }

            _settleItem.ExecuteLater(0L);
        }

        void Settle()
        {
            if (!_mounted)
            {
                return;
            }

            _pane.style.transitionDuration = _paneDuration;
            _pane.style.opacity = 1f;
            _pane.style.translate = new StyleTranslate(
                new Translate(new Length(0f), new Length(0f), 0f));

            _backdrop.style.transitionDuration = _backdropDuration;
            ApplyBackdropColor(1f);
        }

        // TweeqRoot は ITweeqThemed に当たると探索を打ち切る。モーダル自身が ITweeqThemed なので、
        // 中身へはここから配り直さないとテーマが届かない（複合部品の転送責務）。
        // 外装の不透明化は TweeqBalloon が Theme.SurfaceOpaque を使うことで全ポップアップ共通になった
        void DistributeTheme(VisualElement parent)
        {
            TweeqThemeDistribution.Distribute(parent, _theme);
        }

        #endregion

        #region Emphasize

        void OnEmphasizeTick(TimerState state)
        {
            if (_emphasizeStartMs < 0L)
            {
                _emphasizeStartMs = state.now;
            }

            long elapsed = state.now - _emphasizeStartMs;
            if (elapsed >= EMPHASIZE_DURATION_MS)
            {
                StopEmphasize();
                return;
            }

            // 1 → 1.03(35%) → 1 の折れ線。角が立たないよう smoothstep（ease 相当）に通す
            float phase = elapsed / (float)EMPHASIZE_DURATION_MS;
            float ramp = phase <= EMPHASIZE_PEAK_PHASE
                ? phase / EMPHASIZE_PEAK_PHASE
                : (1f - phase) / (1f - EMPHASIZE_PEAK_PHASE);

            ApplyEmphasizeScale(ramp * ramp * (3f - 2f * ramp));
        }

        void StopEmphasize()
        {
            _emphasizing = false;
            _emphasizeStartMs = -1L;
            _emphasizeItem?.Pause();
            ApplyEmphasizeScale(0f);
        }

        // 毎フレーム経路。Scale / StyleScale は構造体なのでここでの確保は無い
        void ApplyEmphasizeScale(float weight)
        {
            float scale = Mathf.Lerp(1f, EMPHASIZE_PEAK_SCALE, Mathf.Clamp01(weight));
            _pane.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }

        #endregion

        #region Events

        void OnBackdropPointerDown(PointerDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // パネルの中身は最前面の要素が拾うので、背面 UI へはそもそも届かない。
            // ここで止めるのは「層より外へ抜けていく」経路の保険
            if (!(evt.target is VisualElement target) || target != _backdrop)
            {
                // pane の中身のクリックは素通し（内側の部品が自分で処理する）
                return;
            }

            evt.StopPropagation();
            PerformOutsideClick();
        }

        #endregion

        #region Helpers

        static bool IsUsableSize(float value)
        {
            return !float.IsNaN(value) && value > 0f;
        }

        #endregion
    }
}
