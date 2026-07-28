using System;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A string-specialized <see cref="ShuffleInput{T}" />. A wrapper that lets it be placed from UXML / UI
    /// Builder (m7-wave2-spec.md, "UXML support"), with a built-in random draw from a set of options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[UxmlElement]</c> can't be attached to a generic type, so exposing it to UXML requires a
    /// non-generic class with the type parameter closed.
    /// </para>
    /// <para>
    /// The base class's <see cref="ShuffleInput{T}.Generate" /> follows the contract "clicking does nothing
    /// while unset," so a UXML-only setup would end up unresponsive. To address that, a draw from
    /// <see cref="Options" /> is installed as the default Generate at construction time
    /// (assigning to <see cref="ShuffleInput{T}.Generate" /> still overrides it as usual).
    /// </para>
    /// <para>
    /// <c>ITweeqThemed</c> is already satisfied by the base class's <c>Theme</c> property as-is. It's
    /// declared explicitly on this type too, so it's included among what TweeqRoot queries beneath it.
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class StringShuffleInput : ShuffleInput<string>, ITweeqThemed
    {
        #region Fields

        string[] _options = Array.Empty<string>();

        #endregion

        #region Construction

        public StringShuffleInput()
        {
            this.Generate = NextFromOptions;
        }

        public StringShuffleInput(string[] options)
            : this()
        {
            this.Options = options;
        }

        #endregion

        #region Public API

        /// <summary>
        /// The population to draw from. Both the setter and getter go through a copy (decoupling the
        /// caller's array from internal state). While empty, clicking never changes the value.
        /// </summary>
        public string[] Options
        {
            get
            {
                string[] copy = new string[_options.Length];
                Array.Copy(_options, copy, _options.Length);
                return copy;
            }

            set
            {
                if (value == null)
                {
                    _options = Array.Empty<string>();
                    return;
                }

                _options = new string[value.Length];
                Array.Copy(value, _options, value.Length);
            }
        }

        /// <summary>
        /// The default <see cref="ShuffleInput{T}.Generate" />. Draws one from the options, and if it
        /// happens to draw the same value as the current one, shifts to the next one instead.
        /// </summary>
        /// <remarks>
        /// A shuffle's operational feedback is precisely "it changes when you press it," so a draw that
        /// lands on the same value is never left as a no-op (behavior Vue doesn't have, but in the
        /// original, where Generate was an application-side implementation, that was the application's responsibility).
        /// </remarks>
        public string NextFromOptions(string current)
        {
            int count = _options.Length;
            if (count == 0)
            {
                return current;
            }

            int index = UnityEngine.Random.Range(0, count);

            if (string.Equals(_options[index], current, StringComparison.Ordinal))
            {
                index = (index + 1) % count;
            }

            return _options[index];
        }

        #endregion

        #region Uxml attributes

        // Attributes can't be attached to the base (generic) class's declarations, so the name is made explicit and bridged here.
        // All of these are kept as non-public properties, to avoid adding extra public aliases.
        [UxmlAttribute("value")]
        string UxmlValue
        {
            get => this.value;
            set => this.value = value;
        }

        [UxmlAttribute("options")]
        string UxmlOptions
        {
            get => StringDropdownInput.Join(this.Options);
            set => this.Options = StringDropdownInput.Split(value);
        }

        [UxmlAttribute("disabled")]
        bool UxmlDisabled
        {
            get => this.Disabled;
            set => this.Disabled = value;
        }

        #endregion
    }
}
