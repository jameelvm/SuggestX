using System.Text;
using Microsoft.Extensions.Options;
using org.apache.zookeeper;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.TrieBuilder.Services;

/// <summary>
/// Owns the one znode this whole system currently writes:
/// <c>{RootPath}/trie/current_version</c>. TrieBuilder is the sole writer
/// (DESIGN.md §1 decision 4); SuggestionService (Phase 5) will only ever
/// read it, to learn which <c>trie:v{N}:*</c> Redis namespace is the one to
/// serve from. The znode is flipped only after both the new version's Redis
/// keys and its S3 snapshot are already fully in place — the last step of
/// the blue/green swap, never the first (decision 8, decision 14).
/// </summary>
public interface IZooKeeperVersionPublisher
{
    /// <summary>Null when no version has ever been published — a brand-new deployment.</summary>
    Task<int?> ReadCurrentVersionAsync(CancellationToken ct);

    Task PublishCurrentVersionAsync(int version, CancellationToken ct);
}

public sealed class ZooKeeperVersionPublisher(
    org.apache.zookeeper.ZooKeeper zooKeeper,
    IOptions<ZooKeeperOptions> options,
    ILogger<ZooKeeperVersionPublisher> logger) : IZooKeeperVersionPublisher
{
    private string VersionPath => $"{options.Value.RootPath}/trie/current_version";

    public async Task<int?> ReadCurrentVersionAsync(CancellationToken ct)
    {
        var path = VersionPath;

        // existsAsync returns null rather than throwing when the node is
        // missing — the expected case on a brand-new deployment, not an
        // error condition (same shape as this project's other AWS SDK
        // null-vs-empty defensiveness, just from a different client library).
        var stat = await zooKeeper.existsAsync(path);
        if (stat is null) return null;

        var result = await zooKeeper.getDataAsync(path);
        var text = Encoding.UTF8.GetString(result.Data);
        return int.TryParse(text, out var version) ? version : null;
    }

    public async Task PublishCurrentVersionAsync(int version, CancellationToken ct)
    {
        // ZooKeeper requires every ancestor znode to already exist — there is
        // no recursive create. Only two fixed levels ever exist under this
        // service's root, so ensuring them inline is simpler and more honest
        // than a generic "mkdir -p" helper this project would only ever call
        // with one path.
        await EnsurePersistentNodeAsync(options.Value.RootPath);
        await EnsurePersistentNodeAsync($"{options.Value.RootPath}/trie");

        var path = VersionPath;
        var data = Encoding.UTF8.GetBytes(version.ToString());

        var stat = await zooKeeper.existsAsync(path);
        if (stat is null)
        {
            await zooKeeper.createAsync(path, data, org.apache.zookeeper.ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);
        }
        else
        {
            // TrieBuilder is the sole writer under this root (decision 4) —
            // unconditional overwrite, the same last-writer-wins semantics as
            // the Aggregator checkpoint's PutItem upsert, not optimistic
            // concurrency versioning.
            await zooKeeper.setDataAsync(path, data);
        }

        logger.LogInformation("Published current trie version {Version} to ZooKeeper at {Path}", version, path);
    }

    private async Task EnsurePersistentNodeAsync(string path)
    {
        if (await zooKeeper.existsAsync(path) is not null) return;

        try
        {
            await zooKeeper.createAsync(path, [], org.apache.zookeeper.ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);
        }
        catch (KeeperException.NodeExistsException)
        {
            // Harmless race against this same method's own prior call, since
            // TrieBuilder is the only writer under this root — never a race
            // against a different process.
        }
    }
}
