using System;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// string 特化の <see cref="ShuffleInput{T}" />。UXML / UI Builder から置けるようにするための
    /// ラッパ（m7-wave2-spec.md「UXML 対応」）と、選択肢からのランダム抽選を内蔵する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[UxmlElement]</c> はジェネリック型に付けられないため、UXML 化するには型を閉じた
    /// 非ジェネリックのクラスが必要になる。
    /// </para>
    /// <para>
    /// 基底の <see cref="ShuffleInput{T}.Generate" /> は「未設定ならクリックしても何も起きない」
    /// 契約なので、UXML だけで組んだ場合は無反応になってしまう。そこで
    /// <see cref="Options" /> からの抽選を既定の Generate として構築時に入れておく
    /// （<see cref="ShuffleInput{T}.Generate" /> へ代入すれば従来どおり差し替えられる）。
    /// </para>
    /// <para>
    /// <c>ITweeqThemed</c> は基底の <c>Theme</c> プロパティがそのまま満たす。TweeqRoot が
    /// 配下を Query する対象に入るよう、この型でも明示しておく。
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
        /// 抽選の母集団。設定・取得ともにコピーを通す（呼び出し側の配列と内部状態を切り離す）。
        /// 空の間はクリックしても値が動かない。
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
        /// 既定の <see cref="ShuffleInput{T}.Generate" />。選択肢から 1 つ引き、
        /// 現在値と同じものを引いてしまったら隣へずらす。
        /// </summary>
        /// <remarks>
        /// シャッフルは「押したら変わる」ことが操作のフィードバックそのものなので、
        /// 同じ値を引いた回を空振りにしない（Vue には無い挙動だが、Generate が
        /// アプリ側実装だった原典ではそこがアプリの責務だった）。
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

        // 基底（ジェネリック）側の宣言には属性を付けられないので、ここで名前を明示して橋渡しする。
        // public な別名を増やさないため、いずれも非公開プロパティにしてある
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
