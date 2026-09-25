using SuggestX.CollectionService.Services;

namespace SuggestX.CollectionService;

public static class CollectionServiceCollectionExtensions
{
    public static IServiceCollection AddCollectionServices(this IServiceCollection services)
    {
        services.AddSingleton<IPublishedEventStats, PublishedEventStats>();
        services.AddSingleton<ISearchEventPublisher, FirehoseSearchEventPublisher>();
        return services;
    }
}
