namespace SuggestX.CollectionService.Configuration;

/// <summary>
/// CollectionService's own config — not in SuggestX.ServiceDefaults, since
/// nothing else needs it. 10s default is a demo-scale value, not a claim
/// about production cadence (the doc flushes far less often); see
/// CLAUDE.md's note on batch cadences.
/// </summary>
public sealed class CollectionOptions
{
    public const string SectionName = "Collection";

    public int FlushIntervalSeconds { get; set; } = 10;
}
