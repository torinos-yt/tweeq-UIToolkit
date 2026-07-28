using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 折りたたみ可能なパラメータグループ（仕様 §3）。
    ///
    /// 本家は grid-template-rows を 1fr↔0fr で遷移させるが、UI Toolkit に grid は無い。
    /// 代わりに clip 要素の max-height を「実測高↔0」で遷移させ、閉／遷移中だけ
    /// overflow:hidden にする（開き切ったら解除しないと入力欄のフォーカスリングが切れる）。
    /// </summary>
    [UxmlElement]
    public partial class ParameterGroup : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>開閉状態の PlayerPrefs キー接頭辞。</summary>
        public const string PREFS_PREFIX = "tweeq.";

        /// <summary>開閉状態の PlayerPrefs キー接尾辞。</summary>
        public const string PREFS_SUFFIX = ".expanded";

        const float CHEVRON_SIZE = 12f;

        // 本家 .heading の gap 0.25em 相当
        const float CHEVRON_GAP = 4f;

        // TransitionEndEvent が届かない環境（アニメ無効・値が動かない等）でも
        // 開き切った状態へ必ず戻すための保険。遷移時間 + 余裕
        const long FINISH_FALLBACK_MARGIN_MS = 80;

        // 「pin した高さが既に目標高」と見なす許容差（px）。1px 未満の遷移は
        // 目に見えないので、走らない遷移を待つより即座に開き切る
        const float PIN_EPSILON = 0.5f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        readonly ParameterHeading _heading;
        readonly VisualElement _chevron;
        readonly VisualElement _clip;
        readonly VisualElement _content;

        string _name = string.Empty;
        bool _expanded = true;
        bool _hovered;

        IVisualElementScheduledItem _transitionItem;
        IVisualElementScheduledItem _finishItem;

        // 遷移の始点として固定した clip の高さ。往復反転で「もう目標高に居る」判定に使う
        float _pinnedHeight;

        // 最後に測れた content の高さ。クリップ中の実測が 0 を返したときの保険
        float _naturalContentHeight;

        #endregion

        #region Public API

        /// <summary>見出し文字列。</summary>
        // VisualElement 組み込みの text 系属性と混ざらないよう、UXML 側は heading-text にする
        [UxmlAttribute("heading-text")]
        public string Label
        {
            get => _heading.Text;
            set => _heading.Text = value;
        }

        /// <summary>開いているか。変更するとアニメーション付きで開閉し、状態を永続化する。</summary>
        [UxmlAttribute("expanded")]
        public bool Expanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value)
                {
                    return;
                }

                _expanded = value;
                SaveExpanded();
                ApplyExpanded(true);
            }
        }

        /// <summary>
        /// 永続化キー。設定した時点で保存済みの開閉状態を読み込む（未保存なら展開のまま）。
        /// </summary>
        // UXML では name（VisualElement 組み込み）と衝突するので group-name にする。
        // 属性は宣言順に適用されるため Expanded より後に置き、保存済み状態が
        // UXML に書かれた既定値へ勝つようにする（コンストラクタと同じ優先順）
        [UxmlAttribute("group-name")]
        public string Name
        {
            get => _name;
            set
            {
                _name = value ?? string.Empty;

                if (TryLoadExpanded(PrefsKey(_name), out bool stored) && stored != _expanded)
                {
                    // 読み込みはユーザー操作ではないのでアニメも保存もしない
                    _expanded = stored;
                    ApplyExpanded(false);
                }
            }
        }

        /// <summary>Parameter などを Add する先。</summary>
        public VisualElement Content => _content;

        /// <summary>
        /// UXML の子や素の Add() が折りたたみ対象に入るようにする（内部構築は hierarchy.Add 経由）。
        /// コンストラクタ中は _content 生成前に呼ばれ得るため null ガードする
        /// </summary>
        public override VisualElement contentContainer => _content ?? this;

        /// <summary>見出し右端のスロット。</summary>
        public VisualElement HeadingRight => _heading.Right;

        /// <summary>配色テーマ。通常は ParameterGrid から配られる。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // 同一インスタンスでも打ち切らない。テーマ設定後に足された行へ届ける
                // 再配布の入り口はこの setter しか無い（M7 転送契約の取りこぼし修正）
                _theme = value ?? TweeqTheme.Dark();
                _heading.Theme = _theme;
                ApplyStaticStyles();
                RefreshContentGaps();
                RefreshHeadingColor();
                TweeqThemeDistribution.Distribute(_content, _theme);
            }
        }

        /// <summary>指定名に対応する PlayerPrefs キーを返す。</summary>
        public static string PrefsKey(string name)
        {
            return string.IsNullOrEmpty(name) ? string.Empty : PREFS_PREFIX + name + PREFS_SUFFIX;
        }

        /// <summary>content 内の行間（gapControl）を配り直す。子を足したあとに呼ぶ。</summary>
        public void RefreshContentGaps()
        {
            TweeqGap.Apply(_content, _theme.GapControl, FlexDirection.Column);
        }

        #endregion

        #region Construction

        public ParameterGroup()
        {
            this.AddToClassList("tweeq-parameter-group");
            this.style.flexDirection = FlexDirection.Column;

            _heading = new ParameterHeading();
            this.hierarchy.Add(_heading);

            _chevron = new VisualElement { name = "tweeq-parameter-group-chevron" };
            _chevron.style.width = CHEVRON_SIZE;
            _chevron.style.height = CHEVRON_SIZE;
            _chevron.style.flexShrink = 0f;
            _chevron.style.marginRight = CHEVRON_GAP;
            _chevron.pickingMode = PickingMode.Ignore;
            _chevron.generateVisualContent += OnGenerateChevron;
            _heading.HeadingContainer.Insert(0, _chevron);

            VisualElement headingBox = _heading.HeadingContainer;

            // ボタン相当。クリックと Enter/Space で開閉する
            headingBox.focusable = true;
            headingBox.RegisterCallback<PointerDownEvent>(OnHeadingPointerDown);
            headingBox.RegisterCallback<ClickEvent>(OnHeadingClick);
            headingBox.RegisterCallback<KeyDownEvent>(OnHeadingKeyDown);
            headingBox.RegisterCallback<PointerEnterEvent>(OnHeadingPointerEnter);
            headingBox.RegisterCallback<PointerLeaveEvent>(OnHeadingPointerLeave);

            _clip = new VisualElement { name = "tweeq-parameter-group-clip" };
            this.hierarchy.Add(_clip);

            _content = new VisualElement { name = "tweeq-parameter-group-content" };
            _content.style.flexDirection = FlexDirection.Column;

            // clip 側の max-height が 0 でも実測高を保つため、縮まないようにする。
            // これで閉じている間も _content.resolvedStyle.height が「開いたときの高さ」になる
            _content.style.flexShrink = 0f;
            _content.RegisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
            _clip.Add(_content);

            ApplyStaticStyles();
            RefreshHeadingColor();

            // パネルに載る前に初期状態を書いておけば、最初の遷移は走らない
            ApplyExpanded(false);

            _clip.RegisterCallback<TransitionEndEvent>(OnClipTransitionEnd);
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        public ParameterGroup(string name, string label)
            : this()
        {
            this.Label = label;
            this.Name = name;
        }

        void ApplyStaticStyles()
        {
            float duration = _theme.HoverTransitionDuration;

            // 本家は `ease`（cubic-bezier(0.25,0.1,0.25,1)）指定なのでそれに合わせる
            ApplyTransition(_clip, duration, EasingMode.Ease, "max-height", "padding-top");
            ApplyTransition(_chevron, duration, EasingMode.Ease, "rotate");

            // 色は Label 自身のインラインスタイルで切り替わるので、遷移も Label へ掛ける
            ApplyTransition(_heading.TextElement, duration, EasingMode.Ease, "color");

            RefreshContentGaps();
        }

        #endregion

        #region Expand / collapse

        void ApplyExpanded(bool animate)
        {
            CancelScheduled();

            _chevron.style.rotate = new Rotate(new Angle(_expanded ? 0f : -90f, AngleUnit.Degree));

            if (!animate || this.panel == null)
            {
                ApplyEndState();
                return;
            }

            // UI Toolkit の遷移は「直前のフレームで解決済みの値 → 新しい値」でしか補間されない。
            // none(auto) からも、遷移途中の値からも、同一フレームで目標値を書くと補間が飛ぶ。
            // 以前は閉じる側だけ「実測高で pin → 次 tick で 0」にしていたため、開く側が
            // 補間されず +80ms のフォールバックで一気に見えていた（feedback-fixes-01.md B）。
            // 開閉どちらも同じ二段構えに揃える。
            //
            // pin は「今まさに描かれている高さ」でなければ反転時に飛ぶので、保持している
            // 自然高ではなく resolvedStyle から取る
            _pinnedHeight = CurrentClipHeight();

            // 遷移中は必ずクリップする
            _clip.style.overflow = Overflow.Hidden;
            _clip.style.maxHeight = _pinnedHeight;
            _clip.style.paddingTop = CurrentClipPaddingTop();

            _transitionItem = this.schedule.Execute(StartTransition).StartingIn(0);
        }

        // pin から 1 tick 後。ここで初めて目標値を書く（＝始点が解決済みなので補間される）
        void StartTransition()
        {
            _transitionItem = null;

            if (this.panel == null)
            {
                ApplyEndState();
                return;
            }

            float gap = _theme.GapControl;

            if (!_expanded)
            {
                _clip.style.paddingTop = 0f;
                _clip.style.maxHeight = 0f;
                return;
            }

            // 計測は pin 後のこのタイミングで行う。クリック時点では未レイアウトの
            // 古い値を掴むことがある
            float content = MeasuredContentHeight();
            if (content <= 0f)
            {
                // 中身がまだ一度もレイアウトされていない（初回展開など）。0 へ向けて
                // 遷移させても動かず、フォールバックで一気に見えるだけなので即開きにする
                ApplyEndState();
                return;
            }

            float target = content + gap;
            if (_pinnedHeight >= target - PIN_EPSILON)
            {
                // 閉じアニメの途中で開き直した等で、既に目標高に達している。
                // 遷移が走らない＝TransitionEndEvent も来ないので、ここで開き切る
                ApplyEndState();
                return;
            }

            _clip.style.paddingTop = gap;
            _clip.style.maxHeight = target;

            // フォールバックは目標値を書いたこの瞬間から数える
            ScheduleFinishExpand();
        }

        // アニメーションを挟まない最終状態。展開なら max-height の枷を外す
        void ApplyEndState()
        {
            float gap = _theme.GapControl;

            if (_expanded)
            {
                _clip.style.paddingTop = gap;
                _clip.style.maxHeight = StyleKeyword.None;
                _clip.style.overflow = Overflow.Visible;
            }
            else
            {
                _clip.style.paddingTop = 0f;
                _clip.style.maxHeight = 0f;
                _clip.style.overflow = Overflow.Hidden;
            }
        }

        void CancelScheduled()
        {
            _transitionItem?.Pause();
            _transitionItem = null;
            _finishItem?.Pause();
            _finishItem = null;
        }

        void ScheduleFinishExpand()
        {
            long delay = (long)(_theme.HoverTransitionDuration * 1000f) + FINISH_FALLBACK_MARGIN_MS;
            _finishItem = this.schedule.Execute(FinishExpand).StartingIn(delay);
        }

        // 開き切ったら max-height の枷を外す。中身が後から伸びても追従させるため
        void FinishExpand()
        {
            _finishItem?.Pause();
            _finishItem = null;

            if (!_expanded)
            {
                return;
            }

            _clip.style.maxHeight = StyleKeyword.None;
            _clip.style.overflow = Overflow.Visible;
        }

        float MeasuredContentHeight()
        {
            float height = _content.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0f)
            {
                return _naturalContentHeight;
            }

            return height;
        }

        float CurrentClipHeight()
        {
            float height = _clip.resolvedStyle.height;
            return float.IsNaN(height) || height < 0f ? 0f : height;
        }

        float CurrentClipPaddingTop()
        {
            float padding = _clip.resolvedStyle.paddingTop;
            return float.IsNaN(padding) || padding < 0f ? 0f : padding;
        }

        void OnClipTransitionEnd(TransitionEndEvent evt)
        {
            // TransitionEndEvent はバブルするので、content 内の入力欄（背景色の遷移など）が
            // 遷移を終えるたびにここへ来る。それでアニメを打ち切らないよう target を見る
            if (evt == null || !ReferenceEquals(evt.target, _clip) || !_expanded)
            {
                return;
            }

            // 自分が張った展開遷移の終わりでなければ無視する。閉じ遷移を pin で
            // 打ち切った直後や、即開きの padding-top 遷移の終わりで開き切って
            // しまうと、開くアニメが省略されて見える
            if (_finishItem == null)
            {
                return;
            }

            FinishExpand();
        }

        void OnContentGeometryChanged(GeometryChangedEvent evt)
        {
            // バブルしてくる子孫のレイアウト変化は無視する
            if (evt == null || !ReferenceEquals(evt.target, _content))
            {
                return;
            }

            // clip が max-height 0 でも content は flexShrink 0 で自然高を保つ。
            // ただし環境依存で 0 が返ることがあるので、測れた値は保険に残す
            float height = evt.newRect.height;
            if (!float.IsNaN(height) && height > 0f)
            {
                _naturalContentHeight = height;
            }

            RefreshContentGaps();
        }

        #endregion

        #region Heading interaction

        void OnHeadingPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0)
            {
                return;
            }

            // クリックでフォーカスを取り、以降キーボードでも開閉できるようにする
            _heading.HeadingContainer.Focus();
        }

        void OnHeadingClick(ClickEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            this.Expanded = !_expanded;
            evt.StopPropagation();
        }

        void OnHeadingKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            bool activate = evt.keyCode == KeyCode.Return
                || evt.keyCode == KeyCode.KeypadEnter
                || evt.keyCode == KeyCode.Space;

            if (!activate)
            {
                return;
            }

            this.Expanded = !_expanded;
            evt.StopPropagation();
        }

        void OnHeadingPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            RefreshHeadingColor();
        }

        void OnHeadingPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            RefreshHeadingColor();
        }

        void RefreshHeadingColor()
        {
            Color color = _hovered ? _theme.Text : _theme.TextMuted;

            // 文字色は transition で、シェブロンは Painter2D なので即時で切り替わる
            _heading.HeadingContainer.style.color = color;
            _heading.TextColor = color;
            _chevron.MarkDirtyRepaint();
        }

        #endregion

        #region Persistence

        void SaveExpanded()
        {
            string key = PrefsKey(_name);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            // バッチモードやサンドボックスでは PlayerPrefs が使えないことがある。
            // 折りたたみ状態の保存で例外を投げて上位を止めるのは割に合わない
            try
            {
                PlayerPrefs.SetInt(key, _expanded ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{nameof(ParameterGroup)}: 開閉状態を保存できない（{key}）: {exception.Message}");
            }
        }

        static bool TryLoadExpanded(string key, out bool expanded)
        {
            expanded = true;

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            try
            {
                if (!PlayerPrefs.HasKey(key))
                {
                    return false;
                }

                expanded = PlayerPrefs.GetInt(key, 1) != 0;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{nameof(ParameterGroup)}: 開閉状態を読めない（{key}）: {exception.Message}");
                return false;
            }
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ParameterGrid.Find(this)?.RequestRefresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // content から子を外しただけで遷移を打ち切らないよう、自分自身の剥がれだけ見る
            if (evt != null && !ReferenceEquals(evt.target, this))
            {
                return;
            }

            // 剥がしたパネルのスケジュール項目を掴んだままだと、次に載せたとき
            // 遷移途中の pin（max-height 固定＋overflow hidden）で止まったままになる
            CancelScheduled();
            ApplyEndState();
        }

        #endregion

        #region Painting

        void OnGenerateChevron(MeshGenerationContext context)
        {
            Painter2D painter = context?.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _chevron.contentRect;
            float width = rect.width;
            float height = rect.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }

            // 下向き三角。回転しても見た目が偏らないよう、正方形の中心で釣り合う位置に置く
            float halfWidth = width * 0.26f;
            float halfHeight = height * 0.16f;
            float centerX = width * 0.5f;
            float centerY = height * 0.5f;

            painter.fillColor = _hovered ? _theme.Text : _theme.TextMuted;
            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX - halfWidth, centerY - halfHeight));
            painter.LineTo(new Vector2(centerX + halfWidth, centerY - halfHeight));
            painter.LineTo(new Vector2(centerX, centerY + halfHeight * 2f));
            painter.ClosePath();
            painter.Fill();
        }

        #endregion

        #region Helpers

        static void ApplyTransition(
            VisualElement element, float duration, EasingMode easing, params string[] properties)
        {
            if (element == null || properties == null || properties.Length == 0)
            {
                return;
            }

            List<StylePropertyName> names = new List<StylePropertyName>(properties.Length);
            List<TimeValue> durations = new List<TimeValue>(properties.Length);
            List<EasingFunction> easings = new List<EasingFunction>(properties.Length);

            for (int i = 0; i < properties.Length; i++)
            {
                names.Add(new StylePropertyName(properties[i]));
                durations.Add(new TimeValue(duration, TimeUnit.Second));
                easings.Add(new EasingFunction(easing));
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(durations);
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
        }

        #endregion
    }
}
