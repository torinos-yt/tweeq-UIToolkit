using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Common implementation for an N-element numeric tuple (spec §2). Internally it's just N
    /// <see cref="NumberInput"/> instances lined up in a horizontal <see cref="InputGroup"/>, leaving
    /// focus movement to plain Tab order.
    /// </summary>
    /// <remarks>
    /// Only the value transport (float[] vs. Vector2/3/4) is delegated to derived classes; axis creation,
    /// range resolution, and "one confirm per gesture" are all centralized here.
    /// The change hook only receives "the axis index that changed and its previous value" so that derived
    /// classes can be notified without allocating arrays, boxing, or closures (this runs every frame during
    /// an axis drag, so allocating here would trigger GC).
    /// An axis's value is only ever the single source of truth after passing through
    /// <see cref="NumberInput.value"/>'s clamp/step, so the base class keeps no duplicate
    /// (keeping one would only create room for it to drift from the rounded result).
    /// This is an abstract class but still carries <c>[UxmlElement]</c>. It isn't registered as a UXML
    /// element itself, but the <c>[UxmlAttribute]</c>s declared here (min/max/step/precision/…) can only be
    /// inherited by a derived class's UxmlSerializedData if the base class also carries the attribute.
    /// </remarks>
    [UxmlElement]
    public abstract partial class VecInputBase : VisualElement, ITweeqThemed
    {
        #region Constants

        const int MIN_DIMENSIONS = 2;
        const int MAX_DIMENSIONS = 4;

        // Matches NumberInput.Precision's default. If this drifts, the displayed digits would change the moment it's distributed to the axes
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

        // A guard to avoid mistaking notifications during write-back to children as our own input
        bool _syncing;

        #endregion

        #region Public API

        /// <summary>The number of axes. Clamped to 2–4 in the constructor.</summary>
        public int Dimensions => _dimensions;

        /// <summary>The lower bound for each axis. null = unbounded / length 1 = shared across all axes / length N = per axis.</summary>
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

        /// <summary>The upper bound for each axis. Interpreted the same way as <see cref="Min"/>.</summary>
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

        /// <summary>The quantization step for each axis. Interpreted the same way as <see cref="Min"/>.</summary>
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

        /// <summary>The axis labels. null reverts to the default (the first N of "X","Y","Z","W").</summary>
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

        /// <summary>The at-rest display digits for all axes. Default 4 (same as NumberInput's default).</summary>
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
        /// The disabled state. Propagates to each axis's <see cref="NumberInput"/>, so the visuals also follow the axis-side implementation.
        /// </summary>
        /// <remarks>
        /// Another reference implementation's latest version (InputVec.vue:74-75) carries the same
        /// extension, so this is treated as matching the latest Vue spec.
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

        /// <summary>Invalid-value display. Propagates to each axis's <see cref="NumberInput"/>.</summary>
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

        /// <summary>The color theme. Propagates directly to the child NumberInputs.</summary>
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
        /// The axis's NumberInput. Use this when you need to touch an individual visual detail like
        /// Precision or Bar (an addition not present in API contract §6). Null if out of range.
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

                // The axis index is re-derived from the event side (capturing it in a lambda would add a closure allocation on the spot)
                axis.RegisterValueChangedCallback(HandleAxisValueChanged);
                axis.Confirmed += HandleAxisConfirmed;

                _axes[i] = axis;
                _group.Add(axis);
            }

            ApplyRanges();

            // Even though the default values already match, treat the base class's value as the single source of truth
            ApplyPrecision();
        }

        #endregion

        #region Derived API

        /// <summary>The axis's current value. Reads a single axis without making a copy. 0 if out of range.</summary>
        protected float GetAxisValue(int index)
        {
            if (index < 0 || index >= _dimensions)
            {
                return 0f;
            }

            return _axes[index].value;
        }

        /// <summary>
        /// Writes all axes without raising events. Arguments beyond the axis count are discarded, so for a
        /// 2D instance it doesn't matter what you pass for <paramref name="v2"/> onward.
        /// </summary>
        /// <remarks>
        /// Taking 4 arguments instead of an array is so the caller (a typed derived class) doesn't need to allocate a temporary array.
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
        /// An axis's value changed due to user interaction. Given <paramref name="changedAxis"/> and
        /// <paramref name="previousAxisValue"/>, a derived class can assemble the pre-change value too, without a copy.
        /// </summary>
        protected virtual void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
        }

        /// <summary>Called exactly once on drag-confirm, Enter, or blur (not once per axis).</summary>
        protected virtual void OnConfirmed()
        {
        }

        /// <summary>
        /// Dispatches a ChangeEvent for derived classes implementing <c>INotifyValueChanged&lt;T&gt;</c>.
        /// Silently dropped if there's no panel (so it doesn't fail during EditMode tests or before attachment).
        /// </summary>
        /// <remarks>
        /// ChangeEvent is pooled, so for a value type T, no new allocation or boxing occurs even during a drag.
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

        // One gesture = one axis, so the received confirm is forwarded exactly once as-is (no looping over all axes)
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

        // null / empty → unspecified, length 1 → shared across all axes, otherwise → per axis (missing entries are unspecified)
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
