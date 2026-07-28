using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Contract for TweeqTabs / TweeqTab (m8-modal-tabs-spec.md §D and the "test contract").
    ///
    /// A VisualElement can be created and styled without a panel, so this stays entirely in
    /// EditMode. However, TweeqTab registration is driven by AttachToPanelEvent in production,
    /// and a panel cannot be composed from EditMode, so here we directly drive ConnectToTabs /
    /// DisconnectFromTabs, the entry points to that same path. Actual key events, focus
    /// movement, and appearance are covered on the uloop side.
    /// </summary>
    public class TweeqTabsTests
    {
        #region Helpers

        /// <summary>In-memory storage for tests. Never touches real PlayerPrefs.</summary>
        sealed class MemoryStorage : ITweeqTabStorage
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public readonly List<string> Deleted = new List<string>();

            public int SetCount;

            public string Get(string key, string defaultValue)
            {
                return Values.TryGetValue(key, out string value) ? value : defaultValue;
            }

            public void Set(string key, string value)
            {
                Values[key] = value;
                SetCount++;
            }

            public void Delete(string key)
            {
                Values.Remove(key);
                Deleted.Add(key);
            }
        }

        /// <summary>An ITweeqThemed implementation that just counts whether the theme arrived (same tool as TweeqRootTests).</summary>
        sealed class ThemedProbe : VisualElement, ITweeqThemed
        {
            TweeqTheme _theme;

            public TweeqTheme Theme
            {
                get => _theme;
                set => _theme = value;
            }
        }

        const string TABS_NAME = "tweeq.tests.tabs";

        MemoryStorage _storage;

        [SetUp]
        public void SwapStorage()
        {
            _storage = new MemoryStorage();
            TweeqTabs.Storage = _storage;
        }

        [TearDown]
        public void RestoreStorage()
        {
            // The swap is static, so failing to restore it would leak into other tests or the real project
            TweeqTabs.Storage = null;
        }

        static TweeqTab MakeTab(string name, string id = null, bool disabled = false)
        {
            TweeqTab tab = new TweeqTab(name);

            if (!string.IsNullOrEmpty(id))
            {
                tab.Id = id;
            }

            tab.IsDisabled = disabled;
            return tab;
        }

        static void Attach(TweeqTabs tabs, params TweeqTab[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                tabs.Add(items[i]);
                items[i].ConnectToTabs();
            }
        }

        static TweeqTabs CreateThree(string tabsName = TABS_NAME)
        {
            TweeqTabs tabs = new TweeqTabs(tabsName);
            Attach(tabs, MakeTab("First"), MakeTab("Second"), MakeTab("Third"));
            return tabs;
        }

        #endregion

        #region Registration

        [Test]
        public void Register_AddsTabsAndActivatesTheFirst()
        {
            TweeqTabs tabs = CreateThree();

            Assert.AreEqual(3, tabs.Tabs.Count);
            Assert.AreEqual("first", tabs.ActiveId);
            Assert.IsTrue(tabs.Tabs[0].IsActive);
            Assert.IsFalse(tabs.Tabs[1].IsActive);
            Assert.AreEqual(DisplayStyle.None, tabs.Tabs[1].style.display.value,
                "非アクティブは display:none（意図的逸脱）");
        }

        [Test]
        public void Register_SameInstanceTwiceIsIgnored()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            TweeqTab first = MakeTab("First");
            Attach(tabs, first);

            first.ConnectToTabs();
            tabs.RegisterTab(first);

            Assert.AreEqual(1, tabs.Tabs.Count);
        }

        [Test]
        public void Register_DuplicateIdIsIgnored()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            TweeqTab kept = MakeTab("First", "same");
            TweeqTab dropped = MakeTab("Second", "same");

            // Even if the display name differs, a matching id prevents double registration (the Vue original pushed it through unchecked)
            Attach(tabs, kept, dropped);

            Assert.AreEqual(1, tabs.Tabs.Count);
            Assert.AreEqual("same", tabs.ActiveId);
            Assert.IsNull(dropped.Owner);
            Assert.AreEqual(DisplayStyle.None, dropped.style.display.value,
                "弾いたパネルが重なって見えないこと");
        }

        [Test]
        public void Register_SyncFromHierarchyPicksUpChildren()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            TweeqTab first = MakeTab("First");
            TweeqTab second = MakeTab("Second");
            tabs.Add(first);
            tabs.Add(second);

            Assert.AreEqual(0, tabs.Tabs.Count, "Add だけでは登録されない");

            tabs.SyncTabsFromHierarchy();

            Assert.AreEqual(2, tabs.Tabs.Count);
            Assert.AreEqual("first", tabs.ActiveId);
        }

        [Test]
        public void Unregister_RemovesTheTabAndReselects()
        {
            TweeqTabs tabs = CreateThree();
            TweeqTab first = tabs.Tabs[0];

            first.DisconnectFromTabs();

            Assert.AreEqual(2, tabs.Tabs.Count);
            Assert.AreEqual("second", tabs.ActiveId, "消えたアクティブタブの次が選ばれる");
        }

        [Test]
        public void Unregister_TabBecomesStandaloneAndVisibleAgain()
        {
            TweeqTabs tabs = CreateThree();
            TweeqTab third = tabs.Tabs[2];

            Assert.IsFalse(third.IsActive);

            third.DisconnectFromTabs();

            Assert.IsNull(third.Owner);
            Assert.IsTrue(third.IsActive, "親から外れたら単独の可視要素へ戻る");
            Assert.AreEqual(DisplayStyle.Flex, third.style.display.value);
        }

        [Test]
        public void Unregister_UnknownTabDoesNotThrow()
        {
            TweeqTabs tabs = CreateThree();

            Assert.DoesNotThrow(() => tabs.UnregisterTab(MakeTab("Ghost")));
            Assert.DoesNotThrow(() => tabs.UnregisterTab(null));
            Assert.AreEqual(3, tabs.Tabs.Count);
        }

        #endregion

        #region Id

        [Test]
        public void Id_FallsBackToSlugOfTheName()
        {
            TweeqTab tab = MakeTab("My Long Tab");

            Assert.AreEqual("my-long-tab", tab.Id);
        }

        [Test]
        public void Id_ExplicitValueWinsOverTheSlug()
        {
            TweeqTab tab = MakeTab("My Long Tab", "custom");

            Assert.AreEqual("custom", tab.Id);

            // Setting it to empty falls back to the slug
            tab.Id = null;
            Assert.AreEqual("my-long-tab", tab.Id);
        }

        [Test]
        public void Id_EmptyNameProducesEmptyId()
        {
            Assert.AreEqual(string.Empty, new TweeqTab().Id);
            Assert.AreEqual(string.Empty, TweeqTab.NormalizeId(null));
        }

        [Test]
        public void Id_ChangeReSyncsTheParent()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            TweeqTab only = MakeTab("Before");
            Attach(tabs, only);

            Assert.AreEqual("before", tabs.ActiveId);

            only.Id = "after";

            Assert.AreEqual("after", tabs.ActiveId, "id が変わっても選択を失わない");
        }

        #endregion

        #region Active resolution

        [Test]
        public void Resolve_SkipsALeadingDisabledTab()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("Disabled", disabled: true), MakeTab("First"), MakeTab("Last"));

            // The Vue original grabs tabs[0], so nothing got selected when the first tab was disabled
            Assert.AreEqual("first", tabs.ActiveId);
        }

        [Test]
        public void Resolve_IgnoresADisabledPersistedValue()
        {
            _storage.Values[TweeqTabs.PrefsKey(TABS_NAME, null)] = "second";

            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("First"), MakeTab("Second", disabled: true), MakeTab("Third"));

            Assert.AreEqual("first", tabs.ActiveId);
        }

        [Test]
        public void Resolve_IgnoresADisabledDefaultTabId()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME) { DefaultTabId = "second" };
            Attach(tabs, MakeTab("First"), MakeTab("Second", disabled: true), MakeTab("Third"));

            Assert.AreEqual("first", tabs.ActiveId);
        }

        [Test]
        public void Resolve_PersistedBeatsTheDefaultTabId()
        {
            _storage.Values[TweeqTabs.PrefsKey(TABS_NAME, null)] = "third";

            TweeqTabs tabs = new TweeqTabs(TABS_NAME) { DefaultTabId = "second" };
            Attach(tabs, MakeTab("First"), MakeTab("Second"), MakeTab("Third"));

            Assert.AreEqual("third", tabs.ActiveId);
        }

        [Test]
        public void Resolve_DefaultTabIdBeatsTheFirstTab()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME) { DefaultTabId = "third" };
            Attach(tabs, MakeTab("First"), MakeTab("Second"), MakeTab("Third"));

            Assert.AreEqual("third", tabs.ActiveId);
        }

        [Test]
        public void Resolve_AllDisabledLeavesNothingSelected()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("First", disabled: true), MakeTab("Second", disabled: true));

            Assert.AreEqual(string.Empty, tabs.ActiveId);
            Assert.IsNull(tabs.ActiveTab);
        }

        [Test]
        public void Resolve_KeepsAValidActiveTabWhenTabsChange()
        {
            TweeqTabs tabs = CreateThree();
            tabs.SelectTab("third");

            Attach(tabs, MakeTab("Fourth"));

            Assert.AreEqual("third", tabs.ActiveId, "有効な選択は増減で動かさない");
        }

        #endregion

        #region SelectTab / Events

        [Test]
        public void SelectTab_BlocksDisabledTabsUnconditionally()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("First"), MakeTab("Second", disabled: true));

            int changed = 0;
            int clicked = 0;
            tabs.Changed += _ => changed++;
            tabs.Clicked += _ => clicked++;

            // The Vue original only blocked this when an event argument was present, so a programmatic select would slip through
            tabs.SelectTab("second");

            Assert.AreEqual("first", tabs.ActiveId);
            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, clicked);
        }

        [Test]
        public void SelectTab_UnknownIdIsIgnored()
        {
            TweeqTabs tabs = CreateThree();

            Assert.DoesNotThrow(() => tabs.SelectTab("nope"));
            Assert.DoesNotThrow(() => tabs.SelectTab(null));
            Assert.AreEqual("first", tabs.ActiveId);
        }

        [Test]
        public void Events_ChangedAndClickedAreExclusive()
        {
            TweeqTabs tabs = CreateThree();

            List<string> changed = new List<string>();
            List<string> clicked = new List<string>();
            tabs.Changed += tab => changed.Add(tab.Id);
            tabs.Clicked += tab => clicked.Add(tab.Id);

            tabs.SelectTab("second");
            tabs.SelectTab("second");
            tabs.SelectTab("third");

            Assert.AreEqual(new[] { "second", "third" }, changed.ToArray());
            Assert.AreEqual(new[] { "second" }, clicked.ToArray());
        }

        [Test]
        public void Events_FirstRegistrationRaisesChangedOnce()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            int changed = 0;
            tabs.Changed += _ => changed++;

            Attach(tabs, MakeTab("First"), MakeTab("Second"));

            Assert.AreEqual(1, changed);
        }

        #endregion

        #region Keyboard

        [Test]
        public void Keyboard_ArrowWrapsAndSkipsDisabled()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("First"), MakeTab("Blocked", disabled: true), MakeTab("Last"));

            tabs.MoveSelection(1);
            Assert.AreEqual("last", tabs.ActiveId, "disabled を飛ばす");

            tabs.MoveSelection(1);
            Assert.AreEqual("first", tabs.ActiveId, "末尾から先頭へラップ");

            tabs.MoveSelection(-1);
            Assert.AreEqual("last", tabs.ActiveId, "先頭から末尾へラップ");
        }

        [Test]
        public void Keyboard_HomeAndEndPickTheEnabledEnds()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(
                tabs,
                MakeTab("Head", disabled: true),
                MakeTab("First"),
                MakeTab("Middle"),
                MakeTab("Last"),
                MakeTab("Tail", disabled: true));

            tabs.SelectLastEnabled();
            Assert.AreEqual("last", tabs.ActiveId);

            tabs.SelectFirstEnabled();
            Assert.AreEqual("first", tabs.ActiveId);
        }

        [Test]
        public void Keyboard_MoveOntoTheSameTabDoesNotRaiseClicked()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("Only"), MakeTab("Blocked", disabled: true));

            int clicked = 0;
            int changed = 0;
            tabs.Clicked += _ => clicked++;
            tabs.Changed += _ => changed++;

            tabs.MoveSelection(1);

            // Key-driven selection movement has a different meaning from "reselecting via click", so it does not raise Clicked
            Assert.AreEqual(0, clicked);
            Assert.AreEqual(0, changed);
            Assert.AreEqual("only", tabs.ActiveId);
        }

        [Test]
        public void Keyboard_MoveWithoutEnabledTabsDoesNothing()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("First", disabled: true));

            Assert.DoesNotThrow(() => tabs.MoveSelection(1));
            Assert.DoesNotThrow(() => tabs.SelectFirstEnabled());
            Assert.DoesNotThrow(() => tabs.SelectLastEnabled());
            Assert.AreEqual(string.Empty, tabs.ActiveId);
        }

        #endregion

        #region Header

        [Test]
        public void Header_UsesRovingTabIndex()
        {
            TweeqTabs tabs = CreateThree();

            Assert.AreEqual(0, tabs.GetHeader(0).tabIndex);
            Assert.AreEqual(-1, tabs.GetHeader(1).tabIndex);
            Assert.AreEqual(-1, tabs.GetHeader(2).tabIndex);

            tabs.SelectTab("second");

            Assert.AreEqual(-1, tabs.GetHeader(0).tabIndex);
            Assert.AreEqual(0, tabs.GetHeader(1).tabIndex);
        }

        [Test]
        public void Header_DisabledIsNotPickableNorFocusable()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            Attach(tabs, MakeTab("First"), MakeTab("Blocked", disabled: true));

            Assert.AreEqual(PickingMode.Position, tabs.GetHeader(0).pickingMode);
            Assert.IsTrue(tabs.GetHeader(0).focusable);

            Assert.AreEqual(PickingMode.Ignore, tabs.GetHeader(1).pickingMode,
                "hover に反応させない");
            Assert.IsFalse(tabs.GetHeader(1).focusable);
        }

        [Test]
        public void Header_IndicatorMarksTheActiveTab()
        {
            TweeqTabs tabs = CreateThree();

            Assert.AreEqual(tabs.Theme.Text, tabs.GetHeader(0).style.borderBottomColor.value);
            Assert.AreEqual(Color.clear, tabs.GetHeader(1).style.borderBottomColor.value);
        }

        [Test]
        public void Header_OutOfRangeIsNull()
        {
            TweeqTabs tabs = CreateThree();

            Assert.IsNull(tabs.GetHeader(-1));
            Assert.IsNull(tabs.GetHeader(3));
        }

        [Test]
        public void Header_FollowsTheTabNameChange()
        {
            TweeqTabs tabs = CreateThree();
            tabs.Tabs[0].TabName = "Renamed";

            Label label = tabs.GetHeader(0).Q<Label>("tweeq-tabs-header-label");

            Assert.IsNotNull(label);
            Assert.AreEqual("Renamed", label.text);
        }

        #endregion

        #region Persistence

        [Test]
        public void Persist_WritesTheSelectedTab()
        {
            TweeqTabs tabs = CreateThree();

            tabs.SelectTab("third");

            Assert.AreEqual("third", _storage.Values[TweeqTabs.PrefsKey(TABS_NAME, null)]);
        }

        [Test]
        public void Persist_RestoresTheStoredTabOnRegistration()
        {
            _storage.Values[TweeqTabs.PrefsKey(TABS_NAME, null)] = "third";

            TweeqTabs tabs = CreateThree();

            Assert.AreEqual("third", tabs.ActiveId);
            Assert.IsTrue(tabs.Tabs[2].IsActive);
        }

        [Test]
        public void Persist_AutoResolutionDoesNotOverwriteTheStoredTab()
        {
            // Tabs are registered one at a time. If we wrote out the provisional selection
            // made while the persisted tab hasn't arrived yet, restoration would collapse to
            // the first tab every time
            string key = TweeqTabs.PrefsKey(TABS_NAME, null);
            _storage.Values[key] = "third";

            TweeqTabs tabs = CreateThree();

            Assert.AreEqual("third", tabs.ActiveId);
            Assert.AreEqual("third", _storage.Values[key]);
            Assert.AreEqual(0, _storage.SetCount, "解決による暫定選択は永続化しない");
        }

        [Test]
        public void Persist_StorageKeyBeatsTabsName()
        {
            // The Vue original has a bug where storageKey exists on the type but is never used. We adopt the fix from another reference implementation
            TweeqTabs tabs = new TweeqTabs(TABS_NAME) { StorageKey = "explicit.key" };
            Attach(tabs, MakeTab("First"), MakeTab("Second"));

            tabs.SelectTab("second");

            Assert.AreEqual("tweeq.explicit.key", tabs.ResolvedStorageKey);
            Assert.AreEqual("second", _storage.Values["tweeq.explicit.key"]);
            Assert.IsFalse(_storage.Values.ContainsKey(TweeqTabs.PrefsKey(TABS_NAME, null)));
        }

        [Test]
        public void Persist_IsSkippedWhenBothNamesAreEmpty()
        {
            TweeqTabs tabs = new TweeqTabs();
            Attach(tabs, MakeTab("First"), MakeTab("Second"));

            tabs.SelectTab("second");

            Assert.AreEqual(string.Empty, tabs.ResolvedStorageKey);
            Assert.AreEqual(0, _storage.SetCount);
            Assert.AreEqual(0, _storage.Deleted.Count);
        }

        [Test]
        public void Persist_ClearDeletesTheKey()
        {
            TweeqTabs tabs = CreateThree();
            tabs.SelectTab("second");

            tabs.ClearPersistedActiveTab();

            string key = TweeqTabs.PrefsKey(TABS_NAME, null);
            Assert.Contains(key, _storage.Deleted);
            Assert.IsFalse(_storage.Values.ContainsKey(key));
        }

        [Test]
        public void Persist_ClearWithoutAKeyDoesNothing()
        {
            TweeqTabs tabs = new TweeqTabs();
            Attach(tabs, MakeTab("First"));

            Assert.DoesNotThrow(() => tabs.ClearPersistedActiveTab());
            Assert.AreEqual(0, _storage.Deleted.Count);
        }

        [Test]
        public void Persist_KeyIsBuiltFromNameOrStorageKey()
        {
            Assert.AreEqual("tweeq.demo.active", TweeqTabs.PrefsKey("demo", null));
            Assert.AreEqual("tweeq.custom", TweeqTabs.PrefsKey("demo", "custom"));
            Assert.AreEqual(string.Empty, TweeqTabs.PrefsKey(null, null));
        }

        [Test]
        public void Storage_NullRestoresTheDefaultImplementation()
        {
            TweeqTabs.Storage = null;

            Assert.AreSame(TweeqTabPlayerPrefsStorage.Instance, TweeqTabs.Storage);
        }

        #endregion

        #region Standalone TweeqTab

        [Test]
        public void Standalone_ConnectWithoutTabsDoesNotThrow()
        {
            // The Vue original throws on inject failure, but on-site during a live show a runtime exception is a disaster
            VisualElement plain = new VisualElement();
            TweeqTab tab = MakeTab("Alone");
            plain.Add(tab);

            Assert.DoesNotThrow(() => tab.ConnectToTabs());
            Assert.DoesNotThrow(() => tab.DisconnectFromTabs());

            Assert.IsNull(tab.Owner);
            Assert.IsTrue(tab.IsActive, "単独でも見えたまま");
        }

        [Test]
        public void Standalone_PropertyChangesDoNotThrow()
        {
            TweeqTab tab = MakeTab("Alone");

            Assert.DoesNotThrow(() =>
            {
                tab.TabName = "Renamed";
                tab.Id = "renamed";
                tab.IsDisabled = true;
            });
        }

        [Test]
        public void Standalone_UpdateTabForAnUnknownTabIsIgnored()
        {
            TweeqTabs tabs = CreateThree();

            // The Vue original threw a TypeError from tabs[-1].isActive
            Assert.DoesNotThrow(() => tabs.UpdateTab(MakeTab("Ghost")));
            Assert.DoesNotThrow(() => tabs.UpdateTab(null));
            Assert.AreEqual(3, tabs.Tabs.Count);
        }

        #endregion

        #region Layout / Theme

        [Test]
        public void Layout_ContentContainerRoutesChildrenToThePanels()
        {
            TweeqTabs tabs = new TweeqTabs(TABS_NAME);
            TweeqTab tab = MakeTab("First");
            tabs.Add(tab);

            // parent returns the logical parent (the owner of contentContainer, i.e. TweeqTabs
            // itself), so we check the actual storage location via hierarchy.parent
            Assert.AreEqual("tweeq-tabs-panels", tab.hierarchy.parent.name,
                "UXML の子はパネル層へ入る（ヘッダーとは混ざらない）");
        }

        [Test]
        public void Layout_HorizontalStacksTablistAboveThePanels()
        {
            TweeqTabs tabs = CreateThree();

            Assert.AreEqual(FlexDirection.Column, tabs.style.flexDirection.value);
            Assert.AreEqual(
                TweeqTabs.INDICATOR_WIDTH, tabs.GetHeader(0).style.borderBottomWidth.value);
            Assert.AreEqual(0f, tabs.GetHeader(0).style.borderLeftWidth.value);
        }

        [Test]
        public void Layout_VerticalMovesTheIndicatorToTheLeftEdge()
        {
            TweeqTabs tabs = CreateThree();

            tabs.Vertical = true;

            Assert.AreEqual(FlexDirection.Row, tabs.style.flexDirection.value);
            Assert.AreEqual(
                TweeqTabs.INDICATOR_WIDTH, tabs.GetHeader(0).style.borderLeftWidth.value);
            Assert.AreEqual(0f, tabs.GetHeader(0).style.borderBottomWidth.value);
            Assert.AreEqual(tabs.Theme.Text, tabs.GetHeader(0).style.borderLeftColor.value);

            // Vertical panels are wrapped in a ScrollView (horizontal ones are not)
            Assert.AreEqual(2, tabs.hierarchy.childCount);
            Assert.IsInstanceOf<ScrollView>(tabs.hierarchy.ElementAt(1));
            Assert.AreEqual("tweeq-tabs-panels", tabs.Tabs[0].hierarchy.parent.name,
                "ScrollView を噛ませてもパネル層の中身は動かない");
        }

        [Test]
        public void Layout_VerticalTogglesBackToHorizontal()
        {
            TweeqTabs tabs = CreateThree();

            tabs.Vertical = true;
            tabs.Vertical = false;

            Assert.AreEqual(FlexDirection.Column, tabs.style.flexDirection.value);
            Assert.AreEqual("tweeq-tabs-panels", tabs.Tabs[0].hierarchy.parent.name);
            Assert.AreEqual(2, tabs.hierarchy.childCount, "ScrollView は外れている");
        }

        [Test]
        public void Theme_ReachesThemedDescendantsInsideThePanels()
        {
            TweeqTabs tabs = CreateThree();
            ThemedProbe probe = new ThemedProbe();
            tabs.Tabs[0].Add(probe);

            TweeqTheme custom = TweeqTheme.Light();
            tabs.Theme = custom;

            Assert.AreSame(custom, tabs.Tabs[0].Theme, "TweeqTab は ITweeqThemed として受け取る");
            Assert.AreSame(custom, probe.Theme, "そこから中身へ転送する");
        }

        [Test]
        public void Theme_NullFallsBackToDark()
        {
            TweeqTabs tabs = CreateThree();

            tabs.Theme = null;

            Assert.IsNotNull(tabs.Theme);
            Assert.AreEqual(ColorMode.Dark, tabs.Theme.Mode);
        }

        #endregion

        #region UXML

        [Test]
        public void Uxml_TypesAreUxmlElementsWithGeneratedSerializedData()
        {
            AssertUxmlElement(typeof(TweeqTabs));
            AssertUxmlElement(typeof(TweeqTab));
        }

        [Test]
        public void Uxml_AttributesAreExposed()
        {
            AssertUxmlAttribute(typeof(TweeqTabs), nameof(TweeqTabs.TabsName));
            AssertUxmlAttribute(typeof(TweeqTabs), nameof(TweeqTabs.Vertical));
            AssertUxmlAttribute(typeof(TweeqTabs), nameof(TweeqTabs.StorageKey));
            AssertUxmlAttribute(typeof(TweeqTabs), nameof(TweeqTabs.DefaultTabId));

            AssertUxmlAttribute(typeof(TweeqTab), nameof(TweeqTab.TabName));
            AssertUxmlAttribute(typeof(TweeqTab), nameof(TweeqTab.Id));
            AssertUxmlAttribute(typeof(TweeqTab), nameof(TweeqTab.IsDisabled));
        }

        static void AssertUxmlElement(Type type)
        {
            Assert.IsNotEmpty(type.GetCustomAttributes(typeof(UxmlElementAttribute), false),
                $"{type.Name}: [UxmlElement] が無いと UXML から使えない");
            Assert.IsNotNull(
                type.GetNestedType("UxmlSerializedData", BindingFlags.Public | BindingFlags.NonPublic),
                $"{type.Name}: UxmlSerializedData が生成されていない（partial 宣言漏れ）");
            Assert.IsNotNull(type.GetConstructor(Type.EmptyTypes),
                $"{type.Name}: UXML からの生成にはパラメータなしコンストラクタが必要");
        }

        static void AssertUxmlAttribute(Type type, string propertyName)
        {
            PropertyInfo property = type.GetProperty(propertyName);

            Assert.IsNotNull(property, $"{type.Name}.{propertyName} が無い");
            Assert.IsNotEmpty(property.GetCustomAttributes(typeof(UxmlAttributeAttribute), false),
                $"{type.Name}.{propertyName} に [UxmlAttribute] が無い");
        }

        #endregion
    }
}
