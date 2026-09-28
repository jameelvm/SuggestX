namespace SuggestX.TrieBuilder.Services;

/// <summary>
/// Dev-only visibility into the build worker's progress — same purpose as
/// every other service's stats holder (<c>IAggregatorStats</c>,
/// <c>IPublishedEventStats</c>): proof the previous step actually worked.
/// <see cref="CurrentVersion"/> is this service's own local counter, not
/// yet the ZooKeeper-coordinated "current version" pointer Module 3 adds —
/// see <c>TrieBuildWorker</c>'s remarks for exactly what that means today.
/// </summary>
public interface ITrieBuildStats
{
    int CurrentVersion { get; }
    int FlattenedPrefixCount { get; }
    DateTimeOffset? LastBuildAt { get; }

    void RecordBuild(int version, int flattenedPrefixCount);
}

public sealed class TrieBuildStats : ITrieBuildStats
{
    public int CurrentVersion { get; private set; }
    public int FlattenedPrefixCount { get; private set; }
    public DateTimeOffset? LastBuildAt { get; private set; }

    public void RecordBuild(int version, int flattenedPrefixCount)
    {
        CurrentVersion = version;
        FlattenedPrefixCount = flattenedPrefixCount;
        LastBuildAt = DateTimeOffset.UtcNow;
    }
}
