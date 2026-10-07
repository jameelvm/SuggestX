using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SuggestX.Contracts.Dtos;
using SuggestX.SuggestionService.Configuration;

namespace SuggestX.SuggestionService.Services;

/// <summary>Ranked matches for a prefix, plus which of them were reordered by personalization — see <see cref="SuggestionResponse.PersonalizedPhrases"/>.</summary>
public sealed record SuggestionMatchResult(IReadOnlyList<SuggestionItem> Items, IReadOnlyList<string> PersonalizedPhrases);

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
    Task<SuggestionMatchResult> GetTopMatchesAsync(
        string rawPrefix, int limit, IReadOnlyList<string> recentPhrases, CancellationToken ct);
}

public sealed class RedisSuggestionReader(
    IConnectionMultiplexer redis,
    ICurrentTrieVersion currentVersion,
    IOptions<SuggestionServiceOptions> options,
    ILogger<RedisSuggestionReader> logger) : ISuggestionReader
{
    public async Task<SuggestionMatchResult> GetTopMatchesAsync(
        string rawPrefix, int limit, IReadOnlyList<string> recentPhrases, CancellationToken ct)
    {
        // Same normalization as Aggregator's DynamoPhraseFrequencyWriter —
        // enforced in exactly one place there because every downstream
        // reader is meant to trust it's already normalized. This is that
        // trust being exercised: TrieBuilder flattened lower-cased prefixes,
        // so a request has to be lower-cased identically to land on the
        // same Redis key.
        var normalized = rawPrefix.Trim().ToLowerInvariant();
        if (normalized.Length == 0 || limit <= 0) return new([], []);

        if (currentVersion.Version is not { } version)
        {
            // A real, distinguishable "nothing published yet" case — not an
            // error. Matches TrieBuilder's own /_debug/search "no trie
            // built yet" framing for the same underlying situation.
            logger.LogDebug("No trie version known yet; returning no suggestions for {Prefix}", normalized);
            return new([], []);
        }

        var maxLength = options.Value.MaxPrefixLength;
        List<SuggestionItem> candidates;
        if (normalized.Length <= maxLength)
        {
            candidates = await FetchAsync(version, normalized);
        }
        else
        {
            // Decision 6's degradation: beyond the bound TrieBuilder actually
            // precomputed, fall back to the longest prefix it did flatten,
            // then filter down to phrases that still genuinely match
            // everything the user typed. A real degradation — a phrase that
            // would rank in the true top-N for the full prefix but fell
            // outside this shorter prefix's own top-N is missed — not a
            // silently wrong answer.
            var truncated = normalized[..maxLength];
            var all = await FetchAsync(version, truncated);
            candidates = all.Where(c => c.Phrase.StartsWith(normalized, StringComparison.Ordinal)).ToList();
        }

        // Personalization reorders within `candidates` only — it can never
        // surface a phrase the flattened cache didn't already return, since
        // that would mean querying DynamoDB by exact phrase, which
        // SuggestionService deliberately never touches (decision 2). The
        // full candidate set (up to TrieBuilder's own TopN) is considered
        // here, not just the first `limit` of it, so a personally-recent
        // phrase ranked just outside the requested limit can still be
        // promoted into view.
        return Personalize(candidates, recentPhrases, limit);
    }

    private static SuggestionMatchResult Personalize(
        List<SuggestionItem> candidates, IReadOnlyList<string> recentPhrases, int limit)
    {
        var recentSet = recentPhrases
            .Select(p => p.Trim().ToLowerInvariant())
            .Where(p => p.Length > 0)
            .ToHashSet();

        if (recentSet.Count == 0) return new(candidates.Take(limit).ToList(), []);

        var boosted = candidates.Where(c => recentSet.Contains(c.Phrase.ToLowerInvariant()));
        var rest = candidates.Where(c => !recentSet.Contains(c.Phrase.ToLowerInvariant()));
        var merged = boosted.Concat(rest).Take(limit).ToList();

        var personalizedPhrases = merged
            .Where(m => recentSet.Contains(m.Phrase.ToLowerInvariant()))
            .Select(m => m.Phrase)
            .ToList();

        return new(merged, personalizedPhrases);
    }

    private async Task<List<SuggestionItem>> FetchAsync(int version, string prefix)
    {
        RedisValue value;
        try
        {
            var db = redis.GetDatabase();
            value = await db.StringGetAsync($"trie:v{version}:{prefix}");
        }
        catch (RedisException ex)
        {
            // Redis is this service's only store (decision 2) — there is
            // nothing else to fall back to. "Must degrade, not crash" (see
            // the connection-level comment in SuggestXHostingExtensions)
            // means returning no suggestions, the same real, honest
            // degradation already used for "no trie version known yet,"
            // not letting an unhandled exception surface as a raw 500.
            logger.LogError(ex, "Redis unreachable fetching {Prefix} at version {Version}", prefix, version);
            return [];
        }

        if (value.IsNullOrEmpty) return [];

        return JsonSerializer.Deserialize<List<SuggestionItem>>((string)value!) ?? [];
    }
}
