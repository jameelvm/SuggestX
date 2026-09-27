using Microsoft.Extensions.Options;
using SuggestX.TrieBuilder.Configuration;
using SuggestX.TrieBuilder.Domain;
using SuggestX.TrieBuilder.Services;

namespace SuggestX.TrieBuilder.Jobs;

/// <summary>
/// The doc's "trie builder creates or updates tries using aggregated data"
/// step — Module 1 of Phase 4: read every current phrase/frequency, build a
/// fresh compressed trie, publish it. The flattened prefix -> top-N
/// projection into Redis (Module 2) and the ZooKeeper-coordinated version
/// swap (Module 3) aren't built yet — this module only proves the trie
/// itself is built correctly from real data, observable via
/// GET /_debug/search.
/// </summary>
public sealed class TrieBuildWorker(
    IPhraseFrequencyReader frequencyReader,
    ITrieHolder trieHolder,
    IOptions<TrieBuilderOptions> options,
    ILogger<TrieBuildWorker> logger) : BackgroundService
{
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
            // ITrieHolder until the next cycle succeeds.
            logger.LogError(ex, "Failed to read phrase frequencies from DynamoDB");
            return;
        }

        var trie = CompressedTrie.Build(frequencies);
        trieHolder.Replace(trie);

        logger.LogInformation(
            "Trie rebuilt: {PhraseCount} phrases, {NodeCount} nodes",
            trie.PhraseCount, trie.NodeCount);
    }
}
