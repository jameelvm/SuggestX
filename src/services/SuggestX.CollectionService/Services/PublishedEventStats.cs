namespace SuggestX.CollectionService.Services;

/// <summary>
/// Dev-only visibility into how many events this instance has successfully
/// handed to Firehose — the successor to the old buffered-count debug
/// endpoint, which counted events this process was still holding. Nothing
/// is held here any more, so the useful question changed from "how many are
/// waiting?" to "how many made it out?".
/// </summary>
public interface IPublishedEventStats
{
    int PublishedCount { get; }
    DateTimeOffset? LastPublishedAt { get; }

    void RecordPublished();
}

public sealed class PublishedEventStats : IPublishedEventStats
{
    private int _publishedCount;

    public int PublishedCount => _publishedCount;
    public DateTimeOffset? LastPublishedAt { get; private set; }

    public void RecordPublished()
    {
        Interlocked.Increment(ref _publishedCount);
        LastPublishedAt = DateTimeOffset.UtcNow;
    }
}
