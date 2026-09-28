using Microsoft.Extensions.Options;
using SuggestX.TrieBuilder.Configuration;
using SuggestX.TrieBuilder.Domain;
using SuggestX.TrieBuilder.Services;

namespace SuggestX.TrieBuilder.Jobs;

/// <summary>
/// The doc's "trie builder creates or updates tries using aggregated data"
/// step. Module 1: read every current phrase/frequency, build a fresh
/// compressed trie, publish it in-process via <see cref="ITrieHolder"/>.
/// Module 2: walk that trie once to precompute <c>prefix -> top-N</c> and
/// write the flattened result into Redis under a versioned namespace,
/// cleaning up the previous version's keys once the new one is fully
/// written. Module 3 (this addition): persist that same flattened result to
/// S3 for durability, and flip a ZooKeeper znode to say which version is
/// current — only after Redis already has it live — so a restart recovers
/// exactly where the last successful cycle left off instead of resetting
/// the version counter to 1 and re-publishing over an unrelated `trie:v1:*`
/// namespace, closing the gap Module 2 deliberately left open (decision 14).
/// </summary>
public sealed class TrieBuildWorker(
    IPhraseFrequencyReader frequencyReader,
    ITrieHolder trieHolder,
    IFlattenedCachePublisher cachePublisher,
    ITrieSnapshotStore snapshotStore,
    IZooKeeperVersionPublisher versionPublisher,
    ITrieBuildStats stats,
    IOptions<TrieBuilderOptions> options,
    ILogger<TrieBuildWorker> logger) : BackgroundService
{
    private int _version;
    private HashSet<string> _previousPrefixes = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(options.Value.BuildIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        // Builds immediately on startup, same reasoning as Aggregator's own
        // polling worker: a fresh TrieBuilder may start well after real
        // frequency data already exists, so waiting a full interval before
        // the first build would be a pure, avoidable delay.
        do
        {
            await BuildAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Reads ZooKeeper's <c>current_version</c> znode once, before the build
    /// loop starts. A value means a previous process instance already
    /// published successfully: resume counting from there, and load that
    /// version's S3 snapshot to know which Redis prefixes it holds, so the
    /// very next cycle's blue/green publish (Module 2) cleans them up
    /// correctly instead of leaking them forever. No value at all — a
    /// brand-new deployment — leaves both fields at their zero/empty
    /// defaults, identical to Module 2's original behavior.
    /// </summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        int? recoveredVersion;
        try
        {
            recoveredVersion = await versionPublisher.ReadCurrentVersionAsync(ct);
        }
        catch (Exception ex)
        {
            // A ZooKeeper blip at startup must not stop TrieBuilder from
            // running at all — worst case, it starts back at version 1 and
            // republishes everything, which is exactly Module 2's original,
            // already-accepted behavior, not a new failure mode.
            logger.LogError(ex, "Failed to read current trie version from ZooKeeper; starting fresh at version 1");
            return;
        }

        if (recoveredVersion is not { } version) return;

        _version = version;

        var prefixes = await snapshotStore.LoadPrefixesAsync(version, ct);
        _previousPrefixes = prefixes is not null ? [.. prefixes] : [];

        stats.RecordRecovery(version);
        logger.LogInformation(
            "Recovered version {Version} from ZooKeeper with {PrefixCount} known prefixes to clean up on next publish",
            version, _previousPrefixes.Count);
    }

    private async Task BuildAsync(CancellationToken ct)
    {
        IReadOnlyList<(string Phrase, long Frequency)> frequencies;
        try
        {
            frequencies = await frequencyReader.ReadAllAsync(ct);
        }
        catch (Exception ex)
        {
            // A transient DynamoDB blip must not crash the build loop — the
            // previously published trie (if any) keeps being served from
            // ITrieHolder, and the previously published Redis version keeps
            // being whatever it already was, until the next cycle succeeds.
            logger.LogError(ex, "Failed to read phrase frequencies from DynamoDB");
            return;
        }

        var trie = CompressedTrie.Build(frequencies);
        trieHolder.Replace(trie);

        logger.LogInformation(
            "Trie rebuilt: {PhraseCount} phrases, {NodeCount} nodes",
            trie.PhraseCount, trie.NodeCount);

        var flattened = trie.FlattenPrefixes(options.Value.MaxPrefixLength, options.Value.TopN);
        _version++;

        try
        {
            await cachePublisher.PublishAsync(_version, flattened, _previousPrefixes, ct);
        }
        catch (Exception ex)
        {
            // The in-process trie already switched over successfully above
            // (ITrieHolder), so the debug endpoint reflects real, current
            // data either way. A failed Redis publish just means this
            // cycle's version isn't visible in the cache — logged, not
            // fatal, and the version counter still advances so the next
            // cycle publishes as a new version rather than silently
            // retrying the same one against whatever partial state this
            // attempt left behind.
            logger.LogError(ex, "Failed to publish trie version {Version} to Redis", _version);
            return;
        }

        // Durability and coordination only follow a Redis publish that has
        // already succeeded — the same blue/green ordering as the Redis
        // publish itself, one level up: don't tell S3/ZooKeeper a version is
        // real until the thing that actually serves it already agrees.
        try
        {
            await snapshotStore.SaveAsync(_version, flattened, ct);
            await versionPublisher.PublishCurrentVersionAsync(_version, ct);
        }
        catch (Exception ex)
        {
            // Redis is already correctly serving this version — the read
            // path (the part that matters most) is unaffected. What's lost
            // is only this cycle's contribution to *recovery* state: a
            // restart before the next successful cycle would resume from an
            // older version than this one. Accepted, not fixed here, the
            // same "small bounded gap over defensive machinery" tradeoff as
            // decision 13's checkpoint race.
            logger.LogError(ex, "Failed to persist snapshot/version pointer for version {Version} to S3/ZooKeeper", _version);
        }

        _previousPrefixes = [.. flattened.Keys];
        stats.RecordBuild(_version, flattened.Count);
    }
}
