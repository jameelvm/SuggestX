using Amazon;
using Amazon.DynamoDBv2;
using Amazon.KinesisFirehose;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.ServiceDefaults.Aws;

public static class AwsClientFactory
{
    /// <summary>
    /// Registers the AWS clients any service here may need. All singletons —
    /// the SDK clients are thread-safe and hold connection pools, so
    /// creating them per request is a known source of socket exhaustion.
    /// </summary>
    public static IServiceCollection AddSuggestXAwsClients(this IServiceCollection services)
    {
        services.AddSingleton<IAmazonS3>(sp =>
            new AmazonS3Client(Credentials(sp), Configure(sp, new AmazonS3Config
            {
                // LocalStack is addressed as host/bucket/key rather than
                // bucket.host/key, so virtual-host addressing must be off.
                ForcePathStyle = true
            })));

        services.AddSingleton<IAmazonDynamoDB>(sp =>
            new AmazonDynamoDBClient(Credentials(sp), Configure(sp, new AmazonDynamoDBConfig())));

        // CollectionService publishes every accepted search event here;
        // Firehose owns the buffering and the eventual S3 write. See
        // DESIGN.md decision 11.
        services.AddSingleton<IAmazonKinesisFirehose>(sp =>
            new AmazonKinesisFirehoseClient(Credentials(sp), Configure(sp, new AmazonKinesisFirehoseConfig())));

        return services;
    }

    private static AWSCredentials Credentials(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<AwsOptions>>().Value;

        // Real AWS: fall through to the default chain (instance role, IRSA,
        // environment, SSO). Static keys exist only for LocalStack.
        if (string.IsNullOrWhiteSpace(options.AccessKey))
            return DefaultAWSCredentialsIdentityResolver.GetCredentials();

        return new BasicAWSCredentials(options.AccessKey, options.SecretKey);
    }

    private static TConfig Configure<TConfig>(IServiceProvider sp, TConfig config)
        where TConfig : ClientConfig
    {
        var options = sp.GetRequiredService<IOptions<AwsOptions>>().Value;

        if (options.UsesCustomEndpoint)
        {
            // Setting ServiceURL and RegionEndpoint together throws in SDK v4.
            // AuthenticationRegion supplies the region the signature needs.
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return config;
    }
}
