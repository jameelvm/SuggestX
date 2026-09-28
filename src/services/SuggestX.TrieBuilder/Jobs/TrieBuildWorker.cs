using Microsoft.Extensions.Options;
using SuggestX.TrieBuilder.Configuration;
using SuggestX.TrieBuilder.Domain;
using SuggestX.TrieBuilder.Services;

namespace SuggestX.TrieBuilder.Jobs;

/// <summary>
/// The doc's "trie builder creates or updates tries using aggregated data"
/// step. Module 1: read every current phrase/frequency, build a fresh
/// compressed trie, publish it in-process via <see cref="ITrieHolder"/>.
/// Module 2 (this addition): walk that trie once to precompute
/// <c>prefix -> top-N</c> and write the flattened result into Redis under
/// a versioned namespace, cleaning up the previous version's keys once the
/// new one is fully written.
/// <para>
/// <b>What Module 2 does not yet do, on purpose:</b> the version number
/// here is this process's own private, in-memory counter — nothing durable
/// or shared tracks "which version is current" the way a real reader would
/// need to know. There is no reader yet (SuggestionService is Phase 5), so
/// this is a real, deliberate scoping choice, not an oversight: Module 3
/// is what introduces ZooKeeper as the durable, coordinated source of
/// truth for the current version, mirroring exactly how Aggregator's own
/// Module 3 replaced an in-memory-only checkpoint with a durable one. A
/// TrieBuilder restart today resets this counter to 1 and starts
/// overwriting <c>trie:v1:*</c> again — harmless right now precisely
/// because nothing reads a "current version" pointer yet, but this is the
/// exact gap Module 3 exists to close before anything depends on it.
/// </para>
/// </summary>
public sealed class TrieBuildWorker(
    IPhraseFrequencyReader frequencyReader,
    ITrieHolder trieHolder,
    IFlattenedCachePublisher cachePublisher,
    ITrieBuildStats stats,
    IOptions<TrieBuilderOptions> options,
    ILogger<TrieBuildWorker> logger) : BackgroundService
{
    private int _version;
    private HashSet<string> _previousPrefixes = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

        _previousPrefixes = [.. flattened.Keys];
        stats.RecordBuild(_version, flattened.Count);
    }
}
