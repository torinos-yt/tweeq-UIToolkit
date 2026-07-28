using UnityEngine;
using UnityEngine.UIElements;

// クラス側に string Label プロパティがあるため、Label 型は別名で参照する
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// 「ラベル｜入力」の 1 行（仕様 §3）。
    /// ラベル幅は自分では決めず、祖先の <see cref="ParameterGrid"/> から配られる。
    /// </summary>
    [UxmlElement]
    public partial class Parameter : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>ラベル要素の USS クラス。</summary>
        public const string LABEL_USS_CLASS_NAME = "tweeq-parameter__label";

        /// <summary>入力コンテナの USS クラス。</summary>
        public const string INPUT_USS_CLASS_NAME = "tweeq-parameter__input";

        const float LABEL_FONT_SIZE = 12f;

        // MeasureTextSize は詰めて返すことがあるため、切れ防止に 1px だけ余裕を持たせる
        const float MEASURE_PAD = 1f;

        // 0.5px 未満の差で書き戻すと GeometryChangedEvent が往復し続ける
        const float WIDTH_EPSILON = 0.5f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();
        readonly UILabel _label;
        readonly VisualElement _input;
        string _hint = string.Empty;

        // Grid から最後に配られた幅。resolvedStyle は次のレイアウトまで更新されないので、
        // 「変化したときだけ書く」判定はこちらで持つ
        float _appliedLabelWidth = float.NaN;

        #endregion

        #region Public API

        /// <summary>ラベル文字列。変更すると Grid のラベル列幅が再計算される。</summary>
        [UxmlAttribute("label")]
        public string Label
        {
            get => _label.text;
            set
            {
                string text = value ?? string.Empty;
                if (_label.text == text)
                {
                    return;
                }

                _label.text = text;
                ParameterGrid.Find(this)?.RequestRefresh();
            }
        }

        /// <summary>
        /// ツールチップ文言。Tooltip 基盤が入るまでは保持するだけ（仕様 §3）。
        /// UI Toolkit 標準の tooltip にも流しておく。
        /// </summary>
        public string Hint
        {
            get => _hint;
            set
            {
                _hint = value ?? string.Empty;
                this.tooltip = _hint;
            }
        }

        /// <summary>入力コントロールを Add する先。</summary>
        public VisualElement InputContainer => _input;

        /// <summary>
        /// UXML の子や素の Add() が入力列に入るようにする（内部構築は hierarchy.Add 経由なので安全）。
        /// コンストラクタ中は _input 生成前に呼ばれ得るため null ガードする
        /// </summary>
        public override VisualElement contentContainer => _input ?? this;

        /// <summary>配色テーマ。通常は ParameterGrid から配られる。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // 同一インスタンスでも打ち切らない。テーマ設定後に足された子へ届ける
                // 再配布の入り口はこの setter しか無い（M7 転送契約の取りこぼし修正）
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                TweeqThemeDistribution.Distribute(_input, _theme);
            }
        }

        /// <summary>入力コンテナ内の隙間（gapRelated）を配り直す。子を足したあとに呼ぶ。</summary>
        public void RefreshInputGaps()
        {
            TweeqGap.Apply(_input, _theme.RelatedGap, FlexDirection.Row);
        }

        #endregion

        #region Construction

        public Parameter()
        {
            this.AddToClassList("tweeq-parameter");
            this.style.flexDirection = FlexDirection.Row;

            // 本家 .TqParameterGrid の align-items:start（行は上揃え）
            this.style.alignItems = Align.FlexStart;

            _label = new UILabel(string.Empty);
            _label.AddToClassList(LABEL_USS_CLASS_NAME);

            // 高さ＝line-height＝InputHeight。UI Toolkit に line-height が無いので
            // 「固定高 + 垂直中央揃え」で代替する
            _label.style.unityTextAlign = TextAnchor.MiddleLeft;
            _label.style.whiteSpace = WhiteSpace.NoWrap;
            _label.style.flexShrink = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;
            _label.style.marginLeft = 0f;
            _label.style.paddingLeft = 0f;
            _label.style.paddingRight = 0f;
            _label.style.width = ParameterGrid.MIN_LABEL_WIDTH;
            this.hierarchy.Add(_label);

            _input = new VisualElement();
            _input.AddToClassList(INPUT_USS_CLASS_NAME);
            _input.style.flexGrow = 1f;
            _input.style.flexShrink = 1f;
            _input.style.flexDirection = FlexDirection.Row;
            _input.style.alignItems = Align.Center;

            // 本家 .input の min-width:0。これが無いと値列が縮まず、入力欄がはみ出す
            _input.style.minWidth = 0f;
            _input.RegisterCallback<GeometryChangedEvent>(OnInputGeometryChanged);
            this.hierarchy.Add(_input);

            ApplyStaticStyles();

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        public Parameter(string label)
            : this()
        {
            this.Label = label;
        }

        void ApplyStaticStyles()
        {
            _label.style.height = _theme.InputHeight;
            _label.style.fontSize = LABEL_FONT_SIZE;
            _label.style.color = _theme.TextMuted;

            // ラベル列と値列の間隔（本家 grid-gap = gapControl）
            _label.style.marginRight = _theme.GapControl;

            RefreshInputGaps();
        }

        #endregion

        #region Grid interop

        /// <summary>ラベルの希望幅（テキスト実測値）。テキストが無ければ 0。</summary>
        internal float MeasureLabelWidth()
        {
            string text = _label.text;
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            Vector2 size = _label.MeasureTextSize(
                text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);

            if (float.IsNaN(size.x) || size.x <= 0f)
            {
                // フォント未解決（パネル外など）。この行は幅の要求を出さない
                return 0f;
            }

            return size.x + MEASURE_PAD;
        }

        /// <summary>Grid が決めた共有ラベル幅を適用する。変化が無ければ何も書かない。</summary>
        internal void ApplyLabelWidth(float width)
        {
            if (!float.IsNaN(_appliedLabelWidth)
                && Mathf.Abs(_appliedLabelWidth - width) <= WIDTH_EPSILON)
            {
                return;
            }

            _appliedLabelWidth = width;
            _label.style.width = width;
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ParameterGrid.Find(this)?.RequestRefresh();
        }

        void OnInputGeometryChanged(GeometryChangedEvent evt)
        {
            // UI Toolkit には子の追加を知らせるイベントが無いので、
            // レイアウトが動いたタイミングで隙間を配り直す。
            // このイベントはバブルするため、入力欄内部の変化は無視する
            if (evt == null || !ReferenceEquals(evt.target, _input))
            {
                return;
            }

            RefreshInputGaps();
            StretchInputChildren();
        }

        // UXML の子は flexGrow 未指定のまま入ってくるため、入力部品が内在幅（24px 等）まで潰れる。
        // InputGroup.ApplyStretch と同じ「明示指定があれば尊重・無ければ伸ばす」保険を掛ける
        void StretchInputChildren()
        {
            if (_input == null)
            {
                return;
            }

            for (int i = 0; i < _input.childCount; i++)
            {
                VisualElement child = _input[i];
                if (child != null && child.style.flexGrow.keyword == StyleKeyword.Null)
                {
                    child.style.flexGrow = 1f;
                }
            }
        }

        #endregion
    }
}
