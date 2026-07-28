namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Position within a group. Used to flatten the corner that touches a neighbor (spec §1).
    /// </summary>
    public enum TweeqBoxPosition
    {
        /// <summary>Standalone. All corners keep their full rounding.</summary>
        None,

        /// <summary>First. Flattens the 2 corners on the leading side.</summary>
        Start,

        /// <summary>Middle. Flattens all 4 corners.</summary>
        Middle,

        /// <summary>Last. Flattens the 2 corners on the trailing side.</summary>
        End,
    }

    /// <summary>
    /// An input box that InputGroup can assign a position to.
    /// Applying corner rounding is each box's own responsibility (the group holds neither divider
    /// lines nor merged borders).
    /// </summary>
    public interface ITweeqInputBox
    {
        /// <summary>Position within a horizontal (FlexDirection.Row) group.</summary>
        TweeqBoxPosition InlinePosition { get; set; }

        /// <summary>Position within a vertical (FlexDirection.Column) group.</summary>
        TweeqBoxPosition BlockPosition { get; set; }
    }
}
