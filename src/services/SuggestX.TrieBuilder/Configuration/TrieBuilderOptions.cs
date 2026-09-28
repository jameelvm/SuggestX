namespace SuggestX.TrieBuilder.Configuration;

/// <summary>
/// TrieBuilder's own config — not in SuggestX.ServiceDefaults, since nothing
/// else needs it. 20s default is a demo-scale value, not a claim about
/// production cadence; see CLAUDE.md's note on batch cadences.
/// </summary>
public sealed class TrieBuilderOptions
{
    public const string SectionName = "TrieBuilder";

    public int BuildIntervalSeconds { get; set; } = 20;

    /// <summary>
    /// DESIGN.md decision 6's bound: precompute top-N for every prefix up
    /// to this many characters, not every possible prefix a user could
    /// type. Beyond this bound, SuggestionService (Phase 5) is designed to
    /// fall back to the last cached prefix and filter client-side — this
    /// service only needs to know where to stop precomputing.
    /// </summary>
    public int MaxPrefixLength { get; set; } = 6;

    /// <summary>The doc's own "top ten" default.</summary>
    public int TopN { get; set; } = 10;
}
