using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// TweeqModalDialog の契約（m8-modal-tabs-spec.md §B「テスト契約」）を検証する。
    ///
    /// キー配線は「パネル root のバブル段階 → <see cref="TweeqModalDialog.PerformKey"/>」という
    /// 二段構えで、判断は後段に閉じている。EditMode ではポインタ／キーイベントを合成できないので
    /// 後段を直接叩いて確かめる。以下は Play Mode 側の担当:
    /// - 実際の KeyDownEvent が root のバブル段階まで上がってくること
    /// - 内側の部品（TextField 編集・ドラッグ中の Escape）が先に StopPropagation する優先順
    /// </summary>
    public class TweeqModalDialogTests
    {
        TweeqModalTestPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
        }

        TweeqModalTestPanel RequirePanel()
        {
            if (_panel == null)
            {
                _panel = TweeqModalTestPanel.Create();
            }

            return _panel;
        }

        static TweeqModalDialog Create()
        {
            return new TweeqModalDialog();
        }

        #region 構造

        [Test]
        public void Content_GoesIntoTheBodyScrollView()
        {
            TweeqModalDialog dialog = Create();
            VisualElement child = new VisualElement();

            dialog.Add(child);

            Assert.AreSame(dialog.Body.contentContainer, dialog.contentContainer);
            Assert.AreSame(dialog.Body.contentContainer, child.hierarchy.parent);
        }

        [Test]
        public void Shell_LivesInsideThePane()
        {
            TweeqModalDialog dialog = Create();
            VisualElement content = dialog.Pane.contentContainer;

            // タイトル → 本文 → フッターの順（縦積み）
            Assert.AreEqual(3, content.hierarchy.childCount);
            Assert.AreSame(dialog.Body, content.hierarchy.ElementAt(1));
        }

        [Test]
        public void Title_IsHiddenWhenEmpty()
        {
            TweeqModalDialog dialog = Create();
            Label title = dialog.Pane.Q<Label>("tweeq-modal-dialog-title");

            Assert.IsNotNull(title);
            Assert.AreEqual(DisplayStyle.None, title.style.display.value);

            dialog.Title = "Settings";

            Assert.AreEqual(DisplayStyle.Flex, title.style.display.value);
            Assert.AreEqual("Settings", title.text);
        }

        [Test]
        public void Title_UsesTheHeadingFontWithoutDoubleBolding()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            Assume.That(
                TweeqFonts.IsEmpty(theme.FontHeading),
                Is.False,
                "既定テーマの FontHeading が空。同梱フォントが Resources から読めていない");

            TweeqModalDialog dialog = Create();
            dialog.Theme = theme;
            Label title = dialog.Pane.Q<Label>("tweeq-modal-dialog-title");

            Assert.AreSame(theme.FontHeading.font, title.style.unityFontDefinition.value.font);
            Assert.AreEqual(FontStyle.Normal, title.style.unityFontStyleAndWeight.value);
            Assert.AreEqual(TweeqModalDialog.TITLE_FONT_SIZE, title.style.fontSize.value.value, 0.001f);
        }

        [Test]
        public void Title_KeepsFauxBoldWhenTheHeadingFontIsUnavailable()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.FontHeading = default;

            TweeqModalDialog dialog = Create();
            dialog.Theme = theme;
            Label title = dialog.Pane.Q<Label>("tweeq-modal-dialog-title");

            Assert.AreEqual(FontStyle.Bold, title.style.unityFontStyleAndWeight.value);
        }

        #endregion

        #region フッター

        [Test]
        public void Footer_UsesTheDefaultLabels()
        {
            TweeqModalDialog dialog = Create();

            Assert.AreEqual(TweeqModalDialog.DEFAULT_CANCEL_LABEL, dialog.CancelLabel);
            Assert.AreEqual(TweeqModalDialog.DEFAULT_CONFIRM_LABEL, dialog.ConfirmLabel);

            // Cancel は控えめな塗り（Vue の subtle）
            Assert.IsTrue(dialog.CancelButton.Subtle);
            Assert.IsFalse(dialog.ConfirmButton.Subtle);
        }

        [Test]
        public void Footer_LabelsAreSettable()
        {
            TweeqModalDialog dialog = Create();

            dialog.ConfirmLabel = "Done";
            dialog.CancelLabel = "Discard";

            Assert.AreEqual("Done", dialog.ConfirmButton.Label);
            Assert.AreEqual("Discard", dialog.CancelButton.Label);
        }

        [Test]
        public void Footer_StretchSharesTheWidth()
        {
            TweeqModalDialog dialog = Create();

            Assert.IsTrue(dialog.FooterStretch);
            Assert.AreEqual(1f, dialog.CancelButton.style.flexGrow.value, 0.001f);
            Assert.AreEqual(1f, dialog.ConfirmButton.style.flexGrow.value, 0.001f);
        }

        [Test]
        public void Footer_WithoutStretchAlignsToTheRight()
        {
            TweeqModalDialog dialog = Create();

            dialog.FooterStretch = false;

            VisualElement footer = dialog.Pane.Q("tweeq-modal-dialog-footer");
            Assert.IsNotNull(footer);
            Assert.AreEqual(Justify.FlexEnd, footer.style.justifyContent.value);
            Assert.AreEqual(0f, dialog.ConfirmButton.style.flexGrow.value, 0.001f);
        }

        [Test]
        public void Footer_ButtonsAreWired()
        {
            TweeqModalDialog dialog = Create();
            int confirmed = 0;
            int cancelled = 0;
            dialog.Confirmed += () => confirmed++;
            dialog.Cancelled += () => cancelled++;

            dialog.Open = true;
            dialog.CancelButton.PerformClick();

            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(0, confirmed);
            Assert.IsFalse(dialog.Open, "Cancelled のあと Open は自動で倒れる");

            dialog.Open = true;
            dialog.ConfirmButton.PerformClick();

            Assert.AreEqual(1, confirmed);
            Assert.IsFalse(dialog.Open);
        }

        #endregion

        #region キー

        [Test]
        public void Key_EscapeCancelsAndCloses()
        {
            TweeqModalDialog dialog = Create();
            int cancelled = 0;
            dialog.Cancelled += () => cancelled++;
            dialog.Open = true;

            bool handled = dialog.PerformKey(KeyCode.Escape, null);

            Assert.IsTrue(handled);
            Assert.AreEqual(1, cancelled);
            Assert.IsFalse(dialog.Open);
        }

        [Test]
        public void Key_EnterConfirmsAndCloses()
        {
            TweeqModalDialog dialog = Create();
            int confirmed = 0;
            dialog.Confirmed += () => confirmed++;
            dialog.Open = true;

            bool handled = dialog.PerformKey(KeyCode.Return, null);

            Assert.IsTrue(handled);
            Assert.AreEqual(1, confirmed);
            Assert.IsFalse(dialog.Open);
        }

        [Test]
        public void Key_KeypadEnterConfirmsToo()
        {
            TweeqModalDialog dialog = Create();
            int confirmed = 0;
            dialog.Confirmed += () => confirmed++;
            dialog.Open = true;

            Assert.IsTrue(dialog.PerformKey(KeyCode.KeypadEnter, null));
            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Key_EnterPassesThroughInsideAMultilineTextField()
        {
            TweeqModalDialog dialog = Create();
            int confirmed = 0;
            dialog.Confirmed += () => confirmed++;

            TextField field = new TextField { multiline = true };
            dialog.Add(field);
            dialog.Open = true;

            // 実フォーカスは TextField 内部の入力要素に載るので、子から遡って持ち主を見つける
            VisualElement inner = field.hierarchy.childCount > 0
                ? field.hierarchy.ElementAt(0)
                : field;

            bool handled = dialog.PerformKey(KeyCode.Return, inner);

            Assert.IsFalse(handled, "複数行編集では改行を優先する");
            Assert.AreEqual(0, confirmed);
            Assert.IsTrue(dialog.Open);
        }

        [Test]
        public void Key_EnterStillConfirmsInsideASingleLineTextField()
        {
            TweeqModalDialog dialog = Create();
            int confirmed = 0;
            dialog.Confirmed += () => confirmed++;

            TextField field = new TextField();
            dialog.Add(field);
            dialog.Open = true;

            Assert.IsTrue(dialog.PerformKey(KeyCode.Return, field));
            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Key_EscapeIsIgnoredWhileClosed()
        {
            TweeqModalDialog dialog = Create();
            int cancelled = 0;
            dialog.Cancelled += () => cancelled++;

            Assert.IsFalse(dialog.PerformKey(KeyCode.Escape, null));
            Assert.AreEqual(0, cancelled);
        }

        [Test]
        public void Key_UnknownKeysAreNotConsumed()
        {
            TweeqModalDialog dialog = Create();
            dialog.Open = true;

            Assert.IsFalse(dialog.PerformKey(KeyCode.Space, null));
            Assert.IsTrue(dialog.Open);
        }

        [Test]
        public void Key_FiresOncePerOpenAcrossReopen()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModalDialog dialog = Create();
            panel.Root.Add(dialog);
            int cancelled = 0;
            dialog.Cancelled += () => cancelled++;

            dialog.Open = true;
            dialog.PerformKey(KeyCode.Escape, null);

            // 閉じた時にハンドラを外し損ねていると、2 周目で 2 回発火する
            dialog.Open = true;
            dialog.PerformKey(KeyCode.Escape, null);

            Assert.AreEqual(2, cancelled);
            Assert.IsFalse(dialog.Open);
        }

        [Test]
        public void Key_IsIgnoredWhileAPopoverIsOpenInTheLayer()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModalDialog dialog = Create();
            panel.Root.Add(dialog);
            int cancelled = 0;
            int confirmed = 0;
            dialog.Cancelled += () => cancelled++;
            dialog.Confirmed += () => confirmed++;
            dialog.Open = true;

            VisualElement anchor = new VisualElement();
            dialog.Add(anchor);

            TweeqPopover popover = new TweeqPopover();
            popover.Open(anchor);
            Assume.That(popover.IsOpen, Is.True, "ポップオーバーを層に載せられなかった");

            bool escapeHandled = dialog.PerformKey(KeyCode.Escape, null);
            bool enterHandled = dialog.PerformKey(KeyCode.Return, null);

            // ネストしたドロップダウン等が開いている間はそちらがキーの持ち主
            Assert.IsFalse(escapeHandled);
            Assert.IsFalse(enterHandled);
            Assert.AreEqual(0, cancelled);
            Assert.AreEqual(0, confirmed);
            Assert.IsTrue(dialog.Open);

            popover.Close();

            Assert.IsTrue(dialog.PerformKey(KeyCode.Escape, null));
            Assert.AreEqual(1, cancelled);
        }

        #endregion

        #region テーマ

        [Test]
        public void Theme_ReachesTheFooterButtonsAndTheContent()
        {
            TweeqModalDialog dialog = Create();
            ButtonInput inner = new ButtonInput("Inner");
            dialog.Add(inner);

            TweeqTheme theme = TweeqTheme.Light();
            dialog.Theme = theme;

            Assert.AreSame(theme, dialog.CancelButton.Theme);
            Assert.AreSame(theme, dialog.ConfirmButton.Theme);
            Assert.AreSame(theme, inner.Theme);
        }

        #endregion
    }
}
