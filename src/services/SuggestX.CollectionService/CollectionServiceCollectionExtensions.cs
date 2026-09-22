using SuggestX.CollectionService.Configuration;
using SuggestX.CollectionService.Jobs;
using SuggestX.CollectionService.Services;

namespace SuggestX.CollectionService;

public static class CollectionServiceCollectionExtensions
{
    public static IServiceCollection AddCollectionServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CollectionOptions>(configuration.GetSection(CollectionOptions.SectionName));

        // Singleton: the buffer's whole point is being shared, in-memory
        // state across every request this instance handles between flushes.
        services.AddSingleton<ISearchEventBuffer, SearchEventBuffer>();
        services.AddHostedService<SearchEventFlushWorker>();
        return services;
    }
}
