using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SuggestX.Contracts.Dtos;
using SuggestX.SuggestionService.Configuration;
using SuggestX.SuggestionService.Services;

namespace SuggestX.SuggestionService.Api;

/// <summary>
/// The doc's <c>getSuggestions(prefix)</c> API — the only thing this
/// service does. Every request is one Redis <c>GET</c> plus, beyond
/// decision 6's bound, a small in-process filter over an already-small
/// list; never a trie traversal, never a call to DynamoDB or S3.
/// </summary>
[ApiController]
[Route("suggestions")]
public sealed class SuggestionsController(
    ISuggestionReader reader,
    ICurrentTrieVersion currentVersion,
    IOptions<SuggestionServiceOptions> options) : ControllerBase
{
    /// <summary>
    /// <paramref name="recent"/> is the caller's own client-side recent-
    /// search cache (Phase 7 personalization) — a comma-separated list of
    /// phrases the browser itself remembers, never anything this service
    /// stores. There is no per-user identity here to key a server-side
    /// profile by (this system has none, by design), so the client simply
    /// resends its own small history on every request.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? prefix, [FromQuery] int? limit, [FromQuery] string? recent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return BadRequest(new { error = "prefix is required" });

        var effectiveLimit = limit is > 0 ? limit.Value : options.Value.DefaultLimit;
        var recentPhrases = ParseRecent(recent);
        var result = await reader.GetTopMatchesAsync(prefix, effectiveLimit, recentPhrases, ct);

        return Ok(new SuggestionResponse(prefix, result.Items, result.PersonalizedPhrases));
    }

    private static IReadOnlyList<string> ParseRecent(string? recent) =>
        string.IsNullOrWhiteSpace(recent)
            ? []
            : recent.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Dev-only: which trie version this instance is currently serving from.</summary>
    [HttpGet("_debug/status")]
    public IActionResult DebugStatus() => Ok(new
    {
        currentVersion = currentVersion.Version
    });
}
