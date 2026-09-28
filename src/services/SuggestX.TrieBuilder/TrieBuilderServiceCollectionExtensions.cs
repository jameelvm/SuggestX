using SuggestX.TrieBuilder.Configuration;
using SuggestX.TrieBuilder.Jobs;
using SuggestX.TrieBuilder.Services;

namespace SuggestX.TrieBuilder;

public static class TrieBuilderServiceCollectionExtensions
{
    public static IServiceCollection AddTrieBuilderServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TrieBuilderOptions>(configuration.GetSection(TrieBuilderOptions.SectionName));

        services.AddSingleton<ITrieHolder, TrieHolder>();
        services.AddSingleton<IPhraseFrequencyReader, DynamoPhraseFrequencyReader>();
        services.AddSingleton<IFlattenedCachePublisher, RedisFlattenedCachePublisher>();
        services.AddSingleton<ITrieSnapshotStore, S3TrieSnapshotStore>();
        services.AddSingleton<IZooKeeperVersionPublisher, ZooKeeperVersionPublisher>();
        services.AddSingleton<ITrieBuildStats, TrieBuildStats>();
        services.AddHostedService<TrieBuildWorker>();
        return services;
    }
}
