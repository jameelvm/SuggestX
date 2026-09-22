using Microsoft.AspNetCore.Mvc;
using SuggestX.CollectionService.Domain;
using SuggestX.CollectionService.Services;
using SuggestX.Contracts.Dtos;

namespace SuggestX.CollectionService.Api;

[ApiController]
[Route("search-events")]
public sealed class SearchEventsController(ISearchEventBuffer buffer) : ControllerBase
{
    /// <summary>
    /// The doc's "collection service logs the phrase, timestamp, and
    /// metadata" — fired by the client once a search is actually submitted
    /// (already debounced client-side), not per keystroke. Accepted means
    /// "buffered," not "durably stored" — durability starts at the next
    /// flush to S3 (Module 2). A dropped event here undercounts a trend; it
    /// never corrupts one, which is the trade this whole pipeline already
    /// makes explicit in DESIGN.md.
    /// </summary>
    [HttpPost]
    public IActionResult Post([FromBody] SearchEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "query is required" });

        buffer.Enqueue(new BufferedSearchEvent(
            request.Query.Trim(),
            DateTimeOffset.UtcNow,
            request.SessionId,
            request.Locale));

        return Accepted();
    }

    /// <summary>
    /// Dev-only visibility into the buffer before Module 2 gives it anywhere
    /// durable to go — same purpose as JameX's Encoder debug endpoint: proof
    /// the previous step actually worked before building the next one on
    /// top of it.
    /// </summary>
    [HttpGet("_debug/count")]
    public IActionResult DebugCount() => Ok(new { bufferedCount = buffer.Count });
}
