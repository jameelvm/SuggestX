using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using org.apache.zookeeper;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.ServiceDefaults.ZooKeeper;

/// <summary>
/// Registers a single, shared <see cref="org.apache.zookeeper.ZooKeeper"/>
/// client — the same singleton-client pattern as <c>AwsClientFactory</c> and
/// the Redis <c>IConnectionMultiplexer</c> registration, since this SDK's
/// client is itself a connection pool/session manager, not something to
/// construct per call. Registered here rather than per-service because both
/// sides of decision 4's coordination contract need it: TrieBuilder writes,
/// SuggestionService (Phase 5) reads.
/// </summary>
public static class ZooKeeperClientFactory
{
    public static IServiceCollection AddSuggestXZooKeeperClient(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ZooKeeperOptions>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SuggestX.ZooKeeper");

            // The constructor returns immediately and connects in the
            // background — ZooKeeperNetEx owns reconnection on its own, the
            // watcher below only observes session-state transitions for our
            // own logging, it never drives reconnect logic itself.
            return new org.apache.zookeeper.ZooKeeper(
                options.ConnectionString,
                options.SessionTimeoutMs,
                new SessionStateWatcher(logger));
        });

        return services;
    }

    private sealed class SessionStateWatcher(ILogger logger) : Watcher
    {
        public override Task process(WatchedEvent @event)
        {
            logger.LogInformation("ZooKeeper session state: {State}", @event.getState());
            return Task.CompletedTask;
        }
    }
}
