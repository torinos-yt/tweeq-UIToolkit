using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// A disposable UIDocument for tests that need a real panel even in EditMode.
    ///
    /// TweeqModal / TweeqPopover's job is to "mount onto the overlay layer", so
    /// without a panel the contract itself can't be observed (obtaining the layer depends on the panel).
    /// In environments where a panel can't be created, <see cref="Create"/> falls back to marking the test Ignore.
    /// </summary>
    public sealed class TweeqModalTestPanel : IDisposable
    {
        readonly GameObject _gameObject;
        readonly PanelSettings _settings;
        readonly UIDocument _document;

        /// <summary>The root element mounted on the panel. Add the element under test here.</summary>
        public VisualElement Root { get; }

        TweeqModalTestPanel()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "TweeqModalTestPanelSettings";

            // PanelSettings without a theme set emits a "no theme" warning.
            // Since we don't verify appearance, borrow whatever is available in the project to silence it
            ThemeStyleSheet theme = FindAnyTheme();
            if (theme != null)
            {
                _settings.themeStyleSheet = theme;
            }

            _gameObject = new GameObject("tweeq-modal-test-panel")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            _document = _gameObject.AddComponent<UIDocument>();
            _document.panelSettings = _settings;

            Root = _document.rootVisualElement;
        }

        /// <summary>Prepares a single panel. Marks the test Ignore if it couldn't be created.</summary>
        public static TweeqModalTestPanel Create()
        {
            TweeqModalTestPanel panel = new TweeqModalTestPanel();
            if (panel.Root == null || panel.Root.panel == null)
            {
                panel.Dispose();
                Assert.Ignore("could not create a runtime panel in EditMode (this contract is verified on the Play Mode side)");
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
    /// Verifies TweeqModal's contract (m8-modal-tabs-spec.md §A "Test Contract").
    ///
    /// The open/close state machine, content persistence, and theme propagation are panel-independent,
    /// so those are verified directly; only mounting onto the layer borrows <see cref="TweeqModalTestPanel"/>.
    /// The following need real layout and rendering, so they're the Play Mode side's responsibility:
    /// - Tracking the max size (layer size − 2×48). Layout doesn't resolve in EditMode
    /// - The appearance animation (opacity / translateY) and emphasize's actual waveform. The scheduler doesn't run during tests
    /// - The actual measurement of "not hitting the background UI" via panel.Pick (substituted here with pickingMode and coverage)
    /// </summary>
    public class TweeqModalTests
    {
        TweeqModalTestPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
        }

        TweeqModalTestPanel RequirePanel()
        {
            if (_panel == null)
            {
                _panel = TweeqModalTestPanel.Create();
            }

            return _panel;
        }

        static TweeqOverlayLayer LayerOf(VisualElement element)
        {
            return element?.hierarchy.parent as TweeqOverlayLayer;
        }

        #region Content

        [Test]
        public void Content_GoesIntoThePane()
        {
            TweeqModal modal = new TweeqModal();
            VisualElement child = new VisualElement();

            modal.Add(child);

            Assert.AreSame(modal.Pane.contentContainer, modal.contentContainer);
            Assert.AreSame(modal.Pane.contentContainer, child.hierarchy.parent);
        }

        [Test]
        public void Content_SurvivesOpenAndClose()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();
            VisualElement child = new VisualElement();
            modal.Add(child);
            panel.Root.Add(modal);

            modal.Open = true;
            modal.Open = false;

            // Persists inside the balloon, so it isn't discarded on open/close (equivalent to the Vue original's popover display toggling)
            Assert.AreSame(modal.Pane.contentContainer, child.hierarchy.parent);
            Assert.AreEqual(1, modal.childCount);
        }

        #endregion

        #region Open/Close

        [Test]
        public void Open_WithoutPanelIsSilentNoOp()
        {
            TweeqModal modal = new TweeqModal();

            Assert.DoesNotThrow(() => modal.Open = true);

            // It just doesn't mount because there's nowhere to place it; the Open request itself is still retained
            Assert.IsTrue(modal.Open);
            Assert.IsNull(modal.Backdrop.hierarchy.parent);
        }

        [Test]
        public void Open_RaisesOpenedAndClosedOnce()
        {
            TweeqModal modal = new TweeqModal();
            int opened = 0;
            int closed = 0;
            modal.Opened += () => opened++;
            modal.Closed += () => closed++;

            modal.Open = true;
            modal.Open = true;
            modal.Open = false;
            modal.Open = false;

            Assert.AreEqual(1, opened);
            Assert.AreEqual(1, closed);
        }

        [Test]
        public void Open_MountsTheBackdropAndPaneOnTheOverlayLayer()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();
            panel.Root.Add(modal);

            modal.Open = true;

            Assert.IsNotNull(LayerOf(modal.Backdrop), "backdrop is not mounted on the overlay layer");
            Assert.AreSame(modal.Backdrop, modal.Pane.hierarchy.parent);
        }

        [Test]
        public void Close_RemovesTheBackdropFromTheLayer()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();
            panel.Root.Add(modal);
            modal.Open = true;
            TweeqOverlayLayer layer = LayerOf(modal.Backdrop);

            modal.Open = false;

            // It must stop absorbing pointers the instant it closes (leaving it would accidentally block clicks on what's behind)
            Assert.IsNull(modal.Backdrop.hierarchy.parent);
            Assert.IsNotNull(layer);
            Assert.AreEqual(0, layer.hierarchy.childCount);
        }

        [Test]
        public void Open_MountsAgainAfterReopen()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();
            panel.Root.Add(modal);

            modal.Open = true;
            modal.Open = false;
            modal.Open = true;

            Assert.IsNotNull(LayerOf(modal.Backdrop));
        }

        [Test]
        public void Open_SetBeforeAttachMountsOnAttach()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();

            // UXML's open="true" is the path taken via attribute application (before panel attachment)
            modal.Open = true;
            panel.Root.Add(modal);

            Assert.IsNotNull(LayerOf(modal.Backdrop));
        }

        [Test]
        public void Detach_TakesTheBackdropDownWithIt()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();
            panel.Root.Add(modal);
            modal.Open = true;

            modal.RemoveFromHierarchy();

            Assert.IsNull(modal.Backdrop.hierarchy.parent, "left behind on the layer after removing the owner");
        }

        #endregion

        #region backdrop

        [Test]
        public void Backdrop_CatchesPointersAndCoversTheLayer()
        {
            TweeqModal modal = new TweeqModal();

            // An intentional deviation from the Vue original's popover="manual" (allows interacting with the background). A misclick would be an accident, so it's blocked
            Assert.AreEqual(PickingMode.Position, modal.Backdrop.pickingMode);
            Assert.AreEqual(Position.Absolute, modal.Backdrop.style.position.value);
            Assert.AreEqual(0f, modal.Backdrop.style.left.value.value);
            Assert.AreEqual(0f, modal.Backdrop.style.top.value.value);
            Assert.AreEqual(0f, modal.Backdrop.style.right.value.value);
            Assert.AreEqual(0f, modal.Backdrop.style.bottom.value.value);
        }

        [Test]
        public void Backdrop_ClickRaisesOutsideClickedWithoutClosing()
        {
            TweeqModal modal = new TweeqModal();
            int outside = 0;
            int closed = 0;
            modal.OutsideClicked += () => outside++;
            modal.Closed += () => closed++;
            modal.Open = true;

            modal.PerformOutsideClick();

            Assert.AreEqual(1, outside);
            Assert.AreEqual(0, closed);
            Assert.IsTrue(modal.Open, "PaneModal does not close on an outside click");
        }

        [Test]
        public void Backdrop_UsesTheThemeBackgroundColor()
        {
            TweeqModal modal = new TweeqModal();
            TweeqTheme theme = TweeqTheme.Light();

            modal.Theme = theme;

            // The darkening is a difference in Theme.Background's alpha (alpha moves with the open/close animation)
            Color color = modal.Backdrop.style.backgroundColor.value;
            Assert.AreEqual(theme.Background.r, color.r, 0.001f);
            Assert.AreEqual(theme.Background.g, color.g, 0.001f);
            Assert.AreEqual(theme.Background.b, color.b, 0.001f);
        }

        #endregion

        #region Chrome

        [Test]
        public void Pane_UsesThePopupChrome()
        {
            TweeqModal modal = new TweeqModal();

            Assert.AreEqual(TweeqArrowSide.None, modal.Pane.ArrowSide);
            Assert.AreEqual(TweeqModal.PANE_RADIUS, modal.Pane.Radius);
            Assert.AreEqual(TweeqModal.PANE_PADDING, modal.Pane.PaddingVertical);
            Assert.AreEqual(TweeqModal.PANE_PADDING, modal.Pane.PaddingHorizontal);
        }

        [Test]
        public void Modal_TakesNoSpaceInTheOwnerTree()
        {
            TweeqModal modal = new TweeqModal();

            Assert.AreEqual(DisplayStyle.None, modal.style.display.value);
            Assert.AreEqual(PickingMode.Ignore, modal.pickingMode);
        }

        #endregion

        #region Theme

        [Test]
        public void Theme_ReachesThePaneAndTheContent()
        {
            TweeqModal modal = new TweeqModal();
            ButtonInput button = new ButtonInput("OK");
            VisualElement wrapper = new VisualElement();
            wrapper.Add(button);
            modal.Add(wrapper);

            TweeqTheme theme = TweeqTheme.Light();
            modal.Theme = theme;

            // Chrome opacification became a shared mechanism where TweeqBalloon uses Theme.SurfaceOpaque,
            // so the theme itself is passed through as the same instance untouched
            Assert.AreSame(theme, modal.Pane.Theme);
            Assert.AreEqual(1f, theme.SurfaceOpaque.a, "the chrome composite color is always opaque");

            // TweeqRoot halts its search at ITweeqThemed, so it's the modal's responsibility to distribute the theme to its content
            Assert.AreSame(theme, button.Theme);
        }

        [Test]
        public void Theme_NullFallsBackToDark()
        {
            TweeqModal modal = new TweeqModal();

            modal.Theme = null;

            Assert.IsNotNull(modal.Theme);
            Assert.AreEqual(ColorMode.Dark, modal.Theme.Mode);
        }

        #endregion

        #region emphasize

        [Test]
        public void Emphasize_WithoutPanelIsSilentAndKeepsTheScaleSettled()
        {
            TweeqModal modal = new TweeqModal();

            Assert.DoesNotThrow(() => modal.Emphasize());
            Assert.DoesNotThrow(() => modal.Emphasize());

            Assert.IsFalse(modal.IsEmphasizing);
            Assert.AreEqual(1f, modal.Pane.style.scale.value.value.x, 0.001f);
        }

        [Test]
        public void Emphasize_RestartsWhileRunning()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();
            panel.Root.Add(modal);
            modal.Open = true;

            modal.Emphasize();
            Assert.IsTrue(modal.IsEmphasizing);

            // Even under repeated triggering it restarts from the beginning without throwing
            Assert.DoesNotThrow(() => modal.Emphasize());
            Assert.IsTrue(modal.IsEmphasizing);
            Assert.AreEqual(1f, modal.Pane.style.scale.value.value.x, 0.001f);
        }

        [Test]
        public void Emphasize_StopsWhenTheModalCloses()
        {
            TweeqModalTestPanel panel = RequirePanel();
            TweeqModal modal = new TweeqModal();
            panel.Root.Add(modal);
            modal.Open = true;
            modal.Emphasize();

            modal.Open = false;

            Assert.IsFalse(modal.IsEmphasizing);
            Assert.AreEqual(1f, modal.Pane.style.scale.value.value.x, 0.001f);
        }

        #endregion
    }
}
