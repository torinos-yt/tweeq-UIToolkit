using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// TweeqRoot のテーマ配布を検証する。
    ///
    /// USS カスタムプロパティの解決（CustomStyleResolvedEvent）は実パネルとスタイル解決が
    /// 必要で EditMode から合成できないため、ここでは配布ロジックと優先順位だけを見る。
    /// USS 経路は uloop でのスクショ確認担当。
    /// </summary>
    public class TweeqRootTests
    {
        #region Helpers

        /// <summary>テーマが届いたかを数えるだけの ITweeqThemed 実装。</summary>
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
            // 複合部品は自分の子へ自分で転送する契約なので、ルートは中まで潜らない
            TweeqRoot root = new TweeqRoot();
            ThemedProbe outer = new ThemedProbe();
            ThemedProbe inner = new ThemedProbe();
            root.Add(outer);
            outer.Add(inner);

            root.Redistribute();

            Assert.AreEqual(1, outer.AssignCount);
            Assert.AreEqual(0, inner.AssignCount, "ITweeqThemed の配下はその部品の責務");
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
                "入れ子ルートはテーマ境界。外側に踏み潰されない");
            Assert.AreNotSame(outerRoot.Theme, innerProbe.Theme);
        }

        [Test]
        public void Redistribute_PicksUpChildrenAddedLater()
        {
            TweeqRoot root = new TweeqRoot();
            root.Redistribute();

            ThemedProbe late = new ThemedProbe();
            root.Add(late);
            Assert.AreEqual(0, late.AssignCount, "追加だけでは配られない");

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

            Assert.AreSame(custom, probe.Theme, "C# 代入は即配布される");
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
                "自分で塗らない指定なら USS / 既定へ戻ること");
        }

        #endregion

        #region UXML

        [Test]
        public void Type_IsUxmlElementWithGeneratedSerializedData()
        {
            Type type = typeof(TweeqRoot);

            Assert.IsNotEmpty(type.GetCustomAttributes(typeof(UxmlElementAttribute), false),
                "[UxmlElement] が付いていないと UXML から使えない");
            Assert.IsNotNull(
                type.GetNestedType("UxmlSerializedData", BindingFlags.Public | BindingFlags.NonPublic),
                "UxmlSerializedData が生成されていない（partial 宣言漏れ）");
            Assert.IsNotNull(type.GetConstructor(Type.EmptyTypes),
                "UXML からの生成にはパラメータなしコンストラクタが必要");
        }

        [Test]
        public void UxmlAttributes_ExposePaintBackgroundOnly()
        {
            // Theme は UXML に出さない契約（TweeqRoot / コードで配布する）
            PropertyInfo theme = typeof(TweeqRoot).GetProperty(nameof(TweeqRoot.Theme));
            PropertyInfo paint = typeof(TweeqRoot).GetProperty(nameof(TweeqRoot.PaintBackground));

            Assert.IsEmpty(theme.GetCustomAttributes(typeof(UxmlAttributeAttribute), false));
            Assert.IsNotEmpty(paint.GetCustomAttributes(typeof(UxmlAttributeAttribute), false));
        }

        #endregion
    }
}
