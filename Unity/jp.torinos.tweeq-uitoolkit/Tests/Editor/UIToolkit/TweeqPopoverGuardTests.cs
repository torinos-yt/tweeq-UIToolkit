using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// TweeqPopover の外側クリック判定（m8-modal-tabs-spec.md §C）を検証する。
    ///
    /// 旧実装は「target がオーバーレイ層の中なら閉じない」だったので、層にモーダルが載ると
    /// モーダル内のクリックでネストしたポップオーバーが閉じなくなった。判定は
    /// <see cref="TweeqPopover.IsOutsideClick"/> に集約したので、ここを直接叩いて確かめる
    /// （PointerDownEvent の合成は EditMode では組めないため、root からの配送そのものは
    /// Play Mode 側の担当）。
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

        // アンカー付きで開いた popover と、その中身を 1 つ返す
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

        #region 既存の入れ子ポップアップ（回帰）

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

            // ピッカー内 Dropdown のリスト相当。層に兄弟として開く
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

        #region モーダル（今回の修正）

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

                // 層の中でもポップオーバーでなければ外側。モーダル内のクリックで
                // ネストしたドロップダウンが正しく閉じる
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
