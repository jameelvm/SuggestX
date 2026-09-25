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

    /// <summary>
    /// Aggregator's exclusive DynamoDB table for its own read-progress
    /// checkpoint — separate from suggestx-phrase-frequencies, since a
    /// checkpoint is operational state, not a phrase count, and shouldn't
    /// need special-casing in anything that later scans that table.
    /// </summary>
    public string CheckpointTable { get; set; } = "suggestx-aggregator-checkpoints";
}
