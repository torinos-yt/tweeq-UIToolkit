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
    /// A disposable UIDocument for streaming synthetic events through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manipulators and focus wiring are entirely about "what happens when an event arrives",
    /// so the contract can't be observed without a panel. Since scale factors would change the
    /// meaning of threshold px values, we pin ConstantPixelSize / scale=1 to guarantee 1px = 1px.
    /// </para>
    /// <para>
    /// An EditMode panel has no "element under the pointer", so delivering a PointerDown
    /// requires the element under test to call <c>CapturePointer</c> itself beforehand.
    /// </para>
    /// <para>
    /// To use this from an external project, add <c>jp.torinos.tweeq-uitoolkit</c> to
    /// <c>testables</c> in the consumer's manifest.
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

        /// <summary>The root element mounted on the panel. Add the element under test here.</summary>
        public VisualElement Root { get; }

        /// <summary>Prepares a single panel. Ignores the test if one couldn't be created.</summary>
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

            // A PanelSettings with no theme set logs a "no theme" warning.
            // We don't verify appearance, so silence it by borrowing whatever theme exists in the project.
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
