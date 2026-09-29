namespace SuggestX.SuggestionService.Configuration;

/// <summary>
/// This service's own config — nothing else in the system needs it.
/// </summary>
public sealed class SuggestionServiceOptions
{
    public const string SectionName = "SuggestionService";

    /// <summary>
    /// How often <c>CurrentVersionPoller</c> re-reads ZooKeeper's
    /// <c>current_version</c> znode. Polling, not a ZooKeeper watch — every
    /// other cross-service handoff in this system is already poll-based
    /// (Aggregator polling S3, TrieBuilder polling DynamoDB), and a watch
    /// would add real complexity (ZooKeeper watches are one-shot and must be
    /// re-armed after every fire) for a benefit — near-instant version
    /// pickup instead of a few seconds' staleness — that doesn't matter
    /// given decision 7 already accepts a much larger staleness window
    /// upstream of this. See DESIGN.md decision 16.
    /// </summary>
    public int VersionPollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Must match TrieBuilder's own <c>TrieBuilder:MaxPrefixLength</c> —
    /// nothing enforces this at runtime, since the two services are
    /// deployed independently and neither reads the other's config. A real,
    /// accepted coupling: if they drift apart, prefixes longer than
    /// whichever bound TrieBuilder actually used will silently get the
    /// degraded fallback behavior (decision 6) even inside what this
    /// service thinks is the "normal" range, or vice versa. See DESIGN.md
    /// decision 16 for why this wasn't worth closing here.
    /// </summary>
    public int MaxPrefixLength { get; set; } = 6;

    public int DefaultLimit { get; set; } = 10;

    /// <summary>
    /// `Cache-Control: public, max-age={this}` on every `GET /suggestions`
    /// response — the doc's own "edge cache" client-side lever, built as a
    /// response header a real CDN/browser cache would honor, even though
    /// no CDN actually sits in front of this local stack. 5s: comfortably
    /// under TrieBuilder's own rebuild cadence, so a cached response is
    /// never staler than the data already legitimately can be. `public`,
    /// not `private`, is deliberate and correct here, not an oversight —
    /// this system has no cookies or sessions, so the entire personalized
    /// response is already fully determined by the URL itself (the
    /// `recent` query parameter *is* the personalization), which is
    /// exactly what makes a shared cache safe to key by URL the normal way.
    /// </summary>
    public int CacheMaxAgeSeconds { get; set; } = 5;
}
