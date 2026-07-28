using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// UI Toolkit has no CSS gap, so the gap is created using margins on the children.
    /// This is shared logic across the ParameterGrid family, so it is centralized here.
    /// </summary>
    internal static class TweeqGap
    {
        /// <summary>Distributes a main-axis margin to every child except the first.</summary>
        public static void Apply(VisualElement container, float gap, FlexDirection direction)
        {
            if (container == null)
            {
                return;
            }

            bool horizontal = direction == FlexDirection.Row || direction == FlexDirection.RowReverse;
            int count = container.childCount;

            for (int i = 0; i < count; i++)
            {
                VisualElement child = container.ElementAt(i);
                if (child == null)
                {
                    continue;
                }

                // Assigning the same value is filtered out on the inline-style side, so the layout stays clean
                float value = i == 0 ? 0f : gap;
                if (horizontal)
                {
                    child.style.marginLeft = value;
                }
                else
                {
                    child.style.marginTop = value;
                }
            }
        }
    }

    /// <summary>
    /// The 2-column layout of a label column | input column (spec §3).
    ///
    /// The original uses `grid-template-columns: minmax(60px, max-content) minmax(0, 1fr)`, but
    /// UI Toolkit has no CSS grid. So instead, the shared max-content column is reproduced by
    /// "measuring the desired label width of every descendant Parameter, and distributing
    /// max(60, the maximum value) to every row" (spec §5-5).
    /// Parameters inside a ParameterGroup also receive their width from the same Grid (spec §5-6).
    /// </summary>
    [UxmlElement]
    public partial class ParameterGrid : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>Lower bound of the label column width (the 60px in the original's minmax).</summary>
        public const float MIN_LABEL_WIDTH = 60f;

        // padding-left from the original .TqParameterGrid
        const float PADDING_LEFT = 3f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        // Reused so it isn't recreated on every Refresh
        readonly List<Parameter> _parameters = new List<Parameter>();

        IVisualElementScheduledItem _pendingRefresh;

        #endregion

        #region Public API

        /// <summary>
        /// Color theme. Propagated to every descendant Parameter / ParameterHeading /
        /// ParameterGroup on each Refresh, so there is no need to set it individually.
        /// </summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // Don't bail out even for the same instance, so rows added after the theme is set still receive it.
                // This setter is the only entry point for redistribution (fix for a gap in the M7 propagation contract)
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                RequestRefresh();
                TweeqThemeDistribution.Distribute(this, _theme);
            }
        }

        /// <summary>Immediately re-lays-out row gaps and redistributes label column widths.</summary>
        public void Refresh()
        {
            ApplyRowGaps();
            DistributeLabelWidths();
        }

        #endregion

        #region Construction

        public ParameterGrid()
        {
            this.AddToClassList("tweeq-parameter-grid");
            this.style.flexDirection = FlexDirection.Column;
            this.style.alignItems = Align.Stretch;

            ApplyStaticStyles();

            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        void ApplyStaticStyles()
        {
            this.style.paddingLeft = PADDING_LEFT;
        }

        #endregion

        #region Refresh scheduling

        /// <summary>
        /// A recalculation request called from a child (Parameter / Group). Multiple requests within the
        /// same frame are folded into one.
        /// </summary>
        internal void RequestRefresh()
        {
            if (this.panel == null)
            {
                // The scheduler doesn't run outside a panel, so finish immediately
                Refresh();
                return;
            }

            if (_pendingRefresh != null)
            {
                return;
            }

            _pendingRefresh = this.schedule.Execute(() =>
            {
                _pendingRefresh = null;
                Refresh();
            }).StartingIn(0);
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // GeometryChangedEvent bubbles, so it keeps firing endlessly while an input field is
            // animating too. Only trigger a recalculation on this element's own layout change
            // (i.e. rows added/removed, panel width change). Row additions are also notified
            // via Parameter's own AttachToPanelEvent
            if (evt == null || !ReferenceEquals(evt.target, this))
            {
                return;
            }

            // Writing the width back brings us here again, but if the value hasn't changed
            // ApplyLabelWidth writes nothing, so the loop stops after one round trip
            RequestRefresh();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            RequestRefresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // If we keep holding onto the scheduled item from the panel we were detached from, the next
            // time we're attached it would be mistaken for "already pending" and never recalculate again
            _pendingRefresh?.Pause();
            _pendingRefresh = null;
        }

        #endregion

        #region Layout

        void ApplyRowGaps()
        {
            TweeqGap.Apply(this.contentContainer, _theme.GapControl, FlexDirection.Column);
        }

        void DistributeLabelWidths()
        {
            _parameters.Clear();
            Collect(this.contentContainer);

            float target = MIN_LABEL_WIDTH;
            for (int i = 0; i < _parameters.Count; i++)
            {
                float measured = _parameters[i].MeasureLabelWidth();
                if (measured > target)
                {
                    target = measured;
                }
            }

            for (int i = 0; i < _parameters.Count; i++)
            {
                _parameters[i].ApplyLabelWidth(target);
            }
        }

        // Collect the Parameters this Grid is responsible for, distributing the theme along the way.
        // Nested ParameterGrids distribute their own widths, so we don't recurse into them (spec §5-6).
        void Collect(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            int count = element.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                VisualElement child = element.hierarchy.ElementAt(i);
                if (child == null || child is ParameterGrid)
                {
                    continue;
                }

                if (child is Parameter parameter)
                {
                    parameter.Theme = _theme;
                    _parameters.Add(parameter);

                    // The contents of a Parameter are the input field, so there's no point descending further
                    continue;
                }

                if (child is ParameterHeading heading)
                {
                    heading.Theme = _theme;
                    continue;
                }

                if (child is ParameterGroup group)
                {
                    group.Theme = _theme;
                    Collect(group.Content);
                    continue;
                }

                Collect(child);
            }
        }

        #endregion

        #region Helpers

        /// <summary>Walks up toward the ancestors (excluding itself) to find the Grid that will distribute widths (spec §5-6).</summary>
        internal static ParameterGrid Find(VisualElement element)
        {
            VisualElement current = element?.hierarchy.parent;
            while (current != null)
            {
                if (current is ParameterGrid grid)
                {
                    return grid;
                }

                current = current.hierarchy.parent;
            }

            return null;
        }

        #endregion
    }
}
