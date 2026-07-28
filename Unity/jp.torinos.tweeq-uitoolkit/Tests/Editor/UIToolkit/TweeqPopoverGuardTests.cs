using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies TweeqPopover's outside-click determination (m8-modal-tabs-spec.md §C).
    ///
    /// The old implementation was "don't close if target is inside the overlay layer", so once a modal
    /// mounted on the layer, clicks inside the modal would stop nested popovers from closing. The determination
    /// has since been consolidated into <see cref="TweeqPopover.IsOutsideClick"/>, so this hits that directly to verify
    /// (PointerDownEvent can't be synthesized in EditMode, so dispatch from root itself is the
    /// Play Mode side's responsibility).
    /// </summary>
    public class TweeqPopoverGuardTests
    {
        TweeqModalTestPanel _panel;
        TweeqPopover _popover;

        [TearDown]
        public void TearDown()
        {
            _popover?.Close();
            _popover = null;
            _panel?.Dispose();
            _panel = null;
        }

        // Returns a popover opened with an anchor, plus one item of its content
        TweeqPopover OpenPopover(out VisualElement content)
        {
            _panel = _panel ?? TweeqModalTestPanel.Create();

            VisualElement anchor = new VisualElement();
            _panel.Root.Add(anchor);

            TweeqPopover popover = new TweeqPopover();
            content = new VisualElement();
            popover.Add(content);
            popover.Open(anchor);

            Assume.That(popover.IsOpen, Is.True, "ポップオーバーを層に載せられなかった");
            return popover;
        }

        #region Existing nested popup (regression)

        [Test]
        public void Inside_TheOwnContentIsNotAnOutsideClick()
        {
            _popover = OpenPopover(out VisualElement content);

            Assert.IsFalse(_popover.IsOutsideClick(content));
            Assert.IsFalse(_popover.IsOutsideClick(_popover));
        }

        [Test]
        public void Inside_ANestedPopoverIsNotAnOutsideClick()
        {
            _popover = OpenPopover(out VisualElement content);

            // Equivalent to a Dropdown's list inside a picker. Opens as a sibling on the layer
            TweeqPopover nested = new TweeqPopover { LightDismiss = false };
            VisualElement nestedContent = new VisualElement();
            nested.Add(nestedContent);
            nested.Open(content);

            try
            {
                Assume.That(nested.IsOpen, Is.True);
                Assert.IsFalse(_popover.IsOutsideClick(nestedContent), "入れ子のポップオーバー内で親が閉じている");
                Assert.IsFalse(_popover.IsOutsideClick(nested));
            }
            finally
            {
                nested.Close();
            }
        }

        #endregion

        #region Modal (this fix)

        [Test]
        public void Outside_AModalOnTheSameLayerIsAnOutsideClick()
        {
            _popover = OpenPopover(out VisualElement content);

            TweeqModal modal = new TweeqModal();
            _panel.Root.Add(modal);
            modal.Open = true;

            try
            {
                Assume.That(modal.Backdrop.hierarchy.parent, Is.Not.Null);

                // Even inside the layer, it's outside if it isn't a popover. This lets clicks inside
                // the modal correctly close nested dropdowns
                Assert.IsTrue(_popover.IsOutsideClick(modal.Backdrop));
                Assert.IsTrue(_popover.IsOutsideClick(modal.Pane));
                Assert.IsTrue(_popover.IsOutsideClick(modal.Pane.contentContainer));
            }
            finally
            {
                modal.Open = false;
            }
        }

        [Test]
        public void Outside_ARegularElementIsAnOutsideClick()
        {
            _popover = OpenPopover(out VisualElement content);

            VisualElement other = new VisualElement();
            _panel.Root.Add(other);

            Assert.IsTrue(_popover.IsOutsideClick(other));
            Assert.IsTrue(_popover.IsOutsideClick(_panel.Root));
        }

        [Test]
        public void Outside_NullTargetIsAnOutsideClick()
        {
            _popover = OpenPopover(out VisualElement content);

            Assert.IsTrue(_popover.IsOutsideClick(null));
        }

        #endregion
    }
}
