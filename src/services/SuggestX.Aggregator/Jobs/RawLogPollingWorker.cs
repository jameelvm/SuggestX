using Microsoft.Extensions.Options;
using SuggestX.Aggregator.Configuration;
using SuggestX.Aggregator.Services;

namespace SuggestX.Aggregator.Jobs;

/// <summary>
/// The doc's "aggregator retrieves raw data from HDFS" step, made concrete —
/// Module 1 of Phase 3: read and log what's new. Module 3: the checkpoint
/// this worker reads/advances is now durably persisted in DynamoDB (see
/// <see cref="DynamoAggregatorCheckpoint"/>), so a restart resumes instead
/// of reprocessing the whole bucket. The map-reduce into
/// suggestx-phrase-frequencies is still Module 2, not yet built.
/// </summary>
public sealed class RawLogPollingWorker(
    IRawLogReader reader,
    IAggregatorCheckpoint checkpoint,
    IAggregatorStats stats,
    IOptions<AggregatorOptions> options,
    ILogger<RawLogPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Must complete before the first poll — otherwise the first cycle
        // would run with LastProcessedKey still null and reprocess the
        // whole bucket even when a durable checkpoint already exists.
        await checkpoint.InitializeAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        // Polls immediately on startup, then on every tick — unlike
        // CollectionService's flush worker, which waits for the first tick
        // before its first flush. Deliberately different: a fresh
        // CollectionService instance has nothing buffered yet, so an
        // immediate flush would be a wasted no-op; a fresh Aggregator may
        // start well after raw logs already exist, so waiting a full
        // interval before the first read is a pure, avoidable delay.
        do
        {
            await PollAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken ct)
    {
        IReadOnlyList<Domain.RawLogBatch> batches;
        try
        {
            batches = await reader.ReadNewBatchesAsync(checkpoint.LastProcessedKey, ct);
        }
        catch (Exception ex)
        {
            // A transient S3 blip must not crash the poll loop — the next
            // tick simply retries from the same checkpoint, re-reading
            // nothing that was already processed.
            logger.LogError(ex, "Failed to list/read new raw-log batches");
            return;
        }

        if (batches.Count == 0) return;

        foreach (var batch in batches)
        {
            logger.LogInformation(
                "Read {EntryCount} entries from {Key}: {Queries}",
                batch.Entries.Count, batch.Key,
                string.Join(", ", batch.Entries.Select(e => e.Query)));

            stats.RecordBatch(batch.Key, batch.Entries.Count);

            // Advanced per-batch, not once at the end of the poll cycle, so
            // a crash partway through a large cycle re-reads only the
            // batches it hadn't finished yet — not everything since the
            // start of the cycle. Awaited: this is the durable write (see
            // DynamoAggregatorCheckpoint), not just an in-memory update.
            await checkpoint.AdvanceAsync(batch.Key, ct);
        }
    }
}
