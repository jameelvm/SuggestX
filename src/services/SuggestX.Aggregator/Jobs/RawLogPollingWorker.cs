using Microsoft.Extensions.Options;
using SuggestX.Aggregator.Configuration;
using SuggestX.Aggregator.Services;

namespace SuggestX.Aggregator.Jobs;

/// <summary>
/// The doc's "aggregator" step, fully assembled: Module 1 reads what's new,
/// Module 2 (<see cref="IPhraseFrequencyWriter"/>) turns it into phrase
/// counts in suggestx-phrase-frequencies, Module 3
/// (<see cref="DynamoAggregatorCheckpoint"/>) durably remembers how far it
/// got, so a restart resumes instead of reprocessing the whole bucket.
/// </summary>
public sealed class RawLogPollingWorker(
    IRawLogReader reader,
    IPhraseFrequencyWriter frequencyWriter,
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

            try
            {
                // Counts applied BEFORE the checkpoint advances, and the
                // checkpoint only advances if this succeeds — reversing the
                // order would let a crash between the two silently lose a
                // batch's counts forever (checkpoint says "done," but the
                // ADD never happened). A failure here stops this cycle
                // rather than skipping ahead to the next batch: checkpoints
                // must advance strictly in order, so processing batch N+1
                // and advancing past batch N's key when N failed would
                // permanently strand N's counts — it would never be read
                // again. The next poll cycle retries from the same
                // checkpoint instead.
                await frequencyWriter.ApplyAsync(batch.Entries, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to apply frequency counts for {Key} — stopping this poll cycle, will retry next tick",
                    batch.Key);
                return;
            }

            // Advanced per-batch, not once at the end of the poll cycle, so
            // a crash partway through a large cycle re-reads only the
            // batches it hadn't finished yet — not everything since the
            // start of the cycle. Awaited: this is the durable write (see
            // DynamoAggregatorCheckpoint), not just an in-memory update.
            await checkpoint.AdvanceAsync(batch.Key, ct);
        }
    }
}
