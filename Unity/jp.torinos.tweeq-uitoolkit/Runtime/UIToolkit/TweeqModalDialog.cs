using System;
using UnityEngine;
using UnityEngine.UIElements;

// クラス側に string Title プロパティを持つので、Label 型は別名で参照する（ButtonInput と同じ理由）
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// タイトル＋スクロール本文＋Cancel/確定フッターの定型モーダル
    /// （m8-modal-tabs-spec.md §B・Vue 版 PaneModalComplex / PaneModalTabs のシェル相当）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本家はスキーマ駆動フォーム（InputComplex）前提のシングルトンだが、こちらは
    /// <b>シェルだけを汎用化</b>して中身を利用者に任せる（意図的逸脱・仕様書が根拠）。
    /// </para>
    /// <para>
    /// <b>値のロールバックは利用者責務</b>。スキーマが無いので、Cancel で元へ戻したい場合は
    /// <see cref="TweeqModal.Opened"/> で現在値を控えて <see cref="Cancelled"/> で書き戻すこと。
    /// このクラスは中身の値には一切触らない。
    /// </para>
    /// <para>
    /// キーは開いている間だけパネル root の<b>バブル段階</b>で拾う。内側の部品
    /// （TextField 編集・ドラッグ中の Escape 復元・LightDismiss ポップオーバー）が先に処理して
    /// StopPropagation した場合は届かない＝それが正しい優先順。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqModalDialog : TweeqModal
    {
        #region Constants

        /// <summary>タイトルの文字サイズ（px）。</summary>
        public const float TITLE_FONT_SIZE = 14f;

        /// <summary><see cref="ConfirmLabel"/> の既定値。</summary>
        public const string DEFAULT_CONFIRM_LABEL = "Save";

        /// <summary><see cref="CancelLabel"/> の既定値。</summary>
        public const string DEFAULT_CANCEL_LABEL = "Cancel";

        // 本文 ScrollView のクリップ境界の内側に取る安全マージン。
        // フォーカスリング（inset −3px）等の枠外描画が viewport で切られるのを防ぐ
        const float CLIP_SAFE_PADDING = 4f;

        #endregion

        #region Fields

        readonly UILabel _title;
        readonly ScrollView _body;
        readonly VisualElement _footer;
        readonly ButtonInput _cancel;
        readonly ButtonInput _confirm;

        string _titleText = string.Empty;
        bool _footerStretch = true;

        // 毎回のメソッドグループ変換はデリゲートを確保するので、登録／解除で使い回す実体を持つ
        readonly EventCallback<KeyDownEvent> _onRootKeyDown;

        // 登録先を覚えておく。層が入れ替わっても必ず同じ相手から外すため
        VisualElement _keyRoot;

        #endregion

        #region Public API

        /// <summary>確定ボタン（または Enter）で発火する。発火後 <see cref="TweeqModal.Open"/> は false になる。</summary>
        public event Action Confirmed;

        /// <summary>取り消しボタン（または Escape）で発火する。発火後 <see cref="TweeqModal.Open"/> は false になる。</summary>
        public event Action Cancelled;

        /// <summary>見出し。空なら行ごと消える。</summary>
        [UxmlAttribute("title")]
        public string Title
        {
            get => _titleText;
            set
            {
                string text = value ?? string.Empty;
                if (_titleText == text)
                {
                    return;
                }

                _titleText = text;
                ApplyTitle();
            }
        }

        /// <summary>確定ボタンの文字列。</summary>
        [UxmlAttribute("confirm-label")]
        public string ConfirmLabel
        {
            get => _confirm.Label;
            set => _confirm.Label = string.IsNullOrEmpty(value) ? DEFAULT_CONFIRM_LABEL : value;
        }

        /// <summary>取り消しボタンの文字列。</summary>
        [UxmlAttribute("cancel-label")]
        public string CancelLabel
        {
            get => _cancel.Label;
            set => _cancel.Label = string.IsNullOrEmpty(value) ? DEFAULT_CANCEL_LABEL : value;
        }

        /// <summary>
        /// フッターのボタンを均等割にするか（既定 true・Vue の PaneModalComplex）。
        /// false は右寄せ（PaneModalTabs）。
        /// </summary>
        [UxmlAttribute("footer-stretch")]
        public bool FooterStretch
        {
            get => _footerStretch;
            set
            {
                if (_footerStretch == value)
                {
                    return;
                }

                _footerStretch = value;
                ApplyFooterLayout();
            }
        }

        /// <summary>本文のスクロールビュー。中身は普通に Add すればここへ入る。</summary>
        public ScrollView Body => _body;

        /// <summary>取り消しボタン。ラベル以外を触りたい場合に使う。</summary>
        public ButtonInput CancelButton => _cancel;

        /// <summary>確定ボタン。ラベル以外を触りたい場合に使う。</summary>
        public ButtonInput ConfirmButton => _confirm;

        /// <summary>中身は本文のスクロールビューへ入る。</summary>
        public override VisualElement contentContainer
            => _body != null ? _body.contentContainer : base.contentContainer;

        /// <summary>取り消しを発火して閉じる。ボタンと Escape の共通経路。</summary>
        public void PerformCancel()
        {
            Cancelled?.Invoke();
            this.Open = false;
        }

        /// <summary>確定を発火して閉じる。ボタンと Enter の共通経路。</summary>
        public void PerformConfirm()
        {
            Confirmed?.Invoke();
            this.Open = false;
        }

        /// <summary>
        /// 開いている間のキー処理。パネル root のバブル段階から呼ばれる。
        /// 戻り値 true は「消費した」＝呼び出し側が StopPropagation する。
        /// </summary>
        /// <param name="keyCode">押されたキー。</param>
        /// <param name="source">キーの発生元（＝フォーカス中の要素）。null 可。</param>
        public bool PerformKey(KeyCode keyCode, VisualElement source)
        {
            if (!this.Open)
            {
                return false;
            }

            // Vue の「:popover-open が 1 つより多ければ何もしない」に相当。
            // 層にポップオーバーが開いている間は、そちらがキーの持ち主（ネストしたドロップダウン等）
            if (HasOpenPopover())
            {
                return false;
            }

            if (keyCode == KeyCode.Escape)
            {
                PerformCancel();
                return true;
            }

            if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter)
            {
                // 複数行 TextField の中では改行を優先する（確定は明示的にボタンで）
                if (IsInsideMultilineText(source))
                {
                    return false;
                }

                PerformConfirm();
                return true;
            }

            return false;
        }

        #endregion

        #region Construction

        public TweeqModalDialog()
        {
            this.name = "tweeq-modal-dialog";

            // 層へ載るのは構築後だが、ハンドラの実体は先に確保しておく（登録／解除で使い回す）
            _onRootKeyDown = OnRootKeyDown;

            TweeqTheme theme = this.Theme;

            // バルーンの中身は縦積み。本文だけが伸縮して内部スクロールになるよう、
            // 収縮を許す指定（UI Toolkit の既定は flex-shrink: 0）をこの階層から掛ける
            VisualElement content = this.Pane.contentContainer;
            content.style.flexDirection = FlexDirection.Column;
            content.style.flexShrink = 1f;
            content.style.minHeight = 0f;

            _title = new UILabel(string.Empty) { name = "tweeq-modal-dialog-title" };
            _title.style.fontSize = TITLE_FONT_SIZE;
            _title.style.flexShrink = 0f;
            _title.style.marginLeft = 0f;
            _title.style.marginRight = 0f;
            _title.style.marginTop = 0f;
            _title.style.marginBottom = 0f;
            _title.style.display = DisplayStyle.None;
            this.Pane.Add(_title);

            _body = new ScrollView(ScrollViewMode.Vertical) { name = "tweeq-modal-dialog-body" };
            _body.style.flexGrow = 1f;
            _body.style.flexShrink = 1f;
            _body.style.minHeight = 0f;

            // viewport は枠外描画（フォーカスリング等）を切るので、クリップ境界の内側に
            // 安全マージンを取る（TweeqTabs の CLIP_SAFE_PADDING と同じ理由）
            VisualElement bodyContent = _body.contentContainer;
            if (bodyContent != null)
            {
                bodyContent.style.paddingTop = CLIP_SAFE_PADDING;
                bodyContent.style.paddingBottom = CLIP_SAFE_PADDING;
                bodyContent.style.paddingLeft = CLIP_SAFE_PADDING;
                bodyContent.style.paddingRight = CLIP_SAFE_PADDING;
            }

            this.Pane.Add(_body);

            _footer = new VisualElement { name = "tweeq-modal-dialog-footer" };
            _footer.style.flexDirection = FlexDirection.Row;
            _footer.style.flexShrink = 0f;

            _cancel = new ButtonInput(DEFAULT_CANCEL_LABEL)
            {
                name = "tweeq-modal-dialog-cancel",
                Theme = theme,
                Subtle = true,
            };
            _confirm = new ButtonInput(DEFAULT_CONFIRM_LABEL)
            {
                name = "tweeq-modal-dialog-confirm",
                Theme = theme,
            };

            _cancel.Clicked += PerformCancel;
            _confirm.Clicked += PerformConfirm;

            _footer.Add(_cancel);
            _footer.Add(_confirm);
            this.Pane.Add(_footer);

            ApplyFooterLayout();
            ApplyDialogTheme();
        }

        #endregion

        #region Key routing

        protected override void OnMounted(TweeqOverlayLayer layer)
        {
            base.OnMounted(layer);

            if (_keyRoot != null || layer == null)
            {
                return;
            }

            VisualElement root = layer.hierarchy.parent;
            if (root == null)
            {
                return;
            }

            // バブル段階（TrickleDown ではない）。内側の部品が先に処理して止めたらここへは来ない
            _keyRoot = root;
            _keyRoot.RegisterCallback(_onRootKeyDown);
        }

        protected override void OnUnmounted()
        {
            if (_keyRoot != null)
            {
                _keyRoot.UnregisterCallback(_onRootKeyDown);
                _keyRoot = null;
            }

            base.OnUnmounted();
        }

        void OnRootKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // キーイベントの target はフォーカス中の要素。複数行判定はここを起点に遡る
            if (PerformKey(evt.keyCode, evt.target as VisualElement))
            {
                evt.StopPropagation();
            }
        }

        bool HasOpenPopover()
        {
            return ContainsPopover(this.Layer);
        }

        static bool ContainsPopover(VisualElement parent)
        {
            if (parent == null)
            {
                return false;
            }

            int childCount = parent.hierarchy.childCount;
            for (int index = 0; index < childCount; index++)
            {
                VisualElement child = parent.hierarchy.ElementAt(index);
                if (child == null)
                {
                    continue;
                }

                if (child is TweeqPopover || ContainsPopover(child))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsInsideMultilineText(VisualElement source)
        {
            // TextField の実フォーカスは内部の入力要素へ移るので、祖先を遡って持ち主を探す
            for (VisualElement node = source; node != null; node = node.hierarchy.parent)
            {
                if (node is TextField field && field.multiline)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Presentation

        protected override void OnThemeApplied()
        {
            ApplyDialogTheme();
        }

        void ApplyDialogTheme()
        {
            TweeqTheme theme = this.Theme;
            if (theme == null || _title == null)
            {
                return;
            }

            _title.style.color = theme.Text;

            // 見出しフォント（Geist SemiBold）は実ウェイトを持つので FontStyle.Bold を併せない。
            // ロードできなかった場合だけ、太く見せる手段が擬似ボールドしか無いので Bold を残す
            FontDefinition heading = theme.FontHeading;
            TweeqFonts.Apply(_title, heading);
            _title.style.unityFontStyleAndWeight = TweeqFonts.IsEmpty(heading)
                ? FontStyle.Bold
                : FontStyle.Normal;

            _cancel.Theme = theme;
            _confirm.Theme = theme;

            ApplyGaps();
        }

        void ApplyTitle()
        {
            _title.text = _titleText;
            _title.style.display = string.IsNullOrEmpty(_titleText)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            ApplyGaps();
        }

        // UI Toolkit に CSS の gap が無いのでマージンで作る。タイトルが消えている時に
        // 本文の上へ余白が残らないよう、可視状態を見て自前で配る
        void ApplyGaps()
        {
            TweeqTheme theme = this.Theme;
            float section = theme != null ? theme.GapSection : 18f;
            bool hasTitle = !string.IsNullOrEmpty(_titleText);

            _title.style.marginTop = 0f;
            _body.style.marginTop = hasTitle ? section : 0f;
            _footer.style.marginTop = section;

            TweeqGap.Apply(_footer, theme != null ? theme.GapControl : 9f, FlexDirection.Row);
        }

        void ApplyFooterLayout()
        {
            // 均等割は Vue の ModalComplex（2 択を等価に見せる）、右寄せは ModalTabs
            _footer.style.justifyContent = _footerStretch ? Justify.FlexStart : Justify.FlexEnd;

            ApplyFooterButton(_cancel);
            ApplyFooterButton(_confirm);
        }

        void ApplyFooterButton(ButtonInput button)
        {
            button.style.flexGrow = _footerStretch ? 1f : 0f;
            button.style.flexShrink = _footerStretch ? 1f : 0f;
        }

        #endregion
    }
}
