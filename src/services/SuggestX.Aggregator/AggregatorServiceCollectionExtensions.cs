using SuggestX.Aggregator.Configuration;
using SuggestX.Aggregator.Jobs;
using SuggestX.Aggregator.Services;

namespace SuggestX.Aggregator;

public static class AggregatorServiceCollectionExtensions
{
    public static IServiceCollection AddAggregatorServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AggregatorOptions>(configuration.GetSection(AggregatorOptions.SectionName));

        services.AddSingleton<IAggregatorCheckpoint, DynamoAggregatorCheckpoint>();
        services.AddSingleton<IAggregatorStats, AggregatorStats>();
        services.AddSingleton<IRawLogReader, S3RawLogReader>();
        services.AddSingleton<IPhraseFrequencyWriter, DynamoPhraseFrequencyWriter>();
        services.AddHostedService<RawLogPollingWorker>();
        return services;
    }
}
