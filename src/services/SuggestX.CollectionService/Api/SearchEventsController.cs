using Amazon.KinesisFirehose;
using Microsoft.AspNetCore.Mvc;
using SuggestX.CollectionService.Services;
using SuggestX.Contracts.Dtos;

namespace SuggestX.CollectionService.Api;

[ApiController]
[Route("search-events")]
public sealed class SearchEventsController(
    ISearchEventPublisher publisher,
    IPublishedEventStats stats,
    ILogger<SearchEventsController> logger) : ControllerBase
{
    /// <summary>
    /// The doc's "collection service logs the phrase, timestamp, and
    /// metadata" — fired by the client once a search is actually submitted
    /// (already debounced client-side), not per keystroke.
    /// <para>
    /// 202 here means <em>durably accepted by Firehose</em>, not merely
    /// "held in this process's memory." That distinction is the entire
    /// point of DESIGN.md decision 11: the previous version acked the
    /// client the instant the event landed in an in-process queue, so an
    /// ungraceful kill silently lost everything not yet flushed. Now a
    /// failure to hand the event off is reported to the caller as a 503
    /// rather than being absorbed and forgotten.
    /// </para>
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] SearchEventRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "query is required" });

        var entry = new SearchLogEntry(
            request.Query.Trim(),
            DateTimeOffset.UtcNow,
            request.SessionId,
            request.Locale);

        try
        {
            await publisher.PublishAsync(entry, ct);
        }
        catch (Exception ex) when (ex is AmazonKinesisFirehoseException or HttpRequestException or TaskCanceledException)
        {
            // Deliberately surfaced, not swallowed: the caller is the only
            // party that can decide whether to retry, and silently dropping
            // here would reintroduce exactly the invisible data loss this
            // design replaced.
            logger.LogError(ex, "Failed to publish search event to Firehose");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "search event could not be accepted; retry" });
        }

        return Accepted();
    }

    /// <summary>
    /// Dev-only: how many events this instance has successfully published.
    /// </summary>
    [HttpGet("_debug/status")]
    public IActionResult DebugStatus() => Ok(new
    {
        stats.PublishedCount,
        stats.LastPublishedAt
    });
}
