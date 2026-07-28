using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace TweeqDemo.CustomWidgets.Tests
{
    /// <summary>
    /// EndpointInput（ext-custom-widgets-spec.md EXT-02）の契約検証。
    ///
    /// 状態機械は panel 非依存なので大半はパネル無しで完結する。
    /// ポインタ配線（TweeqScrubManipulator との結線）と ChangeEvent の送出だけ
    /// パッケージが公開する <see cref="TweeqRuntimeTestPanel"/> を使う
    /// （外部プロジェクトが testables 経由でテスト土台を借りられることの実証）。
    /// </summary>
    public class EndpointInputTests
    {
        TweeqRuntimeTestPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
        }

        static EndpointInput Make()
        {
            return new EndpointInput();
        }

        static EndpointInput Make(string initial)
        {
            EndpointInput input = new EndpointInput();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        #region Value normalization

        [Test]
        public void Default_IsAllZero()
        {
            Assert.AreEqual("0.0.0.0", Make().value);
        }

        [Test]
        public void Value_StripsLeadingZeros()
        {
            Assert.AreEqual("1.2.3.4", Make("001.02.3.004").value);
        }

        [Test]
        public void Value_ClampsOctetsToMax()
        {
            Assert.AreEqual("255.0.255.7", Make("300.0.999.7").value);
        }

        [Test]
        public void Value_ClampsAbsurdlyLongDigitsWithoutOverflow()
        {
            // long でも溢れる桁数。TryParse は上限で頭打ちにしながら積むので落ちない
            Assert.AreEqual("255.255.9.9", Make("99999999999999999999.300.9.9").value);
        }

        [Test]
        public void Value_TrimsSurroundingWhitespace()
        {
            Assert.AreEqual("10.0.0.1", Make("  10.0.0.1  ").value);
        }

        [Test]
        public void Value_KeepsCurrentWhenUnparsable()
        {
            EndpointInput input = Make("10.0.0.1");

            input.value = "not-an-address";
            Assert.AreEqual("10.0.0.1", input.value);

            input.value = "1.2.3";
            Assert.AreEqual("10.0.0.1", input.value);

            input.value = "1.2.3.4.5";
            Assert.AreEqual("10.0.0.1", input.value);

            input.value = "1.2.3.-4";
            Assert.AreEqual("10.0.0.1", input.value);

            input.value = "1.2.3.4:80:90";
            Assert.AreEqual("10.0.0.1", input.value);

            input.value = null;
            Assert.AreEqual("10.0.0.1", input.value);
        }

        [Test]
        public void Value_ParsesPortAndClampsIt()
        {
            EndpointInput input = Make();
            input.PortEnabled = true;
            input.SetValueWithoutNotify("1.2.3.4:99999");

            Assert.AreEqual("1.2.3.4:65535", input.value);
        }

        [Test]
        public void Value_WithoutPortResetsPortToZero()
        {
            EndpointInput input = Make();
            input.PortEnabled = true;
            input.SetValueWithoutNotify("1.2.3.4:8080");
            input.SetValueWithoutNotify("9.9.9.9");

            Assert.AreEqual("9.9.9.9:0", input.value);
        }

        [Test]
        public void EndpointAddress_TryParseRoundTrips()
        {
            EndpointAddress address;
            Assert.IsTrue(EndpointAddress.TryParse("192.168.000.1:8080", out address));
            Assert.IsTrue(address.HasPort);
            Assert.AreEqual(8080, address.Port);
            Assert.AreEqual(192, address.GetOctet(0));
            Assert.AreEqual(168, address.GetOctet(1));
            Assert.AreEqual(0, address.GetOctet(2));
            Assert.AreEqual(1, address.GetOctet(3));
            Assert.AreEqual("192.168.0.1:8080", address.Format(true));
            Assert.AreEqual("192.168.0.1", address.Format(false));
        }

        #endregion

        #region Port toggle

        [Test]
        public void PortEnabled_ChangesSegmentCount()
        {
            EndpointInput input = Make();
            Assert.AreEqual(4, input.SegmentCount);

            input.PortEnabled = true;
            Assert.AreEqual(5, input.SegmentCount);

            input.PortEnabled = false;
            Assert.AreEqual(4, input.SegmentCount);
        }

        [Test]
        public void PortEnabled_KeepsPortWhileHidden()
        {
            EndpointInput input = Make();
            input.SetValueWithoutNotify("1.2.3.4:8080");

            Assert.AreEqual("1.2.3.4", input.value);

            input.PortEnabled = true;
            Assert.AreEqual("1.2.3.4:8080", input.value);
        }

        [Test]
        public void PortEnabled_PullsFocusBackWhenPortHides()
        {
            EndpointInput input = Make();
            input.PortEnabled = true;
            input.FocusSegment(4);
            Assert.AreEqual(4, input.FocusedSegment);

            input.PortEnabled = false;
            Assert.AreEqual(3, input.FocusedSegment);
        }

        #endregion

        #region Segment composition

        [Test]
        public void SetSegment_ComposesValue()
        {
            EndpointInput input = Make();
            input.SetSegment(0, 192);
            input.SetSegment(1, 168);
            input.SetSegment(2, 0);
            input.SetSegment(3, 42);

            Assert.AreEqual("192.168.0.42", input.value);
            Assert.AreEqual(168, input.GetSegment(1));
        }

        [Test]
        public void SetSegment_ClampsToSegmentMax()
        {
            EndpointInput input = Make();
            input.SetSegment(0, 900);
            Assert.AreEqual(255, input.GetSegment(0));

            input.PortEnabled = true;
            input.SetSegment(4, 99999);
            Assert.AreEqual(65535, input.GetSegment(4));
        }

        [Test]
        public void SetSegment_IgnoresHiddenPortIndex()
        {
            EndpointInput input = Make();
            input.SetSegment(4, 8080);

            Assert.AreEqual(0, input.GetSegment(4));
        }

        [Test]
        public void SetSegmentText_KeepsDigitsOnly()
        {
            EndpointInput input = Make();
            input.FocusSegment(0);
            input.SetSegmentText(0, "1a2b");

            Assert.AreEqual("12", input.GetSegmentText(0));
            Assert.AreEqual(12, input.GetSegment(0));
        }

        [Test]
        public void SetSegmentText_ClampsWhileTyping()
        {
            EndpointInput input = Make();
            input.FocusSegment(0);
            input.SetSegmentText(0, "999");

            Assert.AreEqual("255", input.GetSegmentText(0));
            Assert.AreEqual(255, input.GetSegment(0));
        }

        [Test]
        public void SetSegmentText_EmptyCommitsToZero()
        {
            EndpointInput input = Make("10.0.0.1");
            input.FocusSegment(0);
            input.SetSegmentText(0, string.Empty);

            Assert.AreEqual(string.Empty, input.GetSegmentText(0));
            Assert.AreEqual("0.0.0.1", input.value);

            input.CommitEditing();
            Assert.AreEqual("0", input.GetSegmentText(0));
        }

        #endregion

        #region Segment navigation

        [Test]
        public void SetSegmentText_DotMovesToNextSegment()
        {
            EndpointInput input = Make();
            input.FocusSegment(0);
            input.SetSegmentText(0, "12.");

            Assert.AreEqual("12", input.GetSegmentText(0));
            Assert.AreEqual(1, input.FocusedSegment);
        }

        [Test]
        public void SetSegmentText_ColonMovesIntoPortWhenEnabled()
        {
            EndpointInput input = Make();
            input.PortEnabled = true;
            input.FocusSegment(3);
            input.SetSegmentText(3, "4:");

            Assert.AreEqual(4, input.FocusedSegment);
        }

        [Test]
        public void SetSegmentText_DotOnLastSegmentStaysPut()
        {
            EndpointInput input = Make();
            input.FocusSegment(3);
            input.SetSegmentText(3, "9.");

            Assert.AreEqual(3, input.FocusedSegment);
        }

        [Test]
        public void MoveSegment_ClampsAtBothEnds()
        {
            EndpointInput input = Make();
            input.FocusSegment(0);
            input.MoveSegment(-1);
            Assert.AreEqual(0, input.FocusedSegment);

            input.FocusSegment(3);
            input.MoveSegment(1);
            Assert.AreEqual(3, input.FocusedSegment);
        }

        #endregion

        #region Session

        [Test]
        public void BeginSession_RecordsStartValue()
        {
            EndpointInput input = Make("10.0.0.1");
            input.BeginSession();

            Assert.IsTrue(input.IsSessionActive);
            Assert.AreEqual("10.0.0.1", input.ValueAtSessionStart);
        }

        [Test]
        public void CancelEditing_RestoresSessionStartValue()
        {
            EndpointInput input = Make("10.0.0.1");
            input.FocusSegment(0);
            input.SetSegmentText(0, "99");

            Assert.AreEqual("99.0.0.1", input.value);

            input.CancelEditing();

            Assert.AreEqual("10.0.0.1", input.value);
            Assert.IsFalse(input.IsSessionActive);
        }

        [Test]
        public void CancelEditing_DoesNotConfirm()
        {
            EndpointInput input = Make("10.0.0.1");
            List<string> confirmed = new List<string>();
            input.Confirmed += confirmed.Add;

            input.FocusSegment(0);
            input.SetSegmentText(0, "99");
            input.CancelEditing();
            input.EndSession();

            Assert.AreEqual(0, confirmed.Count);
        }

        [Test]
        public void EndSession_ConfirmsOnceWhenValueChanged()
        {
            EndpointInput input = Make("10.0.0.1");
            List<string> confirmed = new List<string>();
            input.Confirmed += confirmed.Add;

            input.FocusSegment(0);
            input.SetSegmentText(0, "99");
            input.EndSession();
            input.EndSession();

            Assert.AreEqual(1, confirmed.Count);
            Assert.AreEqual("99.0.0.1", confirmed[0]);
        }

        [Test]
        public void EndSession_StaysSilentWhenValueUnchanged()
        {
            EndpointInput input = Make("10.0.0.1");
            List<string> confirmed = new List<string>();
            input.Confirmed += confirmed.Add;

            input.FocusSegment(0);
            input.EndSession();

            Assert.AreEqual(0, confirmed.Count);
        }

        [Test]
        public void CommitEditing_ThenEndSession_ConfirmsOnce()
        {
            EndpointInput input = Make("10.0.0.1");
            List<string> confirmed = new List<string>();
            input.Confirmed += confirmed.Add;

            input.FocusSegment(0);
            input.SetSegmentText(0, "99");
            input.CommitEditing();
            input.EndSession();

            Assert.AreEqual(1, confirmed.Count);
        }

        [Test]
        public void Disabled_EndsSessionWithoutConfirming()
        {
            EndpointInput input = Make("10.0.0.1");
            List<string> confirmed = new List<string>();
            input.Confirmed += confirmed.Add;

            input.FocusSegment(0);
            input.SetSegmentText(0, "99");
            input.Disabled = true;

            Assert.IsFalse(input.IsSessionActive);
            Assert.AreEqual(0, confirmed.Count);
        }

        [Test]
        public void Disabled_BlocksTyping()
        {
            EndpointInput input = Make("10.0.0.1");
            input.Disabled = true;
            input.SetSegmentText(0, "99");

            Assert.AreEqual("10.0.0.1", input.value);
        }

        #endregion

        #region Scrub

        [Test]
        public void Scrub_FourPixelsIsOneStep()
        {
            EndpointInput input = Make("0.0.0.0");
            input.BeginSegmentScrub(0);
            input.UpdateSegmentScrub(0, 40f, 0f, false, false);
            input.EndSegmentScrub(0);

            Assert.AreEqual(10, input.GetSegment(0));
        }

        [Test]
        public void Scrub_ShiftIsTenTimesFaster()
        {
            EndpointInput input = Make("0.0.0.0");
            input.BeginSegmentScrub(0);
            input.UpdateSegmentScrub(0, 4f, 0f, true, false);

            Assert.AreEqual(10, input.GetSegment(0));
        }

        [Test]
        public void Scrub_AltIsTenTimesFiner()
        {
            EndpointInput input = Make("0.0.0.0");
            input.BeginSegmentScrub(0);
            input.UpdateSegmentScrub(0, 40f, 0f, false, true);

            Assert.AreEqual(1, input.GetSegment(0));
        }

        [Test]
        public void Scrub_ClampsToSegmentRange()
        {
            EndpointInput input = Make("0.0.0.0");
            input.BeginSegmentScrub(0);
            input.UpdateSegmentScrub(0, 4000f, 0f, false, false);
            Assert.AreEqual(255, input.GetSegment(0));

            input.UpdateSegmentScrub(0, -8000f, 0f, false, false);
            Assert.AreEqual(0, input.GetSegment(0));
        }

        [Test]
        public void Scrub_CancelRestoresStartValue()
        {
            EndpointInput input = Make("7.0.0.0");
            input.BeginSegmentScrub(0);
            input.UpdateSegmentScrub(0, 40f, 0f, false, false);
            Assert.AreEqual(17, input.GetSegment(0));

            input.CancelSegmentScrub(0);
            Assert.AreEqual(7, input.GetSegment(0));
        }

        [Test]
        public void Scrub_OnPortUsesSixteenBitRange()
        {
            EndpointInput input = Make();
            input.PortEnabled = true;
            input.BeginSegmentScrub(4);
            input.UpdateSegmentScrub(4, 4000f, 0f, false, false);

            Assert.AreEqual(1000, input.GetSegment(4));
        }

        [Test]
        public void Scrub_BlockedWhileDisabled()
        {
            EndpointInput input = Make("0.0.0.0");
            input.Disabled = true;
            input.BeginSegmentScrub(0);
            input.UpdateSegmentScrub(0, 40f, 0f, false, false);

            Assert.AreEqual(0, input.GetSegment(0));
        }

        #endregion

        #region ITweeqInputBox / theme

        static float Radius(StyleLength style)
        {
            return style.value.value;
        }

        [Test]
        public void InlinePosition_SquaresTrailingCorners()
        {
            EndpointInput input = Make();
            input.InlinePosition = TweeqBoxPosition.Start;

            Assert.AreEqual(input.Theme.InputRadius, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(input.Theme.InputRadius, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void BlockPosition_SquaresBottomCorners()
        {
            EndpointInput input = Make();
            input.BlockPosition = TweeqBoxPosition.Start;

            Assert.AreEqual(input.Theme.InputRadius, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomLeftRadius));
        }

        [Test]
        public void Theme_ArrivesThroughDistribution()
        {
            EndpointInput input = Make();
            VisualElement parent = new VisualElement();
            parent.Add(input);

            TweeqTheme theme = TweeqTheme.Light();
            TweeqThemeDistribution.Distribute(parent, theme);

            Assert.AreSame(theme, input.Theme);
        }

        [Test]
        public void Theme_NullFallsBackToDark()
        {
            EndpointInput input = Make();
            input.Theme = null;

            Assert.IsNotNull(input.Theme);
        }

        [Test]
        public void Invalid_TurnsSegmentTextToErrorColor()
        {
            EndpointInput input = Make();
            TextField field = input.Q<TextField>("tweeq-endpoint-segment-text");
            Assert.IsNotNull(field);

            Assert.AreEqual(input.Theme.Text, field.style.color.value);

            input.Invalid = true;
            Assert.AreEqual(input.Theme.Error, field.style.color.value);
        }

        [Test]
        public void FocusRing_IsThePackageComponent()
        {
            // 外部 asmdef から TweeqFocusRing を採用できることの実証（EXT-03-C）
            TweeqFocusRing ring = Make().Q<TweeqFocusRing>();

            Assert.IsNotNull(ring);
            Assert.AreEqual("tweeq-endpoint-focus-ring", ring.name);
            Assert.IsFalse(ring.Visible);
        }

        [Test]
        public void FocusRing_ShowsWhileTheSessionIsOpen()
        {
            EndpointInput input = Make();
            TweeqFocusRing ring = input.Q<TweeqFocusRing>();

            input.BeginSession();
            Assert.IsTrue(ring.Visible);

            input.EndSession();
            Assert.IsFalse(ring.Visible);
        }

        [Test]
        public void Segments_UseTheSharedTextFieldNormalization()
        {
            // ApplyTextField（EXT-03-A）が効いていれば、区画の TextField は
            // 24px の枠を使い切り、内側の既定クロームが消えている
            EndpointInput input = Make();
            TextField field = input.Q<TextField>("tweeq-endpoint-segment-text");
            VisualElement textInput = field.Q("unity-text-input");

            Assert.AreEqual(12f, field.style.fontSize.value.value);
            Assert.AreEqual(100f, textInput.style.height.value.value);
            Assert.AreEqual(LengthUnit.Percent, textInput.style.height.value.unit);
            Assert.AreEqual(Color.clear, textInput.style.backgroundColor.value);
            Assert.AreEqual(0f, textInput.style.borderTopWidth.value);
            Assert.AreEqual(0f, textInput.style.paddingLeft.value.value);
        }

        [Test]
        public void Disabled_KeepsTheInsetBorderChrome()
        {
            EndpointInput input = Make();

            input.Disabled = true;
            Assert.AreEqual(Color.clear, input.style.backgroundColor.value);
            Assert.AreEqual(1f, input.style.borderTopWidth.value);

            input.Disabled = false;
            Assert.AreEqual(0f, input.style.borderTopWidth.value);
            Assert.AreEqual(input.Theme.Input, input.style.backgroundColor.value);
        }

        #endregion

        #region Panel wiring

        static void SendPointer(
            VisualElement element, EventType type, Vector2 position, EventModifiers modifiers)
        {
            Event systemEvent = new Event
            {
                type = type,
                mousePosition = position,
                button = 0,
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

        EndpointInput Mounted()
        {
            _panel = TweeqRuntimeTestPanel.Create();

            EndpointInput input = new EndpointInput();
            input.SetValueWithoutNotify("0.0.0.0");
            _panel.Root.Add(input);
            return input;
        }

        [Test]
        public void Pointer_DragScrubsThroughManipulator()
        {
            EndpointInput input = Mounted();
            VisualElement segment = input.Q("tweeq-endpoint-segment-0");
            Assert.IsNotNull(segment);

            // EditMode のパネルは「ポインタ下の要素」を持たないので、事前に掴んでおく
            segment.CapturePointer(PointerId.mousePointerId);

            SendPointer(segment, EventType.MouseDown, new Vector2(10f, 10f), EventModifiers.None);

            // 1 回目の移動は閾値超えの宣言だけで値には乗らない
            SendPointer(segment, EventType.MouseDrag, new Vector2(20f, 10f), EventModifiers.None);
            SendPointer(segment, EventType.MouseDrag, new Vector2(60f, 10f), EventModifiers.None);
            SendPointer(segment, EventType.MouseUp, new Vector2(60f, 10f), EventModifiers.None);

            Assert.AreEqual(10, input.GetSegment(0));
        }

        [Test]
        public void Pointer_ClickBelowThresholdFocusesSegment()
        {
            EndpointInput input = Mounted();
            VisualElement segment = input.Q("tweeq-endpoint-segment-2");
            Assert.IsNotNull(segment);

            segment.CapturePointer(PointerId.mousePointerId);

            SendPointer(segment, EventType.MouseDown, new Vector2(10f, 10f), EventModifiers.None);
            SendPointer(segment, EventType.MouseDrag, new Vector2(11f, 10f), EventModifiers.None);
            SendPointer(segment, EventType.MouseUp, new Vector2(11f, 10f), EventModifiers.None);

            Assert.AreEqual(2, input.FocusedSegment);
            Assert.IsTrue(input.IsSessionActive);
            Assert.AreEqual(0, input.GetSegment(2));
        }

        [Test]
        public void Value_SendsChangeEvent()
        {
            EndpointInput input = Mounted();

            // ChangeEvent はプール品なので、コールバックを抜けた後の中身は当てにならない
            List<string> transitions = new List<string>();
            input.RegisterCallback<ChangeEvent<string>>(
                evt => transitions.Add(evt.previousValue + "→" + evt.newValue));

            input.value = "192.168.0.1";

            Assert.AreEqual(1, transitions.Count);
            Assert.AreEqual("0.0.0.0→192.168.0.1", transitions[0]);
        }

        [Test]
        public void SetValueWithoutNotify_StaysSilent()
        {
            EndpointInput input = Mounted();
            int count = 0;
            input.RegisterCallback<ChangeEvent<string>>(evt => count++);

            input.SetValueWithoutNotify("192.168.0.1");

            Assert.AreEqual(0, count);
            Assert.AreEqual("192.168.0.1", input.value);
        }

        #endregion
    }
}
