using System;
using UnityEngine;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Where <see cref="TweeqTabs"/> reads and writes the active tab id (M8 spec §D "Persistence").
    /// </summary>
    /// <remarks>
    /// The default implementation is <see cref="TweeqTabPlayerPrefsStorage"/>. Tests, or
    /// applications that maintain their own config file, should swap in
    /// <see cref="TweeqTabs.Storage"/> instead.
    /// Implementations must not throw exceptions (a runtime exception during a live show is an
    /// incident).
    /// </remarks>
    public interface ITweeqTabStorage
    {
        /// <summary>Reads the stored value. Returns <paramref name="defaultValue"/> as-is if nothing was saved.</summary>
        string Get(string key, string defaultValue);

        /// <summary>Saves a value.</summary>
        void Set(string key, string value);

        /// <summary>Clears the stored value (i.e. reverts to default).</summary>
        void Delete(string key);
    }

    /// <summary>
    /// Default implementation of <see cref="ITweeqTabStorage"/>. Positioned as the counterpart to
    /// the Vue original's localStorage.
    /// </summary>
    /// <remarks>
    /// PlayerPrefs can be unavailable in batch mode or sandboxes. Throwing an exception and halting
    /// the caller just because saving the tab selection state failed isn't worth it, so it's
    /// caught and only a warning is logged (same policy as saving <see cref="ParameterGroup"/>'s
    /// open/closed state).
    /// </remarks>
    public sealed class TweeqTabPlayerPrefsStorage : ITweeqTabStorage
    {
        /// <summary>
        /// Shared instance. This is the fallback destination when <see cref="TweeqTabs.Storage"/>
        /// is assigned null, so tests can verify "was the override cleared?" by checking against
        /// this reference.
        /// </summary>
        public static readonly TweeqTabPlayerPrefsStorage Instance = new TweeqTabPlayerPrefsStorage();

        /// <inheritdoc />
        public string Get(string key, string defaultValue)
        {
            if (string.IsNullOrEmpty(key))
            {
                return defaultValue;
            }

            try
            {
                return PlayerPrefs.GetString(key, defaultValue);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(TweeqTabPlayerPrefsStorage)}: 読み込めない（{key}）: {exception.Message}");
                return defaultValue;
            }
        }

        /// <inheritdoc />
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            try
            {
                PlayerPrefs.SetString(key, value ?? string.Empty);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(TweeqTabPlayerPrefsStorage)}: 保存できない（{key}）: {exception.Message}");
            }
        }

        /// <inheritdoc />
        public void Delete(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            try
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{nameof(TweeqTabPlayerPrefsStorage)}: 削除できない（{key}）: {exception.Message}");
            }
        }
    }
}
