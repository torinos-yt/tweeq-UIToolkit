using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A numeric tuple with a fixed 4 axes (spec §2). The default axis labels are X / Y / Z / W.
    /// </summary>
    /// <remarks>The design intent is the same as <see cref="Vec3Input"/> (no allocation on the notification path).</remarks>
    [UxmlElement]
    public partial class Vec4Input : VecInputBase, INotifyValueChanged<Vector4>
    {
        #region Constants

        const int DIMENSIONS = 4;

        #endregion

        #region Public API

        /// <summary>
        /// Fires every time the value changes. Since only 1 axis moves per gesture, spec §2's
        /// "once per frame" is satisfied without needing coalescing.
        /// </summary>
        public event Action<Vector4> ValueChanged;

        /// <summary>Fires exactly once on drag confirm, Enter, or blur (not once per axis).</summary>
        public event Action<Vector4> Confirmed;

        /// <summary>
        /// The current value. Only the name is lowercased to match the <c>INotifyValueChanged</c> convention.
        /// </summary>
        [UxmlAttribute]
        public Vector4 value
        {
            get => ReadValue();
            set
            {
                Vector4 previous = ReadValue();
                WriteAxes(value);
                Vector4 current = ReadValue();

                // The comparison is done between values re-read from the axes (the axes' held
                // values are the single source of truth). Vector4.Equals does an exact
                // component-wise comparison, so it isn't swallowed by the approximate == check.
                if (previous.Equals(current))
                {
                    return;
                }

                Notify(previous, current);
            }
        }

        /// <summary>Sets the value without firing events.</summary>
        public void SetValueWithoutNotify(Vector4 newValue)
        {
            WriteAxes(newValue);
        }

        #endregion

        #region Construction

        public Vec4Input() : base(DIMENSIONS)
        {
        }

        #endregion

        #region Notification

        protected override void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
            Vector4 current = ReadValue();

            // Only 1 axis moved, so reverting that component to its previous value reproduces the pre-change value
            Vector4 previous = current;
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

        void Notify(Vector4 previous, Vector4 current)
        {
            this.SendChangeEvent(previous, current);
            ValueChanged?.Invoke(current);
        }

        #endregion

        #region Internals

        Vector4 ReadValue()
        {
            return new Vector4(
                this.GetAxisValue(0),
                this.GetAxisValue(1),
                this.GetAxisValue(2),
                this.GetAxisValue(3));
        }

        void WriteAxes(Vector4 source)
        {
            this.SetAxesWithoutNotify(source.x, source.y, source.z, source.w);
        }

        #endregion
    }
}
