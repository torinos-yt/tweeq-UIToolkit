using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A numeric tuple whose dimension count is decided at runtime (spec §2). If the axis
    /// count is fixed, use <see cref="Vec2Input"/> / <see cref="Vec3Input"/> / <see cref="Vec4Input"/> instead.
    /// </summary>
    /// <remarks>
    /// Since the value type is an array, <c>INotifyValueChanged&lt;T&gt;</c> is not adopted
    /// here (intentional deviation from spec §5-3). Both directions always go through a
    /// defensive copy, and the internal array's reference is never exposed outward. As a
    /// trade-off, this allocates one array per notification, so choose the typed version for
    /// use cases that dislike GC during drags.
    /// </remarks>
    [UxmlElement]
    public partial class VecInput : VecInputBase
    {
        #region Constants

        // The axis count used via UXML (i.e. parameterless construction). Dimensions can't be
        // moved once the array is allocated, so it can't be a UXML attribute; the default is
        // set to the minimum axis count.
        const int UXML_DIMENSIONS = 2;

        #endregion

        #region Public API

        /// <summary>
        /// Fires every time the value changes. Since only 1 axis moves per gesture, spec §2's
        /// "once per frame" is satisfied without needing coalescing.
        /// </summary>
        public event Action<float[]> ValueChanged;

        /// <summary>Fires exactly once on drag confirm, Enter, or blur (not once per axis).</summary>
        public event Action<float[]> Confirmed;

        /// <summary>
        /// The current value. get returns a copy, and set receives a copy.
        /// When supplied from UXML, the length must match the axis count (default
        /// <see cref="UXML_DIMENSIONS"/>) (a mismatched array is ignored with a warning).
        /// </summary>
        [UxmlAttribute]
        public float[] Value
        {
            get => ReadValue();
            set
            {
                if (!SetValueWithoutNotifyInternal(value))
                {
                    return;
                }

                RaiseValueChanged();
            }
        }

        /// <summary>Sets the value without firing events. Ignored if the length doesn't match the axis count.</summary>
        public void SetValueWithoutNotify(float[] value)
        {
            SetValueWithoutNotifyInternal(value);
        }

        #endregion

        #region Construction

        /// <summary>
        /// The default constructor for construction from UXML / UI Builder. The axis count is
        /// <see cref="UXML_DIMENSIONS"/>. Use <see cref="VecInput(int)"/> if you want to choose the axis count.
        /// </summary>
        public VecInput() : base(UXML_DIMENSIONS)
        {
        }

        public VecInput(int dimensions) : base(dimensions)
        {
        }

        #endregion

        #region Notification

        protected override void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
            // The array version doesn't distinguish "which axis", so it just distributes the current value as-is and is done
            RaiseValueChanged();
        }

        protected override void OnConfirmed()
        {
            Confirmed?.Invoke(ReadValue());
        }

        void RaiseValueChanged()
        {
            ValueChanged?.Invoke(ReadValue());
        }

        #endregion

        #region Internals

        bool SetValueWithoutNotifyInternal(float[] value)
        {
            if (value == null)
            {
                Debug.LogWarning("VecInput: ignored a null value assignment.");
                return false;
            }

            if (value.Length != this.Dimensions)
            {
                Debug.LogWarning(
                    $"VecInput: ignored a value of length {value.Length} (expected {this.Dimensions} axes).");
                return false;
            }

            // The axis count is clamped to 2-4, so any excess is discarded by the base class
            this.SetAxesWithoutNotify(
                value[0],
                value[1],
                this.Dimensions > 2 ? value[2] : 0f,
                this.Dimensions > 3 ? value[3] : 0f);

            return true;
        }

        float[] ReadValue()
        {
            float[] snapshot = new float[this.Dimensions];

            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = this.GetAxisValue(i);
            }

            return snapshot;
        }

        #endregion
    }
}
