using Microsoft.AspNetCore.Mvc;
using SuggestX.Aggregator.Services;

namespace SuggestX.Aggregator.Api;

/// <summary>Dev-only visibility into the polling worker's progress, and into the table it writes.</summary>
[ApiController]
[Route("_debug")]
public sealed class AggregatorDebugController(
    IAggregatorStats stats, IPhraseFrequencySnapshotReader frequencyReader) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        stats.BatchesProcessed,
        stats.EntriesProcessed,
        stats.LastPollAt,
        stats.LastProcessedKey
    });

    /// <summary>
    /// The whole `suggestx-phrase-frequencies` table, ranked highest-first
    /// — the insights panel's frequency table. A full scan, not a bounded
    /// query; fine at this project's demo scale, the same "toy-scale
    /// simplification" as every other full-table debug dump in this
    /// codebase (TrieBuilder's own `/_debug/tree`, its DynamoDB reader).
    /// </summary>
    [HttpGet("frequencies")]
    public async Task<IActionResult> Frequencies(CancellationToken ct)
    {
        var entries = await frequencyReader.ReadAllAsync(ct);
        var ranked = entries
            .OrderByDescending(e => e.Frequency)
            .ThenBy(e => e.Phrase, StringComparer.Ordinal)
            .Select(e => new { phrase = e.Phrase, frequency = e.Frequency });

        return Ok(new { count = entries.Count, entries = ranked });
    }
}
