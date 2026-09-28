using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.Options;
using SuggestX.Contracts.Dtos;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.TrieBuilder.Services;

/// <summary>
/// TrieBuilder's exclusive durability store (<see cref="StorageOptions.TrieSnapshotsBucket"/>,
/// decision 3): one JSON object per published version, holding the same
/// flattened <c>prefix -> top-N</c> content just written to Redis. This is
/// what lets Module 3's recovery avoid two different expensive things after
/// a restart — re-scanning all of <c>suggestx-phrase-frequencies</c> before
/// having anything to serve, and forgetting which Redis prefixes the last
/// live version actually published (needed to clean them up correctly once
/// a new version replaces them; see decision 14's previously-unclosed gap).
/// </summary>
public interface ITrieSnapshotStore
{
    Task SaveAsync(
        int version,
        IReadOnlyDictionary<string, IReadOnlyList<(string Phrase, long Frequency)>> flattened,
        CancellationToken ct);

    /// <summary>
    /// Returns just the prefix keys from a previously saved version — the
    /// only piece of a snapshot Module 3's recovery actually needs (to seed
    /// <c>TrieBuildWorker</c>'s previous-prefix-set). Null when no snapshot
    /// exists for that version, e.g. ZooKeeper named a version whose S3
    /// object never landed or has since been removed — treated as a
    /// recoverable gap (start with an empty previous-prefix set), not a
    /// fatal error.
    /// </summary>
    Task<IReadOnlySet<string>?> LoadPrefixesAsync(int version, CancellationToken ct);
}

public sealed class S3TrieSnapshotStore(
    IAmazonS3 s3, IOptions<StorageOptions> storageOptions, ILogger<S3TrieSnapshotStore> logger) : ITrieSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task SaveAsync(
        int version,
        IReadOnlyDictionary<string, IReadOnlyList<(string Phrase, long Frequency)>> flattened,
        CancellationToken ct)
    {
        var document = new TrieSnapshotDocument(
            version,
            DateTimeOffset.UtcNow,
            flattened.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(m => new SuggestionItem(m.Phrase, m.Frequency)).ToList()));

        var json = JsonSerializer.Serialize(document, JsonOptions);

        await s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = storageOptions.Value.TrieSnapshotsBucket,
            Key = SnapshotKey(version),
            ContentBody = json,
            ContentType = "application/json"
        }, ct);
    }

    public async Task<IReadOnlySet<string>?> LoadPrefixesAsync(int version, CancellationToken ct)
    {
        try
        {
            using var response = await s3.GetObjectAsync(storageOptions.Value.TrieSnapshotsBucket, SnapshotKey(version), ct);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync(ct);

            var document = JsonSerializer.Deserialize<TrieSnapshotDocument>(json, JsonOptions);
            return document?.Prefixes.Keys.ToHashSet();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning(
                "No trie snapshot found in S3 for version {Version} — starting recovery with an empty previous-prefix set",
                version);
            return null;
        }
    }

    private static string SnapshotKey(int version) => $"v{version}.json";

    private sealed record TrieSnapshotDocument(
        int Version, DateTimeOffset BuiltAt, Dictionary<string, List<SuggestionItem>> Prefixes);
}
