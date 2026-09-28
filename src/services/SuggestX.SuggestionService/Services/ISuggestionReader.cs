using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SuggestX.Contracts.Dtos;
using SuggestX.SuggestionService.Configuration;

namespace SuggestX.SuggestionService.Services;

/// <summary>
/// The doc's "suggestion service retrieves the top ten from the Redis
/// cache," taken literally (decision 2): one <see cref="StringGetAsync"/>
/// against whichever <c>trie:v{N}:{prefix}</c> namespace
/// <see cref="ICurrentTrieVersion"/> currently names. No trie traversal, no
/// DynamoDB/S3 access — everything expensive already happened offline in
/// TrieBuilder.
/// </summary>
public interface ISuggestionReader
{
    Task<IReadOnlyList<SuggestionItem>> GetTopMatchesAsync(string rawPrefix, int limit, CancellationToken ct);
}

public sealed class RedisSuggestionReader(
    IConnectionMultiplexer redis,
    ICurrentTrieVersion currentVersion,
    IOptions<SuggestionServiceOptions> options,
    ILogger<RedisSuggestionReader> logger) : ISuggestionReader
{
    public async Task<IReadOnlyList<SuggestionItem>> GetTopMatchesAsync(string rawPrefix, int limit, CancellationToken ct)
    {
        // Same normalization as Aggregator's DynamoPhraseFrequencyWriter —
        // enforced in exactly one place there because every downstream
        // reader is meant to trust it's already normalized. This is that
        // trust being exercised: TrieBuilder flattened lower-cased prefixes,
        // so a request has to be lower-cased identically to land on the
        // same Redis key.
        var normalized = rawPrefix.Trim().ToLowerInvariant();
        if (normalized.Length == 0 || limit <= 0) return [];

        if (currentVersion.Version is not { } version)
        {
            // A real, distinguishable "nothing published yet" case — not an
            // error. Matches TrieBuilder's own /_debug/search "no trie
            // built yet" framing for the same underlying situation.
            logger.LogDebug("No trie version known yet; returning no suggestions for {Prefix}", normalized);
            return [];
        }

        var maxLength = options.Value.MaxPrefixLength;
        if (normalized.Length <= maxLength)
        {
            var matches = await FetchAsync(version, normalized);
            return matches.Take(limit).ToList();
        }

        // Decision 6's degradation: beyond the bound TrieBuilder actually
        // precomputed, fall back to the longest prefix it did flatten, then
        // filter down to phrases that still genuinely match everything the
        // user typed. A real degradation — a phrase that would rank in the
        // true top-N for the full prefix but fell outside this shorter
        // prefix's own top-N is missed — not a silently wrong answer.
        var truncated = normalized[..maxLength];
        var candidates = await FetchAsync(version, truncated);
        return candidates
            .Where(c => c.Phrase.StartsWith(normalized, StringComparison.Ordinal))
            .Take(limit)
            .ToList();
    }

    private async Task<List<SuggestionItem>> FetchAsync(int version, string prefix)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync($"trie:v{version}:{prefix}");

        if (value.IsNullOrEmpty) return [];

        return JsonSerializer.Deserialize<List<SuggestionItem>>((string)value!) ?? [];
    }
}
