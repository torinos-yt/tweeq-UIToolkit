using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies TweeqRoot's theme distribution.
    ///
    /// USS custom property resolution (CustomStyleResolvedEvent) requires a real panel and style
    /// resolution, which can't be synthesized from EditMode, so this only looks at the distribution
    /// logic and priority order. The USS path is verified via uloop screenshot checks.
    /// </summary>
    public class TweeqRootTests
    {
        #region Helpers

        /// <summary>An ITweeqThemed implementation that just counts whether the theme arrived.</summary>
        sealed class ThemedProbe : VisualElement, ITweeqThemed
        {
            TweeqTheme _theme;

            public int AssignCount;

            public TweeqTheme Theme
            {
                get => _theme;
                set
                {
                    _theme = value ?? TweeqTheme.Dark();
                    AssignCount++;
                }
            }
        }

        static TweeqTheme CustomTheme()
        {
            return TweeqTheme.FromSeeds(
                ColorMode.Light,
                new Color32(0xFF, 0xFF, 0xFF, 0xFF),
                new Color32(0xFF, 0x66, 0x00, 0xFF),
                TweeqTheme.DEFAULT_GRAY);
        }

        #endregion

        #region Distribution

        [Test]
        public void Redistribute_AssignsThemeToDirectChild()
        {
            TweeqRoot root = new TweeqRoot();
            ThemedProbe probe = new ThemedProbe();
            root.Add(probe);

            root.Redistribute();

            Assert.AreSame(root.Theme, probe.Theme);
            Assert.AreEqual(1, probe.AssignCount);
        }

        [Test]
        public void Redistribute_ReachesThemedUnderPlainContainers()
        {
            TweeqRoot root = new TweeqRoot();
            VisualElement middle = new VisualElement();
            VisualElement inner = new VisualElement();
            ThemedProbe probe = new ThemedProbe();

            root.Add(middle);
            middle.Add(inner);
            inner.Add(probe);

            root.Redistribute();

            Assert.AreSame(root.Theme, probe.Theme);
        }

        [Test]
        public void Redistribute_StopsAtThemedChild()
        {
            // Composite components are contracted to forward the theme to their own children themselves, so the root doesn't descend inside
            TweeqRoot root = new TweeqRoot();
            ThemedProbe outer = new ThemedProbe();
            ThemedProbe inner = new ThemedProbe();
            root.Add(outer);
            outer.Add(inner);

            root.Redistribute();

            Assert.AreEqual(1, outer.AssignCount);
            Assert.AreEqual(0, inner.AssignCount, "what's under an ITweeqThemed is that component's own responsibility");
        }

        [Test]
        public void Redistribute_SkipsNestedRootSubtree()
        {
            TweeqRoot outerRoot = new TweeqRoot();
            TweeqRoot innerRoot = new TweeqRoot();
            ThemedProbe outerProbe = new ThemedProbe();
            ThemedProbe innerProbe = new ThemedProbe();

            outerRoot.Add(outerProbe);
            outerRoot.Add(innerRoot);
            innerRoot.Add(innerProbe);

            innerRoot.Theme = CustomTheme();
            outerRoot.Redistribute();

            Assert.AreEqual(1, outerProbe.AssignCount);
            Assert.AreSame(innerRoot.Theme, innerProbe.Theme,
                "a nested root is a theme boundary; it isn't overwritten by the outer one");
            Assert.AreNotSame(outerRoot.Theme, innerProbe.Theme);
        }

        [Test]
        public void Redistribute_PicksUpChildrenAddedLater()
        {
            TweeqRoot root = new TweeqRoot();
            root.Redistribute();

            ThemedProbe late = new ThemedProbe();
            root.Add(late);
            Assert.AreEqual(0, late.AssignCount, "adding alone does not distribute the theme");

            root.Redistribute();

            Assert.AreSame(root.Theme, late.Theme);
        }

        [Test]
        public void Redistribute_EmptyRoot_DoesNotThrow()
        {
            TweeqRoot root = new TweeqRoot();

            Assert.DoesNotThrow(() => root.Redistribute());
        }

        #endregion

        #region Theme property

        [Test]
        public void DefaultTheme_IsDark()
        {
            TweeqRoot root = new TweeqRoot();

            Assert.IsNotNull(root.Theme);
            Assert.AreEqual(ColorMode.Dark, root.Theme.Mode);
        }

        [Test]
        public void ThemeAssignment_DistributesImmediately()
        {
            TweeqRoot root = new TweeqRoot();
            ThemedProbe probe = new ThemedProbe();
            root.Add(probe);

            TweeqTheme custom = CustomTheme();
            root.Theme = custom;

            Assert.AreSame(custom, probe.Theme, "a C# assignment distributes immediately");
        }

        [Test]
        public void ThemeAssignment_Null_FallsBackToDark()
        {
            TweeqRoot root = new TweeqRoot();
            ThemedProbe probe = new ThemedProbe();
            root.Add(probe);

            root.Theme = null;

            Assert.IsNotNull(root.Theme);
            Assert.AreEqual(ColorMode.Dark, root.Theme.Mode);
            Assert.AreSame(root.Theme, probe.Theme);
        }

        #endregion

        #region Background

        [Test]
        public void PaintBackground_DefaultOn_UsesThemeBackground()
        {
            TweeqRoot root = new TweeqRoot();

            Assert.IsTrue(root.PaintBackground);
            Assert.AreEqual(root.Theme.Background, root.style.backgroundColor.value);
        }

        [Test]
        public void PaintBackground_FollowsThemeAssignment()
        {
            TweeqRoot root = new TweeqRoot();
            TweeqTheme custom = CustomTheme();

            root.Theme = custom;

            Assert.AreEqual(custom.Background, root.style.backgroundColor.value);
        }

        [Test]
        public void PaintBackground_Off_ClearsInlineColor()
        {
            TweeqRoot root = new TweeqRoot();

            root.PaintBackground = false;

            Assert.AreEqual(StyleKeyword.Null, root.style.backgroundColor.keyword,
                "when told not to paint it itself, it should fall back to USS / the default");
        }

        #endregion

        #region UXML

        [Test]
        public void Type_IsUxmlElementWithGeneratedSerializedData()
        {
            Type type = typeof(TweeqRoot);

            Assert.IsNotEmpty(type.GetCustomAttributes(typeof(UxmlElementAttribute), false),
                "without [UxmlElement] it can't be used from UXML");
            Assert.IsNotNull(
                type.GetNestedType("UxmlSerializedData", BindingFlags.Public | BindingFlags.NonPublic),
                "UxmlSerializedData was not generated (missing partial declaration)");
            Assert.IsNotNull(type.GetConstructor(Type.EmptyTypes),
                "construction from UXML requires a parameterless constructor");
        }

        [Test]
        public void UxmlAttributes_ExposePaintBackgroundOnly()
        {
            // Contract: Theme isn't exposed to UXML (distributed via TweeqRoot / code instead)
            PropertyInfo theme = typeof(TweeqRoot).GetProperty(nameof(TweeqRoot.Theme));
            PropertyInfo paint = typeof(TweeqRoot).GetProperty(nameof(TweeqRoot.PaintBackground));

            Assert.IsEmpty(theme.GetCustomAttributes(typeof(UxmlAttributeAttribute), false));
            Assert.IsNotEmpty(paint.GetCustomAttributes(typeof(UxmlAttributeAttribute), false));
        }

        #endregion
    }
}
