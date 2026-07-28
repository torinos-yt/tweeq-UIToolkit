using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies TweeqModalDialog's contract (m8-modal-tabs-spec.md §B "Test Contract").
    ///
    /// Key wiring is a two-stage setup: "the panel root's bubble phase → <see cref="TweeqModalDialog.PerformKey"/>",
    /// with the decision confined to the latter stage. EditMode cannot synthesize pointer/key events,
    /// so this verifies by hitting the latter stage directly. The following are the Play Mode side's responsibility:
    /// - That an actual KeyDownEvent bubbles up through the root's bubble phase
    /// - The priority order by which inner components (TextField editing, Escape while dragging) call StopPropagation first
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

        #region Structure

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

            // Title → body → footer order (stacked vertically)
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

        #region Footer

        [Test]
        public void Footer_UsesTheDefaultLabels()
        {
            TweeqModalDialog dialog = Create();

            Assert.AreEqual(TweeqModalDialog.DEFAULT_CANCEL_LABEL, dialog.CancelLabel);
            Assert.AreEqual(TweeqModalDialog.DEFAULT_CONFIRM_LABEL, dialog.ConfirmLabel);

            // Cancel uses a subdued fill (the Vue original's subtle)
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

        #region Key

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

            // Actual focus lands on TextField's internal input element, so walk back from the child to find its owner
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

            // If the handler isn't removed on close, it fires twice on the second round
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

            // While a nested dropdown or similar is open, it owns the key instead
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

        #region Theme

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
