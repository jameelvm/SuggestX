using System.Text.Json;
using StackExchange.Redis;
using SuggestX.Contracts.Dtos;

namespace SuggestX.TrieBuilder.Services;

/// <summary>
/// Writes a build cycle's flattened <c>prefix -> top-N</c> projection into
/// Redis under a versioned namespace (<c>trie:v{N}:{prefix}</c>), and
/// removes the previous version's keys once the new version is fully
/// written — never the other way around, so a reader can never observe a
/// half-written version (DESIGN.md decision 8's blue/green principle,
/// still without the ZooKeeper-coordinated "which version is current"
/// pointer that Module 3 adds; see the class remarks on
/// <c>TrieBuildWorker</c> for what that means in practice today).
/// </summary>
public interface IFlattenedCachePublisher
{
    Task PublishAsync(
        int version,
        IReadOnlyDictionary<string, IReadOnlyList<(string Phrase, long Frequency)>> flattened,
        IReadOnlySet<string> previousVersionPrefixes,
        CancellationToken ct);
}

public sealed class RedisFlattenedCachePublisher(
    IConnectionMultiplexer redis, ILogger<RedisFlattenedCachePublisher> logger) : IFlattenedCachePublisher
{
    public async Task PublishAsync(
        int version,
        IReadOnlyDictionary<string, IReadOnlyList<(string Phrase, long Frequency)>> flattened,
        IReadOnlySet<string> previousVersionPrefixes,
        CancellationToken ct)
    {
        var db = redis.GetDatabase();

        // Write the entire new version first. Nothing reads this namespace
        // yet (SuggestionService arrives Phase 5), but the ordering itself
        // is the point: a version is never partially visible before it's
        // complete.
        foreach (var (prefix, matches) in flattened)
        {
            var items = matches.Select(m => new SuggestionItem(m.Phrase, m.Frequency)).ToList();
            var payload = JsonSerializer.Serialize(items);
            await db.StringSetAsync($"trie:v{version}:{prefix}", payload);
        }

        // Only now remove the previous version — after the new one is
        // fully in place, never before. Deleting by the exact prefix set we
        // remembered writing last cycle, not a Redis KEYS/SCAN, since
        // scanning the whole keyspace to find "everything matching
        // trie:v{oldVersion}:*" is real cost this service already knows
        // how to avoid: it wrote every one of those keys itself.
        var previousVersion = version - 1;
        if (previousVersionPrefixes.Count > 0)
        {
            var keysToDelete = previousVersionPrefixes
                .Select(p => (RedisKey)$"trie:v{previousVersion}:{p}")
                .ToArray();
            await db.KeyDeleteAsync(keysToDelete);
        }

        logger.LogInformation(
            "Published trie version {Version}: {PrefixCount} prefixes written, {DeletedCount} old keys removed",
            version, flattened.Count, previousVersionPrefixes.Count);
    }
}
