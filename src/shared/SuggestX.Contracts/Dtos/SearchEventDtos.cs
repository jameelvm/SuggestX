namespace SuggestX.Contracts.Dtos;

/// <summary>
/// What a client sends to CollectionService when a search is actually
/// submitted (not per keystroke — the client debounces before this fires; see
/// DESIGN.md's client-side optimization section). This is the raw material
/// the whole offline pipeline (Collection → Aggregator → TrieBuilder) works
/// from — the doc's "phrase, timestamp, and metadata" logged by the
/// collection service.
/// </summary>
public sealed record SearchEventRequest(string Query, string? SessionId = null, string? Locale = null);
