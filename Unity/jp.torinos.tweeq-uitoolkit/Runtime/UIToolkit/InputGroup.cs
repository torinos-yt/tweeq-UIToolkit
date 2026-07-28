using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A container that lines up input boxes adjacent to each other (spec §1).
    /// It has no divider lines or merged borders — the "connection" is expressed only through a 2px gap and each child's corner rounding.
    /// </summary>
    [UxmlElement]
    public partial class InputGroup : VisualElement, ITweeqThemed
    {
        #region Fields

        FlexDirection _direction = FlexDirection.Row;
        TweeqTheme _theme = TweeqTheme.Dark();

        // GeometryChangedEvent fires on every layout pass, so this guards
        // reassignment to only happen when the child composition changes
        int _positionedChildCount = -1;

        #endregion

        #region Public API

        /// <summary>The layout direction. Row (default) assigns InlinePosition; Column assigns BlockPosition.</summary>
        [UxmlAttribute("direction")]
        public FlexDirection Direction
        {
            get => _direction;
            set
            {
                if (_direction == value)
                {
                    return;
                }

                _direction = value;
                this.style.flexDirection = _direction;
                RefreshPositions();
            }
        }

        /// <summary>The color theme that supplies the gap value. Falls back to Dark() if null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                RefreshPositions();
            }
        }

        /// <summary>Adds a child and reassigns positions.</summary>
        public new void Add(VisualElement child)
        {
            if (child == null)
            {
                return;
            }

            base.Add(child);
            RefreshPositions();
        }

        /// <summary>Removes a child and reassigns positions.</summary>
        public new void Remove(VisualElement child)
        {
            if (child == null || child.parent != this)
            {
                return;
            }

            base.Remove(child);
            RefreshPositions();
        }

        /// <summary>Removes all children.</summary>
        public new void Clear()
        {
            base.Clear();
            RefreshPositions();
        }

        /// <summary>
        /// Reassigns child positions and gaps. Only children implementing <see cref="ITweeqInputBox"/> are targeted for positioning.
        /// Call manually if the child composition was modified directly.
        /// </summary>
        public void RefreshPositions()
        {
            int childCount = this.childCount;
            int boxCount = 0;

            for (int i = 0; i < childCount; i++)
            {
                if (this.ElementAt(i) is ITweeqInputBox)
                {
                    boxCount++;
                }
            }

            _positionedChildCount = childCount;

            float gap = _theme != null ? _theme.GapGroup : 0f;
            bool row = _direction == FlexDirection.Row || _direction == FlexDirection.RowReverse;
            int boxIndex = 0;

            for (int i = 0; i < childCount; i++)
            {
                VisualElement child = this.ElementAt(i);
                if (child == null)
                {
                    continue;
                }

                ApplyGap(child, gap, row, i == childCount - 1);

                if (!(child is ITweeqInputBox box))
                {
                    continue;
                }

                // With fewer than 2, no "connection" exists, so nothing is assigned (spec §1)
                TweeqBoxPosition position = boxCount < 2
                    ? TweeqBoxPosition.None
                    : Resolve(boxIndex, boxCount);

                if (row)
                {
                    box.InlinePosition = position;
                    box.BlockPosition = TweeqBoxPosition.None;
                }
                else
                {
                    box.BlockPosition = position;
                    box.InlinePosition = TweeqBoxPosition.None;
                }

                ApplyStretch(child);
                boxIndex++;
            }
        }

        #endregion

        #region Construction

        public InputGroup()
        {
            this.AddToClassList("tweeq-input-group");
            this.style.flexDirection = _direction;
            this.style.flexGrow = 1f;

            // Adding children via InputGroup.Add is the canonical route, but these two fallbacks
            // catch direct hierarchy manipulation or Add calls made through the VisualElement type
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel, TrickleDown.TrickleDown);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        #endregion

        #region Internals

        static TweeqBoxPosition Resolve(int index, int count)
        {
            if (index == 0)
            {
                return TweeqBoxPosition.Start;
            }

            return index == count - 1 ? TweeqBoxPosition.End : TweeqBoxPosition.Middle;
        }

        // UI Toolkit 6000.3's inline styles have no flex gap, so child margins are used instead.
        // Only the last one has its margin removed, because this is "spacing", not "padding".
        // The check runs over all children, not just ITweeqInputBox implementers (spacing is still needed even if the last child is a label, etc.)
        static void ApplyGap(VisualElement child, float gap, bool row, bool last)
        {
            float value = last ? 0f : gap;
            child.style.marginRight = row ? value : 0f;
            child.style.marginBottom = row ? 0f : value;
        }

        // Splits width (height) evenly. NumberInput's children are all absolutely positioned and have no content width,
        // so without this they'd collapse down to minWidth. If the caller has already specified it explicitly, that is respected
        static void ApplyStretch(VisualElement child)
        {
            if (child.style.flexGrow.keyword == StyleKeyword.Null)
            {
                child.style.flexGrow = 1f;
            }

            if (child.style.flexBasis.keyword == StyleKeyword.Null)
            {
                child.style.flexBasis = 0f;
            }
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            RefreshPositions();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // Even if this callback re-enters due to a style update, do nothing if the child count is unchanged
            if (_positionedChildCount == this.childCount)
            {
                return;
            }

            RefreshPositions();
        }

        #endregion
    }
}
