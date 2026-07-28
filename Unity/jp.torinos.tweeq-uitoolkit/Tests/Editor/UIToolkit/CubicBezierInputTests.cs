using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Pins the CubicBezierInput contract (the "test contract" section of m10-cubicbezier-spec.md).
    ///
    /// Open/close and the drag session are a panel-independent imperative layer, so how many times
    /// events fire is fully covered here. Everything below needs a laid-out panel, so the pad
    /// geometry is exercised through the public static helpers instead of through real pointer
    /// positions, and pixel-level appearance is left to the Play Mode side:
    /// - hit testing against the actually painted handles
    /// - the curve / control lines / handle discs as drawn
    /// </summary>
    public class CubicBezierInputTests
    {
        const float EPSILON = 1e-5f;

        // A pad size that matches the shipped theme (PopupWidth 240 - PopupPadding 9 * 2).
        const float PAD_SIZE = 222f;

        TweeqRuntimeTestPanel _panel;
        CubicBezierInput _input;
        List<Vector4> _confirmed;
        List<Vector4> _changed;

        [TearDown]
        public void TearDown()
        {
            _input?.Close();
            _input = null;
            _panel?.Dispose();
            _panel = null;
        }

        #region Arrange

        // Every case runs on a panel: ChangeEvent is only sent while attached, and keyboard events
        // are routed through the focused element
        CubicBezierInput Arrange()
        {
            _panel = TweeqRuntimeTestPanel.Create();

            _input = new CubicBezierInput();
            _confirmed = new List<Vector4>();
            _changed = new List<Vector4>();
            _input.Confirmed += value => _confirmed.Add(value);
            _input.RegisterValueChangedCallback(evt => _changed.Add(evt.newValue));

            _panel.Root.Add(_input);
            _input.Focus();

            return _input;
        }

        static void AssertCurve(Vector4 expected, Vector4 actual)
        {
            Assert.AreEqual(expected.x, actual.x, EPSILON, "x1");
            Assert.AreEqual(expected.y, actual.y, EPSILON, "y1");
            Assert.AreEqual(expected.z, actual.z, EPSILON, "x2");
            Assert.AreEqual(expected.w, actual.w, EPSILON, "y2");
        }

        static float Radius(StyleLength length)
        {
            return length.value.value;
        }

        // The overlay layer hangs off the panel's topmost element, which sits above the document
        // root the field itself was added to
        VisualElement PadInPanel()
        {
            VisualElement root = _panel.Root.panel != null ? _panel.Root.panel.visualTree : null;
            return root != null ? root.Q("tweeq-cubic-bezier-pad") : null;
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

            using (PointerUpEvent up = PointerUpEvent.GetPooled(systemEvent))
            {
                element.SendEvent(up);
            }
        }

        // An EditMode panel has no "element under the pointer", so a press only reaches the target
        // through a capture taken beforehand. The click releases it, hence re-grabbing per click
        void Click(VisualElement element)
        {
            element.CapturePointer(PointerId.mousePointerId);
            SendPointer(element, EventType.MouseDown, Vector2.zero);
            SendPointer(element, EventType.MouseUp, Vector2.zero);
        }

        void SendKey(KeyCode keyCode)
        {
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', keyCode, EventModifiers.None))
            {
                _input.SendEvent(evt);
            }
        }

        #endregion

        #region Value

        [Test]
        public void Value_DefaultIsCssEase()
        {
            // Vue declares modelValue as required and ships no default, so CSS `ease` stands in
            AssertCurve(new Vector4(0.25f, 0.1f, 0.25f, 1f), CubicBezierInput.DEFAULT_VALUE);
            AssertCurve(CubicBezierInput.DEFAULT_VALUE, new CubicBezierInput().value);
        }

        [Test]
        public void Value_SetterClampsBothAxesToTheUnitSquare()
        {
            Arrange();

            _input.value = new Vector4(-1f, 2f, 0.5f, -0.25f);

            AssertCurve(new Vector4(0f, 1f, 0.5f, 0f), _input.value);
        }

        [Test]
        public void Value_SetterKeepsTheCurrentValueOnNaN()
        {
            Arrange();
            _input.SetValueWithoutNotify(new Vector4(0.2f, 0.3f, 0.4f, 0.5f));

            _input.value = new Vector4(float.NaN, 0.9f, 0.9f, 0.9f);

            AssertCurve(new Vector4(0.2f, 0.3f, 0.4f, 0.5f), _input.value);
            Assert.AreEqual(0, _changed.Count);
        }

        [Test]
        public void Value_SetterKeepsTheCurrentValueOnInfinity()
        {
            Arrange();
            _input.SetValueWithoutNotify(new Vector4(0.2f, 0.3f, 0.4f, 0.5f));

            _input.value = new Vector4(0.9f, float.PositiveInfinity, 0.9f, 0.9f);
            _input.SetValueWithoutNotify(new Vector4(0.9f, 0.9f, float.NegativeInfinity, 0.9f));

            AssertCurve(new Vector4(0.2f, 0.3f, 0.4f, 0.5f), _input.value);
            Assert.AreEqual(0, _changed.Count);
        }

        [Test]
        public void Value_SetterSendsChangeEventOnce()
        {
            Arrange();

            _input.value = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);

            Assert.AreEqual(1, _changed.Count);
            AssertCurve(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), _changed[0]);
        }

        [Test]
        public void Value_SetterIgnoresAnUnchangedAssignment()
        {
            Arrange();
            _input.value = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);

            _input.value = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);

            Assert.AreEqual(1, _changed.Count);
        }

        [Test]
        public void Value_SetValueWithoutNotifyStaysSilentButStillClamps()
        {
            Arrange();

            _input.SetValueWithoutNotify(new Vector4(0.5f, 5f, 0.5f, 0.5f));

            Assert.AreEqual(0, _changed.Count);
            AssertCurve(new Vector4(0.5f, 1f, 0.5f, 0.5f), _input.value);
        }

        #endregion

        #region Picker open / close

        [Test]
        public void Picker_OpenAndCloseFlipIsOpen()
        {
            Arrange();

            Assert.IsFalse(_input.IsOpen);

            _input.Open();
            Assert.IsTrue(_input.IsOpen);

            _input.Close();
            Assert.IsFalse(_input.IsOpen);
        }

        [Test]
        public void Picker_OpenMountsThePadOnTheOverlayLayer()
        {
            Arrange();

            _input.Open();

            // The pad lives inside the popover, i.e. on the overlay layer rather than under the
            // field, so this also proves the logical flag isn't running ahead of the presentation
            Assert.IsNotNull(PadInPanel(), "the pad was not mounted");

            _input.Close();

            Assert.IsNull(PadInPanel(), "the pad stayed mounted after close");
        }

        [Test]
        public void Picker_CloseKeepsTheValue()
        {
            Arrange();
            _input.Open();
            _input.BeginDrag(CubicBezierHandle.P1);
            _input.UpdateDrag(0.8f, 0.9f);
            _input.EndDrag();

            _input.Close();

            // Continuous edits stay committed on close; only Escape during a drag rolls back
            AssertCurve(new Vector4(0.8f, 0.9f, 0.25f, 1f), _input.value);
        }

        [Test]
        public void Picker_DisabledDoesNotOpenAndClosesWhatIsOpen()
        {
            Arrange();

            _input.Disabled = true;
            _input.Open();
            Assert.IsFalse(_input.IsOpen);

            _input.Disabled = false;
            _input.Open();
            Assert.IsTrue(_input.IsOpen);

            _input.Disabled = true;
            Assert.IsFalse(_input.IsOpen);
        }

        [Test]
        public void Picker_EscapeOnTheFieldClosesAndKeepsTheValue()
        {
            Arrange();
            _input.SetValueWithoutNotify(new Vector4(0.4f, 0.4f, 0.6f, 0.6f));
            _input.Open();

            SendKey(KeyCode.Escape);

            Assert.IsFalse(_input.IsOpen);
            AssertCurve(new Vector4(0.4f, 0.4f, 0.6f, 0.6f), _input.value);
        }

        #endregion

        #region Drag session

        [Test]
        public void Drag_P1MovesOnlyTheFirstPair()
        {
            Arrange();
            _input.SetValueWithoutNotify(new Vector4(0.25f, 0.1f, 0.25f, 1f));

            _input.BeginDrag(CubicBezierHandle.P1);
            _input.UpdateDrag(0.6f, 0.7f);

            AssertCurve(new Vector4(0.6f, 0.7f, 0.25f, 1f), _input.value);
            Assert.AreEqual(CubicBezierHandle.P1, _input.ActiveHandle);
        }

        [Test]
        public void Drag_P2MovesOnlyTheSecondPair()
        {
            Arrange();
            _input.SetValueWithoutNotify(new Vector4(0.25f, 0.1f, 0.25f, 1f));

            _input.BeginDrag(CubicBezierHandle.P2);
            _input.UpdateDrag(0.6f, 0.7f);

            AssertCurve(new Vector4(0.25f, 0.1f, 0.6f, 0.7f), _input.value);
        }

        [Test]
        public void Drag_UvIsClampedToTheUnitSquare()
        {
            Arrange();

            _input.BeginDrag(CubicBezierHandle.P1);
            _input.UpdateDrag(-3f, 4f);

            AssertCurve(new Vector4(0f, 1f, 0.25f, 1f), _input.value);
        }

        [Test]
        public void Drag_OneDragRaisesOneConfirmed()
        {
            Arrange();

            _input.BeginDrag(CubicBezierHandle.P1);
            _input.UpdateDrag(0.3f, 0.3f);
            _input.UpdateDrag(0.4f, 0.4f);
            _input.UpdateDrag(0.5f, 0.5f);
            _input.EndDrag();

            // The value streams out per move, the commit lands exactly once
            Assert.AreEqual(3, _changed.Count);
            Assert.AreEqual(1, _confirmed.Count);
            AssertCurve(new Vector4(0.5f, 0.5f, 0.25f, 1f), _confirmed[0]);
            Assert.AreEqual(CubicBezierHandle.None, _input.ActiveHandle);
        }

        [Test]
        public void Drag_GrabbingWithoutMovingLeavesTheCurveAlone()
        {
            Arrange();

            _input.BeginDrag(CubicBezierHandle.P2);
            _input.EndDrag();

            Assert.AreEqual(0, _changed.Count);
            Assert.AreEqual(1, _confirmed.Count);
            AssertCurve(CubicBezierInput.DEFAULT_VALUE, _input.value);
        }

        [Test]
        public void Drag_CancelRestoresTheStartValueWithoutConfirming()
        {
            Arrange();
            _input.SetValueWithoutNotify(new Vector4(0.25f, 0.1f, 0.25f, 1f));

            _input.BeginDrag(CubicBezierHandle.P1);
            _input.UpdateDrag(0.8f, 0.8f);
            _input.CancelDrag();

            AssertCurve(new Vector4(0.25f, 0.1f, 0.25f, 1f), _input.value);
            Assert.AreEqual(0, _confirmed.Count);

            // The rollback is notified too, so a listener never sees the abandoned value as final
            Assert.AreEqual(2, _changed.Count);
            AssertCurve(new Vector4(0.25f, 0.1f, 0.25f, 1f), _changed[1]);
        }

        [Test]
        public void Drag_EscapeWhileDraggingRollsBackAndKeepsThePickerOpen()
        {
            Arrange();
            _input.SetValueWithoutNotify(new Vector4(0.25f, 0.1f, 0.25f, 1f));
            _input.Open();

            _input.BeginDrag(CubicBezierHandle.P2);
            _input.UpdateDrag(0.9f, 0.2f);
            SendKey(KeyCode.Escape);

            // The guard swallows Escape so light dismiss never sees it: the drag is cancelled,
            // the picker stays up
            AssertCurve(new Vector4(0.25f, 0.1f, 0.25f, 1f), _input.value);
            Assert.AreEqual(0, _confirmed.Count);
            Assert.AreEqual(CubicBezierHandle.None, _input.ActiveHandle);
            Assert.IsTrue(_input.IsOpen);
        }

        [Test]
        public void Drag_IdleCallsAreIgnored()
        {
            Arrange();

            _input.UpdateDrag(0.9f, 0.9f);
            _input.EndDrag();
            _input.CancelDrag();

            Assert.AreEqual(0, _changed.Count);
            Assert.AreEqual(0, _confirmed.Count);
            AssertCurve(CubicBezierInput.DEFAULT_VALUE, _input.value);
        }

        [Test]
        public void Drag_BeginIsIgnoredForNoneAndWhileDisabled()
        {
            Arrange();

            _input.BeginDrag(CubicBezierHandle.None);
            Assert.AreEqual(CubicBezierHandle.None, _input.ActiveHandle);

            _input.Disabled = true;
            _input.BeginDrag(CubicBezierHandle.P1);
            _input.UpdateDrag(0.9f, 0.9f);

            Assert.AreEqual(CubicBezierHandle.None, _input.ActiveHandle);
            AssertCurve(CubicBezierInput.DEFAULT_VALUE, _input.value);
        }

        #endregion

        #region Pad geometry

        [Test]
        public void Pad_UvIsFlippedOnY()
        {
            Rect pad = new Rect(0f, 0f, PAD_SIZE, PAD_SIZE);

            // (0,0) is drawn at the bottom of the pad and (1,1) at the top, so uv has to read the
            // larger panel y as the smaller value
            Vector2 lowerLeft = HandleCenter(pad, 0f, 0f);
            Vector2 upperRight = HandleCenter(pad, 1f, 1f);
            Assert.Greater(lowerLeft.y, upperRight.y, "the plot is not flipped on Y");

            Vector2 origin = CubicBezierInput.PadToUv(pad, lowerLeft);
            Vector2 opposite = CubicBezierInput.PadToUv(pad, upperRight);
            Vector2 center = CubicBezierInput.PadToUv(pad, new Vector2(PAD_SIZE * 0.5f, PAD_SIZE * 0.5f));

            Assert.AreEqual(0f, origin.x, EPSILON);
            Assert.AreEqual(0f, origin.y, EPSILON);
            Assert.AreEqual(1f, opposite.x, EPSILON);
            Assert.AreEqual(1f, opposite.y, EPSILON);
            Assert.AreEqual(0.5f, center.x, EPSILON);
            Assert.AreEqual(0.5f, center.y, EPSILON);
        }

        [Test]
        public void Pad_UvIsClampedOutsideThePad()
        {
            Rect pad = new Rect(0f, 0f, PAD_SIZE, PAD_SIZE);

            Vector2 uv = CubicBezierInput.PadToUv(pad, new Vector2(-500f, 500f));

            Assert.AreEqual(0f, uv.x, EPSILON);
            Assert.AreEqual(0f, uv.y, EPSILON);
        }

        [Test]
        public void Pad_UvIsZeroForADegenerateRect()
        {
            Assert.AreEqual(Vector2.zero, CubicBezierInput.PadToUv(new Rect(0f, 0f, 0f, 0f), Vector2.one));
        }

        [Test]
        public void Pad_HitTestFindsTheHandleUnderThePointer()
        {
            Rect pad = new Rect(0f, 0f, PAD_SIZE, PAD_SIZE);
            Vector4 curve = new Vector4(0.25f, 0.1f, 0.75f, 0.9f);

            Vector2 p1 = HandleCenter(pad, curve.x, curve.y);
            Vector2 p2 = HandleCenter(pad, curve.z, curve.w);

            Assert.AreEqual(CubicBezierHandle.P1, CubicBezierInput.HitTestHandles(curve, pad, p1));
            Assert.AreEqual(CubicBezierHandle.P2, CubicBezierInput.HitTestHandles(curve, pad, p2));
        }

        [Test]
        public void Pad_HitTestIgnoresEmptySpace()
        {
            Rect pad = new Rect(0f, 0f, PAD_SIZE, PAD_SIZE);
            Vector4 curve = new Vector4(0.25f, 0.1f, 0.75f, 0.9f);

            // Vue only starts a drag from a circle, so the blank pad has to stay inert
            Assert.AreEqual(
                CubicBezierHandle.None,
                CubicBezierInput.HitTestHandles(curve, pad, new Vector2(PAD_SIZE * 0.5f, PAD_SIZE * 0.5f)));
        }

        [Test]
        public void Pad_HitTestPrefersTheTopmostHandleOnOverlap()
        {
            Rect pad = new Rect(0f, 0f, PAD_SIZE, PAD_SIZE);
            Vector4 curve = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

            // Vue paints the P2 circle last, so it is the one the pointer lands on
            Assert.AreEqual(
                CubicBezierHandle.P2,
                CubicBezierInput.HitTestHandles(curve, pad, HandleCenter(pad, 0.5f, 0.5f)));
        }

        // Mirrors the plot inset the painter uses (one handle radius plus half its stroke)
        static Vector2 HandleCenter(Rect pad, float x, float y)
        {
            float size = Mathf.Min(pad.width, pad.height);
            float margin = size * 0.035f + 1f;
            float side = size - margin * 2f;
            float left = (pad.width - side) * 0.5f;
            float top = (pad.height - side) * 0.5f;
            return new Vector2(left + x * side, top + (1f - y) * side);
        }

        #endregion

        #region Field

        [Test]
        public void Field_ClickTogglesThePicker()
        {
            Arrange();

            Click(_input);
            Assert.IsTrue(_input.IsOpen, "the first click did not open the picker");

            Click(_input);
            Assert.IsFalse(_input.IsOpen, "the second click did not close the picker");
        }

        [Test]
        public void Field_ClickWhileDisabledDoesNotOpen()
        {
            Arrange();
            _input.Disabled = true;

            Click(_input);

            Assert.IsFalse(_input.IsOpen);
        }

        [Test]
        public void Field_CornerRadiusFusesWithNeighbours()
        {
            Arrange();
            float radius = _input.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(_input.style.borderTopLeftRadius), EPSILON);
            Assert.AreEqual(radius, Radius(_input.style.borderBottomRightRadius), EPSILON);

            _input.InlinePosition = TweeqBoxPosition.Start;

            Assert.AreEqual(radius, Radius(_input.style.borderTopLeftRadius), EPSILON);
            Assert.AreEqual(0f, Radius(_input.style.borderTopRightRadius), EPSILON);
            Assert.AreEqual(0f, Radius(_input.style.borderBottomRightRadius), EPSILON);

            _input.BlockPosition = TweeqBoxPosition.End;

            // The two axes combine with OR, so the top corners flatten as well
            Assert.AreEqual(0f, Radius(_input.style.borderTopLeftRadius), EPSILON);
            Assert.AreEqual(radius, Radius(_input.style.borderBottomLeftRadius), EPSILON);
        }

        [Test]
        public void Field_DisabledSwitchesToTheInsetBorderChrome()
        {
            Arrange();

            _input.Disabled = true;

            Assert.AreEqual(
                TweeqInputBoxStyles.DISABLED_BORDER_WIDTH,
                _input.style.borderTopWidth.value,
                EPSILON);

            _input.Disabled = false;

            Assert.AreEqual(0f, _input.style.borderTopWidth.value, EPSILON);
        }

        [Test]
        public void Field_ThemeArrivesThroughDistribution()
        {
            Arrange();
            TweeqTheme theme = TweeqTheme.Light();

            TweeqThemeDistribution.Distribute(_panel.Root, theme);

            Assert.AreSame(theme, _input.Theme);
        }

        [Test]
        public void Field_ThemeFallsBackToDarkOnNull()
        {
            Arrange();

            _input.Theme = null;

            Assert.IsNotNull(_input.Theme);
            Assert.AreEqual(ColorMode.Dark, _input.Theme.Mode);
        }

        #endregion
    }
}
