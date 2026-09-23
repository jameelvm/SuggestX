namespace SuggestX.Aggregator.Configuration;

/// <summary>
/// Aggregator's own config — not in SuggestX.ServiceDefaults, since nothing
/// else needs it. 15s default is a demo-scale value, not a claim about
/// production cadence (the doc's own aggregator runs every ~15 minutes);
/// see CLAUDE.md's note on batch cadences.
/// </summary>
public sealed class AggregatorOptions
{
    public const string SectionName = "Aggregator";

    public int PollIntervalSeconds { get; set; } = 15;
}
