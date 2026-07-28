using System;
using UnityEngine;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Where <see cref="TweeqTabs"/> reads and writes the active tab id (M8 spec §D "Persistence").
    /// </summary>
    /// <remarks>
    /// The default implementation is <see cref="TweeqMemoryStorage"/> — the selection lives
    /// only for the current session, so a library widget never writes to disk unless the host
    /// opts in. Assign <see cref="TweeqPlayerPrefsStorage.Instance"/> (or a custom
    /// implementation) to <see cref="TweeqTabs.Storage"/> to persist across runs.
    /// Implementations must not throw exceptions (a runtime exception during a live show is an
    /// incident).
    /// </remarks>
    public interface ITweeqStorage
    {
        /// <summary>Reads the stored value. Returns <paramref name="defaultValue"/> as-is if nothing was saved.</summary>
        string Get(string key, string defaultValue);

        /// <summary>Saves a value.</summary>
        void Set(string key, string value);

        /// <summary>Clears the stored value (i.e. reverts to default).</summary>
        void Delete(string key);
    }

    /// <summary>
    /// Session-only storage: keeps values in memory until domain reload or application quit.
    /// This is the default so the library leaves no trace on disk unless the host opts in.
    /// </summary>
    public sealed class TweeqMemoryStorage : ITweeqStorage
    {
        /// <summary>
        /// Shared instance. This is the fallback destination when <see cref="TweeqTabs.Storage"/>
        /// is assigned null, so tests can verify "was the override cleared?" by checking against
        /// this reference.
        /// </summary>
        public static readonly TweeqMemoryStorage Instance = new TweeqMemoryStorage();

        readonly System.Collections.Generic.Dictionary<string, string> _values =
            new System.Collections.Generic.Dictionary<string, string>();

        /// <inheritdoc />
        public string Get(string key, string defaultValue)
        {
            if (string.IsNullOrEmpty(key))
            {
                return defaultValue;
            }

            return _values.TryGetValue(key, out string stored) ? stored : defaultValue;
        }

        /// <inheritdoc />
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            _values[key] = value ?? string.Empty;
        }

        /// <inheritdoc />
        public void Delete(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            _values.Remove(key);
        }

        /// <summary>Drops every stored value (e.g. between tests or on an app-level reset).</summary>
        public void Clear()
        {
            _values.Clear();
        }
    }

    /// <summary>
    /// PlayerPrefs-backed implementation of <see cref="ITweeqStorage"/>. Positioned as the
    /// counterpart to the Vue original's localStorage; opt in via
    /// <see cref="TweeqTabs.Storage"/> when selections should survive restarts.
    /// </summary>
    /// <remarks>
    /// PlayerPrefs can be unavailable in batch mode or sandboxes. Throwing an exception and halting
    /// the caller just because saving the tab selection state failed isn't worth it, so it's
    /// caught and only a warning is logged (same policy as saving <see cref="ParameterGroup"/>'s
    /// open/closed state).
    /// </remarks>
    public sealed class TweeqPlayerPrefsStorage : ITweeqStorage
    {
        /// <summary>
        /// Shared instance. This is the fallback destination when <see cref="TweeqTabs.Storage"/>
        /// is assigned null, so tests can verify "was the override cleared?" by checking against
        /// this reference.
        /// </summary>
        public static readonly TweeqPlayerPrefsStorage Instance = new TweeqPlayerPrefsStorage();

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
                    $"{nameof(TweeqPlayerPrefsStorage)}: could not read ({key}): {exception.Message}");
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
                    $"{nameof(TweeqPlayerPrefsStorage)}: could not save ({key}): {exception.Message}");
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
                    $"{nameof(TweeqPlayerPrefsStorage)}: could not delete ({key}): {exception.Message}");
            }
        }
    }
}
