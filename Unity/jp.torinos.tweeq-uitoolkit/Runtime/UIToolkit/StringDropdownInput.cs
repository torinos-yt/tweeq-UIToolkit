using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A string-specialized <see cref="DropdownInput{T}" />. A thin wrapper that lets it be placed from
    /// UXML / UI Builder, with behavior left exactly as the base class's (m7-wave2-spec.md, "UXML support").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[UxmlElement]</c> can't be attached to a generic type, so exposing it to UXML requires a
    /// non-generic class with the type parameter closed. The base class's public API can be used as-is,
    /// so all that's here is the bridging of UXML attributes.
    /// </para>
    /// <para>
    /// Every attribute is a non-public property with its name made explicit via <c>[UxmlAttribute("...")]</c>.
    /// Attributes can't be attached to the base (generic) class's declarations, and from C# the base
    /// class's <see cref="DropdownInput{T}.Options" /> and similar should be used directly, so this shape avoids adding extra public aliases.
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class StringDropdownInput : DropdownInput<string>
    {
        #region Constants

        // string[] can't be used raw as a UXML attribute, so it's flattened to a single comma-separated
        // string (the same shape Unity's own ToggleDropdown uses). An option containing a comma can't be
        // represented in UXML, so in that case Options should be passed from code instead.
        const char SEPARATOR = ',';

        #endregion

        #region Construction

        public StringDropdownInput()
        {
        }

        public StringDropdownInput(string[] options)
            : base(options)
        {
        }

        #endregion

        #region Uxml attributes

        [UxmlAttribute("value")]
        string UxmlValue
        {
            get => this.value;
            set => this.value = value;
        }

        [UxmlAttribute("options")]
        string UxmlOptions
        {
            get => Join(this.Options);
            set => this.Options = Split(value);
        }

        [UxmlAttribute("labels")]
        string UxmlLabels
        {
            get => Join(this.Labels);

            // An empty string means "no label specified," i.e. it reverts to the Labelizer / the value itself.
            set
            {
                string[] labels = Split(value);
                this.Labels = labels.Length > 0 ? labels : null;
            }
        }

        [UxmlAttribute("prefix")]
        string UxmlPrefix
        {
            get => this.Prefix;
            set => this.Prefix = value;
        }

        [UxmlAttribute("suffix")]
        string UxmlSuffix
        {
            get => this.Suffix;
            set => this.Suffix = value;
        }

        [UxmlAttribute("disabled")]
        bool UxmlDisabled
        {
            get => this.Disabled;
            set => this.Disabled = value;
        }

        [UxmlAttribute("invalid")]
        bool UxmlInvalid
        {
            get => this.Invalid;
            set => this.Invalid = value;
        }

        #endregion

        #region Csv

        /// <summary>Splits a comma-separated string into an array of options. Empty elements are dropped.</summary>
        public static string[] Split(string csv)
        {
            if (string.IsNullOrEmpty(csv))
            {
                return Array.Empty<string>();
            }

            string[] parts = csv.Split(SEPARATOR);
            List<string> result = new List<string>(parts.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result.ToArray();
        }

        /// <summary>Folds an array of options into a comma-separated string. null becomes an empty string.</summary>
        public static string Join(string[] values)
        {
            return values == null || values.Length == 0
                ? string.Empty
                : string.Join(SEPARATOR.ToString(), values);
        }

        #endregion
    }
}
