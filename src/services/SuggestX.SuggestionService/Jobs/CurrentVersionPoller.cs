using Microsoft.Extensions.Options;
using SuggestX.SuggestionService.Configuration;
using SuggestX.SuggestionService.Services;

namespace SuggestX.SuggestionService.Jobs;

/// <summary>
/// Keeps <see cref="ICurrentTrieVersion"/> up to date by re-reading
/// ZooKeeper's <c>current_version</c> znode on a timer (decision 16). A
/// failed poll — ZooKeeper unreachable — is logged and otherwise ignored:
/// the last successfully known version keeps being served, matching this
/// project's failure-mode table exactly ("ZooKeeper unreachable" degrades
/// to serving a cached version, not to failing every request).
/// </summary>
public sealed class CurrentVersionPoller(
    IZooKeeperVersionReader reader,
    ICurrentTrieVersion currentVersion,
    IOptions<SuggestionServiceOptions> options,
    ILogger<CurrentVersionPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.VersionPollIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        // Polls immediately on startup, same reasoning as every other
        // timer-driven worker in this system: waiting a full interval
        // before the first read would be a pure, avoidable delay before
        // this service can answer a single real request.
        do
        {
            await PollAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            var version = await reader.ReadCurrentVersionAsync(ct);

            // Null means "TrieBuilder hasn't published anything yet," not
            // "the version just disappeared" — deliberately leaves whatever
            // was last known (if anything) in place rather than clearing it.
            if (version is { } v) currentVersion.Set(v);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to poll current trie version from ZooKeeper; continuing to serve last-known version {Version}",
                currentVersion.Version);
        }
    }
}
