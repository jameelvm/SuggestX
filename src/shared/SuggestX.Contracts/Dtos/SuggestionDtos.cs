namespace SuggestX.Contracts.Dtos;

/// <summary>
/// Response to <c>GET /suggestions?prefix=</c> — the design doc's
/// <c>getSuggestions(prefix)</c> API. Deliberately thin: everything expensive
/// (ranking, trie traversal) already happened offline in TrieBuilder, so this
/// is just "here is the row TrieBuilder already computed."
/// </summary>
public sealed record SuggestionResponse(string Prefix, IReadOnlyList<SuggestionItem> Suggestions);

/// <summary>One ranked completion, as precomputed by TrieBuilder.</summary>
public sealed record SuggestionItem(string Phrase, long Frequency);
