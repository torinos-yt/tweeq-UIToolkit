using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies TweeqScrubManipulator's contract (ext-custom-widgets-spec.md EXT-01-B "Test Contract")
    /// using synthesized events.
    ///
    /// Actual cursor hiding (HideCursorWhileScrubbing=true) touches OS state, so it's the
    /// Play Mode side's responsibility. This only pins down that the default is false.
    /// </summary>
    public class TweeqScrubManipulatorTests
    {
        TweeqRuntimeTestPanel _panel;
        VisualElement _target;
        TweeqScrubManipulator _manipulator;
        List<string> _log;
        List<ScrubUpdate> _updates;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
            _target = null;
            _manipulator = null;
        }

        void Arrange()
        {
            _panel = TweeqRuntimeTestPanel.Create();

            _target = new VisualElement { focusable = true };
            _target.style.width = 200f;
            _target.style.height = 24f;

            _manipulator = new TweeqScrubManipulator();
            _log = new List<string>();
            _updates = new List<ScrubUpdate>();

            _manipulator.ScrubBegan += () => _log.Add("began");
            _manipulator.ScrubUpdated += update =>
            {
                _log.Add("updated");
                _updates.Add(update);
            };
            _manipulator.ScrubEnded += () => _log.Add("ended");
            _manipulator.ScrubCancelled += () => _log.Add("cancelled");
            _manipulator.Clicked += () => _log.Add("clicked");

            _target.AddManipulator(_manipulator);
            _panel.Root.Add(_target);

            // EditMode's panel has no concept of "the element under the pointer", so PointerDown
            // can only reach target via capture. Grab it once, up front, before the press
            _target.CapturePointer(PointerId.mousePointerId);
        }

        #region Event helpers

        static void SendPointer(
            VisualElement element, EventType type, Vector2 position, int button,
            EventModifiers modifiers)
        {
            Event systemEvent = new Event
            {
                type = type,
                mousePosition = position,
                button = button,
                modifiers = modifiers,
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

        void Down(float x, float y)
        {
            SendPointer(_target, EventType.MouseDown, new Vector2(x, y), 0, EventModifiers.None);
        }

        void Move(float x, float y)
        {
            Move(x, y, EventModifiers.None);
        }

        void Move(float x, float y, EventModifiers modifiers)
        {
            SendPointer(_target, EventType.MouseDrag, new Vector2(x, y), 0, modifiers);
        }

        void Up(float x, float y)
        {
            SendPointer(_target, EventType.MouseUp, new Vector2(x, y), 0, EventModifiers.None);
        }

        void Escape()
        {
            using (KeyDownEvent evt =
                   KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                _target.SendEvent(evt);
            }
        }

        void CaptureOut()
        {
            using (PointerCaptureOutEvent evt =
                   PointerCaptureOutEvent.GetPooled(_target, null, PointerId.mousePointerId))
            {
                _target.SendEvent(evt);
            }
        }

        #endregion

        #region Thresholds

        [Test]
        public void Thresholds_MatchTheNumberInputValues()
        {
            Assert.AreEqual(3f, TweeqScrubManipulator.MOUSE_DRAG_THRESHOLD);
            Assert.AreEqual(5f, TweeqScrubManipulator.TOUCH_DRAG_THRESHOLD);
        }

        [Test]
        public void HideCursorWhileScrubbing_DefaultsToFalse()
        {
            Assert.IsFalse(new TweeqScrubManipulator().HideCursorWhileScrubbing);
        }

        [Test]
        public void BelowThreshold_ReleaseIsAClick()
        {
            Arrange();

            Down(10f, 10f);
            Move(12f, 10f);
            Up(12f, 10f);

            Assert.AreEqual(new[] { "clicked" }, _log.ToArray());
            Assert.IsFalse(_manipulator.IsScrubbing);
        }

        [Test]
        public void BeyondThreshold_StartsScrubbingAndCommitsOnRelease()
        {
            Arrange();

            Down(10f, 10f);
            Move(20f, 10f);
            Move(30f, 14f);
            Up(30f, 14f);

            Assert.AreEqual(new[] { "began", "updated", "ended" }, _log.ToArray());
            Assert.IsFalse(_manipulator.IsScrubbing);
        }

        [Test]
        public void ThresholdCrossingMove_DoesNotEmitItsOwnDelta()
        {
            Arrange();

            Down(10f, 10f);
            Move(20f, 10f);

            // The point where it crosses the threshold becomes the origin; the 10px leading up to it isn't applied to the value
            Assert.AreEqual(new[] { "began" }, _log.ToArray());
            Assert.IsTrue(_manipulator.IsScrubbing);
        }

        [Test]
        public void ScrubUpdate_CarriesTheDeltaSincePreviousMove()
        {
            Arrange();

            Down(10f, 10f);
            Move(20f, 10f);
            Move(32f, 17f);

            Assert.AreEqual(1, _updates.Count);
            Assert.AreEqual(12f, _updates[0].DeltaX, 0.001f);
            Assert.AreEqual(7f, _updates[0].DeltaY, 0.001f);
        }

        [Test]
        public void NonPrimaryButton_IsIgnored()
        {
            Arrange();

            SendPointer(_target, EventType.MouseDown, new Vector2(10f, 10f), 1, EventModifiers.None);
            Move(40f, 10f);
            Up(40f, 10f);

            Assert.IsEmpty(_log);
        }

        #endregion

        #region Modifiers

        [Test]
        public void ShiftAndAlt_ArePropagatedToScrubUpdate()
        {
            Arrange();

            Down(10f, 10f);
            Move(20f, 10f);
            Move(30f, 10f, EventModifiers.Shift);
            Move(40f, 10f, EventModifiers.Alt);
            Move(50f, 10f, EventModifiers.Shift | EventModifiers.Alt);

            Assert.AreEqual(3, _updates.Count);

            Assert.IsTrue(_updates[0].Shift);
            Assert.IsFalse(_updates[0].Alt);

            Assert.IsFalse(_updates[1].Shift);
            Assert.IsTrue(_updates[1].Alt);

            Assert.IsTrue(_updates[2].Shift);
            Assert.IsTrue(_updates[2].Alt);
        }

        [Test]
        public void ModifiersHeldOnPress_SurviveUntilTheFirstMove()
        {
            Arrange();

            SendPointer(
                _target, EventType.MouseDown, new Vector2(10f, 10f), 0, EventModifiers.Shift);
            Move(20f, 10f, EventModifiers.Shift);
            Move(30f, 10f, EventModifiers.Shift);

            Assert.AreEqual(1, _updates.Count);
            Assert.IsTrue(_updates[0].Shift);
        }

        #endregion

        #region Cancel

        [Test]
        public void Escape_CancelsTheScrubInsteadOfCommitting()
        {
            Arrange();

            Down(10f, 10f);
            Move(40f, 10f);
            Escape();

            Assert.AreEqual(new[] { "began", "cancelled" }, _log.ToArray());
            Assert.IsFalse(_manipulator.IsScrubbing);
        }

        [Test]
        public void Escape_AfterCancel_ReleaseDoesNotCommitOrClick()
        {
            Arrange();

            Down(10f, 10f);
            Move(40f, 10f);
            Escape();
            Up(40f, 10f);

            Assert.AreEqual(new[] { "began", "cancelled" }, _log.ToArray());
        }

        [Test]
        public void Escape_WithoutAPress_IsIgnored()
        {
            Arrange();

            Escape();

            Assert.IsEmpty(_log);
        }

        [Test]
        public void CaptureOut_CancelsTheScrub()
        {
            Arrange();

            Down(10f, 10f);
            Move(40f, 10f);
            CaptureOut();

            Assert.AreEqual(new[] { "began", "cancelled" }, _log.ToArray());
            Assert.IsFalse(_manipulator.IsScrubbing);
        }

        [Test]
        public void CaptureOut_BeforeTheThreshold_DoesNotClick()
        {
            Arrange();

            Down(10f, 10f);
            CaptureOut();
            Up(10f, 10f);

            Assert.IsEmpty(_log);
        }

        [Test]
        public void CommitDoesNotAlsoCancel()
        {
            Arrange();

            Down(10f, 10f);
            Move(40f, 10f);
            Up(40f, 10f);

            // ReleasePointer inside PointerUp calls PointerCaptureOut back, so getting the
            // state-teardown order wrong would fire both ended and cancelled
            Assert.AreEqual(new[] { "began", "ended" }, _log.ToArray());
        }

        #endregion
    }
}
