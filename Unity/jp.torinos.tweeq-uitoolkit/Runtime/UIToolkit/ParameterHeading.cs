using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// セクション見出しの 1 行（仕様 §3）。高さ 24px・14px bold・右スロット付き。
    /// </summary>
    [UxmlElement]
    public partial class ParameterHeading : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>見出しテキスト要素の USS クラス。</summary>
        public const string TEXT_USS_CLASS_NAME = "tweeq-parameter-heading__text";

        /// <summary>右スロットの USS クラス。</summary>
        public const string RIGHT_USS_CLASS_NAME = "tweeq-parameter-heading__right";

        const float FONT_SIZE = 14f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();
        readonly VisualElement _headingContainer;
        readonly Label _text;
        readonly VisualElement _right;
        bool _hasCustomTextColor;

        #endregion

        #region Public API

        /// <summary>見出し文字列。</summary>
        [UxmlAttribute("text")]
        public string Text
        {
            get => _text.text;
            set => _text.text = value ?? string.Empty;
        }

        /// <summary>右端に寄せて置くスロット。</summary>
        public VisualElement Right => _right;

        /// <summary>
        /// 見出しテキスト側のコンテナ。ParameterGroup がシェブロンを差し込み、
        /// クリック／ホバーを受けるために使う。
        /// </summary>
        public VisualElement HeadingContainer => _headingContainer;

        /// <summary>見出しテキスト要素そのもの。色遷移を掛けたい場合に使う。</summary>
        public VisualElement TextElement => _text;

        /// <summary>
        /// 見出し文字色。既定は Theme.Text。明示的に設定すると Theme 変更でも上書きされない
        /// （ParameterGroup は TextMuted↔Text のホバー遷移を自前で持つため）。
        /// </summary>
        public Color TextColor
        {
            get => _text.style.color.value;
            set
            {
                _hasCustomTextColor = true;
                _text.style.color = value;
            }
        }

        /// <summary>配色テーマ。通常は ParameterGrid から配られる。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                TweeqTheme next = value ?? TweeqTheme.Dark();
                if (ReferenceEquals(_theme, next))
                {
                    return;
                }

                _theme = next;
                ApplyStaticStyles();
            }
        }

        #endregion

        #region Construction

        public ParameterHeading()
        {
            this.AddToClassList("tweeq-parameter-heading");
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;

            _headingContainer = new VisualElement();
            _headingContainer.style.flexDirection = FlexDirection.Row;
            _headingContainer.style.alignItems = Align.Center;
            _headingContainer.style.flexGrow = 1f;
            _headingContainer.style.minWidth = 0f;
            this.hierarchy.Add(_headingContainer);

            _text = new Label(string.Empty);
            _text.AddToClassList(TEXT_USS_CLASS_NAME);

            // ウェイトは ApplyStaticStyles でフォントの実ウェイトと併せて決める
            _text.style.unityTextAlign = TextAnchor.MiddleLeft;
            _text.style.whiteSpace = WhiteSpace.NoWrap;
            _text.style.marginLeft = 0f;
            _text.style.marginRight = 0f;
            _text.style.marginTop = 0f;
            _text.style.marginBottom = 0f;
            _text.style.paddingLeft = 0f;
            _text.style.paddingRight = 0f;
            _headingContainer.Add(_text);

            _right = new VisualElement();
            _right.AddToClassList(RIGHT_USS_CLASS_NAME);
            _right.style.flexDirection = FlexDirection.Row;
            _right.style.alignItems = Align.Center;
            _right.style.flexShrink = 0f;
            this.hierarchy.Add(_right);

            ApplyStaticStyles();
        }

        public ParameterHeading(string text)
            : this()
        {
            this.Text = text;
        }

        void ApplyStaticStyles()
        {
            this.style.height = _theme.InputHeight;
            _text.style.fontSize = FONT_SIZE;
            ApplyHeadingFont();

            if (!_hasCustomTextColor)
            {
                _text.style.color = _theme.Text;
            }
        }

        // 見出しフォント（Geist SemiBold）は実ウェイトを持つので、FontStyle.Bold を併せると
        // レガシー Font の擬似ボールドが二重に乗って潰れる。ロードできなかった（＝空＝
        // パネル既定フォントに落ちた）場合だけ、太く見せる手段が擬似ボールドしか無いので Bold を残す
        void ApplyHeadingFont()
        {
            FontDefinition heading = _theme.FontHeading;
            TweeqFonts.Apply(_text, heading);
            _text.style.unityFontStyleAndWeight = TweeqFonts.IsEmpty(heading)
                ? FontStyle.Bold
                : FontStyle.Normal;
        }

        #endregion
    }
}
