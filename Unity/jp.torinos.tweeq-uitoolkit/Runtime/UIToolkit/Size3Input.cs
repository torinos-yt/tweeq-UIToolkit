using System;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A 3D size input with a ratio lock: the 3-axis counterpart of <see cref="SizeInput"/>.
    /// Fuses the same chain toggle onto the right end of a <see cref="Vec3Input"/>; while locked, changing one
    /// axis makes the other two follow.
    /// </summary>
    /// <remarks>
    /// Same semantics as <see cref="SizeInput"/>: the following baseline is "the value at the start of editing",
    /// retaken at the start of each gesture (which closes with Confirmed), and the lock defaults to on.
    /// The calculation is <see cref="SizeLogic.Apply3"/>.
    /// </remarks>
    public class Size3Input : VisualElement, INotifyValueChanged<Vector3>, ITweeqThemed
    {
        #region Fields

        readonly InputGroup _group;
        readonly Vec3Input _field;
        readonly ButtonToggleInput _chain;
        readonly VisualElement _chainIcon;

        Vector3 _value;
        Vector3 _baseline;
        bool _hasBaseline;
        bool _keepRatio = true;
        bool _disabled;
        bool _syncing;

        TweeqTheme _theme = TweeqTheme.Dark();

        #endregion

        #region Public API

        /// <summary>Fires every time the value changes.</summary>
        public event Action<Vector3> ValueChanged;

        /// <summary>Fires only once per gesture, on drag confirm, Enter, or blur.</summary>
        public event Action<Vector3> Confirmed;

        /// <summary>Fires when the ratio lock toggles (including automatic release).</summary>
        public event Action<bool> KeepRatioChanged;

        public Vector3 value
        {
            get => _value;
            set
            {
                if (_value.Equals(value))
                {
                    return;
                }

                Vector3 previous = _value;
                SetValueWithoutNotify(value);
                Notify(previous, _value);
            }
        }

        /// <summary>Ratio lock. Defaults to on.</summary>
        public bool KeepRatio
        {
            get => _keepRatio;
            set
            {
                if (_keepRatio == value)
                {
                    return;
                }

                _keepRatio = value;
                _chain.SetValueWithoutNotify(value);
                _hasBaseline = false;
                _chainIcon.MarkDirtyRepaint();
                KeepRatioChanged?.Invoke(value);
            }
        }

        /// <summary>The numeric tuple itself. Use this to set per-axis ranges, steps and Precision.</summary>
        public Vec3Input Field => _field;

        /// <summary>The chain toggle itself.</summary>
        public ButtonToggleInput Chain => _chain;

        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                _group.Theme = _theme;
                _field.Theme = _theme;
                _chain.Theme = _theme;
                ApplyChainSize();
                ApplyBoxFusion();
                _chainIcon.MarkDirtyRepaint();
            }
        }

        /// <summary>Disabled state. Distributed to both the numeric field and the chain toggle.</summary>
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
                _field.Disabled = _disabled;
                _chain.Disabled = _disabled;
            }
        }

        /// <summary>Sets the value without firing a ChangeEvent. The editing baseline is also retaken here.</summary>
        public void SetValueWithoutNotify(Vector3 newValue)
        {
            _value = newValue;

            _syncing = true;
            _field.SetValueWithoutNotify(newValue);
            _syncing = false;

            _hasBaseline = false;
        }

        #endregion

        #region Construction

        public Size3Input()
        {
            this.AddToClassList("tweeq-size-input");
            this.style.flexDirection = FlexDirection.Row;
            this.style.flexGrow = 1f;

            _group = new InputGroup { Theme = _theme };
            _field = new Vec3Input { name = "tweeq-size3-field", Theme = _theme };
            _chain = new ButtonToggleInput { name = "tweeq-size-chain", Theme = _theme };
            _chain.SetValueWithoutNotify(_keepRatio);
            _chain.style.flexGrow = 0f;
            _chain.style.flexShrink = 0f;

            _chainIcon = new VisualElement
            {
                name = "tweeq-size-chain-icon",
                pickingMode = PickingMode.Ignore,
            };
            _chainIcon.style.position = Position.Absolute;
            _chainIcon.style.left = 0f;
            _chainIcon.style.top = 0f;
            _chainIcon.style.right = 0f;
            _chainIcon.style.bottom = 0f;
            _chainIcon.generateVisualContent += context =>
                SizeChainIcon.Paint(context, _chainIcon, _theme, _keepRatio);
            _chain.hierarchy.Add(_chainIcon);

            _field.ValueChanged += OnFieldChanged;
            _field.Confirmed += OnFieldConfirmed;
            _chain.Confirmed += next => this.KeepRatio = next;

            _group.Add(_field);
            _group.Add(_chain);
            this.hierarchy.Add(_group);

            // Vec3Input is not an ITweeqInputBox, so InputGroup cannot round its ends; reapplied after each layout pass
            this.RegisterCallback<GeometryChangedEvent>(evt => ApplyBoxFusion());

            ApplyChainSize();
            ApplyBoxFusion();
        }

        #endregion

        #region Internals

        void ApplyChainSize()
        {
            float size = _theme != null ? _theme.InputHeight : 24f;
            _chain.style.width = size;
            _chain.style.flexBasis = size;
        }

        void ApplyBoxFusion()
        {
            _field.GetAxis(0).InlinePosition = TweeqBoxPosition.Start;
            _field.GetAxis(1).InlinePosition = TweeqBoxPosition.Middle;
            _field.GetAxis(2).InlinePosition = TweeqBoxPosition.Middle;
            _chain.InlinePosition = TweeqBoxPosition.End;
        }

        void OnFieldChanged(Vector3 next)
        {
            if (_syncing)
            {
                return;
            }

            if (!_hasBaseline)
            {
                // This gesture's baseline is "the value before it started moving". Held fixed until the next Confirmed
                _baseline = _value;
                _hasBaseline = true;
            }

            Size3ApplyResult result = SizeLogic.Apply3(
                _value.x, _value.y, _value.z,
                next.x, next.y, next.z,
                _baseline.x, _baseline.y, _baseline.z,
                _keepRatio);

            this.KeepRatio = result.KeepRatio;

            Vector3 applied = new Vector3((float)result.X, (float)result.Y, (float)result.Z);

            // Realign the field's display to the result even when the input was cancelled out
            if (!_field.value.Equals(applied))
            {
                _syncing = true;
                _field.SetValueWithoutNotify(applied);
                _syncing = false;
            }

            if (_value.Equals(applied))
            {
                return;
            }

            Vector3 previous = _value;
            _value = applied;
            Notify(previous, applied);
        }

        void OnFieldConfirmed(Vector3 fieldValue)
        {
            if (_syncing)
            {
                return;
            }

            _hasBaseline = false;
            Confirmed?.Invoke(_value);
        }

        void Notify(Vector3 previous, Vector3 current)
        {
            if (this.panel != null)
            {
                using (ChangeEvent<Vector3> changeEvent = ChangeEvent<Vector3>.GetPooled(previous, current))
                {
                    changeEvent.target = this;
                    this.SendEvent(changeEvent);
                }
            }

            ValueChanged?.Invoke(current);
        }

        #endregion
    }
}
