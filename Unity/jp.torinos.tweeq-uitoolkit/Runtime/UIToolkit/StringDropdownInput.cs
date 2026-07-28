using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// string 特化の <see cref="DropdownInput{T}" />。UXML / UI Builder から置けるようにするための
    /// 薄いラッパで、振る舞いは基底そのまま（m7-wave2-spec.md「UXML 対応」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[UxmlElement]</c> はジェネリック型に付けられないため、UXML 化するには型を閉じた
    /// 非ジェネリックのクラスが必要になる。基底の public API はそのまま使えるので、
    /// ここにあるのは UXML 属性の橋渡しだけ。
    /// </para>
    /// <para>
    /// 属性はすべて <c>[UxmlAttribute("...")]</c> で名前を明示した非公開プロパティにしている。
    /// 基底側（ジェネリック）の宣言には属性を付けられず、かつ C# からは基底の
    /// <see cref="DropdownInput{T}.Options" /> などをそのまま使うべきなので、
    /// public な別名を増やさないための形。
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class StringDropdownInput : DropdownInput<string>
    {
        #region Constants

        // string[] を素で UXML 属性にはできないので、カンマ区切り 1 本にする
        // （Unity 自身の ToggleDropdown と同じ形）。要素にカンマを含む選択肢は
        // UXML では表現できないため、その場合はコードから Options を渡す
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

            // 空文字は「ラベル指定なし」＝ Labelizer / 値そのものへ戻す意味にする
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

        /// <summary>カンマ区切り文字列を選択肢の配列へ分解する。空要素は落とす。</summary>
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

        /// <summary>選択肢の配列をカンマ区切り文字列へ畳む。null は空文字。</summary>
        public static string Join(string[] values)
        {
            return values == null || values.Length == 0
                ? string.Empty
                : string.Join(SEPARATOR.ToString(), values);
        }

        #endregion
    }
}
