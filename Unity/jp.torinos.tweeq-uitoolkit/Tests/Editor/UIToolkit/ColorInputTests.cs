using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// ColorInput の論理層（string-color-spec.md「テスト契約」の ColorInput 項目）を検証する。
    ///
    /// ColorInput は開閉・ドラッグセッション・プリセット・HEX 同期を panel 非依存の
    /// 命令的 API として持ち、ポップオーバーと描画だけをその上に乗せる。したがって
    /// 「イベントが何回どの順で飛ぶか」はここで完結する。以下は panel と描画が要るので
    /// Play Mode 側の担当:
    /// - スウォッチ押下でピッカーが開く／外側クリックで閉じる
    /// - SV パッド・Hue バー・Alpha バーの当たり判定とカーソル位置
    /// - SV グラデーションが hue 変化時にだけ焼き直されること
    /// </summary>
    public class ColorInputTests
    {
        const double EPSILON = 1e-4;

        static ColorInput Create(Color initial)
        {
            ColorInput input = new ColorInput();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        static float Radius(StyleLength length)
        {
            return length.value.value;
        }

        static VisualElement Swatch(ColorInput input)
        {
            return input.Q("tweeq-color-swatch");
        }

        static void AssertColor(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, EPSILON, "r");
            Assert.AreEqual(expected.g, actual.g, EPSILON, "g");
            Assert.AreEqual(expected.b, actual.b, EPSILON, "b");
            Assert.AreEqual(expected.a, actual.a, EPSILON, "a");
        }

        #region Presets

        [Test]
        public void Preset_ClickRaisesValueChangedAndConfirmed()
        {
            // 原典は confirm が飛ばないバグ。React 修正版 + test-contracts の契約を採用している
            ColorInput input = Create(Color.white);
            input.Presets = new[] { Color.red, Color.green };

            List<Color> changed = new List<Color>();
            List<Color> confirmed = new List<Color>();
            input.ValueChanged += value => changed.Add(value);
            input.Confirmed += value => confirmed.Add(value);

            input.PerformPresetClick(0);

            Assert.AreEqual(1, changed.Count);
            Assert.AreEqual(1, confirmed.Count);
            AssertColor(Color.red, changed[0]);
            AssertColor(Color.red, confirmed[0]);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Preset_ClickOnCurrentValueStillConfirms()
        {
            ColorInput input = Create(Color.red);
            input.Presets = new[] { Color.red };

            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.PerformPresetClick(0);

            // 値は動かないので ValueChanged は出ないが、確定操作であることは変わらない
            Assert.AreEqual(0, changed);
            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Preset_OutOfRangeIndexIsIgnored()
        {
            ColorInput input = Create(Color.white);
            input.Presets = new[] { Color.red };
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            Assert.DoesNotThrow(() => input.PerformPresetClick(-1));
            Assert.DoesNotThrow(() => input.PerformPresetClick(1));

            Assert.AreEqual(0, confirmed);
            AssertColor(Color.white, input.value);
        }

        [Test]
        public void Preset_ClickWhileDisabledIsIgnored()
        {
            ColorInput input = Create(Color.white);
            input.Presets = new[] { Color.red };
            input.Disabled = true;
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.PerformPresetClick(0);

            Assert.AreEqual(0, confirmed);
            AssertColor(Color.white, input.value);
        }

        [Test]
        public void Preset_DefaultPaletteIsNotEmpty()
        {
            Assert.Greater(new ColorInput().Presets.Length, 0);
            Assert.AreEqual(ColorInput.DefaultPresets.Length, new ColorInput().Presets.Length);
        }

        [Test]
        public void Preset_GetReturnsCopy()
        {
            ColorInput input = Create(Color.white);
            input.Presets = new[] { Color.red, Color.green };

            Color[] snapshot = input.Presets;
            snapshot[0] = Color.blue;

            AssertColor(Color.red, input.Presets[0]);
        }

        [Test]
        public void Preset_SetCopiesInput()
        {
            ColorInput input = Create(Color.white);
            Color[] source = { Color.red, Color.green };
            input.Presets = source;

            source[1] = Color.blue;

            AssertColor(Color.green, input.Presets[1]);
        }

        [Test]
        public void Preset_NullBecomesEmpty()
        {
            ColorInput input = Create(Color.white);

            input.Presets = null;

            Assert.AreEqual(0, input.Presets.Length);
        }

        #endregion

        #region Drag session

        [Test]
        public void Drag_SvSessionConfirmsExactlyOnce()
        {
            ColorInput input = Create(Color.red);
            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.BeginPickerDrag(ColorPickerAxis.SaturationValue);
            input.UpdatePickerDrag(0.5f, 0.5f);
            input.UpdatePickerDrag(0.25f, 0.25f);
            input.UpdatePickerDrag(0.1f, 0.75f);
            input.EndPickerDrag();

            // pointermove ごとに ValueChanged（間引きなし・Vue 準拠）／終了で Confirmed 1 回
            Assert.AreEqual(3, changed);
            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Drag_SvMapsPositionToSaturationAndValue()
        {
            ColorInput input = Create(Color.red);

            input.BeginPickerDrag(ColorPickerAxis.SaturationValue);

            // 左上 = 彩度 0 / 明度 1（白）
            input.UpdatePickerDrag(0f, 0f);
            AssertColor(Color.white, input.value);

            // 下端 = 明度 0（黒）
            input.UpdatePickerDrag(1f, 1f);
            AssertColor(Color.black, input.value);

            // 右上 = 彩度 1 / 明度 1（純色）
            input.UpdatePickerDrag(1f, 0f);
            AssertColor(Color.red, input.value);

            input.EndPickerDrag();
        }

        [Test]
        public void Drag_PositionIsClampedToTheTrack()
        {
            ColorInput input = Create(Color.red);

            input.BeginPickerDrag(ColorPickerAxis.SaturationValue);
            input.UpdatePickerDrag(-5f, 3f);
            input.EndPickerDrag();

            Assert.AreEqual(0.0, input.Hsva.S, EPSILON);
            Assert.AreEqual(0.0, input.Hsva.V, EPSILON);
        }

        [Test]
        public void Drag_HueKeepsSaturationAndValue()
        {
            ColorInput input = Create(Color.red);

            input.BeginPickerDrag(ColorPickerAxis.Hue);
            input.UpdatePickerDrag(0.5f, 0f);
            input.EndPickerDrag();

            Assert.AreEqual(180.0, input.Hsva.H, 1e-3);
            Assert.AreEqual(1.0, input.Hsva.S, EPSILON);
            Assert.AreEqual(1.0, input.Hsva.V, EPSILON);
            AssertColor(Color.cyan, input.value);
        }

        [Test]
        public void Drag_AlphaMovesOnlyAlpha()
        {
            ColorInput input = Create(Color.red);

            input.BeginPickerDrag(ColorPickerAxis.Alpha);
            input.UpdatePickerDrag(0.25f, 0.5f);
            input.EndPickerDrag();

            Assert.AreEqual(0.25, input.Hsva.A, EPSILON);
            AssertColor(new Color(1f, 0f, 0f, 0.25f), input.value);
        }

        [Test]
        public void Drag_UpdateWithoutBeginIsIgnored()
        {
            ColorInput input = Create(Color.red);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.UpdatePickerDrag(0.5f, 0.5f);

            Assert.AreEqual(0, changed);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Drag_EndWithoutBeginDoesNotConfirm()
        {
            ColorInput input = Create(Color.red);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            Assert.DoesNotThrow(() => input.EndPickerDrag());

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Drag_EndTwiceConfirmsOnlyOnce()
        {
            ColorInput input = Create(Color.red);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginPickerDrag(ColorPickerAxis.SaturationValue);
            input.UpdatePickerDrag(0.5f, 0.5f);
            input.EndPickerDrag();
            input.EndPickerDrag();

            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Drag_CancelRestoresStartValueWithoutConfirming()
        {
            ColorInput input = Create(Color.red);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginPickerDrag(ColorPickerAxis.SaturationValue);
            input.UpdatePickerDrag(0.5f, 0.5f);
            input.CancelPickerDrag();

            Assert.AreEqual(0, confirmed);
            Assert.AreEqual(ColorPickerAxis.None, input.ActiveAxis);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Drag_WhileDisabledDoesNotStart()
        {
            ColorInput input = Create(Color.red);
            input.Disabled = true;

            input.BeginPickerDrag(ColorPickerAxis.SaturationValue);
            input.UpdatePickerDrag(0.5f, 0.5f);

            Assert.AreEqual(ColorPickerAxis.None, input.ActiveAxis);
            AssertColor(Color.red, input.value);
        }

        #endregion

        #region Channel scrub

        // 感度基準は tweakWidth = PopupWidth = 240（仕様 §A）。
        // 240px 動かすと pad / 単チャンネルは 0→1、hue は 1 周する
        const float TWEAK_WIDTH = 240f;

        static ColorInput CreateScrubbing(ColorTweakMode mode, double h, double s, double v, double a)
        {
            ColorInput input = Create(Color.white);
            input.SetHsva(h, s, v, a);
            input.SetScrubMode(mode);
            input.BeginChannelScrub(Vector2.zero);
            return input;
        }

        [Test]
        public void Scrub_ThresholdMatchesTheSpec()
        {
            Assert.AreEqual(3f, ColorInput.ScrubThreshold);
        }

        [Test]
        public void Scrub_PadMapsHorizontalToSaturationAndVerticalToValue()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Pad, 0.0, 0.5, 0.5, 1.0);

            // dx = Δx/240、dy = −Δy/240（上方向が正）
            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.1f, -TWEAK_WIDTH * 0.1f));

            Assert.AreEqual(0.6, input.Hsva.S, EPSILON);
            Assert.AreEqual(0.6, input.Hsva.V, EPSILON);
            Assert.AreEqual(0.0, input.Hsva.H, EPSILON);
        }

        [Test]
        public void Scrub_HueMapsFullWidthToOneTurn()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Hue, 0.0, 1.0, 1.0, 1.0);

            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.5f, 0f));

            Assert.AreEqual(180.0, input.Hsva.H, 1e-3);
            Assert.AreEqual(1.0, input.Hsva.S, EPSILON);
            Assert.AreEqual(1.0, input.Hsva.V, EPSILON);
            AssertColor(Color.cyan, input.value);
        }

        [Test]
        public void Scrub_HueWrapsAroundInsteadOfClamping()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Hue, 270.0, 1.0, 1.0, 1.0);

            // 270 + 180 = 450 → 90
            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.5f, 0f));

            Assert.AreEqual(90.0, input.Hsva.H, 1e-3);
        }

        [Test]
        public void Scrub_SaturationUsesHorizontalAxisOnly()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Saturation, 0.0, 0.5, 0.5, 1.0);

            input.UpdateChannelScrub(new Vector2(-TWEAK_WIDTH * 0.1f, -TWEAK_WIDTH * 0.5f));

            Assert.AreEqual(0.4, input.Hsva.S, EPSILON);
            Assert.AreEqual(0.5, input.Hsva.V, EPSILON);
        }

        [Test]
        public void Scrub_ValueUsesVerticalAxisOnly()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Value, 0.0, 1.0, 0.5, 1.0);

            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.5f, -TWEAK_WIDTH * 0.1f));

            Assert.AreEqual(0.6, input.Hsva.V, EPSILON);
            Assert.AreEqual(1.0, input.Hsva.S, EPSILON);
        }

        [Test]
        public void Scrub_AlphaUsesHorizontalAxis()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Alpha, 0.0, 1.0, 1.0, 1.0);

            input.UpdateChannelScrub(new Vector2(-TWEAK_WIDTH * 0.5f, 0f));

            Assert.AreEqual(0.5, input.Hsva.A, EPSILON);
            Assert.AreEqual(0.5f, input.value.a, EPSILON);
        }

        [Test]
        public void Scrub_RgbChannelsMoveInZeroToOneSpace()
        {
            // 表示は 0-255 だが内部は 0-1。240px = チャンネル全域
            ColorInput input = CreateScrubbing(ColorTweakMode.Red, 0.0, 1.0, 1.0, 1.0);

            input.UpdateChannelScrub(new Vector2(-TWEAK_WIDTH * 0.5f, 0f));

            AssertColor(new Color(0.5f, 0f, 0f, 1f), input.value);
        }

        [Test]
        public void Scrub_ClampsChannelsToTheirRange()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Pad, 0.0, 0.5, 0.5, 1.0);

            input.UpdateChannelScrub(new Vector2(-TWEAK_WIDTH * 3f, TWEAK_WIDTH * 3f));

            Assert.AreEqual(0.0, input.Hsva.S, EPSILON);
            Assert.AreEqual(0.0, input.Hsva.V, EPSILON);
        }

        [Test]
        public void Scrub_ModeSwitchRecapturesTheBasisWithoutJumping()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Pad, 0.0, 0.5, 0.5, 1.0);

            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.1f, 0f));
            Color afterPad = input.value;
            Assert.AreEqual(0.6, input.Hsva.S, EPSILON);

            // 切替直後は「現在値が新しい基準」なので、同じ位置なら 1 ミリも動かない
            input.SetScrubMode(ColorTweakMode.Hue);
            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.1f, 0f));

            AssertColor(afterPad, input.value);
            Assert.AreEqual(0.0, input.Hsva.H, EPSILON);

            // 以降の移動量は新しい基準からの差分（累積 delta が残っていれば 216° になる）
            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.6f, 0f));

            Assert.AreEqual(180.0, input.Hsva.H, 1e-3);
            Assert.AreEqual(0.6, input.Hsva.S, EPSILON);
            Assert.AreEqual(0.5, input.Hsva.V, EPSILON);
        }

        [Test]
        public void Scrub_ModeSwitchWhileIdleOnlyRemembersTheMode()
        {
            ColorInput input = Create(Color.red);

            input.SetScrubMode(ColorTweakMode.Blue);

            Assert.AreEqual(ColorTweakMode.Blue, input.ScrubMode);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Scrub_ConfirmsExactlyOncePerGesture()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Pad, 0.0, 0.5, 0.5, 1.0);

            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.UpdateChannelScrub(new Vector2(-12f, 0f));
            input.UpdateChannelScrub(new Vector2(-24f, 0f));
            input.UpdateChannelScrub(new Vector2(-36f, 0f));
            input.EndChannelScrub();
            input.EndChannelScrub();

            Assert.AreEqual(3, changed);
            Assert.AreEqual(1, confirmed);
            Assert.IsFalse(input.IsScrubbing);
        }

        [Test]
        public void Scrub_CancelRestoresTheStartValueWithoutConfirming()
        {
            ColorInput input = Create(Color.red);
            input.SetScrubMode(ColorTweakMode.Pad);

            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginChannelScrub(Vector2.zero);
            input.UpdateChannelScrub(new Vector2(-TWEAK_WIDTH * 0.5f, TWEAK_WIDTH * 0.5f));
            Assert.AreNotEqual(Color.red, input.value);

            input.CancelChannelScrub();

            Assert.AreEqual(0, confirmed);
            Assert.IsFalse(input.IsScrubbing);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Scrub_CancelWithoutBeginIsIgnored()
        {
            ColorInput input = Create(Color.red);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            Assert.DoesNotThrow(() => input.CancelChannelScrub());

            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Scrub_BeginClosesTheOpenPicker()
        {
            ColorInput input = Create(Color.red);
            input.OpenPicker();

            input.BeginChannelScrub(Vector2.zero);

            Assert.IsFalse(input.IsPickerOpen);
            Assert.IsTrue(input.IsScrubbing);
        }

        [Test]
        public void Scrub_UpdateWithoutBeginIsIgnored()
        {
            ColorInput input = Create(Color.red);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.UpdateChannelScrub(new Vector2(100f, 100f));

            Assert.AreEqual(0, changed);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Scrub_EndWithoutBeginDoesNotConfirm()
        {
            ColorInput input = Create(Color.red);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            Assert.DoesNotThrow(() => input.EndChannelScrub());

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Scrub_WhileDisabledDoesNotStart()
        {
            ColorInput input = Create(Color.red);
            input.Disabled = true;

            input.BeginChannelScrub(Vector2.zero);
            input.UpdateChannelScrub(new Vector2(100f, 0f));

            Assert.IsFalse(input.IsScrubbing);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Scrub_DisablingWhileScrubbingRollsBack()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Pad, 0.0, 1.0, 1.0, 1.0);
            Color start = input.value;

            input.UpdateChannelScrub(new Vector2(-TWEAK_WIDTH * 0.5f, 0f));
            input.Disabled = true;

            Assert.IsFalse(input.IsScrubbing);
            AssertColor(start, input.value);
        }

        [Test]
        public void Scrub_BeginTwiceKeepsTheFirstBasis()
        {
            ColorInput input = CreateScrubbing(ColorTweakMode.Pad, 0.0, 0.5, 0.5, 1.0);

            // 2 度目の Begin で基準が動くと、掴み直しでも無いのに値が飛ぶ
            input.BeginChannelScrub(new Vector2(100f, 100f));
            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.1f, 0f));

            Assert.AreEqual(0.6, input.Hsva.S, EPSILON);
        }

        #endregion

        #region HEX

        [Test]
        public void Hex_OpaqueColorUsesSixDigits()
        {
            ColorInput input = Create(Color.red);

            // "#" + 6 桁。α=1 のときは 8 桁にしない（FormatHex の契約）
            Assert.AreEqual(7, input.HexText.Length);
            StringAssert.StartsWith("#", input.HexText);
        }

        [Test]
        public void Hex_TranslucentColorUsesEightDigits()
        {
            ColorInput input = Create(new Color(1f, 0f, 0f, 0.5f));

            Assert.AreEqual(9, input.HexText.Length);
            StringAssert.StartsWith("#", input.HexText);
        }

        [Test]
        public void Hex_TextFollowsTheStructValue()
        {
            ColorInput input = Create(Color.red);
            string before = input.HexText;

            input.value = Color.blue;

            Assert.AreNotEqual(before, input.HexText);
        }

        [Test]
        public void Hex_InputUpdatesTheStructValue()
        {
            ColorInput input = Create(Color.white);
            List<Color> changed = new List<Color>();
            input.ValueChanged += value => changed.Add(value);

            input.PerformHexInput("#ff0000");

            Assert.AreEqual(1, changed.Count);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Hex_InputDoesNotConfirm()
        {
            ColorInput input = Create(Color.white);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.PerformHexInput("#ff0000");

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Hex_ConfirmRaisesConfirmedAndNormalizesTheText()
        {
            ColorInput input = Create(Color.white);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.PerformHexInput("#F00");
            input.PerformHexConfirm();

            Assert.AreEqual(1, confirmed);
            AssertColor(Color.red, input.value);

            // 確定後の表示は打った文字ではなく FormatHex の正規形
            Assert.AreEqual(7, input.HexText.Length);
        }

        [Test]
        public void Hex_InvalidInputKeepsTheValue()
        {
            ColorInput input = Create(Color.red);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.PerformHexInput("not a color");
            input.PerformHexInput(null);

            Assert.AreEqual(0, changed);
            AssertColor(Color.red, input.value);
        }

        [Test]
        public void Hex_ValidatorMatchesTheParser()
        {
            Assert.IsTrue(ColorInput.IsValidHex("#ff0000"));
            Assert.IsFalse(ColorInput.IsValidHex("zzz"));
        }

        #endregion

        #region HSVA state

        [Test]
        public void Hsva_HueSurvivesRoundTripThroughBlack()
        {
            ColorInput input = Create(Color.white);
            input.SetHsva(200.0, 1.0, 1.0, 1.0);

            // 黒は hue も彩度も定義できない。Vue の NaN 埋めと同じく直前の値を引き継ぐ
            input.value = Color.black;

            Assert.AreEqual(200.0, input.Hsva.H, 1e-3);
            Assert.AreEqual(0.0, input.Hsva.V, EPSILON);
        }

        [Test]
        public void Hsva_HueSurvivesRoundTripThroughGray()
        {
            ColorInput input = Create(Color.white);
            input.SetHsva(120.0, 1.0, 1.0, 1.0);

            input.value = new Color(0.5f, 0.5f, 0.5f, 1f);

            Assert.AreEqual(120.0, input.Hsva.H, 1e-3);
            Assert.AreEqual(0.0, input.Hsva.S, EPSILON);
        }

        [Test]
        public void Hsva_SetHsvaWrapsHueAndClampsTheRest()
        {
            ColorInput input = Create(Color.white);

            input.SetHsva(-90.0, 2.0, -1.0, 5.0);

            Assert.AreEqual(270.0, input.Hsva.H, 1e-3);
            Assert.AreEqual(1.0, input.Hsva.S, EPSILON);
            Assert.AreEqual(0.0, input.Hsva.V, EPSILON);
            Assert.AreEqual(1.0, input.Hsva.A, EPSILON);
        }

        [Test]
        public void Hsva_SetHsvaDoesNotConfirm()
        {
            ColorInput input = Create(Color.white);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.SetHsva(10.0, 1.0, 1.0, 1.0);

            Assert.AreEqual(0, confirmed);
        }

        #endregion

        #region Picker state

        [Test]
        public void Picker_OpenTogglesLogicalStateWithoutPanel()
        {
            ColorInput input = Create(Color.red);

            Assert.DoesNotThrow(() => input.OpenPicker());

            Assert.IsTrue(input.IsPickerOpen);
        }

        [Test]
        public void Picker_CloseIsIdempotent()
        {
            ColorInput input = Create(Color.red);

            input.OpenPicker();
            input.ClosePicker();
            input.ClosePicker();

            Assert.IsFalse(input.IsPickerOpen);
        }

        [Test]
        public void Picker_ToggleFlipsTheState()
        {
            ColorInput input = Create(Color.red);

            input.TogglePicker();
            Assert.IsTrue(input.IsPickerOpen);

            input.TogglePicker();
            Assert.IsFalse(input.IsPickerOpen);
        }

        [Test]
        public void Picker_OpenWhileDisabledIsIgnored()
        {
            ColorInput input = Create(Color.red);
            input.Disabled = true;

            input.OpenPicker();

            Assert.IsFalse(input.IsPickerOpen);
        }

        [Test]
        public void Picker_DisablingWhileOpenCloses()
        {
            ColorInput input = Create(Color.red);
            input.OpenPicker();

            input.Disabled = true;

            Assert.IsFalse(input.IsPickerOpen);
        }

        [Test]
        public void Picker_ClosingDoesNotRollBackTheColor()
        {
            ColorInput input = Create(Color.red);

            input.OpenPicker();
            input.SetHsva(120.0, 1.0, 1.0, 1.0);
            input.ClosePicker();

            AssertColor(Color.green, input.value);
        }

        [Test]
        public void ColorSpace_DefaultsToHsvAndRejectsUnknownNames()
        {
            ColorInput input = Create(Color.red);

            Assert.AreEqual(ColorInput.COLOR_SPACE_HSV, input.ColorSpace);

            input.ColorSpace = ColorInput.COLOR_SPACE_RGB;
            Assert.AreEqual(ColorInput.COLOR_SPACE_RGB, input.ColorSpace);

            input.ColorSpace = "cmyk";
            Assert.AreEqual(ColorInput.COLOR_SPACE_HSV, input.ColorSpace);
        }

        #endregion

        #region Value

        [Test]
        public void Value_SetterNotifiesOnce()
        {
            ColorInput input = Create(Color.red);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = Color.blue;
            input.value = Color.blue;

            Assert.AreEqual(1, changed);
        }

        [Test]
        public void Value_SetterDoesNotConfirm()
        {
            ColorInput input = Create(Color.red);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.value = Color.blue;

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Value_SetValueWithoutNotifyIsSilent()
        {
            ColorInput input = Create(Color.red);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.SetValueWithoutNotify(Color.blue);

            Assert.AreEqual(0, changed);
            AssertColor(Color.blue, input.value);
        }

        [Test]
        public void Value_AlphaIsPreserved()
        {
            ColorInput input = Create(new Color(0.2f, 0.4f, 0.6f, 0.3f));

            AssertColor(new Color(0.2f, 0.4f, 0.6f, 0.3f), input.value);
            Assert.AreEqual(0.3, input.Hsva.A, EPSILON);
        }

        #endregion

        #region Box

        [Test]
        public void Box_ImplementsInputBox()
        {
            Assert.IsTrue(new ColorInput() is ITweeqInputBox);
        }

        [Test]
        public void Box_SwatchIsSquareAtInputHeight()
        {
            ColorInput input = Create(Color.red);
            VisualElement swatch = Swatch(input);

            Assert.IsNotNull(swatch);
            Assert.AreEqual(input.Theme.InputHeight, swatch.style.width.value.value);
            Assert.AreEqual(input.Theme.InputHeight, swatch.style.height.value.value);
        }

        [Test]
        public void Box_StandaloneKeepsEveryCorner()
        {
            ColorInput input = Create(Color.red);
            VisualElement swatch = Swatch(input);
            float radius = input.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(swatch.style.borderTopLeftRadius));
            Assert.AreEqual(radius, Radius(swatch.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(swatch.style.borderBottomLeftRadius));
            Assert.AreEqual(radius, Radius(swatch.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_InlineStartFlattensTrailingCorners()
        {
            ColorInput input = Create(Color.red);
            input.InlinePosition = TweeqBoxPosition.Start;

            VisualElement swatch = Swatch(input);
            float radius = input.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(swatch.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(swatch.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(swatch.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(swatch.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_BlockMiddleFlattensEveryCorner()
        {
            ColorInput input = Create(Color.red);
            input.BlockPosition = TweeqBoxPosition.Middle;

            VisualElement swatch = Swatch(input);

            Assert.AreEqual(0f, Radius(swatch.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(swatch.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(swatch.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(swatch.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_DisabledBlocksPicking()
        {
            ColorInput input = Create(Color.red);

            input.Disabled = true;
            Assert.AreEqual(PickingMode.Ignore, input.pickingMode);

            input.Disabled = false;
            Assert.AreEqual(PickingMode.Position, input.pickingMode);
        }

        [Test]
        public void Box_IsFocusable()
        {
            Assert.IsTrue(Create(Color.red).focusable);
        }

        [Test]
        public void Box_ThemeNullFallsBackToDark()
        {
            ColorInput input = Create(Color.red);

            input.Theme = null;

            Assert.IsNotNull(input.Theme);
            Assert.AreEqual(ColorMode.Dark, input.Theme.Mode);
        }

        [Test]
        public void Box_ThemeCarriesThePopupWidthToken()
        {
            Assert.AreEqual(240f, TweeqTheme.Dark().PopupWidth);
            Assert.AreEqual(240f, TweeqTheme.Light().PopupWidth);
        }

        #endregion
    }
}
