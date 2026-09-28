using Microsoft.AspNetCore.Mvc;
using SuggestX.TrieBuilder.Services;

namespace SuggestX.TrieBuilder.Api;

/// <summary>
/// Dev-only visibility into the trie itself — same purpose as every other
/// service's debug controller: proof the previous step actually worked
/// before building the next one on top of it. <c>Search</c> in particular
/// exercises the real, live <c>CompressedTrie.GetTopMatches</c> against
/// real DynamoDB-sourced data, which is otherwise invisible until
/// SuggestionService exists in Phase 5 to serve it from Redis.
/// </summary>
[ApiController]
[Route("_debug")]
public sealed class TrieBuilderDebugController(ITrieHolder trieHolder, ITrieBuildStats stats) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status()
    {
        var trie = trieHolder.Current;
        return Ok(new
        {
            built = trie is not null,
            phraseCount = trie?.PhraseCount ?? 0,
            nodeCount = trie?.NodeCount ?? 0,
            currentVersion = stats.CurrentVersion,
            flattenedPrefixCount = stats.FlattenedPrefixCount,
            lastBuildAt = stats.LastBuildAt,
            recoveredOnStartup = stats.RecoveredOnStartup,
            recoveredVersion = stats.RecoveredVersion
        });
    }

    [HttpGet("search")]
    public IActionResult Search([FromQuery] string prefix, [FromQuery] int limit = 10)
    {
        var trie = trieHolder.Current;
        if (trie is null)
            return Ok(new { prefix, results = Array.Empty<object>(), note = "no trie built yet" });

        var matches = trie.GetTopMatches(prefix, limit);
        return Ok(new
        {
            prefix,
            results = matches.Select(m => new { phrase = m.Phrase, frequency = m.Frequency })
        });
    }

    /// <summary>
    /// The whole current trie, structurally — for the insights panel's
    /// graphical rendering (Phase 6 Module 3), not for anything on the
    /// read/write path. See <c>CompressedTrie.ToSnapshot</c> for why a
    /// full dump is fine here and not something a production system would
    /// do the same way at real scale.
    /// </summary>
    [HttpGet("tree")]
    public IActionResult Tree()
    {
        var trie = trieHolder.Current;
        if (trie is null) return Ok(new { built = false, root = (object?)null });

        return Ok(new { built = true, root = trie.ToSnapshot() });
    }
}
