using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// DropdownInput の開閉状態機械（popover-spec.md「テスト契約」の DropdownInput 項目）を検証する。
    ///
    /// DropdownInput は Open/Close/Commit/Cancel/MoveSelection/PerformPointerUp を panel 非依存の
    /// 論理層として持ち、ポップアップの表示だけを panel 付きで乗せる。したがってキーボードと
    /// 500ms ルールはここで完結する。以下は panel と描画が要るので Play Mode 側の担当:
    /// - フィールド押下 → 開く / option ホバーで値がプレビューされる
    /// - macOS 風配置（選択中 option がフィールドに重なる）とスクロール矢印・オートスクロール
    /// - 外側クリックで「valueAtStart へロールバックして閉じる」（Cancel と同じ経路）
    /// </summary>
    public class DropdownInputTests
    {
        static readonly string[] Options = { "Linear", "Ease In", "Ease Out" };

        static DropdownInput<string> Create(string initial = "Linear")
        {
            DropdownInput<string> dropdown = new DropdownInput<string>(Options);
            dropdown.SetValueWithoutNotify(initial);
            return dropdown;
        }

        static float Radius(StyleLength length)
        {
            return length.value.value;
        }

        #region Arrow keys

        [Test]
        public void Arrow_DownMovesToNextOption()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.MoveSelection(1);

            Assert.AreEqual("Ease In", dropdown.value);
        }

        [Test]
        public void Arrow_UpFromFirstWrapsToLast()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.MoveSelection(-1);

            Assert.AreEqual("Ease Out", dropdown.value);
        }

        [Test]
        public void Arrow_DownFromLastWrapsToFirst()
        {
            DropdownInput<string> dropdown = Create("Ease Out");

            dropdown.MoveSelection(1);

            Assert.AreEqual("Linear", dropdown.value);
        }

        [Test]
        public void Arrow_RaisesValueChangedButNotConfirmed()
        {
            DropdownInput<string> dropdown = Create();
            List<string> changed = new List<string>();
            int confirmed = 0;
            dropdown.ValueChanged += value => changed.Add(value);
            dropdown.Confirmed += _ => confirmed++;

            dropdown.MoveSelection(1);
            dropdown.MoveSelection(1);

            Assert.AreEqual(new[] { "Ease In", "Ease Out" }, changed.ToArray());
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Arrow_WithoutOptionsDoesNothing()
        {
            DropdownInput<string> dropdown = new DropdownInput<string>();
            int changed = 0;
            dropdown.ValueChanged += _ => changed++;

            Assert.DoesNotThrow(() => dropdown.MoveSelection(1));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Arrow_ValueOutsideOptionsFallsBackToFirst()
        {
            DropdownInput<string> dropdown = Create("Cubic");

            dropdown.MoveSelection(1);

            Assert.AreEqual("Linear", dropdown.value);
        }

        [Test]
        public void Arrow_DisabledDoesNotMove()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Disabled = true;

            dropdown.MoveSelection(1);

            Assert.AreEqual("Linear", dropdown.value);
        }

        #endregion

        #region Open / close

        [Test]
        public void Open_TogglesLogicalStateWithoutPanel()
        {
            DropdownInput<string> dropdown = Create();

            Assert.DoesNotThrow(() => dropdown.Open());

            Assert.IsTrue(dropdown.IsOpen);
            Assert.AreEqual("Linear", dropdown.ValueAtStart);
        }

        [Test]
        public void Open_WithoutOptionsIsIgnored()
        {
            DropdownInput<string> dropdown = new DropdownInput<string>();

            dropdown.Open();

            Assert.IsFalse(dropdown.IsOpen);
        }

        [Test]
        public void Open_WhileDisabledIsIgnored()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Disabled = true;

            dropdown.Open();

            Assert.IsFalse(dropdown.IsOpen);
        }

        [Test]
        public void Close_DoesNotConfirmAndKeepsCurrentValue()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            dropdown.Open();
            dropdown.MoveSelection(1);
            dropdown.Close();

            Assert.IsFalse(dropdown.IsOpen);
            Assert.AreEqual("Ease In", dropdown.value);
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Commit_ClosesAndConfirmsOnce()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            string last = null;
            dropdown.Confirmed += value =>
            {
                confirmed++;
                last = value;
            };

            dropdown.Open();
            dropdown.MoveSelection(1);
            dropdown.Commit();

            Assert.IsFalse(dropdown.IsOpen);
            Assert.AreEqual(1, confirmed);
            Assert.AreEqual("Ease In", last);
        }

        [Test]
        public void Commit_WhileClosedIsIgnored()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            dropdown.Commit();

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Commit_TwiceStillConfirmsOnlyOnce()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            dropdown.Open();
            dropdown.Commit();
            dropdown.Commit();

            Assert.AreEqual(1, confirmed);
        }

        #endregion

        #region Escape rollback

        [Test]
        public void Cancel_RollsBackToValueAtStart()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.Open();
            dropdown.MoveSelection(1);
            dropdown.MoveSelection(1);
            dropdown.Cancel();

            Assert.IsFalse(dropdown.IsOpen);
            Assert.AreEqual("Linear", dropdown.value);
        }

        [Test]
        public void Cancel_NotifiesTheRollbackAndDoesNotConfirm()
        {
            DropdownInput<string> dropdown = Create();
            List<string> changed = new List<string>();
            int confirmed = 0;
            dropdown.ValueChanged += value => changed.Add(value);
            dropdown.Confirmed += _ => confirmed++;

            dropdown.Open();
            dropdown.MoveSelection(1);
            dropdown.Cancel();

            // 進めた分と戻した分の 2 回。途中で通知した値を巻き戻すので戻しも通知する
            Assert.AreEqual(new[] { "Ease In", "Linear" }, changed.ToArray());
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Cancel_WithoutChangeDoesNotNotify()
        {
            DropdownInput<string> dropdown = Create();
            int changed = 0;
            dropdown.ValueChanged += _ => changed++;

            dropdown.Open();
            dropdown.Cancel();

            Assert.AreEqual(0, changed);
            Assert.AreEqual("Linear", dropdown.value);
        }

        [Test]
        public void Cancel_WhileClosedIsIgnored()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.SetValueWithoutNotify("Ease Out");

            dropdown.Cancel();

            Assert.AreEqual("Ease Out", dropdown.value);
        }

        #endregion

        #region 500ms rule

        [Test]
        public void PointerUp_WithinGraceIsIgnored()
        {
            long now = 1000;
            DropdownInput<string> dropdown = Create();
            dropdown.TimeSource = () => now;
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            dropdown.Open();
            now += 400;
            dropdown.PerformPointerUp();

            // 押しっぱなしドラッグ選択の途中なので開いたまま
            Assert.IsTrue(dropdown.IsOpen);
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void PointerUp_AtExactlyGraceIsStillIgnored()
        {
            long now = 0;
            DropdownInput<string> dropdown = Create();
            dropdown.TimeSource = () => now;

            dropdown.Open();
            now += 500;
            dropdown.PerformPointerUp();

            Assert.IsTrue(dropdown.IsOpen);
        }

        [Test]
        public void PointerUp_AfterGraceConfirmsAndCloses()
        {
            long now = 1000;
            DropdownInput<string> dropdown = Create();
            dropdown.TimeSource = () => now;
            int confirmed = 0;
            string last = null;
            dropdown.Confirmed += value =>
            {
                confirmed++;
                last = value;
            };

            dropdown.Open();
            now += 400;
            dropdown.PerformPointerUp();

            dropdown.MoveSelection(1);
            now += 200;
            dropdown.PerformPointerUp();

            Assert.IsFalse(dropdown.IsOpen);
            Assert.AreEqual(1, confirmed);
            Assert.AreEqual("Ease In", last);
        }

        [Test]
        public void PointerUp_AfterCloseDoesNotConfirmAgain()
        {
            long now = 0;
            DropdownInput<string> dropdown = Create();
            dropdown.TimeSource = () => now;
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            dropdown.Open();
            now += 600;
            dropdown.PerformPointerUp();
            dropdown.PerformPointerUp();

            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void PointerUp_WhileClosedIsIgnored()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            Assert.DoesNotThrow(() => dropdown.PerformPointerUp());
            Assert.AreEqual(0, confirmed);
        }

        #endregion

        #region Labels

        [Test]
        public void Label_FallsBackToToString()
        {
            DropdownInput<int> dropdown = new DropdownInput<int>(new[] { 10, 20 });
            dropdown.SetValueWithoutNotify(20);

            Assert.AreEqual("20", dropdown.DisplayText);
        }

        [Test]
        public void Label_UsesLabelsArrayByIndex()
        {
            DropdownInput<int> dropdown = new DropdownInput<int>(new[] { 10, 20 })
            {
                Labels = new[] { "Ten", "Twenty" },
            };
            dropdown.SetValueWithoutNotify(20);

            Assert.AreEqual("Twenty", dropdown.DisplayText);
        }

        [Test]
        public void Label_LabelizerBeatsLabelsArray()
        {
            DropdownInput<int> dropdown = new DropdownInput<int>(new[] { 10, 20 })
            {
                Labels = new[] { "Ten", "Twenty" },
                Labelizer = v => "#" + v,
            };
            dropdown.SetValueWithoutNotify(20);

            Assert.AreEqual("#20", dropdown.DisplayText);
        }

        [Test]
        public void Label_IsBuiltOncePerOptionsChange()
        {
            int calls = 0;
            DropdownInput<int> dropdown = new DropdownInput<int>
            {
                Labelizer = v =>
                {
                    calls++;
                    return v.ToString();
                },
            };

            // options 外の値（初期値 0）の表示フォールバックも labelizer を通るため、
            // 絶対回数ではなく「options 内の値移動でキャッシュヒットすること」を検証する
            dropdown.Options = new[] { 1, 2, 3 };
            dropdown.SetValueWithoutNotify(1);
            int afterBuild = calls;

            // 値を動かしてもラベルは作り直さない（キャッシュ参照のみ）
            dropdown.SetValueWithoutNotify(2);
            dropdown.SetValueWithoutNotify(3);

            Assert.AreEqual(afterBuild, calls);
        }

        [Test]
        public void Label_PrefixAndSuffixWrapTheField()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Prefix = "[";
            dropdown.Suffix = "]";

            Assert.AreEqual("[Linear]", dropdown.DisplayText);
        }

        [Test]
        public void Label_NullPrefixBecomesEmpty()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.Prefix = null;
            dropdown.Suffix = null;

            Assert.AreEqual(string.Empty, dropdown.Prefix);
            Assert.AreEqual(string.Empty, dropdown.Suffix);
            Assert.AreEqual("Linear", dropdown.DisplayText);
        }

        #endregion

        #region Options

        [Test]
        public void Options_GetReturnsCopy()
        {
            DropdownInput<string> dropdown = Create();

            string[] snapshot = dropdown.Options;
            snapshot[0] = "Z";

            Assert.AreEqual("Linear", dropdown.Options[0]);
        }

        [Test]
        public void Options_SetCopiesInput()
        {
            string[] source = { "A", "B" };
            DropdownInput<string> dropdown = new DropdownInput<string>(source);

            source[1] = "Z";

            Assert.AreEqual("B", dropdown.Options[1]);
        }

        [Test]
        public void Options_NullBecomesEmpty()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.Options = null;

            Assert.AreEqual(0, dropdown.Options.Length);
        }

        [Test]
        public void Options_ClearedWhileOpenClosesThePopup()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Open();

            dropdown.Options = null;

            Assert.IsFalse(dropdown.IsOpen);
        }

        [Test]
        public void Options_KeepsCurrentValueEvenIfItDisappears()
        {
            DropdownInput<string> dropdown = Create();
            int changed = 0;
            dropdown.ValueChanged += _ => changed++;

            dropdown.Options = new[] { "Ease In" };

            // 勝手に値を動かして通知しない（消えた値の扱いは呼び出し側の責務）
            Assert.AreEqual("Linear", dropdown.value);
            Assert.AreEqual(0, changed);
        }

        #endregion

        #region Value

        [Test]
        public void Value_SetterNotifiesOnce()
        {
            DropdownInput<string> dropdown = Create();
            int changed = 0;
            dropdown.ValueChanged += _ => changed++;

            dropdown.value = "Ease In";
            dropdown.value = "Ease In";

            Assert.AreEqual(1, changed);
        }

        [Test]
        public void Value_SetterDoesNotConfirm()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            dropdown.value = "Ease Out";

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Value_SetValueWithoutNotifyIsSilent()
        {
            DropdownInput<string> dropdown = Create();
            int changed = 0;
            dropdown.ValueChanged += _ => changed++;

            dropdown.SetValueWithoutNotify("Ease Out");

            Assert.AreEqual("Ease Out", dropdown.value);
            Assert.AreEqual(0, changed);
        }

        #endregion

        #region Fuzzy filter

        [Test]
        public void Filter_TypingOpensAndNarrowsTheList()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter("ease");

            Assert.IsTrue(dropdown.IsOpen);
            Assert.IsTrue(dropdown.IsFiltering);
            Assert.AreEqual("ease", dropdown.FilterQuery);

            // "Linear" は "ease" のサブシーケンスを含まない
            Assert.AreEqual(2, dropdown.VisibleCount);
        }

        [Test]
        public void Filter_MapsVisibleRowsBackToOptions()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter("ease");

            Assert.AreEqual(1, dropdown.OptionIndexAt(0));
            Assert.AreEqual(2, dropdown.OptionIndexAt(1));
            Assert.AreEqual(-1, dropdown.OptionIndexAt(2));
            Assert.AreEqual(-1, dropdown.OptionIndexAt(-1));
        }

        [Test]
        public void Filter_PullsTheValueIntoTheResultSet()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter("out");

            Assert.AreEqual(1, dropdown.VisibleCount);
            Assert.AreEqual("Ease Out", dropdown.value);
        }

        [Test]
        public void Filter_KeepsValueAtStartFromBeforeTheQuery()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter("out");

            // 絞り込みが値を動かしても Escape の戻り先は打鍵前のまま
            Assert.AreEqual("Linear", dropdown.ValueAtStart);
        }

        [Test]
        public void Filter_ArrowWrapsInsideTheResults()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.BeginFilter("ease");

            Assert.AreEqual("Ease In", dropdown.value);

            dropdown.MoveSelection(1);
            Assert.AreEqual("Ease Out", dropdown.value);

            // 絞り込みの外にある "Linear" を踏まずに先頭へ戻る
            dropdown.MoveSelection(1);
            Assert.AreEqual("Ease In", dropdown.value);

            dropdown.MoveSelection(-1);
            Assert.AreEqual("Ease Out", dropdown.value);
        }

        [Test]
        public void Filter_EnterConfirmsAndClearsTheFilter()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            string last = null;
            dropdown.Confirmed += value =>
            {
                confirmed++;
                last = value;
            };

            dropdown.BeginFilter("out");
            dropdown.Commit();

            Assert.IsFalse(dropdown.IsOpen);
            Assert.IsFalse(dropdown.IsFiltering);
            Assert.AreEqual(string.Empty, dropdown.FilterQuery);
            Assert.AreEqual(3, dropdown.VisibleCount);
            Assert.AreEqual(1, confirmed);
            Assert.AreEqual("Ease Out", last);
            Assert.AreEqual("Ease Out", dropdown.DisplayText);
        }

        [Test]
        public void Filter_CloseClearsTheFilterAndRestoresTheLabel()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter("ease");
            dropdown.Close();

            Assert.IsFalse(dropdown.IsFiltering);
            Assert.AreEqual(string.Empty, dropdown.FilterQuery);
            Assert.AreEqual(3, dropdown.VisibleCount);
            Assert.AreEqual(dropdown.value, dropdown.DisplayText);
        }

        [Test]
        public void Filter_EscapeRollsBackToValueAtStart()
        {
            DropdownInput<string> dropdown = Create();
            int confirmed = 0;
            dropdown.Confirmed += _ => confirmed++;

            dropdown.BeginFilter("out");
            dropdown.Cancel();

            Assert.AreEqual("Linear", dropdown.value);
            Assert.IsFalse(dropdown.IsOpen);
            Assert.IsFalse(dropdown.IsFiltering);
            Assert.AreEqual(0, confirmed);
            Assert.AreEqual("Linear", dropdown.DisplayText);
        }

        [Test]
        public void Filter_EmptyQueryShowsEveryOption()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter("ease");
            dropdown.SetFilterQuery(string.Empty);

            // 空文字はフィルタ解除ではなく「絞り込み無し」。モードは続く
            Assert.IsTrue(dropdown.IsFiltering);
            Assert.AreEqual(3, dropdown.VisibleCount);
            Assert.AreEqual(0, dropdown.OptionIndexAt(0));
        }

        [Test]
        public void Filter_NullQueryBecomesEmpty()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter(null);

            Assert.IsTrue(dropdown.IsOpen);
            Assert.IsTrue(dropdown.IsFiltering);
            Assert.AreEqual(string.Empty, dropdown.FilterQuery);
            Assert.AreEqual(3, dropdown.VisibleCount);
        }

        [Test]
        public void Filter_RetypingNarrowsAgain()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.BeginFilter("e");
            int afterFirstKey = dropdown.VisibleCount;

            dropdown.SetFilterQuery("ea");
            dropdown.SetFilterQuery("eas");

            Assert.AreEqual(3, afterFirstKey);
            Assert.AreEqual(2, dropdown.VisibleCount);
        }

        [Test]
        public void Filter_NoMatchLeavesTheValueAlone()
        {
            DropdownInput<string> dropdown = Create();
            int changed = 0;
            dropdown.ValueChanged += _ => changed++;

            dropdown.BeginFilter("zzz");

            Assert.AreEqual(0, dropdown.VisibleCount);
            Assert.AreEqual("Linear", dropdown.value);
            Assert.AreEqual(0, changed);

            // 候補が無い状態の ↑↓ は何も起こさない
            Assert.DoesNotThrow(() => dropdown.MoveSelection(1));
            Assert.AreEqual("Linear", dropdown.value);
        }

        [Test]
        public void Filter_WhileDisabledIsIgnored()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Disabled = true;

            dropdown.BeginFilter("ease");

            Assert.IsFalse(dropdown.IsFiltering);
            Assert.IsFalse(dropdown.IsOpen);
        }

        [Test]
        public void Filter_WithoutOptionsIsIgnored()
        {
            DropdownInput<string> dropdown = new DropdownInput<string>();

            dropdown.BeginFilter("ease");

            Assert.IsFalse(dropdown.IsFiltering);
            Assert.IsFalse(dropdown.IsOpen);
        }

        [Test]
        public void Filter_SetQueryWhileNotFilteringIsIgnored()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.SetFilterQuery("ease");

            Assert.IsFalse(dropdown.IsFiltering);
            Assert.AreEqual(string.Empty, dropdown.FilterQuery);
            Assert.AreEqual(3, dropdown.VisibleCount);
        }

        [Test]
        public void Filter_EndFilterKeepsThePopupOpen()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.BeginFilter("ease");

            dropdown.EndFilter();

            // 解除と開閉は別責務（閉じるのは Close / Commit / Cancel）
            Assert.IsTrue(dropdown.IsOpen);
            Assert.IsFalse(dropdown.IsFiltering);
            Assert.AreEqual(3, dropdown.VisibleCount);
        }

        [Test]
        public void Filter_StartedWhileOpenDoesNotReopen()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Open();
            dropdown.MoveSelection(1);

            dropdown.BeginFilter("out");

            // 既に開いているので valueAtStart は Open のときのまま
            Assert.AreEqual("Linear", dropdown.ValueAtStart);
            Assert.AreEqual("Ease Out", dropdown.value);
        }

        [Test]
        public void Filter_OptionsReplacedWhileFilteringStaysInRange()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.BeginFilter("ease");

            Assert.DoesNotThrow(() => dropdown.Options = new[] { "Ease In" });

            Assert.AreEqual(1, dropdown.VisibleCount);
            Assert.AreEqual(0, dropdown.OptionIndexAt(0));
        }

        [Test]
        public void Filter_ClearingOptionsWhileFilteringClosesAndResets()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.BeginFilter("ease");

            dropdown.Options = null;

            Assert.IsFalse(dropdown.IsOpen);
            Assert.IsFalse(dropdown.IsFiltering);
            Assert.AreEqual(0, dropdown.VisibleCount);
        }

        [Test]
        public void Filter_DisablingWhileFilteringResets()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.BeginFilter("ease");

            dropdown.Disabled = true;

            Assert.IsFalse(dropdown.IsOpen);
            Assert.IsFalse(dropdown.IsFiltering);
        }

        [Test]
        public void Filter_UsesLabelizerTextNotTheRawValue()
        {
            DropdownInput<int> dropdown = new DropdownInput<int>(new[] { 10, 20, 30 })
            {
                Labelizer = v => "#" + v,
            };
            dropdown.SetValueWithoutNotify(10);

            dropdown.BeginFilter("20");

            Assert.AreEqual(1, dropdown.VisibleCount);
            Assert.AreEqual(1, dropdown.OptionIndexAt(0));
            Assert.AreEqual(20, dropdown.value);
        }

        #endregion

        #region Box

        [Test]
        public void Box_ImplementsInputBox()
        {
            Assert.IsTrue(new DropdownInput<string>() is ITweeqInputBox);
        }

        [Test]
        public void Box_StandaloneKeepsEveryCorner()
        {
            DropdownInput<string> dropdown = Create();
            float radius = dropdown.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(dropdown.style.borderTopLeftRadius));
            Assert.AreEqual(radius, Radius(dropdown.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(dropdown.style.borderBottomLeftRadius));
            Assert.AreEqual(radius, Radius(dropdown.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_InlineStartFlattensTrailingCorners()
        {
            DropdownInput<string> dropdown = new DropdownInput<string>(Options)
            {
                InlinePosition = TweeqBoxPosition.Start,
            };
            float radius = dropdown.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(dropdown.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(dropdown.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(dropdown.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(dropdown.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_BlockMiddleFlattensEveryCorner()
        {
            DropdownInput<string> dropdown = new DropdownInput<string>(Options)
            {
                BlockPosition = TweeqBoxPosition.Middle,
            };

            Assert.AreEqual(0f, Radius(dropdown.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(dropdown.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(dropdown.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(dropdown.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_DisabledBlocksPicking()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.Disabled = true;
            Assert.AreEqual(PickingMode.Ignore, dropdown.pickingMode);

            dropdown.Disabled = false;
            Assert.AreEqual(PickingMode.Position, dropdown.pickingMode);
        }

        [Test]
        public void Box_DisablingWhileOpenClosesThePopup()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Open();

            dropdown.Disabled = true;

            Assert.IsFalse(dropdown.IsOpen);
        }

        [Test]
        public void Box_IsFocusable()
        {
            Assert.IsTrue(Create().focusable);
        }

        [Test]
        public void Box_ThemeNullFallsBackToDark()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.Theme = null;

            Assert.IsNotNull(dropdown.Theme);
            Assert.AreEqual(ColorMode.Dark, dropdown.Theme.Mode);
        }

        #endregion

        #region Invalid

        // Vue は表示を内部 InputString へ委譲しているので invalid も持つ（m7-disabled-invalid-spec.md）。
        // こちらは委譲先が無いため、StringInput.ShowInvalid と同じ「文字色だけ Error」を自前で掛ける
        [Test]
        public void Invalid_TurnsTheFieldLabelIntoErrorColor()
        {
            DropdownInput<string> dropdown = Create();
            Label label = dropdown.Q<Label>("tweeq-dropdown-label");

            dropdown.Invalid = true;

            Assert.IsTrue(dropdown.Invalid);
            Assert.AreEqual(dropdown.Theme.Error, label.style.color.value);
        }

        [Test]
        public void Invalid_FalseRestoresTheNormalTextColor()
        {
            DropdownInput<string> dropdown = Create();
            Label label = dropdown.Q<Label>("tweeq-dropdown-label");
            dropdown.Invalid = true;

            dropdown.Invalid = false;

            Assert.AreEqual(dropdown.Theme.Text, label.style.color.value);
        }

        [Test]
        public void Invalid_AppliesToTheFilterFieldToo()
        {
            DropdownInput<string> dropdown = Create();
            dropdown.Invalid = true;

            // フィルタ用 TextField は初回の絞り込みで初めて作られるので、後追いでも色が乗ること
            dropdown.BeginFilter("ea");

            TextField filter = dropdown.Q<TextField>("tweeq-dropdown-filter");
            Assert.IsNotNull(filter);
            Assert.AreEqual(dropdown.Theme.Error, filter.style.color.value);
        }

        [Test]
        public void Invalid_IsOverriddenByDisabled()
        {
            DropdownInput<string> dropdown = Create();
            Label label = dropdown.Q<Label>("tweeq-dropdown-label");

            dropdown.Invalid = true;
            dropdown.Disabled = true;

            // 無効なフィールドの赤字は「操作できる不正値」と読み違えられるので dim を優先する
            Assert.AreEqual(dropdown.Theme.TextSubtle, label.style.color.value);
        }

        [Test]
        public void Invalid_DoesNotChangeTheValue()
        {
            DropdownInput<string> dropdown = Create();

            dropdown.Invalid = true;

            Assert.AreEqual("Linear", dropdown.value);
        }

        #endregion
    }
}
