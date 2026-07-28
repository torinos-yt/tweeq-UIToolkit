using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies the logical layer of ColorInput (the ColorInput item in string-color-spec.md's
    /// "test contract").
    ///
    /// ColorInput holds opening/closing, the drag session, presets, and HEX sync as a
    /// panel-independent imperative API, with only the popover and rendering layered on top of
    /// it. So "how many times events fire, and in what order" is fully covered here. The
    /// following need a panel and rendering, so they're covered on the Play Mode side:
    /// - The picker opening on swatch press / closing on an outside click
    /// - Hit testing and cursor position for the SV pad, Hue bar, and Alpha bar
    /// - The SV gradient only being redrawn when hue changes
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
            // The original has a bug where confirm doesn't fire. This adopts the contract from a fixed reference implementation plus test-contracts
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

            // The value doesn't move so ValueChanged doesn't fire, but it's still a confirm operation
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

            // ValueChanged fires on every pointermove (no throttling, matching the Vue original) / Confirmed fires once on end
            Assert.AreEqual(3, changed);
            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Drag_SvMapsPositionToSaturationAndValue()
        {
            ColorInput input = Create(Color.red);

            input.BeginPickerDrag(ColorPickerAxis.SaturationValue);

            // Top-left = saturation 0 / value 1 (white)
            input.UpdatePickerDrag(0f, 0f);
            AssertColor(Color.white, input.value);

            // Bottom edge = value 0 (black)
            input.UpdatePickerDrag(1f, 1f);
            AssertColor(Color.black, input.value);

            // Top-right = saturation 1 / value 1 (pure color)
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

        // The sensitivity basis is tweakWidth = PopupWidth = 240 (spec section A).
        // Moving 240px takes pad / single-channel from 0 to 1, and hue makes one full turn
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

            // dx = Δx/240, dy = -Δy/240 (upward is positive)
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

            // 270 + 180 = 450 -> 90
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
            // The display is 0-255 but internally it's 0-1. 240px = the full channel range
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

            // Right after switching, "the current value becomes the new basis", so staying at the same position doesn't move it a single bit
            input.SetScrubMode(ColorTweakMode.Hue);
            input.UpdateChannelScrub(new Vector2(TWEAK_WIDTH * 0.1f, 0f));

            AssertColor(afterPad, input.value);
            Assert.AreEqual(0.0, input.Hsva.H, EPSILON);

            // Subsequent movement is the delta from the new basis (it would be 216° if the accumulated delta had carried over)
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

            // If the basis moved on the second Begin, the value would jump even though it wasn't re-grabbed
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

            // "#" + 6 digits. When alpha=1, it doesn't become 8 digits (FormatHex's contract)
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

            // The display after confirming is FormatHex's canonical form, not the typed text
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

            // Black can't define hue or saturation. Like the Vue original's NaN-filling, this carries over the previous value
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

        #region SV texture

        // The SV gradient is one texture shared by the picker pad and the scrub overlay, and the
        // picker's elements are only built the first time it opens. So the pad can come into
        // existence after the texture was already baked - these pin that the pad still ends up
        // pointing at a live texture, and that it lets go of one that has been destroyed.

        TweeqRuntimeTestPanel _panel;
        ColorInput _panelInput;

        [TearDown]
        public void TearDown()
        {
            if (_panelInput != null)
            {
                _panelInput.CancelChannelScrub();
                _panelInput.ClosePicker();
                _panelInput.RemoveFromHierarchy();
                _panelInput = null;
            }

            _panel?.Dispose();
            _panel = null;
        }

        ColorInput ArrangeOnPanel(Color initial)
        {
            _panel = TweeqRuntimeTestPanel.Create();

            _panelInput = Create(initial);
            _panel.Root.Add(_panelInput);

            return _panelInput;
        }

        // The picker is mounted on the popover in the overlay layer, not under the field itself
        VisualElement SvPadInPanel()
        {
            IPanel panel = _panel != null && _panel.Root != null ? _panel.Root.panel : null;
            return panel != null ? panel.visualTree.Q("tweeq-color-sv-pad") : null;
        }

        static Texture2D PadTexture(VisualElement pad)
        {
            return pad.style.backgroundImage.value.texture;
        }

        [Test]
        public void SvTexture_PadIsPaintedWhenTheFirstBakeCameFromAScrub()
        {
            ColorInput input = ArrangeOnPanel(Color.red);

            // A swatch drag scrubs in Pad mode, which bakes the gradient while the picker has never
            // been opened. The hue is unchanged from there on, so the pad must not depend on a
            // later re-bake to receive its background.
            input.BeginChannelScrub(new Vector2(40f, 40f));
            input.UpdateChannelScrub(new Vector2(48f, 44f));
            input.EndChannelScrub();

            input.OpenPicker();

            VisualElement pad = SvPadInPanel();
            Assert.IsNotNull(pad, "the SV pad is built on the first open");
            Assert.IsTrue(PadTexture(pad) != null, "the SV pad must hold a live gradient texture");
        }

        [Test]
        public void SvTexture_PadKeepsTheSameInstanceWhileHueIsUnchanged()
        {
            ColorInput input = ArrangeOnPanel(Color.red);

            input.OpenPicker();
            Texture2D first = PadTexture(SvPadInPanel());

            input.ClosePicker();
            input.OpenPicker();

            Assert.IsTrue(first != null);
            Assert.AreSame(first, PadTexture(SvPadInPanel()), "re-opening must not re-allocate");
        }

        [Test]
        public void SvTexture_DetachReleasesThePadBackground()
        {
            ColorInput input = ArrangeOnPanel(Color.red);

            input.OpenPicker();
            VisualElement pad = SvPadInPanel();
            Assert.IsTrue(PadTexture(pad) != null);

            // Detaching destroys the texture. The pad lives on the popover and survives, so a
            // reference left behind here would be a destroyed texture bound to a visible element.
            input.RemoveFromHierarchy();

            Assert.IsTrue(
                ReferenceEquals(PadTexture(pad), null),
                "the destroyed texture must not stay bound to the pad");
        }

        [Test]
        public void SvTexture_ReattachRepaintsThePadWithALiveTexture()
        {
            ColorInput input = ArrangeOnPanel(Color.red);

            input.OpenPicker();
            input.RemoveFromHierarchy();

            _panel.Root.Add(input);
            input.OpenPicker();

            Assert.IsTrue(PadTexture(SvPadInPanel()) != null);
        }

        #endregion
    }
}
