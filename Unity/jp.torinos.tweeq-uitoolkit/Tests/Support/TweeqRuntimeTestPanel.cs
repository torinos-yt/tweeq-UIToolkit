using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Tweeq.UIToolkit.TestSupport
{
    /// <summary>
    /// 合成イベントを流すための使い捨て UIDocument。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manipulator やフォーカス配線は「イベントが来たら何をするか」が全てなので、
    /// panel 無しでは契約を観測できない。倍率が絡むと閾値の px が意味を変えてしまうため、
    /// ConstantPixelSize / scale=1 に固定して 1px = 1px を保証する。
    /// </para>
    /// <para>
    /// EditMode のパネルは「ポインタ下の要素」を持たないので、PointerDown を届けるには
    /// 被験要素側で <c>CapturePointer</c> しておく必要がある。
    /// </para>
    /// <para>
    /// 外部プロジェクトから使うには、consumer 側 manifest の <c>testables</c> に
    /// <c>jp.torinos.tweeq-uitoolkit</c> を入れること。
    /// </para>
    /// </remarks>
    public sealed class TweeqRuntimeTestPanel : IDisposable
    {
        #region Fields

        readonly GameObject _gameObject;
        readonly PanelSettings _settings;
        readonly UIDocument _document;

        #endregion

        #region Public API

        /// <summary>パネルに載ったルート要素。ここへ被験要素を Add する。</summary>
        public VisualElement Root { get; }

        /// <summary>パネルを 1 枚用意する。作れなかった場合はテストを Ignore にする。</summary>
        public static TweeqRuntimeTestPanel Create()
        {
            TweeqRuntimeTestPanel panel = new TweeqRuntimeTestPanel();
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

        #endregion

        #region Construction

        TweeqRuntimeTestPanel()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "TweeqRuntimeTestPanelSettings";
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1f;

            // テーマ未設定の PanelSettings は「テーマ無し」の警告を出す。
            // 見た目は検証しないので、プロジェクトにある物を何でも 1 枚借りて黙らせる
            ThemeStyleSheet theme = FindAnyTheme();
            if (theme != null)
            {
                _settings.themeStyleSheet = theme;
            }

            _gameObject = new GameObject("tweeq-runtime-test-panel")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            _document = _gameObject.AddComponent<UIDocument>();
            _document.panelSettings = _settings;

            Root = _document.rootVisualElement;
        }

        static ThemeStyleSheet FindAnyTheme()
        {
#if UNITY_EDITOR
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
#endif

            return null;
        }

        #endregion
    }
}
