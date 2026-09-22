using System.Collections.Concurrent;
using SuggestX.CollectionService.Domain;

namespace SuggestX.CollectionService.Services;

/// <summary>
/// CollectionService's exclusive store, before it ever becomes durable: one
/// in-memory queue per instance, holding events since the last flush. Never
/// shared or coordinated across instances — each instance flushes its own
/// buffer to its own S3 object (see Module 2), so two instances can never
/// contend for the same key. Module 1 only accepts and holds; nothing reads
/// this back out yet except the debug count endpoint.
/// </summary>
public interface ISearchEventBuffer
{
    void Enqueue(BufferedSearchEvent searchEvent);

    /// <summary>Approximate — <see cref="ConcurrentQueue{T}.Count"/> is O(1)
    /// but not a snapshot under concurrent access. Good enough for a debug
    /// view; never used for anything that needs to be exact.</summary>
    int Count { get; }

    /// <summary>Atomically empties the buffer and returns what was in it.
    /// Events enqueued concurrently with a drain may or may not be included —
    /// acceptable, since this pipeline is already documented as best-effort
    /// (see DESIGN.md's failure-mode table).</summary>
    IReadOnlyList<BufferedSearchEvent> DrainAll();
}

public sealed class SearchEventBuffer : ISearchEventBuffer
{
    private readonly ConcurrentQueue<BufferedSearchEvent> _queue = new();

    public void Enqueue(BufferedSearchEvent searchEvent) => _queue.Enqueue(searchEvent);

    public int Count => _queue.Count;

    public IReadOnlyList<BufferedSearchEvent> DrainAll()
    {
        var drained = new List<BufferedSearchEvent>();
        while (_queue.TryDequeue(out var searchEvent))
            drained.Add(searchEvent);

        return drained;
    }
}
