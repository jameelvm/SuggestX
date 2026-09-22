using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SuggestX.CollectionService.Configuration;
using SuggestX.CollectionService.Services;
using SuggestX.Contracts.Dtos;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.CollectionService.Jobs;

/// <summary>
/// The doc's HDFS write path, made concrete: on a timer, drains the buffer
/// and writes it as one line-delimited-JSON object to suggestx-raw-logs.
/// One instance, one queue, one flush loop — no coordination with any other
/// CollectionService instance is needed, because each instance's object key
/// is unique to it (see <see cref="_instanceId"/>), so two instances can
/// never contend for the same S3 key even if they flush at the exact same
/// millisecond.
/// </summary>
public sealed class SearchEventFlushWorker(
    ISearchEventBuffer buffer,
    IAmazonS3 s3,
    IOptions<StorageOptions> storageOptions,
    IOptions<CollectionOptions> collectionOptions,
    ILogger<SearchEventFlushWorker> logger) : BackgroundService
{
    // One GUID per process lifetime — combined with a millisecond-precision,
    // lexicographically-sortable timestamp prefix, this makes every key this
    // instance ever writes unique, and the timestamp prefix means Aggregator
    // (Phase 3) can list objects in chronological order via S3's own
    // ListObjectsV2 rather than needing to read every object's contents to
    // find out when it was written.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(collectionOptions.Value.FlushIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await FlushAsync(stoppingToken);
    }

    /// <summary>
    /// Flushes whatever is left one last time on graceful shutdown, so a
    /// container restart between two timer ticks does not silently drop the
    /// events sitting in memory at that moment.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await FlushAsync(CancellationToken.None);
        await base.StopAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var events = buffer.DrainAll();
        if (events.Count == 0) return;

        var body = new StringBuilder();
        foreach (var e in events)
        {
            var entry = new SearchLogEntry(e.Query, e.ReceivedAt, e.SessionId, e.Locale);
            body.AppendLine(JsonSerializer.Serialize(entry));
        }

        var key = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{_instanceId}.jsonl";

        try
        {
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = storageOptions.Value.RawLogsBucket,
                Key = key,
                ContentBody = body.ToString(),
                ContentType = "application/x-ndjson"
            }, ct);

            logger.LogInformation(
                "Flushed {Count} search events to s3://{Bucket}/{Key}",
                events.Count, storageOptions.Value.RawLogsBucket, key);
        }
        catch (Exception ex)
        {
            // Best-effort, matching the doc's own framing: a dropped batch
            // undercounts a trend, it never corrupts one. Logged, not
            // rethrown — a transient S3 blip must not crash the flush loop
            // itself, or every subsequent batch is lost too.
            logger.LogError(ex,
                "Failed to flush {Count} search events to S3 — dropped",
                events.Count);
        }
    }
}
