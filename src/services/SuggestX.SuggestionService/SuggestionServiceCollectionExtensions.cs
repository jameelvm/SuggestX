using SuggestX.SuggestionService.Configuration;
using SuggestX.SuggestionService.Jobs;
using SuggestX.SuggestionService.Services;

namespace SuggestX.SuggestionService;

public static class SuggestionServiceCollectionExtensions
{
    public static IServiceCollection AddSuggestionServiceServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SuggestionServiceOptions>(configuration.GetSection(SuggestionServiceOptions.SectionName));

        services.AddSingleton<ICurrentTrieVersion, CurrentTrieVersionHolder>();
        services.AddSingleton<IZooKeeperVersionReader, ZooKeeperVersionReader>();
        services.AddSingleton<ISuggestionReader, RedisSuggestionReader>();
        services.AddHostedService<CurrentVersionPoller>();

        return services;
    }
}
