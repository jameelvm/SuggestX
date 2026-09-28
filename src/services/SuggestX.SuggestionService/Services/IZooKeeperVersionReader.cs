using System.Text;
using Microsoft.Extensions.Options;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.SuggestionService.Services;

/// <summary>
/// Read-only access to the one znode this system publishes,
/// <c>{RootPath}/trie/current_version</c> — TrieBuilder is the sole writer
/// (DESIGN.md decision 4, decision 15); this service only ever reads it.
/// Deliberately its own small reader rather than a shared abstraction with
/// TrieBuilder's own <c>IZooKeeperVersionPublisher</c> (which also reads
/// this same znode, for its own restart recovery) — the couple of lines of
/// overlap are the same "small repeated pattern, not worth abstracting"
/// tradeoff this project already makes elsewhere (e.g. the AWS SDK
/// null-vs-empty guards repeated across services rather than factored out).
/// </summary>
public interface IZooKeeperVersionReader
{
    /// <summary>Null when TrieBuilder has never published a version.</summary>
    Task<int?> ReadCurrentVersionAsync(CancellationToken ct);
}

public sealed class ZooKeeperVersionReader(
    org.apache.zookeeper.ZooKeeper zooKeeper, IOptions<ZooKeeperOptions> options) : IZooKeeperVersionReader
{
    public async Task<int?> ReadCurrentVersionAsync(CancellationToken ct)
    {
        var path = $"{options.Value.RootPath}/trie/current_version";

        var stat = await zooKeeper.existsAsync(path);
        if (stat is null) return null;

        var result = await zooKeeper.getDataAsync(path);
        var text = Encoding.UTF8.GetString(result.Data);
        return int.TryParse(text, out var version) ? version : null;
    }
}
