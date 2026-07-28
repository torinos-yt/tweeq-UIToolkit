using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verification of the componentized focus ring (ext-custom-widgets-spec.md EXT-03-C).
    ///
    /// The ring is required to overlap with the same corner radius as the box, so in addition to
    /// the structure of Attach, the tracking of Apply, and the toggling of Visible, this also pins down
    /// that the adopting side (NumberInput / StringInput) actually uses this component.
    /// </summary>
    public class TweeqFocusRingTests
    {
        const float RADIUS = 4f;

        static float Radius(StyleLength style)
        {
            return style.value.value;
        }

        #region Attach

        [Test]
        public void Attach_AddsAnAbsoluteOverlayToTheHost()
        {
            VisualElement host = new VisualElement();

            TweeqFocusRing ring = TweeqFocusRing.Attach(host);

            Assert.AreEqual(1, host.hierarchy.childCount);
            Assert.AreSame(ring, host.hierarchy.ElementAt(0));
            Assert.AreEqual(Position.Absolute, ring.style.position.value);
            Assert.AreEqual(0f, ring.style.left.value.value);
            Assert.AreEqual(0f, ring.style.top.value.value);
            Assert.AreEqual(0f, ring.style.right.value.value);
            Assert.AreEqual(0f, ring.style.bottom.value.value);
        }

        [Test]
        public void Attach_LandsOnTopOfTheExistingChildren()
        {
            VisualElement host = new VisualElement();
            VisualElement content = new VisualElement();
            host.hierarchy.Add(content);

            TweeqFocusRing ring = TweeqFocusRing.Attach(host);

            Assert.AreSame(ring, host.hierarchy.ElementAt(1));
        }

        [Test]
        public void Attach_RingIsTransparentToThePointer()
        {
            TweeqFocusRing ring = TweeqFocusRing.Attach(new VisualElement());

            Assert.AreEqual(PickingMode.Ignore, ring.pickingMode);
        }

        [Test]
        public void Attach_StartsHiddenWithAOnePixelBorder()
        {
            TweeqFocusRing ring = TweeqFocusRing.Attach(new VisualElement());

            Assert.IsFalse(ring.Visible);
            Assert.AreEqual(DisplayStyle.None, ring.style.display.value);
            Assert.AreEqual(1f, ring.style.borderTopWidth.value);
            Assert.AreEqual(1f, ring.style.borderLeftWidth.value);
            Assert.AreEqual(1f, ring.style.borderRightWidth.value);
            Assert.AreEqual(1f, ring.style.borderBottomWidth.value);
        }

        [Test]
        public void Attach_NullHost_StillReturnsAUsableRing()
        {
            TweeqFocusRing ring = null;

            Assert.DoesNotThrow(() => ring = TweeqFocusRing.Attach(null));
            Assert.IsNotNull(ring);
            Assert.IsNull(ring.hierarchy.parent);
        }

        #endregion

        #region Apply

        [Test]
        public void Apply_TakesTheAccentColourFromTheTheme()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            TweeqFocusRing ring = TweeqFocusRing.Attach(new VisualElement());

            ring.Apply(theme, TweeqBoxPosition.None, TweeqBoxPosition.None);

            Assert.AreEqual(theme.Accent, ring.style.borderTopColor.value);
            Assert.AreEqual(theme.Accent, ring.style.borderLeftColor.value);
            Assert.AreEqual(theme.Accent, ring.style.borderRightColor.value);
            Assert.AreEqual(theme.Accent, ring.style.borderBottomColor.value);
        }

        [Test]
        public void Apply_FollowsTheBoxCornerRule()
        {
            TweeqFocusRing ring = TweeqFocusRing.Attach(new VisualElement());

            ring.Apply(TweeqTheme.Dark(), TweeqBoxPosition.Start, TweeqBoxPosition.End);

            Assert.AreEqual(0f, Radius(ring.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(ring.style.borderTopRightRadius));
            Assert.AreEqual(RADIUS, Radius(ring.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(ring.style.borderBottomRightRadius));
        }

        [Test]
        public void Apply_NullTheme_DoesNotThrow()
        {
            TweeqFocusRing ring = TweeqFocusRing.Attach(new VisualElement());

            Assert.DoesNotThrow(
                () => ring.Apply(null, TweeqBoxPosition.Start, TweeqBoxPosition.End));
        }

        #endregion

        #region Visibility

        [Test]
        public void Visible_TogglesTheDisplayStyle()
        {
            TweeqFocusRing ring = TweeqFocusRing.Attach(new VisualElement());

            ring.Visible = true;
            Assert.IsTrue(ring.Visible);
            Assert.AreEqual(DisplayStyle.Flex, ring.style.display.value);

            ring.Visible = false;
            Assert.IsFalse(ring.Visible);
            Assert.AreEqual(DisplayStyle.None, ring.style.display.value);
        }

        #endregion

        #region Adoption

        [Test]
        public void NumberInput_UsesTheSharedRing()
        {
            TweeqFocusRing ring = new NumberInput().Q<TweeqFocusRing>();

            Assert.IsNotNull(ring);
            Assert.AreEqual("tweeq-number-focus-ring", ring.name);
        }

        [Test]
        public void StringInput_UsesTheSharedRing()
        {
            TweeqFocusRing ring = new StringInput().Q<TweeqFocusRing>();

            Assert.IsNotNull(ring);
            Assert.AreEqual("tweeq-string-focus-ring", ring.name);
        }

        [Test]
        public void AdoptedRings_TrackTheBoxCorners()
        {
            StringInput input = new StringInput
            {
                InlinePosition = TweeqBoxPosition.Start,
                BlockPosition = TweeqBoxPosition.End,
            };

            TweeqFocusRing ring = input.Q<TweeqFocusRing>();

            Assert.AreEqual(
                Radius(input.style.borderBottomLeftRadius),
                Radius(ring.style.borderBottomLeftRadius));
            Assert.AreEqual(
                Radius(input.style.borderTopLeftRadius),
                Radius(ring.style.borderTopLeftRadius));
        }

        [Test]
        public void AdoptedRings_AppearWhileEditingAndHideWhenDisabled()
        {
            StringInput input = new StringInput();
            TweeqFocusRing ring = input.Q<TweeqFocusRing>();

            Assert.IsFalse(ring.Visible);

            input.BeginEditing();
            Assert.IsTrue(ring.Visible);

            input.Disabled = true;
            Assert.IsFalse(ring.Visible);
        }

        #endregion
    }
}
