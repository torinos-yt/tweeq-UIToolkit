using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// <see cref="TweeqTabs"/> の 1 枚（M8 仕様 §D「TweeqTab」）。
    /// 自身はパネル本体で、ヘッダー（タブリストの項目）は親の <see cref="TweeqTabs"/> が描く。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 親への登録は Vue の provide/inject 相当を <see cref="AttachToPanelEvent"/> で代替する。
    /// UXML から組んだ木は「子が揃ってからパネルに載る」ので、このタイミングが最も取りこぼしが少ない。
    /// </para>
    /// <para>
    /// Vue は祖先に Tabs が無いと throw するが、公演現場ではランタイム例外＝事故なので
    /// <b>例外を投げず、単独の可視コンテナとして振る舞う</b>（意図的逸脱・m8-modal-tabs-spec.md §D）。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqTab : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>この要素に付く USS クラス。</summary>
        public const string USS_CLASS_NAME = "tweeq-tab";

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        string _tabName = string.Empty;

        // 明示 id。空なら TabName のスラグへフォールバックする（Vue の computed id と同じ）
        string _explicitId = string.Empty;

        bool _isDisabled;

        // 単独使用（親なし）でも見えていなければならないので初期値は true
        bool _isActive = true;

        TweeqTabs _owner;

        #endregion

        #region Public API

        /// <summary>ヘッダーに出る表示名。空なら id も空になり「名無しタブ」として扱われる。</summary>
        [UxmlAttribute("tab-name")]
        public string TabName
        {
            get => _tabName;
            set
            {
                string next = value ?? string.Empty;
                if (_tabName == next)
                {
                    return;
                }

                _tabName = next;

                // Vue の watch → updateTab 相当。id もラベルもここで変わり得る
                _owner?.UpdateTab(this);
            }
        }

        /// <summary>
        /// タブ id。明示しなければ <see cref="TabName"/> を小文字化・空白→`-` にしたスラグ。
        /// </summary>
        [UxmlAttribute("id")]
        public string Id
        {
            get => string.IsNullOrEmpty(_explicitId) ? NormalizeId(_tabName) : _explicitId;
            set
            {
                string next = value ?? string.Empty;
                if (_explicitId == next)
                {
                    return;
                }

                _explicitId = next;
                _owner?.UpdateTab(this);
            }
        }

        /// <summary>ヘッダーを無効表示にし、選択できなくする。</summary>
        [UxmlAttribute("disabled")]
        public bool IsDisabled
        {
            get => _isDisabled;
            set
            {
                if (_isDisabled == value)
                {
                    return;
                }

                _isDisabled = value;
                _owner?.UpdateTab(this);
            }
        }

        /// <summary>登録先の <see cref="TweeqTabs"/>。単独使用なら null。</summary>
        public TweeqTabs Owner => _owner;

        /// <summary>今このパネルが表示されているか。単独使用なら常に true。</summary>
        public bool IsActive => _isActive;

        /// <summary>配色テーマ。中身の <see cref="ITweeqThemed"/> 子孫へ転送する。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // 同一インスタンスでも打ち切らない。テーマ設定後に足された中身へ届ける。
                // 自身は色を持たない容れ物だが、TweeqRoot は ITweeqThemed で探索を打ち切るので
                // ここで配らないと中身までテーマが届かない
                _theme = value ?? TweeqTheme.Dark();
                TweeqThemeDistribution.Distribute(this, _theme);
            }
        }

        /// <summary>表示名からタブ id を作る（小文字化・空白をハイフンへ）。</summary>
        public static string NormalizeId(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            // カルチャ依存の ToLower はトルコ語ロケールで I を潰すので Invariant 固定
            return name.ToLowerInvariant().Replace(' ', '-');
        }

        /// <summary>
        /// 祖先の <see cref="TweeqTabs"/> を探して登録する。祖先が無ければ何もしない。
        /// 通常は <see cref="AttachToPanelEvent"/> から自動で呼ばれるが、パネルに載せずに
        /// 木を組む場合（テスト・エディタ拡張）はここを直接叩く。
        /// </summary>
        public void ConnectToTabs()
        {
            TweeqTabs tabs = FindTabs();

            if (_owner != null && !ReferenceEquals(_owner, tabs))
            {
                // 付け替えられた。古い親から先に外さないとヘッダーが二重に残る
                _owner.UnregisterTab(this);
            }

            // 親が無いのは異常ではない（単独の可視要素として振る舞う契約）
            tabs?.RegisterTab(this);
        }

        /// <summary>登録先から外れる。単独使用なら何もしない。</summary>
        public void DisconnectFromTabs()
        {
            _owner?.UnregisterTab(this);
        }

        #endregion

        #region Construction

        public TweeqTab()
        {
            this.AddToClassList(USS_CLASS_NAME);
            this.style.flexDirection = FlexDirection.Column;

            // Vue の `.TqTab { height: 100% }` 相当。パネル領域いっぱいに広がる
            this.style.flexGrow = 1f;
            this.style.minHeight = 0f;

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        public TweeqTab(string tabName)
            : this()
        {
            this.TabName = tabName;
        }

        public TweeqTab(string tabName, string id)
            : this()
        {
            this.TabName = tabName;
            this.Id = id;
        }

        #endregion

        #region Tabs interop

        // 親からのみ呼ばれる。登録簿と表示状態の持ち主を 1 つに保つための入口
        internal void SetOwner(TweeqTabs owner)
        {
            _owner = owner;

            if (_owner == null)
            {
                // 単独に戻ったら必ず見える状態へ（display:none のまま孤児になるのを防ぐ）
                SetActive(true);
            }
        }

        internal void SetActive(bool active)
        {
            if (_isActive == active)
            {
                return;
            }

            _isActive = active;

            // 非アクティブは display:none（意図的逸脱・m8-modal-tabs-spec.md §D）。
            // Vue は grid 同一セル重ね＋opacity で高さを最長タブに保つが、UITK に同等の
            // レイアウトが無く、opacity を選ぶ理由だった Monaco 対策も Unity には無い
            this.style.display = _isActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ConnectToTabs();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            DisconnectFromTabs();
        }

        // ParameterGrid.Find と同じ手動の祖先探索。UQuery は型制約でインターフェースを
        // 取れず、contentContainer を差し替えた部品を跨ぐので hierarchy を自前で辿る
        TweeqTabs FindTabs()
        {
            VisualElement current = this.hierarchy.parent;
            while (current != null)
            {
                if (current is TweeqTabs tabs)
                {
                    return tabs;
                }

                current = current.hierarchy.parent;
            }

            return null;
        }

        #endregion
    }
}
