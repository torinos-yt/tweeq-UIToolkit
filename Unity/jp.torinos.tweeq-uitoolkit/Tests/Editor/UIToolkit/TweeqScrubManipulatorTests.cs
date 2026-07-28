using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// 合成ポインタイベントを流すための使い捨て UIDocument。
    ///
    /// Manipulator は「イベントが来たら何をするか」が全てなので、panel 無しでは契約を
    /// 観測できない。倍率が絡むと閾値の px が意味を変えてしまうため、
    /// ConstantPixelSize / scale=1 に固定して 1px = 1px を保証する。
    /// </summary>
    public sealed class TweeqScrubTestPanel : IDisposable
    {
        readonly GameObject _gameObject;
        readonly PanelSettings _settings;
        readonly UIDocument _document;

        /// <summary>パネルに載ったルート要素。ここへ被験要素を Add する。</summary>
        public VisualElement Root { get; }

        TweeqScrubTestPanel()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "TweeqScrubTestPanelSettings";
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1f;

            // テーマ未設定の PanelSettings は「テーマ無し」の警告を出す。
            // 見た目は検証しないので、プロジェクトにある物を何でも 1 枚借りて黙らせる
            ThemeStyleSheet theme = FindAnyTheme();
            if (theme != null)
            {
                _settings.themeStyleSheet = theme;
            }

            _gameObject = new GameObject("tweeq-scrub-test-panel")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            _document = _gameObject.AddComponent<UIDocument>();
            _document.panelSettings = _settings;

            Root = _document.rootVisualElement;
        }

        /// <summary>パネルを 1 枚用意する。作れなかった場合はテストを Ignore にする。</summary>
        public static TweeqScrubTestPanel Create()
        {
            TweeqScrubTestPanel panel = new TweeqScrubTestPanel();
            if (panel.Root == null || panel.Root.panel == null)
            {
                panel.Dispose();
                Assert.Ignore("EditMode でランタイムパネルを作れなかった（この契約は Play Mode 側で検証する）");
            }

            return panel;
        }

        public void Dispose()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }

            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
            }
        }

        static ThemeStyleSheet FindAnyTheme()
        {
            string[] guids = AssetDatabase.FindAssets("t:ThemeStyleSheet");
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                ThemeStyleSheet sheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
                if (sheet != null)
                {
                    return sheet;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// TweeqScrubManipulator の契約（ext-custom-widgets-spec.md EXT-01-B「テスト契約」）を
    /// 合成イベントで検証する。
    ///
    /// 実機のカーソル非表示（HideCursorWhileScrubbing=true）は OS 状態を触るので
    /// Play Mode 側の担当。ここでは既定が false であることだけ固定する。
    /// </summary>
    public class TweeqScrubManipulatorTests
    {
        TweeqScrubTestPanel _panel;
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
            _panel = TweeqScrubTestPanel.Create();

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

            // EditMode のパネルは「ポインタ下の要素」を持たないので、PointerDown は
            // capture 経由でしか target へ届かない。押下前に 1 度だけ掴んでおく
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

            // 閾値を越えた地点が原点。ここまでの 10px は値に乗せない
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

            // PointerUp 内の ReleasePointer が PointerCaptureOut を呼び戻すので、
            // 状態を畳む順序を誤ると ended と cancelled が二重に飛ぶ
            Assert.AreEqual(new[] { "began", "ended" }, _log.ToArray());
        }

        #endregion
    }
}
