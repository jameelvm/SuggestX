namespace SuggestX.Aggregator.Services;

/// <summary>
/// Dev-only visibility into what the polling worker has done — same purpose
/// as CollectionService's debug count endpoint: proof the previous step
/// actually worked before building the next one on top of it. Not a
/// correctness-bearing store; nothing else in the system reads this.
/// </summary>
public interface IAggregatorStats
{
    int BatchesProcessed { get; }
    int EntriesProcessed { get; }
    DateTimeOffset? LastPollAt { get; }
    string? LastProcessedKey { get; }

    void RecordBatch(string key, int entryCount);
}

public sealed class AggregatorStats : IAggregatorStats
{
    private int _batchesProcessed;
    private int _entriesProcessed;

    public int BatchesProcessed => _batchesProcessed;
    public int EntriesProcessed => _entriesProcessed;
    public DateTimeOffset? LastPollAt { get; private set; }
    public string? LastProcessedKey { get; private set; }

    public void RecordBatch(string key, int entryCount)
    {
        Interlocked.Increment(ref _batchesProcessed);
        Interlocked.Add(ref _entriesProcessed, entryCount);
        LastPollAt = DateTimeOffset.UtcNow;
        LastProcessedKey = key;
    }
}
