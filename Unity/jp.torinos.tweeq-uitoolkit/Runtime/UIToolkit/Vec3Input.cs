using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A numeric tuple with a fixed 3 axes (spec §2). The default axis labels are X / Y / Z.
    /// </summary>
    /// <remarks>
    /// Unlike the array-based <see cref="VecInput"/>, the value is a struct, so the
    /// notification path allocates zero bytes. Since notifications fire every frame during an
    /// axis drag, this is the one to use by default in Unity.
    /// </remarks>
    [UxmlElement]
    public partial class Vec3Input : VecInputBase, INotifyValueChanged<Vector3>
    {
        #region Constants

        const int DIMENSIONS = 3;

        #endregion

        #region Public API

        /// <summary>
        /// Fires every time the value changes. Since only 1 axis moves per gesture, spec §2's
        /// "once per frame" is satisfied without needing coalescing.
        /// </summary>
        public event Action<Vector3> ValueChanged;

        /// <summary>Fires exactly once on drag confirm, Enter, or blur (not once per axis).</summary>
        public event Action<Vector3> Confirmed;

        /// <summary>
        /// The current value. Only the name is lowercased to match the <c>INotifyValueChanged</c> convention.
        /// </summary>
        [UxmlAttribute]
        public Vector3 value
        {
            get => ReadValue();
            set
            {
                Vector3 previous = ReadValue();
                WriteAxes(value);
                Vector3 current = ReadValue();

                // The comparison is done between values re-read from the axes (the axes' held
                // values are the single source of truth). Vector3.Equals does an exact
                // component-wise comparison, so it isn't swallowed by the approximate == check.
                if (previous.Equals(current))
                {
                    return;
                }

                Notify(previous, current);
            }
        }

        /// <summary>Sets the value without firing events.</summary>
        public void SetValueWithoutNotify(Vector3 newValue)
        {
            WriteAxes(newValue);
        }

        #endregion

        #region Construction

        public Vec3Input() : base(DIMENSIONS)
        {
        }

        #endregion

        #region Notification

        protected override void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
            Vector3 current = ReadValue();

            // Only 1 axis moved, so reverting that component to its previous value reproduces the pre-change value
            Vector3 previous = current;
            if (changedAxis >= 0 && changedAxis < DIMENSIONS)
            {
                previous[changedAxis] = previousAxisValue;
            }

            Notify(previous, current);
        }

        protected override void OnConfirmed()
        {
            Confirmed?.Invoke(ReadValue());
        }

        void Notify(Vector3 previous, Vector3 current)
        {
            this.SendChangeEvent(previous, current);
            ValueChanged?.Invoke(current);
        }

        #endregion

        #region Internals

        Vector3 ReadValue()
        {
            return new Vector3(this.GetAxisValue(0), this.GetAxisValue(1), this.GetAxisValue(2));
        }

        // The 4th argument is discarded by the base class since the axis count is 3
        void WriteAxes(Vector3 source)
        {
            this.SetAxesWithoutNotify(source.x, source.y, source.z, 0f);
        }

        #endregion
    }
}
