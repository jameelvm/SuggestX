using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SuggestX.Aggregator.Domain;
using SuggestX.Contracts.Dtos;
using SuggestX.ServiceDefaults.Configuration;
using System.Text.Json;

namespace SuggestX.Aggregator.Services;

/// <summary>
/// Read-only access to CollectionService's store. Aggregator never writes
/// here — see CLAUDE.md's architecture table: the pipeline is a linear
/// offline chain, each stage reading the previous stage's store directly,
/// by explicit design.
/// </summary>
public interface IRawLogReader
{
    /// <summary>
    /// Lists and parses every object written after <paramref name="afterKey"/>
    /// (exclusive), in ascending key order — which is chronological order,
    /// since CollectionService's keys are timestamp-prefixed. Null
    /// <paramref name="afterKey"/> reads from the start of the bucket.
    /// </summary>
    Task<IReadOnlyList<RawLogBatch>> ReadNewBatchesAsync(string? afterKey, CancellationToken ct);
}

public sealed class S3RawLogReader(
    IAmazonS3 s3, IOptions<StorageOptions> storageOptions, ILogger<S3RawLogReader> logger) : IRawLogReader
{
    public async Task<IReadOnlyList<RawLogBatch>> ReadNewBatchesAsync(string? afterKey, CancellationToken ct)
    {
        var keys = await ListNewKeysAsync(afterKey, ct);
        var batches = new List<RawLogBatch>(keys.Count);

        foreach (var key in keys)
        {
            var entries = await ReadAndParseAsync(key, ct);
            batches.Add(new RawLogBatch(key, entries));
        }

        return batches;
    }

    private async Task<List<string>> ListNewKeysAsync(string? afterKey, CancellationToken ct)
    {
        var keys = new List<string>();
        string? continuationToken = null;

        do
        {
            var response = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = storageOptions.Value.RawLogsBucket,
                StartAfter = afterKey,
                ContinuationToken = continuationToken
            }, ct);

            // response.S3Objects is null, not an empty list, when a page has
            // no matches — the overwhelmingly common case for a poll cycle
            // that finds nothing new. Learned by hitting the real
            // ArgumentNullException live (LINQ's Select validates its
            // source), not by reading the SDK's docs closely enough first.
            if (response.S3Objects is { Count: > 0 })
                keys.AddRange(response.S3Objects.Select(o => o.Key));

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (continuationToken is not null);

        // ListObjectsV2 already returns keys in ascending lexicographic
        // order, which is chronological order for these timestamp-prefixed
        // keys — StartAfter alone guarantees "newer than afterKey," not
        // ordering, so this is a belt-and-braces sort, not a workaround.
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private async Task<IReadOnlyList<SearchLogEntry>> ReadAndParseAsync(string key, CancellationToken ct)
    {
        using var response = await s3.GetObjectAsync(storageOptions.Value.RawLogsBucket, key, ct);
        using var reader = new StreamReader(response.ResponseStream);

        var entries = new List<SearchLogEntry>();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<SearchLogEntry>(line);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException ex)
            {
                // One malformed line must not sink the rest of a real batch —
                // same reasoning as the flush worker's own best-effort framing.
                logger.LogWarning(ex, "Skipping unparseable line in {Key}", key);
            }
        }

        return entries;
    }
}
