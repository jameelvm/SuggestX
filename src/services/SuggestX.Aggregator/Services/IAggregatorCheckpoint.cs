namespace SuggestX.Aggregator.Services;

/// <summary>
/// Tracks the last S3 key this Aggregator has fully processed, so a poll
/// cycle only lists objects newer than that — the sortable key prefix
/// CollectionService writes (see SearchEventFlushWorker) is what makes
/// "newer than X" a cheap S3 ListObjectsV2/StartAfter call instead of
/// reading every object in the bucket every cycle.
/// <para>
/// In-memory only for now (Module 1) — a restart reprocesses from the start
/// of the bucket. Module 3 adds durable persistence so a restart resumes
/// instead; see DESIGN.md's failure-mode table for why that gap is
/// deliberately left open until then, not silently assumed closed.
/// </para>
/// </summary>
public interface IAggregatorCheckpoint
{
    string? LastProcessedKey { get; }
    void Advance(string key);
}

public sealed class InMemoryAggregatorCheckpoint : IAggregatorCheckpoint
{
    private string? _lastProcessedKey;

    public string? LastProcessedKey => _lastProcessedKey;

    public void Advance(string key) => _lastProcessedKey = key;
}
