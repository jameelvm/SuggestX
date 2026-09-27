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
        services.AddHostedService<TrieBuildWorker>();
        return services;
    }
}
