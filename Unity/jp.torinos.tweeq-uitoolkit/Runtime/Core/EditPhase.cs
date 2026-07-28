namespace Tweeq.Core
{
    /// <summary>
    /// すべての編集ジェスチャが通過する明示的な境界。
    /// Cancel はドラッグ開始値への復元を意味する（Escape）。
    /// </summary>
    public enum EditPhase
    {
        Begin,
        Update,
        Commit,
        Cancel,
    }
}
