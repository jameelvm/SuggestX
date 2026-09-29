namespace SuggestX.Contracts.Dtos;

/// <summary>
/// Response to <c>GET /suggestions?prefix=</c> — the design doc's
/// <c>getSuggestions(prefix)</c> API. Deliberately thin: everything expensive
/// (ranking, trie traversal) already happened offline in TrieBuilder, so this
/// is just "here is the row TrieBuilder already computed."
/// <see cref="PersonalizedPhrases"/> names which entries in
/// <see cref="Suggestions"/> were reordered ahead of where their global
/// frequency alone would have placed them, because the caller's own
/// client-side recent-search list matched them (Phase 7 personalization,
/// DESIGN.md §3 "Should the trie be built per-user or shared?"). It never
/// introduces a phrase the shared trie didn't already return — only
/// reorders within that set.
/// </summary>
public sealed record SuggestionResponse(
    string Prefix, IReadOnlyList<SuggestionItem> Suggestions, IReadOnlyList<string> PersonalizedPhrases);

/// <summary>One ranked completion, as precomputed by TrieBuilder.</summary>
public sealed record SuggestionItem(string Phrase, long Frequency);
