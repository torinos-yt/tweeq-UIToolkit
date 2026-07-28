using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// EditMode でも本物のパネルが要るテスト用の使い捨て UIDocument。
    ///
    /// TweeqModal / TweeqPopover は「オーバーレイ層へ載せる」ことが仕事なので、
    /// panel が無いと契約そのものが観測できない（層の取得が panel 依存）。
    /// パネルを作れない環境では <see cref="Create"/> がテストを Ignore に倒す。
    /// </summary>
    public sealed class TweeqModalTestPanel : IDisposable
    {
        readonly GameObject _gameObject;
        readonly PanelSettings _settings;
        readonly UIDocument _document;

        /// <summary>パネルに載ったルート要素。ここへ被験要素を Add する。</summary>
        public VisualElement Root { get; }

        TweeqModalTestPanel()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "TweeqModalTestPanelSettings";

            // テーマ未設定の PanelSettings は「テーマ無し」の警告を出す。
            // 見た目は検証しないので、プロジェクトにある物を何でも 1 枚借りて黙らせる
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

        /// <summary>パネルを 1 枚用意する。作れなかった場合はテストを Ignore にする。</summary>
        public static TweeqModalTestPanel Create()
        {
            TweeqModalTestPanel panel = new TweeqModalTestPanel();
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
    /// TweeqModal の契約（m8-modal-tabs-spec.md §A「テスト契約」）を検証する。
    ///
    /// 開閉の状態機械・中身の常駐・テーマ転送は panel 非依存なので素で確かめ、
    /// 層への設置だけ <see cref="TweeqModalTestPanel"/> を借りる。以下は実レイアウトと
    /// 描画が要るので Play Mode 側の担当:
    /// - 最大サイズ（層サイズ − 2×48）の追従。EditMode ではレイアウトが降りない
    /// - 出現アニメ（opacity / translateY）と emphasize の実波形。scheduler がテスト中に回らない
    /// - panel.Pick による「背面 UI に当たらない」の実測（ここでは pickingMode と被覆で代用）
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

        #region 中身

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

            // バルーン内に常駐するので開閉では捨てられない（Vue の popover 表示切替と等価）
            Assert.AreSame(modal.Pane.contentContainer, child.hierarchy.parent);
            Assert.AreEqual(1, modal.childCount);
        }

        #endregion

        #region 開閉

        [Test]
        public void Open_WithoutPanelIsSilentNoOp()
        {
            TweeqModal modal = new TweeqModal();

            Assert.DoesNotThrow(() => modal.Open = true);

            // 置き場所が無いので載らないだけ。要求としての Open は保つ
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

            Assert.IsNotNull(LayerOf(modal.Backdrop), "backdrop がオーバーレイ層に載っていない");
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

            // 閉じた瞬間にポインタを吸わなくなること（残っていると背面が押せない事故になる）
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

            // UXML の open="true" は属性適用（パネル接続前）で立つ経路
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

            Assert.IsNull(modal.Backdrop.hierarchy.parent, "所有者を外したのに層へ置き去りになっている");
        }

        #endregion

        #region backdrop

        [Test]
        public void Backdrop_CatchesPointersAndCoversTheLayer()
        {
            TweeqModal modal = new TweeqModal();

            // Vue の popover="manual"（背面操作可）からの意図的逸脱。誤操作＝事故なので遮断する
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
            Assert.IsTrue(modal.Open, "PaneModal は外側クリックでは閉じない");
        }

        [Test]
        public void Backdrop_UsesTheThemeBackgroundColor()
        {
            TweeqModal modal = new TweeqModal();
            TweeqTheme theme = TweeqTheme.Light();

            modal.Theme = theme;

            // 暗転は Theme.Background のアルファ違い（アルファは開閉アニメで動く）
            Color color = modal.Backdrop.style.backgroundColor.value;
            Assert.AreEqual(theme.Background.r, color.r, 0.001f);
            Assert.AreEqual(theme.Background.g, color.g, 0.001f);
            Assert.AreEqual(theme.Background.b, color.b, 0.001f);
        }

        #endregion

        #region 外装

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

        #region テーマ

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

            // 外装の不透明化は TweeqBalloon が Theme.SurfaceOpaque を使う共通方式になったので、
            // テーマ自体は同一インスタンスが素通しで届く
            Assert.AreSame(theme, modal.Pane.Theme);
            Assert.AreEqual(1f, theme.SurfaceOpaque.a, "外装用の合成色は常に不透明");

            // TweeqRoot は ITweeqThemed で探索を打ち切るので、中身へはモーダルが配る責務がある
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

            // 連打しても例外なく先頭から掛け直す
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
