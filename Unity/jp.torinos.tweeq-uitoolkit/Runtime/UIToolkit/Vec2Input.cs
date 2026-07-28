using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A numeric tuple with a fixed 2 axes (spec §2). The default axis labels are X / Y.
    /// </summary>
    /// <remarks>The design intent is the same as <see cref="Vec3Input"/> (no allocation on the notification path).</remarks>
    [UxmlElement]
    public partial class Vec2Input : VecInputBase, INotifyValueChanged<Vector2>
    {
        #region Constants

        const int DIMENSIONS = 2;

        #endregion

        #region Public API

        /// <summary>
        /// Fires every time the value changes. Since only 1 axis moves per gesture, spec §2's
        /// "once per frame" is satisfied without needing coalescing.
        /// </summary>
        public event Action<Vector2> ValueChanged;

        /// <summary>Fires exactly once on drag confirm, Enter, or blur (not once per axis).</summary>
        public event Action<Vector2> Confirmed;

        /// <summary>
        /// The current value. Only the name is lowercased to match the <c>INotifyValueChanged</c> convention.
        /// </summary>
        [UxmlAttribute]
        public Vector2 value
        {
            get => ReadValue();
            set
            {
                Vector2 previous = ReadValue();
                WriteAxes(value);
                Vector2 current = ReadValue();

                // The comparison is done between values re-read from the axes (the axes' held
                // values are the single source of truth). Vector2.Equals does an exact
                // component-wise comparison, so it isn't swallowed by the approximate == check.
                if (previous.Equals(current))
                {
                    return;
                }

                Notify(previous, current);
            }
        }

        /// <summary>Sets the value without firing events.</summary>
        public void SetValueWithoutNotify(Vector2 newValue)
        {
            WriteAxes(newValue);
        }

        #endregion

        #region Construction

        public Vec2Input() : base(DIMENSIONS)
        {
        }

        #endregion

        #region Notification

        protected override void OnAxesChanged(int changedAxis, float previousAxisValue)
        {
            Vector2 current = ReadValue();

            // Only 1 axis moved, so reverting that component to its previous value reproduces the pre-change value
            Vector2 previous = current;
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

        void Notify(Vector2 previous, Vector2 current)
        {
            this.SendChangeEvent(previous, current);
            ValueChanged?.Invoke(current);
        }

        #endregion

        #region Internals

        Vector2 ReadValue()
        {
            return new Vector2(this.GetAxisValue(0), this.GetAxisValue(1));
        }

        // The 3rd and 4th arguments are discarded by the base class since the axis count is 2
        void WriteAxes(Vector2 source)
        {
            this.SetAxesWithoutNotify(source.x, source.y, 0f, 0f);
        }

        #endregion
    }
}
