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
}
