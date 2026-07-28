using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// 表示名のラベルは Label 要素で作る。他の Input と表記を揃えるため別名にする
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// タブ切替（M8 仕様 §D「TweeqTabs」）。ヘッダー（タブリスト）と本体（パネル）を持ち、
    /// 子に置いた <see cref="TweeqTab"/> が自分で登録してくる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vue 原典（`ref/tweeq/src/Tabs`）が正だが、確認済みのバグは React 移植側の修正版を採用する:
    /// (1) <see cref="SelectTab"/> の disabled ガードを無条件に、(2) アクティブ解決の全段で
    /// disabled を除外、(3) 同 id の二重登録ガードと更新時の index &lt; 0 ガード、
    /// (4) キーボードナビ（矢印ラップ・Home/End・roving tabIndex）、
    /// (5) <see cref="StorageKey"/>（Vue は型にあるのに未使用）。
    /// </para>
    /// <para>
    /// <c>contentContainer</c> はパネル層へルーティングしてあるので、UXML でも C# でも
    /// <see cref="TweeqTab"/> をそのまま子に足せる。ヘッダーはその外側の内部要素なので
    /// 利用者の子要素とは混ざらない。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqTabs : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>この要素に付く USS クラス。</summary>
        public const string USS_CLASS_NAME = "tweeq-tabs";

        /// <summary>永続化キーの接頭辞（Vue の appConfig ストアの appId に対応）。</summary>
        public const string PREFS_PREFIX = "tweeq.";

        /// <summary><see cref="StorageKey"/> 省略時に <see cref="TabsName"/> へ付ける接尾辞。</summary>
        public const string PREFS_SUFFIX = ".active";

        // 以下は rem=12px 換算した Vue の style ブロックの実寸（m8-modal-tabs-spec.md §D「見た目」）

        // 横: タブリストとパネルの間（0.5rem）
        const float ROOT_GAP_HORIZONTAL = 6f;

        // 縦: タブリストとパネルの間（1rem）
        const float ROOT_GAP_VERTICAL = 12f;

        // 横のタブリストの項目間（0.2rem）
        const float TABLIST_GAP_HORIZONTAL = 2.4f;

        // 縦のタブリストの項目間
        const float TABLIST_GAP_VERTICAL = 2f;

        // 項目の line-height（2rem）。UI Toolkit に line-height が無いので固定高で代替する
        const float HEADER_LINE_HEIGHT = 24f;

        const float HEADER_PADDING_TOP = 2f;

        // 横の項目の左右 padding（0.4rem）
        const float HEADER_PADDING_INLINE = 4.8f;

        // 縦のラベル padding（0.2rem / 0.6rem）
        const float VERTICAL_LABEL_PADDING_BLOCK = 2.4f;

        const float VERTICAL_LABEL_PADDING_INLINE = 7.2f;

        /// <summary>アクティブを示す線の太さ（横は下線・縦は左線）。</summary>
        public const float INDICATOR_WIDTH = 3f;

        // 非アクティブのラベル。Vue の .tablist-link の opacity
        const float INACTIVE_OPACITY = 0.4f;

        // 縦のパネル側の区切り線と余白（1rem）
        const float PANELS_PADDING_LEFT = 12f;

        const float PANELS_BORDER_WIDTH = 1f;

        // ScrollView の viewport は枠外描画（SwitchInput のフォーカスリング=inset −3px 等）を
        // 容赦なく切る。クリップ境界の内側に取る安全マージン（リング 3px + AA 1px）
        const float CLIP_SAFE_PADDING = 4f;

        #endregion

        #region Storage

        static ITweeqTabStorage _storage = TweeqTabPlayerPrefsStorage.Instance;

        /// <summary>
        /// アクティブタブ id の保存先（全 <see cref="TweeqTabs"/> 共有）。
        /// null を代入すると既定の <see cref="TweeqTabPlayerPrefsStorage.Instance"/> へ戻る。
        /// </summary>
        public static ITweeqTabStorage Storage
        {
            get => _storage;
            set => _storage = value ?? TweeqTabPlayerPrefsStorage.Instance;
        }

        /// <summary>
        /// 永続化キーを組み立てる。<paramref name="storageKey"/> が優先で、無ければ
        /// <paramref name="tabsName"/> + ".active"。どちらも空なら空文字（＝永続化しない）。
        /// </summary>
        public static string PrefsKey(string tabsName, string storageKey)
        {
            if (!string.IsNullOrEmpty(storageKey))
            {
                return PREFS_PREFIX + storageKey;
            }

            if (!string.IsNullOrEmpty(tabsName))
            {
                return PREFS_PREFIX + tabsName + PREFS_SUFFIX;
            }

            return string.Empty;
        }

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        readonly VisualElement _tabList;
        readonly VisualElement _panels;

        // 縦のときだけパネルを包む。横では作らない（ScrollView は viewport を clip するので、
        // スクロールさせない layout に噛ませると中身が切れる）
        ScrollView _scroll;

        readonly List<TweeqTab> _tabs = new List<TweeqTab>();
        readonly List<VisualElement> _headers = new List<VisualElement>();
        readonly List<UILabel> _headerLabels = new List<UILabel>();

        string _tabsName = string.Empty;
        string _storageKey = string.Empty;
        string _defaultTabId = string.Empty;
        string _activeId = string.Empty;
        bool _vertical;

        // 今の _activeId が「利用者が選んだ結果」か「解決の落としどころ」か。
        // タブは 1 枚ずつ登録されてくるので、最初の 1 枚で暫定的に選ばれた結果を
        // 確定扱いすると、後から現れる保存済みタブへ切り替われなくなる
        bool _activeIdIsExplicit;

        TweeqTab _hoveredTab;

        #endregion

        #region Public API

        /// <summary>別のタブへ切り替わったときに発火する。</summary>
        public event Action<TweeqTab> Changed;

        /// <summary>
        /// 既にアクティブなタブをもう一度クリックしたときに発火する。
        /// <see cref="Changed"/> とは排他（同じ操作で両方は飛ばない）。
        /// </summary>
        public event Action<TweeqTab> Clicked;

        /// <summary>永続化キーの元になる名前。<see cref="StorageKey"/> が無ければこちらを使う。</summary>
        [UxmlAttribute("tabs-name")]
        public string TabsName
        {
            get => _tabsName;
            set
            {
                string next = value ?? string.Empty;
                if (_tabsName == next)
                {
                    return;
                }

                _tabsName = next;
                EnsureActiveTab();
            }
        }

        /// <summary>
        /// 明示の永続化キー。Vue は型にあるのに未使用というバグなので、React 修正版
        /// （`StorageKey ?? $"{TabsName}.active"`）を採用する。
        /// </summary>
        [UxmlAttribute("storage-key")]
        public string StorageKey
        {
            get => _storageKey;
            set
            {
                string next = value ?? string.Empty;
                if (_storageKey == next)
                {
                    return;
                }

                _storageKey = next;
                EnsureActiveTab();
            }
        }

        /// <summary>初期タブ id。保存値が無い（または無効）ときの第 2 候補。</summary>
        [UxmlAttribute("default-tab-id")]
        public string DefaultTabId
        {
            get => _defaultTabId;
            set
            {
                string next = value ?? string.Empty;
                if (_defaultTabId == next)
                {
                    return;
                }

                _defaultTabId = next;
                EnsureActiveTab();
            }
        }

        /// <summary>AE / Resolve 風に、タブリストを左・パネルを右へ置く。</summary>
        [UxmlAttribute("vertical")]
        public bool Vertical
        {
            get => _vertical;
            set
            {
                if (_vertical == value)
                {
                    return;
                }

                _vertical = value;
                ApplyLayout();
                ApplyHeaderStaticStyles();
                RefreshHeaderStyles();
            }
        }

        /// <summary>現在のタブ id。変更は <see cref="SelectTab"/> 経由で行う。</summary>
        public string ActiveId => _activeId;

        /// <summary>現在のタブ。1 枚も選べるタブが無ければ null。</summary>
        public TweeqTab ActiveTab => FindTab(_activeId);

        /// <summary>登録済みのタブ（ヘッダーの並び順）。</summary>
        public IReadOnlyList<TweeqTab> Tabs => _tabs;

        /// <summary>この <see cref="TweeqTabs"/> が使う永続化キー。空なら永続化しない。</summary>
        public string ResolvedStorageKey => PrefsKey(_tabsName, _storageKey);

        /// <summary>
        /// UXML の子や素の Add() がパネル層に入るようにする（内部構築は hierarchy.Add 経由なので安全）。
        /// コンストラクタ中は _panels 生成前に呼ばれ得るため null ガードする
        /// </summary>
        public override VisualElement contentContainer => _panels ?? this;

        /// <summary>配色テーマ。ヘッダーへ適用し、パネル内の <see cref="ITweeqThemed"/> 子孫へ転送する。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // 同一インスタンスでも打ち切らない。テーマ設定後に足されたタブへ届ける
                _theme = value ?? TweeqTheme.Dark();
                ApplyLayout();
                ApplyHeaderStaticStyles();
                RefreshHeaderStyles();
                TweeqThemeDistribution.Distribute(_panels, _theme);
            }
        }

        /// <summary>
        /// タブを登録する。同じインスタンス・同じ id の二重登録は弾く（React 修正 3）。
        /// 通常は <see cref="TweeqTab"/> 側から呼ばれる。
        /// </summary>
        public void RegisterTab(TweeqTab tab)
        {
            if (tab == null || _tabs.Contains(tab))
            {
                return;
            }

            // 名無しタブ（id 空）は互いに衝突しないものとして扱う（FindTab が空 id を拾わない）
            if (FindTab(tab.Id) != null)
            {
                // 弾いたパネルを出したままにすると 2 枚が重なって表示される。
                // 原因が分かるよう警告を出したうえで畳む（例外は投げない）
                Debug.LogWarning(
                    $"{nameof(TweeqTabs)}: id '{tab.Id}' のタブが重複している。後から来た方を無視する");
                tab.SetActive(false);
                return;
            }

            _tabs.Add(tab);
            tab.SetOwner(this);

            RebuildHeaders();
            EnsureActiveTab();
            ApplyActive();
        }

        /// <summary>タブの登録を解除する。アクティブだった場合は選択を張り直す。</summary>
        public void UnregisterTab(TweeqTab tab)
        {
            if (tab == null || !_tabs.Remove(tab))
            {
                return;
            }

            if (ReferenceEquals(_hoveredTab, tab))
            {
                _hoveredTab = null;
            }

            tab.SetOwner(null);

            RebuildHeaders();
            EnsureActiveTab();
            ApplyActive();
        }

        /// <summary>
        /// 登録済みタブのプロパティ変更を取り込む（Vue の watch → updateTab 相当）。
        /// 未登録のタブを渡されても何もしない（Vue は `tabs[-1]` で TypeError になるバグ）。
        /// </summary>
        public void UpdateTab(TweeqTab tab)
        {
            if (tab == null || _tabs.IndexOf(tab) < 0)
            {
                return;
            }

            RebuildHeaders();
            EnsureActiveTab();
            ApplyActive();
        }

        /// <summary>
        /// パネル層を走査して、まだ登録されていない <see cref="TweeqTab"/> を拾い上げる。
        /// 各タブは自分の <see cref="AttachToPanelEvent"/> で登録してくるので通常は不要だが、
        /// パネルに載せずに木を組む経路（テスト・エディタ拡張）の取りこぼしを塞ぐ。
        /// </summary>
        public void SyncTabsFromHierarchy()
        {
            CollectAndRegister(_panels);
        }

        /// <summary>プログラムからタブを選ぶ。disabled なタブは無条件に弾く（React 修正 1）。</summary>
        public void SelectTab(string id)
        {
            TweeqTab selected = FindTab(id);
            if (selected == null)
            {
                return;
            }

            // Vue は event 引数があるときだけ弾いていたため、キーボードやプログラム選択で
            // disabled タブが選べてしまっていた
            if (selected.IsDisabled)
            {
                return;
            }

            if (_activeId == selected.Id)
            {
                // 再選択も「利用者がこのタブを選んだ」記録。暫定選択のままにしない
                _activeIdIsExplicit = true;
                Persist(_activeId);
                Clicked?.Invoke(selected);
                return;
            }

            ApplySelection(selected, true);
        }

        /// <summary>
        /// 矢印キー相当の移動。disabled を飛ばしてラップし、選択がフォーカスに追従する。
        /// <paramref name="direction"/> は負で前・正で次。
        /// </summary>
        public void MoveSelection(int direction)
        {
            if (direction == 0)
            {
                return;
            }

            int count = CountEnabled();
            if (count == 0)
            {
                return;
            }

            int current = EnabledIndexOf(_activeId);

            // 現在値が enabled 列に居ない（未選択・disabled）ときは先頭を起点にする（React と同じ）
            int start = current < 0 ? 0 : current;
            int delta = direction < 0 ? -1 : 1;
            int next = ((start + delta) % count + count) % count;

            SelectAndFocus(EnabledAt(next));
        }

        /// <summary>Home キー相当。最初の enabled タブへ。</summary>
        public void SelectFirstEnabled()
        {
            SelectAndFocus(EnabledAt(0));
        }

        /// <summary>End キー相当。最後の enabled タブへ。</summary>
        public void SelectLastEnabled()
        {
            SelectAndFocus(EnabledAt(CountEnabled() - 1));
        }

        /// <summary>
        /// 保存済みのアクティブタブを消して既定（未設定）へ戻す。
        /// Vue の appConfig が「既定値に戻ったらキーを削除する」挙動に対応する。
        /// </summary>
        public void ClearPersistedActiveTab()
        {
            string key = ResolvedStorageKey;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            Storage.Delete(key);
        }

        /// <summary>
        /// <paramref name="index"/> 番目のヘッダー要素。範囲外なら null。
        /// tabIndex / フォーカスの確認や、外から装飾を足したい場合に使う。
        /// </summary>
        public VisualElement GetHeader(int index)
        {
            if (index < 0 || index >= _headers.Count)
            {
                return null;
            }

            return _headers[index];
        }

        /// <summary>id からタブを引く。null / 空 id は常に null（名無しタブを拾わないため）。</summary>
        public TweeqTab FindTab(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab != null && tab.Id == id)
                {
                    return tab;
                }
            }

            return null;
        }

        #endregion

        #region Construction

        public TweeqTabs()
        {
            this.AddToClassList(USS_CLASS_NAME);

            _tabList = new VisualElement { name = "tweeq-tabs-tablist" };
            _tabList.style.flexShrink = 0f;
            this.hierarchy.Add(_tabList);

            _panels = new VisualElement { name = "tweeq-tabs-panels" };
            _panels.style.flexDirection = FlexDirection.Column;
            this.hierarchy.Add(_panels);

            ApplyLayout();

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        public TweeqTabs(string tabsName)
            : this()
        {
            this.TabsName = tabsName;
        }

        #endregion

        #region Layout

        void ApplyLayout()
        {
            this.style.flexDirection = _vertical ? FlexDirection.Row : FlexDirection.Column;

            _tabList.style.flexDirection = _vertical ? FlexDirection.Column : FlexDirection.Row;
            TweeqGap.Apply(
                _tabList,
                _vertical ? TABLIST_GAP_VERTICAL : TABLIST_GAP_HORIZONTAL,
                _tabList.style.flexDirection.value);

            // 付け替え前に前回のモードの余白・罫線を落としておく（切替で残ると二重に見える）
            _panels.style.marginTop = 0f;
            _panels.style.marginLeft = 0f;
            _panels.style.borderLeftWidth = 0f;
            _panels.style.paddingLeft = 0f;

            if (_vertical)
            {
                EnsureScrollView();

                if (!ReferenceEquals(_panels.hierarchy.parent, _scroll.contentContainer))
                {
                    _panels.RemoveFromHierarchy();
                    _scroll.Add(_panels);
                }

                if (!ReferenceEquals(_scroll.hierarchy.parent, this))
                {
                    this.hierarchy.Add(_scroll);
                }

                _panels.style.flexGrow = 0f;

                _scroll.style.flexGrow = 1f;
                _scroll.style.minHeight = 0f;
                _scroll.style.marginLeft = ROOT_GAP_VERTICAL;
                _scroll.style.borderLeftWidth = PANELS_BORDER_WIDTH;
                _scroll.style.borderLeftColor = _theme.Border;
                _scroll.style.paddingLeft = PANELS_PADDING_LEFT;
                return;
            }

            if (_scroll != null && ReferenceEquals(_scroll.hierarchy.parent, this))
            {
                this.hierarchy.Remove(_scroll);
            }

            if (!ReferenceEquals(_panels.hierarchy.parent, this))
            {
                _panels.RemoveFromHierarchy();
                this.hierarchy.Add(_panels);
            }

            _panels.style.flexGrow = 1f;
            _panels.style.minHeight = 0f;
            _panels.style.marginTop = ROOT_GAP_HORIZONTAL;
        }

        void EnsureScrollView()
        {
            if (_scroll != null)
            {
                return;
            }

            _scroll = new ScrollView(ScrollViewMode.Vertical) { name = "tweeq-tabs-scroll" };
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            // クリップ境界の「内側」に置かないと意味が無い（ScrollView 自体の padding は
            // viewport の外に付くので、中身は相変わらず viewport 端に密着して切れる）
            VisualElement content = _scroll.contentContainer;
            if (content != null)
            {
                content.style.paddingTop = CLIP_SAFE_PADDING;
                content.style.paddingBottom = CLIP_SAFE_PADDING;
                content.style.paddingLeft = CLIP_SAFE_PADDING;
                content.style.paddingRight = CLIP_SAFE_PADDING;
            }
        }

        #endregion

        #region Headers

        // ヘッダーは登録の増減・プロパティ変更でしか作り直さない（毎フレーム経路には無い）
        void RebuildHeaders()
        {
            for (int i = 0; i < _headers.Count; i++)
            {
                _headers[i].RemoveFromHierarchy();
            }

            _headers.Clear();
            _headerLabels.Clear();
            _hoveredTab = null;

            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null)
                {
                    continue;
                }

                VisualElement header = new VisualElement { name = "tweeq-tabs-header" };

                // ヘッダー自身がフォーカスを取る（roving tabIndex）。実際の 0 / -1 は
                // RefreshHeaderStyles がアクティブ状態に合わせて配る
                header.focusable = true;

                UILabel label = new UILabel(tab.TabName)
                {
                    name = "tweeq-tabs-header-label",

                    // 当たり判定はヘッダー側で取る（縦のときは行全体がクリック対象）
                    pickingMode = PickingMode.Ignore,
                };
                header.Add(label);

                // 登録が変わるたびに作り直すので、インデックスではなく実体を掴んでおく
                TweeqTab captured = tab;
                header.RegisterCallback<ClickEvent>(_ => SelectTab(captured.Id));
                header.RegisterCallback<PointerEnterEvent>(_ => SetHovered(captured));
                header.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(null));
                header.RegisterCallback<KeyDownEvent>(OnHeaderKeyDown);
                header.RegisterCallback<NavigationMoveEvent>(OnHeaderNavigationMove);

                _tabList.Add(header);
                _headers.Add(header);
                _headerLabels.Add(label);
            }

            TweeqGap.Apply(
                _tabList,
                _vertical ? TABLIST_GAP_VERTICAL : TABLIST_GAP_HORIZONTAL,
                _tabList.style.flexDirection.value);

            ApplyHeaderStaticStyles();
            RefreshHeaderStyles();
        }

        void ApplyHeaderStaticStyles()
        {
            float duration = _theme.HoverTransitionDuration;

            for (int i = 0; i < _headers.Count; i++)
            {
                VisualElement header = _headers[i];
                UILabel label = _headerLabels[i];

                header.style.flexShrink = 0f;
                header.style.flexDirection = FlexDirection.Column;

                label.style.marginTop = 0f;
                label.style.marginBottom = 0f;
                label.style.marginLeft = 0f;
                label.style.marginRight = 0f;
                label.style.whiteSpace = WhiteSpace.NoWrap;

                // Vue の font-weight: bold。フォント自体はパネル既定のまま（FontHeading は使わない）
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.height = HEADER_LINE_HEIGHT;

                if (_vertical)
                {
                    // 項目間の隙間は TweeqGap が主軸側（縦なら marginTop）に配る。
                    // 直交軸に前のモードの残りが居ると列がずれるので必ず落とす
                    header.style.marginLeft = 0f;

                    header.style.borderBottomWidth = 0f;
                    header.style.borderLeftWidth = INDICATOR_WIDTH;
                    header.style.paddingTop = 0f;
                    header.style.paddingBottom = 0f;
                    header.style.paddingLeft = 0f;
                    header.style.paddingRight = 0f;

                    // padding は項目ではなくラベル側に置く＝列幅いっぱいが hover / クリック対象になる
                    label.style.paddingTop = VERTICAL_LABEL_PADDING_BLOCK;
                    label.style.paddingBottom = VERTICAL_LABEL_PADDING_BLOCK;
                    label.style.paddingLeft = VERTICAL_LABEL_PADDING_INLINE;
                    label.style.paddingRight = VERTICAL_LABEL_PADDING_INLINE;
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                }
                else
                {
                    header.style.marginTop = 0f;

                    header.style.borderLeftWidth = 0f;
                    header.style.borderBottomWidth = INDICATOR_WIDTH;
                    header.style.paddingTop = HEADER_PADDING_TOP;
                    header.style.paddingBottom = 0f;
                    header.style.paddingLeft = HEADER_PADDING_INLINE;
                    header.style.paddingRight = HEADER_PADDING_INLINE;

                    label.style.paddingTop = 0f;
                    label.style.paddingBottom = 0f;
                    label.style.paddingLeft = 0f;
                    label.style.paddingRight = 0f;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                }

                // 線の太さは動かさず色だけ遷移させる（レイアウトを揺らさないため）。
                // 横／縦どちらの線にも掛けておけば Vertical 切替で貼り直さなくて済む
                ApplyTransition(
                    header, duration, EasingMode.Ease, "border-bottom-color", "border-left-color");

                // 色も一緒に遷移させないと、hover を外した瞬間に opacity が高いまま色だけ
                // Text へ戻り、暗くなる前に白く光る（Vue の style ブロックのコメントと同じ理由）
                ApplyTransition(label, duration, EasingMode.Ease, "opacity", "color");
            }
        }

        void RefreshHeaderStyles()
        {
            for (int i = 0; i < _headers.Count && i < _tabs.Count; i++)
            {
                VisualElement header = _headers[i];
                UILabel label = _headerLabels[i];
                TweeqTab tab = _tabs[i];
                if (header == null || label == null || tab == null)
                {
                    continue;
                }

                bool active = !string.IsNullOrEmpty(_activeId) && tab.Id == _activeId;
                bool disabled = tab.IsDisabled;
                bool hovered = !disabled && ReferenceEquals(_hoveredTab, tab);

                label.text = tab.TabName;

                // アクティブの線は Text、そこへ hover すると Accent（Vue の .active:hover）
                Color indicator = active
                    ? (hovered ? _theme.Accent : _theme.Text)
                    : Color.clear;

                if (_vertical)
                {
                    header.style.borderLeftColor = indicator;
                }
                else
                {
                    header.style.borderBottomColor = indicator;
                }

                label.style.opacity = active || hovered ? 1f : INACTIVE_OPACITY;
                label.style.color = hovered ? _theme.Accent : _theme.Text;

                // disabled は hover に反応させない（pickingMode を落とせば Enter/Leave も来ない）
                header.pickingMode = disabled ? PickingMode.Ignore : PickingMode.Position;
                header.focusable = !disabled;
                header.tabIndex = active ? 0 : -1;
            }
        }

        void SetHovered(TweeqTab tab)
        {
            if (ReferenceEquals(_hoveredTab, tab))
            {
                return;
            }

            _hoveredTab = tab;
            RefreshHeaderStyles();
        }

        #endregion

        #region Selection

        // 選択の再評価（Vue の watch(tabs) → ensureActiveTab 相当）。
        // 永続値 → DefaultTabId → 先頭 の全段で disabled を除外する（React 修正 2）
        void EnsureActiveTab()
        {
            if (_tabs.Count == 0)
            {
                return;
            }

            string next = ResolveActiveId();

            if (!string.IsNullOrEmpty(next))
            {
                if (next != _activeId)
                {
                    TweeqTab tab = FindTab(next);
                    if (tab != null)
                    {
                        ApplySelection(tab, false);
                    }
                }

                return;
            }

            if (string.IsNullOrEmpty(_activeId))
            {
                return;
            }

            // 選べるタブが 1 枚も無くなった。選択だけ落とす（保存値は利用者の設定なので消さない）
            _activeId = string.Empty;
            _activeIdIsExplicit = false;
            ApplyActive();
        }

        // 実際に選択を切り替える唯一の場所。
        // explicitChoice=false（解決による暫定選択）では永続化しない。タブは 1 枚ずつ
        // 登録されてくるので、ここで書いてしまうと「保存済みタブがまだ登録されていない」
        // 一瞬のあいだに先頭タブで保存値を上書きし、復元が毎回壊れる
        void ApplySelection(TweeqTab selected, bool explicitChoice)
        {
            _activeId = selected.Id;
            _activeIdIsExplicit = explicitChoice;

            if (explicitChoice)
            {
                Persist(_activeId);
            }

            ApplyActive();
            Changed?.Invoke(selected);
        }

        string ResolveActiveId()
        {
            // 利用者が選んだ結果は最優先で維持する
            if (_activeIdIsExplicit)
            {
                string chosen = Selectable(_activeId);
                if (chosen != null)
                {
                    return chosen;
                }
            }

            string persisted = Selectable(LoadPersisted());
            if (persisted != null)
            {
                return persisted;
            }

            string preferred = Selectable(_defaultTabId);
            if (preferred != null)
            {
                return preferred;
            }

            // 保存値も既定も（まだ）居ない。今の暫定選択が有効ならそのまま据え置く
            string current = Selectable(_activeId);
            if (current != null)
            {
                return current;
            }

            // 最終フォールバックも「最初の enabled タブ」。Vue は tabs[0] なので
            // 先頭が disabled だと何も選べないままだった
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab != null && !tab.IsDisabled && !string.IsNullOrEmpty(tab.Id))
                {
                    return tab.Id;
                }
            }

            return string.Empty;
        }

        // 選べる id ならそのまま、選べないなら null（呼び出し側の ?? 連鎖のため）
        string Selectable(string id)
        {
            TweeqTab tab = FindTab(id);
            return tab != null && !tab.IsDisabled ? tab.Id : null;
        }

        void ApplyActive()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null)
                {
                    continue;
                }

                tab.SetActive(!string.IsNullOrEmpty(_activeId) && tab.Id == _activeId);
            }

            RefreshHeaderStyles();
        }

        void SelectAndFocus(TweeqTab tab)
        {
            if (tab == null)
            {
                return;
            }

            // キーボード移動で「同じタブ」に着いたときは Clicked を出さない（React と同じ）。
            // 選択の変化とクリックの再選択は別の意味なので混ぜない
            if (tab.Id != _activeId)
            {
                SelectTab(tab.Id);
            }

            FocusHeader(tab);
        }

        void FocusHeader(TweeqTab tab)
        {
            if (this.panel == null || tab == null)
            {
                return;
            }

            int index = _tabs.IndexOf(tab);
            if (index < 0 || index >= _headers.Count)
            {
                return;
            }

            _headers[index].Focus();
        }

        int CountEnabled()
        {
            int count = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab != null && !tab.IsDisabled)
                {
                    count++;
                }
            }

            return count;
        }

        // enabled だけを詰めた仮想の列に対する添字。List を作らずに数えるのでアロケーション無し
        int EnabledIndexOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }

            int index = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null || tab.IsDisabled)
                {
                    continue;
                }

                if (tab.Id == id)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        TweeqTab EnabledAt(int index)
        {
            if (index < 0)
            {
                return null;
            }

            int current = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null || tab.IsDisabled)
                {
                    continue;
                }

                if (current == index)
                {
                    return tab;
                }

                current++;
            }

            return null;
        }

        #endregion

        #region Persistence

        string LoadPersisted()
        {
            string key = ResolvedStorageKey;
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return Storage.Get(key, string.Empty) ?? string.Empty;
        }

        void Persist(string id)
        {
            string key = ResolvedStorageKey;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                // 既定（未設定）へ戻すのは削除。空文字を書き残すと次回の復元で
                // 「保存済みだが無効な id」として一段余計に落ちる
                Storage.Delete(key);
                return;
            }

            Storage.Set(key, id);
        }

        #endregion

        #region Keyboard

        void OnHeaderKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Home:
                    SelectFirstEnabled();
                    break;

                case KeyCode.End:
                    SelectLastEnabled();
                    break;

                case KeyCode.LeftArrow:
                    if (_vertical)
                    {
                        return;
                    }

                    MoveSelection(-1);
                    break;

                case KeyCode.RightArrow:
                    if (_vertical)
                    {
                        return;
                    }

                    MoveSelection(1);
                    break;

                case KeyCode.UpArrow:
                    if (!_vertical)
                    {
                        return;
                    }

                    MoveSelection(-1);
                    break;

                case KeyCode.DownArrow:
                    if (!_vertical)
                    {
                        return;
                    }

                    MoveSelection(1);
                    break;

                default:
                    return;
            }

            evt.StopPropagation();
        }

        // 矢印キーは KeyDown と別に NavigationMoveEvent も飛び、そちらが勝手にフォーカスを
        // 動かしてしまう（RadioInput と同じ対処）。移動先は自分で決めるので握り潰す
        void OnHeaderNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                    break;

                default:
                    return;
            }

            evt.StopPropagation();
            this.focusController?.IgnoreEvent(evt);
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // 各 TweeqTab も自分の Attach で登録してくるが、順序に依らず揃うよう保険を掛ける
            SyncTabsFromHierarchy();
        }

        void CollectAndRegister(VisualElement parent)
        {
            if (parent == null)
            {
                return;
            }

            int count = parent.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                VisualElement child = parent.hierarchy.ElementAt(i);
                if (child == null)
                {
                    continue;
                }

                // 入れ子のタブ群は相手の担当。中まで潜らない
                if (child is TweeqTabs)
                {
                    continue;
                }

                if (child is TweeqTab tab)
                {
                    RegisterTab(tab);
                    continue;
                }

                CollectAndRegister(child);
            }
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
