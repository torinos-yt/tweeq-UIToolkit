using System;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Common contract for widgets that notify a commit "exactly once per edit session".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extracts, as a bare declaration, the <c>Confirmed</c> event that each widget used to hold
    /// individually, so external asmdefs can bundle them by type (ext-custom-widgets-spec.md
    /// EXT-01-C).
    /// </para>
    /// <para>
    /// Not retrofitted onto existing widgets. The intended split is: continuously-changing values
    /// flow through <c>INotifyValueChanged&lt;T&gt;</c>, while only the commit that should count as
    /// a single Undo unit goes through this event.
    /// </para>
    /// </remarks>
    public interface ITweeqConfirmable<T>
    {
        /// <summary>Fires once, at the end of the edit session, only if the value actually changed.</summary>
        event Action<T> Confirmed;
    }
}
