namespace SuggestX.Contracts.Dtos;

/// <summary>
/// One line of a CollectionService flush object in suggestx-raw-logs — the
/// doc's "phrase, timestamp, and metadata" logged for later processing.
/// Lives here, not just inside CollectionService, because Aggregator
/// (Phase 3) is the other side of this exact wire format: it deserializes
/// objects written in this shape read-only from the same bucket.
/// </summary>
public sealed record SearchLogEntry(string Query, DateTimeOffset ReceivedAt, string? SessionId, string? Locale);
