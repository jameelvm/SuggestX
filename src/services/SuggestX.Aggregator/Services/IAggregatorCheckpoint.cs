using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using SuggestX.Aggregator.Configuration;

namespace SuggestX.Aggregator.Services;

/// <summary>
/// Tracks the last S3 key this Aggregator has fully processed, persisted in
/// DynamoDB so a restart resumes instead of reprocessing the entire bucket
/// from the beginning — closing the gap Module 1 deliberately left open
/// (see DESIGN.md's failure-mode table). Reads are served from an
/// in-memory cache loaded once at startup; every successful advance writes
/// through to DynamoDB immediately, so the durable copy is never more than
/// one batch stale.
/// </summary>
public interface IAggregatorCheckpoint
{
    /// <summary>Loads the last known key from DynamoDB. Call once before polling starts.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>In-memory cached value — fast, no DynamoDB round-trip per poll.</summary>
    string? LastProcessedKey { get; }

    /// <summary>Persists the new checkpoint and updates the in-memory cache.</summary>
    Task AdvanceAsync(string key, CancellationToken ct);
}

public sealed class DynamoAggregatorCheckpoint(
    IAmazonDynamoDB dynamo,
    IOptions<AggregatorOptions> options,
    ILogger<DynamoAggregatorCheckpoint> logger) : IAggregatorCheckpoint
{
    // Single fixed row for now — one Aggregator instance, one logical
    // checkpoint. The partition key leaves room to track more than one
    // (e.g. a checkpoint per partition) without a schema change if this
    // service is ever scaled out to multiple concurrent instances.
    private const string CheckpointId = "raw-log-reader";

    private string? _lastProcessedKey;

    public string? LastProcessedKey => _lastProcessedKey;

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            var response = await dynamo.GetItemAsync(new GetItemRequest
            {
                TableName = options.Value.CheckpointTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["checkpointId"] = new AttributeValue { S = CheckpointId }
                }
            }, ct);

            // GetItemResponse.Item is null — not an empty dictionary — when
            // no item exists yet (first run ever). Assumed otherwise, same
            // mistake as S3RawLogReader's S3Objects null check (see
            // PROGRESS.md): the AWS SDK returns null, not an empty
            // collection, for "nothing here" more often than seems
            // intuitive, and it's worth checking rather than assuming
            // every time.
            _lastProcessedKey = response.Item is { Count: > 0 } item && item.TryGetValue("lastProcessedKey", out var value)
                ? value.S
                : null;

            logger.LogInformation(
                "Checkpoint loaded: resuming after {Key}",
                _lastProcessedKey ?? "(none — starting from the beginning of the bucket)");
        }
        catch (Exception ex)
        {
            // A durable checkpoint that can't be read is no worse than not
            // having one — degrade to "start from the beginning" rather
            // than refusing to start the service at all.
            logger.LogError(ex,
                "Failed to load checkpoint from DynamoDB — starting from the beginning of the bucket");
            _lastProcessedKey = null;
        }
    }

    public async Task AdvanceAsync(string key, CancellationToken ct)
    {
        // Cache updates unconditionally, even if the durable write below
        // fails — this process keeps working correctly either way. Only a
        // restart before the next successful write would resume from an
        // older point and re-read a few already-processed batches: safe,
        // not silent, and logged when it happens.
        _lastProcessedKey = key;

        try
        {
            await dynamo.PutItemAsync(new PutItemRequest
            {
                TableName = options.Value.CheckpointTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["checkpointId"] = new AttributeValue { S = CheckpointId },
                    ["lastProcessedKey"] = new AttributeValue { S = key },
                    ["updatedAt"] = new AttributeValue { S = DateTimeOffset.UtcNow.ToString("O") }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist checkpoint {Key} to DynamoDB", key);
        }
    }
}
