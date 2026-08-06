using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Pins the TimeInput contract (the UIToolkit items of m10-time-spec.md).
    ///
    /// The arithmetic itself is covered by Tweeq.Core (TimecodeLogic / TweeqExpression), so these
    /// tests only fix which pure function the widget calls for a given input, and against which
    /// reference value.
    /// </summary>
    public class TimeInputTests
    {
        const double FPS = 24.0;

        TweeqRuntimeTestPanel _panel;
        TimeInput _input;
        List<float> _confirmed;
        List<float> _changed;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
            _input = null;
        }

        #region Arrange

        // Every case runs on a panel: keyboard events are routed to the focused element, so
        // without one they would never reach the widget
        TimeInput Arrange(TimeDisplayMode mode, float initial)
        {
            _panel = TweeqRuntimeTestPanel.Create();

            _input = new TimeInput
            {
                FrameRate = FPS,
                DisplayMode = mode,
            };
            _input.SetValueWithoutNotify(initial);

            _confirmed = new List<float>();
            _changed = new List<float>();
            _input.Confirmed += value => _confirmed.Add(value);
            _input.RegisterValueChangedCallback(evt => _changed.Add(evt.newValue));

            _panel.Root.Add(_input);

            // An EditMode panel has no "element under the pointer", so PointerDown only arrives
            // through a capture
            _input.CapturePointer(PointerId.mousePointerId);
            _input.Focus();

            return _input;
        }

        #endregion

        #region Event helpers

        static void SendPointer(VisualElement element, EventType type, Vector2 position)
        {
            Event systemEvent = new Event
            {
                type = type,
                mousePosition = position,
                button = 0,
                modifiers = EventModifiers.None,
            };

            if (type == EventType.MouseDown)
            {
                using (PointerDownEvent down = PointerDownEvent.GetPooled(systemEvent))
                {
                    element.SendEvent(down);
                }

                return;
            }

            if (type == EventType.MouseUp)
            {
                using (PointerUpEvent up = PointerUpEvent.GetPooled(systemEvent))
                {
                    element.SendEvent(up);
                }

                return;
            }

            using (PointerMoveEvent move = PointerMoveEvent.GetPooled(systemEvent))
            {
                element.SendEvent(move);
            }
        }

        void KeyDown(KeyCode keyCode)
        {
            KeyDown(keyCode, EventModifiers.None);
        }

        void KeyDown(KeyCode keyCode, EventModifiers modifiers)
        {
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', keyCode, modifiers))
            {
                _input.SendEvent(evt);
            }
        }

        void KeyUp(KeyCode keyCode)
        {
            using (KeyUpEvent evt = KeyUpEvent.GetPooled('\0', keyCode, EventModifiers.None))
            {
                _input.SendEvent(evt);
            }
        }

        // PointerEnterEvent cannot be synthesized here: its PreDispatch reads the panel pointer
        // state that only a real mouse builds up. The hover therefore goes through the same entry
        // point the digit callback calls, and the element that carries that callback is checked
        // for the properties it needs to receive one
        void HoverDigit(int scale)
        {
            VisualElement digit = _input.Q<VisualElement>(TimeInput.DIGIT_NAME_PREFIX + scale);
            Assert.IsNotNull(digit, "digit group element not found for scale " + scale);
            Assert.AreEqual(PickingMode.Position, digit.pickingMode);
            Assert.AreEqual(DisplayStyle.Flex, digit.style.display.value);

            _input.PerformDigitHover(scale);
        }

        #endregion

        #region Defaults

        [Test]
        public void Defaults_MatchTheSpec()
        {
            TimeInput input = new TimeInput();

            Assert.AreEqual(24.0, input.FrameRate);
            Assert.AreEqual(TimeDisplayMode.Frames, input.DisplayMode);
            Assert.AreEqual(double.NegativeInfinity, input.Min);
            Assert.AreEqual(double.PositiveInfinity, input.Max);
            Assert.AreEqual(0f, input.value);
            Assert.AreEqual("0F", input.DisplayText);
            Assert.IsFalse(input.IsEditing);
            Assert.IsFalse(input.IsScrubbing);
        }

        [Test]
        public void FramesMode_PrintsTheFrameCountWithSuffix()
        {
            Arrange(TimeDisplayMode.Frames, 2172f);

            Assert.AreEqual("2172F", _input.DisplayText);

            // The whole field is one group, which is the frames scale
            Assert.AreEqual(1, _input.DigitCount);
        }

        [Test]
        public void IdleSameValueReadback_KeepsTheExistingDisplayString()
        {
            Arrange(TimeDisplayMode.Frames, 2172f);
            string initialDisplay = _input.DisplayText;

            _input.SetValueWithoutNotify(2172f);

            Assert.AreSame(initialDisplay, _input.DisplayText);
            Assert.AreEqual("2172F", _input.GetDigitText(0));
        }

        [Test]
        public void FramesIdleReadback_UpdatesTheGlyphWithoutChangingThePublicDisplay()
        {
            Arrange(TimeDisplayMode.Frames, 2172f);
            Label group = _input.Q<Label>(TimeInput.DIGIT_NAME_PREFIX + "0");
            Label firstDigit = group.Q<Label>(TimeInput.FRAME_GLYPH_DIGIT_PREFIX + "6");
            Label secondDigit = group.Q<Label>(TimeInput.FRAME_GLYPH_DIGIT_PREFIX + "7");
            Label thirdDigit = group.Q<Label>(TimeInput.FRAME_GLYPH_DIGIT_PREFIX + "8");
            Label lastDigit = group.Q<Label>(TimeInput.FRAME_GLYPH_DIGIT_PREFIX + "9");
            Label suffix = group.Q<Label>(TimeInput.FRAME_SUFFIX_GLYPH_NAME);

            _input.SetValueWithoutNotify(2173f);

            Assert.AreEqual("2173F", _input.DisplayText);
            Assert.AreEqual("2173F", _input.GetDigitText(0));
            Assert.AreEqual(string.Empty, group.text);
            Assert.AreEqual("2", firstDigit.text);
            Assert.AreEqual("1", secondDigit.text);
            Assert.AreEqual("7", thirdDigit.text);
            Assert.AreEqual("3", lastDigit.text);
            Assert.AreEqual("F", suffix.text);
            Assert.AreEqual(DisplayStyle.Flex, suffix.style.display.value);
        }

        [Test]
        public void TimecodeMode_SplitsIntoDigitGroupsFromTheFramesSide()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);

            Assert.AreEqual("01:30:12", _input.DisplayText);
            Assert.AreEqual(3, _input.DigitCount);
            Assert.AreEqual("12", _input.GetDigitText(0));
            Assert.AreEqual("30", _input.GetDigitText(1));
            Assert.AreEqual("01", _input.GetDigitText(2));
        }

        [Test]
        public void TimecodeMode_AddsTheHourGroupOnlyPastOneHour()
        {
            Arrange(TimeDisplayMode.Timecode, 88572f);

            Assert.AreEqual("1:01:30:12", _input.DisplayText);
            Assert.AreEqual(4, _input.DigitCount);
            Assert.AreEqual("1", _input.GetDigitText(3));
        }

        #endregion

        #region Frame rate

        [Test]
        public void FrameRateChange_KeepsTheValueAndReformatsTheDisplay()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);
            Assert.AreEqual("01:30:12", _input.DisplayText);

            _input.FrameRate = 60.0;

            Assert.AreEqual(2172f, _input.value);
            Assert.AreEqual("00:36:12", _input.DisplayText);
        }

        [Test]
        public void FrameRateChange_IsIgnoredWhenNotPositive()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);

            _input.FrameRate = 0.0;
            _input.FrameRate = -30.0;
            _input.FrameRate = double.NaN;

            Assert.AreEqual(FPS, _input.FrameRate);
        }

        [Test]
        public void FrameRateChange_ScalesTheScrubSpeed()
        {
            Arrange(TimeDisplayMode.Timecode, 0f);
            _input.PerformDigitHover(1);

            _input.FrameRate = 60.0;
            _input.PerformScrubBegin();

            // seconds runs at fps/10 frames per pixel, so at 60fps 10px is one second
            _input.PerformScrubDelta(10f, false, false);

            Assert.AreEqual(60f, _input.value);
        }

        #endregion

        #region Tweak scale

        [Test]
        public void DigitHover_BecomesTheTweakScale()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);

            HoverDigit(1);
            Assert.AreEqual(1, _input.TweakScale);

            HoverDigit(2);
            Assert.AreEqual(2, _input.TweakScale);

            HoverDigit(0);
            Assert.AreEqual(0, _input.TweakScale);
        }

        [Test]
        public void ShiftAndAlt_OffsetTheHoverScaleWhileHeld()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);
            _input.PerformDigitHover(1);

            KeyDown(KeyCode.None, EventModifiers.Shift);
            Assert.AreEqual(2, _input.TweakScale);

            KeyDown(KeyCode.None, EventModifiers.Alt);
            Assert.AreEqual(0, _input.TweakScale);

            KeyDown(KeyCode.None, EventModifiers.None);
            Assert.AreEqual(1, _input.TweakScale);
        }

        [Test]
        public void ModifierOffset_IsClampedToTheScaleRange()
        {
            Arrange(TimeDisplayMode.Timecode, 88572f);

            _input.PerformDigitHover(3);
            KeyDown(KeyCode.None, EventModifiers.Shift);
            Assert.AreEqual(3, _input.TweakScale);

            _input.PerformDigitHover(0);
            KeyDown(KeyCode.None, EventModifiers.Alt);
            Assert.AreEqual(0, _input.TweakScale);
        }

        [Test]
        public void ScaleKeys_OverrideHoverWhileHeld()
        {
            Arrange(TimeDisplayMode.Timecode, 88572f);
            _input.PerformDigitHover(1);

            KeyDown(KeyCode.H);
            Assert.AreEqual(3, _input.TweakScale);

            KeyUp(KeyCode.H);
            Assert.AreEqual(1, _input.TweakScale);

            KeyDown(KeyCode.M);
            Assert.AreEqual(2, _input.TweakScale);
            KeyUp(KeyCode.M);

            KeyDown(KeyCode.S);
            Assert.AreEqual(1, _input.TweakScale);
            KeyUp(KeyCode.S);

            _input.PerformDigitHover(2);
            KeyDown(KeyCode.F);
            Assert.AreEqual(0, _input.TweakScale);
            KeyUp(KeyCode.F);
            Assert.AreEqual(2, _input.TweakScale);
        }

        [Test]
        public void ScaleKeys_BeatTheModifierOffset()
        {
            Arrange(TimeDisplayMode.Timecode, 88572f);
            _input.PerformDigitHover(0);

            KeyDown(KeyCode.S, EventModifiers.Shift);

            // The +1 from Shift only applies to the hover branch
            Assert.AreEqual(1, _input.TweakScale);
        }

        [Test]
        public void HoverIsFrozenWhileScrubbing()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);
            _input.PerformDigitHover(2);
            _input.PerformScrubBegin();

            _input.PerformDigitHover(0);

            Assert.AreEqual(2, _input.TweakScale);
        }

        #endregion

        #region Scrub

        [Test]
        public void Scrub_AccumulatesFromTheValueAtDragStart()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformScrubBegin();

            // frames runs at 1/4 frame per pixel: 2px never reaches a whole frame on its own,
            // but the raw value keeps the remainder so the third sample crosses 1.5 frames
            _input.PerformScrubDelta(2f, false, false);
            Assert.AreEqual(101f, _input.value);

            _input.PerformScrubDelta(2f, false, false);
            Assert.AreEqual(101f, _input.value);

            _input.PerformScrubDelta(2f, false, false);
            Assert.AreEqual(102f, _input.value);
        }

        [Test]
        public void Scrub_UsesTheScaleSpeedOfTheActiveScale()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformDigitHover(1);
            _input.PerformScrubBegin();

            // seconds runs at fps/10 = 2.4 frames per pixel
            _input.PerformScrubDelta(10f, false, false);

            Assert.AreEqual(124f, _input.value);
        }

        [Test]
        public void Scrub_ModifiersShiftTheScaleMidDrag()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformScrubBegin();

            // The hover stays on frames, but Shift borrows the seconds speed while held
            _input.PerformScrubDelta(10f, true, false);

            Assert.AreEqual(124f, _input.value);
        }

        [Test]
        public void Scrub_QuantizesTheOutputToWholeFrames()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformScrubBegin();

            _input.PerformScrubDelta(3f, false, false);

            // 100 + 0.75 rounds to 101 the way JS Math.round does (towards +infinity)
            Assert.AreEqual(101f, _input.value);
        }

        [Test]
        public void Scrub_ClampsTheRawValueSoTheReturnIsImmediate()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.Min = 0.0;
            _input.Max = 200.0;
            _input.PerformScrubBegin();

            _input.PerformScrubDelta(2000f, false, false);
            Assert.AreEqual(200f, _input.value);

            // The accumulator was folded too, so 10px back already moves the value
            _input.PerformScrubDelta(-10f, false, false);
            Assert.AreEqual(198f, _input.value);
        }

        [Test]
        public void ScrubEnd_ConfirmsOnce()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformScrubBegin();
            _input.PerformScrubDelta(40f, false, false);
            _input.PerformScrubEnd();

            Assert.AreEqual(new[] { 110f }, _confirmed.ToArray());
            Assert.IsFalse(_input.IsScrubbing);
        }

        [Test]
        public void ScrubCancel_RestoresTheValueAtDragStartWithoutConfirming()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformScrubBegin();
            _input.PerformScrubDelta(40f, false, false);
            _input.PerformScrubCancel();

            Assert.AreEqual(100f, _input.value);
            Assert.IsEmpty(_confirmed);
        }

        #endregion

        #region Snap

        [Test]
        public void SnapKey_KeepsTheRemainderInsideTheUnit()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformDigitHover(1);
            _input.PerformScrubBegin();
            KeyDown(KeyCode.Q);

            // 100 + 15*2.4 = 136 lands on 148 (= 6*24 + 4), keeping the drag-start remainder of 4
            _input.PerformScrubDelta(15f, false, false);

            Assert.AreEqual(148f, _input.value);
            Assert.AreEqual(4f, _input.value % 24f);
        }

        [Test]
        public void SnapKey_HoldsTheValueUntilTheNextUnitBoundary()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformDigitHover(1);
            _input.PerformScrubBegin();
            KeyDown(KeyCode.Q);

            // 100 + 3*2.4 = 107.2 is under half a unit, so the value stays put
            _input.PerformScrubDelta(3f, false, false);

            Assert.AreEqual(100f, _input.value);
        }

        [Test]
        public void WithoutSnapKey_TheSameDragMovesByFrames()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformDigitHover(1);
            _input.PerformScrubBegin();

            _input.PerformScrubDelta(3f, false, false);

            Assert.AreEqual(107f, _input.value);
        }

        [Test]
        public void SnapKey_AtFramesScale_IsPlainFrameQuantization()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformScrubBegin();
            KeyDown(KeyCode.Q);

            _input.PerformScrubDelta(10f, false, false);

            Assert.AreEqual(103f, _input.value);
        }

        #endregion

        #region Arrow keys

        [Test]
        public void ArrowKeys_StepBySecondFrameAndMinute()
        {
            Arrange(TimeDisplayMode.Timecode, 1000f);

            KeyDown(KeyCode.UpArrow);
            Assert.AreEqual(1024f, _input.value);

            KeyDown(KeyCode.DownArrow);
            Assert.AreEqual(1000f, _input.value);

            KeyDown(KeyCode.UpArrow, EventModifiers.Alt);
            Assert.AreEqual(1001f, _input.value);

            KeyDown(KeyCode.DownArrow, EventModifiers.Alt);
            Assert.AreEqual(1000f, _input.value);

            KeyDown(KeyCode.UpArrow, EventModifiers.Shift);
            Assert.AreEqual(2440f, _input.value);

            KeyDown(KeyCode.DownArrow, EventModifiers.Shift);
            Assert.AreEqual(1000f, _input.value);
        }

        [Test]
        public void ArrowKeys_ClampAndConfirm()
        {
            Arrange(TimeDisplayMode.Timecode, 1000f);
            _input.Max = 1010.0;

            KeyDown(KeyCode.UpArrow);

            Assert.AreEqual(1010f, _input.value);
            Assert.AreEqual(new[] { 1010f }, _confirmed.ToArray());
        }

        [Test]
        public void ArrowKeys_UpdateTheDisplay()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);

            KeyDown(KeyCode.UpArrow);

            Assert.AreEqual("01:31:12", _input.DisplayText);
        }

        #endregion

        #region Text editing

        [Test]
        public void Enter_EvaluatesTimecodeExpressions()
        {
            Arrange(TimeDisplayMode.Timecode, 0f);

            _input.BeginEditing();
            _input.SetEditingText("1:00 + 10f");
            _input.CommitEditing();

            Assert.AreEqual(34f, _input.value);
            Assert.AreEqual("00:01:10", _input.DisplayText);
        }

        [Test]
        public void Enter_AcceptsUnitSuffixes()
        {
            Arrange(TimeDisplayMode.Timecode, 0f);

            _input.BeginEditing();
            _input.SetEditingText("10s");
            _input.CommitEditing();

            Assert.AreEqual(240f, _input.value);
        }

        [Test]
        public void Enter_AcceptsPlainArithmetic()
        {
            Arrange(TimeDisplayMode.Frames, 0f);

            _input.BeginEditing();
            _input.SetEditingText("(2 + 3) * 4");
            _input.CommitEditing();

            Assert.AreEqual(20f, _input.value);
        }

        [Test]
        public void Enter_RestoresTheValueAtEditStartWhenTheExpressionFails()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);

            _input.BeginEditing();
            _input.SetEditingText("not a time");
            _input.CommitEditing();

            Assert.AreEqual(100f, _input.value);
            Assert.AreEqual("00:04:04", _input.DisplayText);
        }

        [Test]
        public void Enter_ClampsToTheRange()
        {
            Arrange(TimeDisplayMode.Frames, 0f);
            _input.Min = 0.0;
            _input.Max = 50.0;

            _input.BeginEditing();
            _input.SetEditingText("500");
            _input.CommitEditing();

            Assert.AreEqual(50f, _input.value);
        }

        [Test]
        public void Confirmed_FiresOncePerEditingSession()
        {
            Arrange(TimeDisplayMode.Frames, 0f);

            _input.BeginEditing();
            _input.SetEditingText("200");
            _input.CommitEditing();
            _input.EndEditing();

            Assert.AreEqual(new[] { 200f }, _confirmed.ToArray());
            Assert.IsFalse(_input.IsEditing);
        }

        [Test]
        public void Blur_ConfirmsWhenEnterWasNotPressed()
        {
            Arrange(TimeDisplayMode.Frames, 0f);

            _input.BeginEditing();
            _input.SetEditingText("200");
            _input.EndEditing();

            Assert.AreEqual(new[] { 200f }, _confirmed.ToArray());
        }

        [Test]
        public void Escape_RestoresTheValueAtEditStartWithoutConfirming()
        {
            Arrange(TimeDisplayMode.Frames, 100f);

            _input.BeginEditing();
            _input.SetEditingText("200");
            _input.CancelEditing();

            Assert.AreEqual(100f, _input.value);
            Assert.AreEqual("100F", _input.DisplayText);
            Assert.IsFalse(_input.IsEditing);
            Assert.IsEmpty(_confirmed);
        }

        [Test]
        public void Editing_HidesTheDigitGroups()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);
            VisualElement digits = _input.Q<VisualElement>("tweeq-time-digits");

            _input.BeginEditing();
            Assert.AreEqual(DisplayStyle.None, digits.style.display.value);

            _input.EndEditing();
            Assert.AreEqual(DisplayStyle.Flex, digits.style.display.value);
        }

        #endregion

        #region Pointer

        [Test]
        public void Drag_ScrubsThroughTheManipulator()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);

            SendPointer(_input, EventType.MouseDown, new Vector2(10f, 10f));
            SendPointer(_input, EventType.MouseDrag, new Vector2(30f, 10f));
            Assert.IsTrue(_input.IsScrubbing);

            // The threshold crossing becomes the origin, so only the next 20px reach the value
            SendPointer(_input, EventType.MouseDrag, new Vector2(50f, 10f));
            Assert.AreEqual(105f, _input.value);
            CollectionAssert.Contains(_changed, 105f);

            SendPointer(_input, EventType.MouseUp, new Vector2(50f, 10f));
            Assert.IsFalse(_input.IsScrubbing);
            Assert.AreEqual(new[] { 105f }, _confirmed.ToArray());
        }

        [Test]
        public void ClickBelowThreshold_EntersTextEditing()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);

            SendPointer(_input, EventType.MouseDown, new Vector2(10f, 10f));
            SendPointer(_input, EventType.MouseUp, new Vector2(11f, 10f));

            Assert.IsTrue(_input.IsEditing);
            Assert.IsFalse(_input.IsScrubbing);
        }

        [Test]
        public void EscapeDuringDrag_RestoresTheValueAtDragStart()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);

            SendPointer(_input, EventType.MouseDown, new Vector2(10f, 10f));
            SendPointer(_input, EventType.MouseDrag, new Vector2(30f, 10f));
            SendPointer(_input, EventType.MouseDrag, new Vector2(90f, 10f));
            Assert.AreEqual(115f, _input.value);

            KeyDown(KeyCode.Escape);

            Assert.AreEqual(100f, _input.value);
            Assert.IsFalse(_input.IsScrubbing);
            Assert.IsEmpty(_confirmed);
        }

        #endregion

        #region Disabled and invalid

        [Test]
        public void Disabled_BlocksEveryInteraction()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.Disabled = true;

            _input.PerformScrubBegin();
            _input.PerformScrubDelta(100f, false, false);
            _input.BeginEditing();
            KeyDown(KeyCode.UpArrow);

            Assert.AreEqual(100f, _input.value);
            Assert.IsFalse(_input.IsScrubbing);
            Assert.IsFalse(_input.IsEditing);
            Assert.IsEmpty(_confirmed);
            Assert.AreEqual(PickingMode.Ignore, _input.pickingMode);
        }

        [Test]
        public void Disabled_MidScrub_DropsTheGestureWithoutConfirming()
        {
            Arrange(TimeDisplayMode.Timecode, 100f);
            _input.PerformScrubBegin();
            _input.PerformScrubDelta(40f, false, false);

            _input.Disabled = true;

            Assert.AreEqual(100f, _input.value);
            Assert.IsFalse(_input.IsScrubbing);
            Assert.IsEmpty(_confirmed);
        }

        [Test]
        public void Invalid_PaintsTheDigitsWithTheErrorColor()
        {
            Arrange(TimeDisplayMode.Timecode, 2172f);
            Label digit = _input.Q<Label>(TimeInput.DIGIT_NAME_PREFIX + "0");

            Assert.AreEqual(_input.Theme.Text, digit.style.color.value);

            _input.Invalid = true;

            Assert.AreEqual(_input.Theme.Error, digit.style.color.value);
        }

        #endregion

        #region Default value

        [Test]
        public void ResetToDefault_RestoresAndConfirms()
        {
            Arrange(TimeDisplayMode.Frames, 500f);
            _input.DefaultValue = 0.0;

            _input.ResetToDefault();

            Assert.AreEqual(0f, _input.value);
            Assert.AreEqual(new[] { 0f }, _confirmed.ToArray());
        }

        #endregion

        #region Interfaces

        [Test]
        public void ImplementsTheSharedContracts()
        {
            TimeInput input = new TimeInput();

            Assert.IsInstanceOf<INotifyValueChanged<float>>(input);
            Assert.IsInstanceOf<ITweeqThemed>(input);
            Assert.IsInstanceOf<ITweeqInputBox>(input);
            Assert.IsInstanceOf<ITweeqConfirmable<float>>(input);
        }

        [Test]
        public void Theme_FallsBackToDarkOnNull()
        {
            TimeInput input = new TimeInput { Theme = null };

            Assert.IsNotNull(input.Theme);
        }

        #endregion
    }
}
