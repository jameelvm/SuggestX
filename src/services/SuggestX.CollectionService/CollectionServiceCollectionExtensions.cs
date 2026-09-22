using SuggestX.CollectionService.Services;

namespace SuggestX.CollectionService;

public static class CollectionServiceCollectionExtensions
{
    public static IServiceCollection AddCollectionServices(this IServiceCollection services)
    {
        // Singleton: the buffer's whole point is being shared, in-memory
        // state across every request this instance handles between flushes.
        services.AddSingleton<ISearchEventBuffer, SearchEventBuffer>();
        return services;
    }
}
