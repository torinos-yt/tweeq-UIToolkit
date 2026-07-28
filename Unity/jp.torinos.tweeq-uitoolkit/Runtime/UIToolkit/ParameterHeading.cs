using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A single section-heading row (spec §3). 24px height, 14px bold, with a right slot.
    /// </summary>
    [UxmlElement]
    public partial class ParameterHeading : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>USS class for the heading text element.</summary>
        public const string TEXT_USS_CLASS_NAME = "tweeq-parameter-heading__text";

        /// <summary>USS class for the right slot.</summary>
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

        /// <summary>The heading string.</summary>
        [UxmlAttribute("text")]
        public string Text
        {
            get => _text.text;
            set => _text.text = value ?? string.Empty;
        }

        /// <summary>The slot placed flush against the right edge.</summary>
        public VisualElement Right => _right;

        /// <summary>
        /// The container on the heading text side. Used by ParameterGroup to insert its chevron
        /// and to receive click/hover events.
        /// </summary>
        public VisualElement HeadingContainer => _headingContainer;

        /// <summary>The heading text element itself. Use this when you want to apply a color transition.</summary>
        public VisualElement TextElement => _text;

        /// <summary>
        /// The heading text color. Defaults to Theme.Text. Once explicitly set, it is no longer
        /// overwritten by Theme changes (because ParameterGroup manages its own TextMuted<->Text
        /// hover transition).
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

        /// <summary>Color theme. Normally distributed by the ParameterGrid.</summary>
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

            // Weight is decided together with the font's actual weight in ApplyStaticStyles
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

        // The heading font (Geist SemiBold) has an actual weight, so combining it with FontStyle.Bold
        // stacks the legacy Font's faux-bold on top and crushes the glyphs. Only when it failed to
        // load (i.e. empty, meaning it fell back to the panel's default font) is faux-bold the only
        // way left to look bold, so keep Bold in that case
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
