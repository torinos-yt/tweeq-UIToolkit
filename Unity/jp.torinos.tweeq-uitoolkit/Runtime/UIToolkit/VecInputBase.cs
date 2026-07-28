using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// N 要素の数値タプル（仕様 §2）の共通実装。内部は横並びの <see cref="InputGroup"/> に N 個の
    /// <see cref="NumberInput"/> を並べただけで、フォーカス移動は素の Tab 順に任せる。
    /// </summary>
    /// <remarks>
    /// 値の運び方（float[] か Vector2/3/4 か）だけを派生に委ね、軸の生成・レンジ解決・
    /// 「1 ジェスチャ 1 回」の確定はここに集約する。
    /// 通知フックが「変わった軸番号と旧値」しか受け取らないのは、派生が配列・ボクシング・
    /// クロージャを作らずに通知できるようにするため（軸ドラッグ中は毎フレーム走るので、
    /// ここで確保すると GC が回る）。
    /// 軸の値は clamp / step を通した後の <see cref="NumberInput.value"/> が唯一の正なので、
    /// 基底側では複製を持たない（持つと丸め結果とずれる余地が増えるだけ）。
    /// 抽象クラスだが <c>[UxmlElement]</c> を付けてある。UXML 要素としては登録されないが、
    /// ここで宣言した <c>[UxmlAttribute]</c>（min/max/step/precision/…）を派生の
    /// UxmlSerializedData が継承できるのは、基底にも属性が付いている場合だけ。
    /// </remarks>
    [UxmlElement]
    public abstract partial class VecInputBase : VisualElement, ITweeqThemed
    {
        #region Constants

        const int MIN_DIMENSIONS = 2;
        const int MAX_DIMENSIONS = 4;

        // NumberInput.Precision の既定と同値。ここでずれると軸へ配った瞬間に表示桁が変わってしまう
        const int DEFAULT_PRECISION = 4;

        static readonly string[] DEFAULT_AXIS_LABELS = { "X", "Y", "Z", "W" };

        #endregion

        #region Fields

        readonly int _dimensions;
        readonly NumberInput[] _axes;
        readonly InputGroup _group;

        double[] _min;
        double[] _max;
        double[] _step;
        string[] _axisLabels;
        int _precision = DEFAULT_PRECISION;
        bool _disabled;
        bool _invalid;

        TweeqTheme _theme = TweeqTheme.Dark();

        // 子へ書き戻している最中の通知を自分の入力と誤認しないためのガード
        bool _syncing;

        #endregion

        #region Public API

        /// <summary>軸数。コンストラクタで 2〜4 に丸められる。</summary>
        public int Dimensions => _dimensions;

        /// <summary>各軸の下限。null=制限なし／長さ1=全軸共通／長さN=軸ごと。</summary>
        [UxmlAttribute]
        public double[] Min
        {
            get => CloneOrNull(_min);
            set
            {
                _min = CloneOrNull(value);
                ApplyRanges();
            }
        }

        /// <summary>各軸の上限。解釈は <see cref="Min"/> と同じ。</summary>
        [UxmlAttribute]
        public double[] Max
        {
            get => CloneOrNull(_max);
            set
            {
                _max = CloneOrNull(value);
                ApplyRanges();
            }
        }

        /// <summary>各軸の量子化幅。解釈は <see cref="Min"/> と同じ。</summary>
        [UxmlAttribute]
        public double[] Step
        {
            get => CloneOrNull(_step);
            set
            {
                _step = CloneOrNull(value);
                ApplyRanges();
            }
        }

        /// <summary>軸ラベル。null で既定（"X","Y","Z","W" の先頭 N 個）に戻る。</summary>
        [UxmlAttribute]
        public string[] AxisLabels
        {
            get => (string[])_axisLabels.Clone();
            set
            {
                _axisLabels = BuildAxisLabels(value, _dimensions);
                ApplyAxisLabels();
            }
        }

        /// <summary>全軸の静止時表示桁。既定 4（NumberInput の既定と同じ）。</summary>
        [UxmlAttribute]
        public int Precision
        {
            get => _precision;
            set
            {
                _precision = value;
                ApplyPrecision();
            }
        }

        /// <summary>
        /// 操作不能状態。各軸の <see cref="NumberInput"/> へ伝播するので、視覚も軸側の実装に従う。
        /// </summary>
        /// <remarks>
        /// tweeq-react 最新版（InputVec.vue:74-75）が同じ拡張を持つので Vue 最新仕様として扱う。
        /// </remarks>
        [UxmlAttribute]
        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value)
                {
                    return;
                }

                _disabled = value;
                ApplyDisabled();
            }
        }

        /// <summary>不正値表示。各軸の <see cref="NumberInput"/> へ伝播する。</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get => _invalid;
            set
            {
                if (_invalid == value)
                {
                    return;
                }

                _invalid = value;
                ApplyInvalid();
            }
        }

        /// <summary>配色テーマ。子の NumberInput へそのまま伝播する。</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;

                for (int i = 0; i < _axes.Length; i++)
                {
                    _axes[i].Theme = _theme;
                }
            }
        }

        /// <summary>
        /// 軸の NumberInput。Precision や Bar など個別の見た目を触りたい場合に使う
        /// （API 契約 §6 には無い追加分）。範囲外なら null。
        /// </summary>
        public NumberInput GetAxis(int index)
        {
            if (index < 0 || index >= _axes.Length)
            {
                return null;
            }

            return _axes[index];
        }

        #endregion

        #region Construction

        protected VecInputBase(int dimensions)
        {
            _dimensions = Mathf.Clamp(dimensions, MIN_DIMENSIONS, MAX_DIMENSIONS);
            _axes = new NumberInput[_dimensions];
            _axisLabels = BuildAxisLabels(null, _dimensions);

            this.AddToClassList("tweeq-vec-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };
            this.hierarchy.Add(_group);

            for (int i = 0; i < _dimensions; i++)
            {
                NumberInput axis = new NumberInput
                {
                    name = "tweeq-vec-axis-" + i.ToString(),
                    Theme = _theme,
                    LeftLabel = _axisLabels[i],
                };

                // 軸番号はイベント側から引き直す（ラムダで捕まえるとその場でクロージャが 1 個増える）
                axis.RegisterValueChangedCallback(HandleAxisValueChanged);
                axis.Confirmed += HandleAxisConfirmed;

                _axes[i] = axis;
                _group.Add(axis);
            }

            ApplyRanges();

            // 既定値どうしが一致していても、基底の値を唯一の正にしておく
            ApplyPrecision();
        }

        #endregion

        #region Derived API

        /// <summary>軸の現在値。複製を作らずに 1 軸だけ読む。範囲外は 0。</summary>
        protected float GetAxisValue(int index)
        {
            if (index < 0 || index >= _dimensions)
            {
                return 0f;
            }

            return _axes[index].value;
        }

        /// <summary>
        /// 全軸をイベント無しで書く。軸数を超える引数は捨てられるので、
        /// 2 次元なら <paramref name="v2"/> 以降に何を渡しても構わない。
        /// </summary>
        /// <remarks>
        /// 配列ではなく 4 引数で受けるのは、呼び出し側（typed 派生）に一時配列を作らせないため。
        /// </remarks>
        protected void SetAxesWithoutNotify(float v0, float v1, float v2, float v3)
        {
            _syncing = true;

            WriteAxis(0, v0);
            WriteAxis(1, v1);
            WriteAxis(2, v2);
            WriteAxis(3, v3);

            _syncing = false;
        }

        /// <summary>
        /// 軸の値がユーザー操作で変わった。<paramref name="changedAxis"/> と
        /// <paramref name="previousAxisValue"/> があれば、派生は変更前の値も複製無しで組み立てられる。
        /// </summary>
        protected virtual void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
        }

        /// <summary>ドラッグ確定・Enter・blur で 1 回だけ呼ばれる（軸数ぶんは呼ばれない）。</summary>
        protected virtual void OnConfirmed()
        {
        }

        /// <summary>
        /// <c>INotifyValueChanged&lt;T&gt;</c> を実装する派生のための ChangeEvent 送出。
        /// panel が無ければ黙って捨てる（EditMode テストや未アタッチ時に落とさないため）。
        /// </summary>
        /// <remarks>
        /// ChangeEvent はプールされるので、値型 T ならドラッグ中でも新規確保もボクシングも起きない。
        /// </remarks>
        protected void SendChangeEvent<T>(T previous, T current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<T> changeEvent = ChangeEvent<T>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Internals

        void WriteAxis(int index, float value)
        {
            if (index >= _dimensions)
            {
                return;
            }

            _axes[index].SetValueWithoutNotify(value);
        }

        void HandleAxisValueChanged(ChangeEvent<float> evt)
        {
            if (_syncing || evt == null || evt.previousValue == evt.newValue)
            {
                return;
            }

            int index = IndexOfAxis(evt.target);
            if (index < 0)
            {
                return;
            }

            OnAxesChanged(index, evt.previousValue);
        }

        // 1 ジェスチャ = 1 軸なので、受け取った確定をそのまま 1 回だけ転送する（全軸ループはしない）
        void HandleAxisConfirmed(float axisValue)
        {
            if (_syncing)
            {
                return;
            }

            OnConfirmed();
        }

        int IndexOfAxis(IEventHandler target)
        {
            for (int i = 0; i < _dimensions; i++)
            {
                if (ReferenceEquals(_axes[i], target))
                {
                    return i;
                }
            }

            return -1;
        }

        void ApplyRanges()
        {
            for (int i = 0; i < _dimensions; i++)
            {
                NumberInput axis = _axes[i];
                if (axis == null)
                {
                    continue;
                }

                axis.Min = Resolve(_min, i, double.NegativeInfinity);
                axis.Max = Resolve(_max, i, double.PositiveInfinity);
                axis.Step = Resolve(_step, i, 0.0);
            }
        }

        void ApplyPrecision()
        {
            for (int i = 0; i < _dimensions; i++)
            {
                if (_axes[i] == null)
                {
                    continue;
                }

                _axes[i].Precision = _precision;
            }
        }

        void ApplyDisabled()
        {
            for (int i = 0; i < _dimensions; i++)
            {
                if (_axes[i] == null)
                {
                    continue;
                }

                _axes[i].Disabled = _disabled;
            }
        }

        void ApplyInvalid()
        {
            for (int i = 0; i < _dimensions; i++)
            {
                if (_axes[i] == null)
                {
                    continue;
                }

                _axes[i].Invalid = _invalid;
            }
        }

        void ApplyAxisLabels()
        {
            for (int i = 0; i < _dimensions; i++)
            {
                if (_axes[i] == null)
                {
                    continue;
                }

                _axes[i].LeftLabel = _axisLabels[i];
            }
        }

        // null / 空 → 指定なし、長さ1 → 全軸共通、それ以外 → 軸ごと（足りない分は指定なし）
        static double Resolve(double[] source, int index, double fallback)
        {
            if (source == null || source.Length == 0)
            {
                return fallback;
            }

            if (source.Length == 1)
            {
                return source[0];
            }

            return index < source.Length ? source[index] : fallback;
        }

        static string[] BuildAxisLabels(string[] source, int dimensions)
        {
            string[] labels = new string[dimensions];

            for (int i = 0; i < dimensions; i++)
            {
                if (source != null && i < source.Length && !string.IsNullOrEmpty(source[i]))
                {
                    labels[i] = source[i];
                    continue;
                }

                labels[i] = i < DEFAULT_AXIS_LABELS.Length ? DEFAULT_AXIS_LABELS[i] : string.Empty;
            }

            return labels;
        }

        static double[] CloneOrNull(double[] source)
        {
            return source == null ? null : (double[])source.Clone();
        }

        #endregion
    }
}
