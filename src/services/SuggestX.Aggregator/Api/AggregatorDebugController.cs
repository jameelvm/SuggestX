using Microsoft.AspNetCore.Mvc;
using SuggestX.Aggregator.Services;

namespace SuggestX.Aggregator.Api;

/// <summary>Dev-only visibility into the polling worker's progress.</summary>
[ApiController]
[Route("_debug")]
public sealed class AggregatorDebugController(IAggregatorStats stats) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        stats.BatchesProcessed,
        stats.EntriesProcessed,
        stats.LastPollAt,
        stats.LastProcessedKey
    });
}
