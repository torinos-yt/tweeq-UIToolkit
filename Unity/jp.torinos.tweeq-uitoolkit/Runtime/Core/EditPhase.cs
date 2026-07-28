namespace Tweeq.Core
{
    /// <summary>
    /// Explicit boundary that every edit gesture passes through.
    /// Cancel means restoring to the value at drag start (Escape).
    /// </summary>
    public enum EditPhase
    {
        Begin,
        Update,
        Commit,
        Cancel,
    }
}
