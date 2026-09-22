namespace SuggestX.CollectionService.Domain;

/// <summary>
/// A search event once it has actually been accepted — the timestamp is
/// assigned here, server-side, at receipt time, not trusted from the client.
/// This is CollectionService's own internal shape; <c>SearchEventRequest</c>
/// (in SuggestX.Contracts) is only what crosses the wire coming in.
/// </summary>
public sealed record BufferedSearchEvent(
    string Query,
    DateTimeOffset ReceivedAt,
    string? SessionId,
    string? Locale);
