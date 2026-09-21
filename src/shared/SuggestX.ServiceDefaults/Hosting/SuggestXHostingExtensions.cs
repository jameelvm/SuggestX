using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Scalar.AspNetCore;
using StackExchange.Redis;
using SuggestX.ServiceDefaults.Aws;
using SuggestX.ServiceDefaults.Configuration;

namespace SuggestX.ServiceDefaults.Hosting;

/// <summary>
/// Everything every service host here would otherwise reimplement: options
/// binding, AWS clients, Redis, health checks, OpenAPI and CORS. Keeping it
/// here is what stops five services drifting apart.
/// </summary>
public static class SuggestXHostingExtensions
{
    /// <summary>Wiring common to every SuggestX service, API or worker.</summary>
    public static WebApplicationBuilder AddSuggestXServiceDefaults(
        this WebApplicationBuilder builder, string serviceName)
    {
        builder.Services
            .Configure<AwsOptions>(builder.Configuration.GetSection(AwsOptions.SectionName))
            .Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName))
            .Configure<DynamoOptions>(builder.Configuration.GetSection(DynamoOptions.SectionName))
            .Configure<ZooKeeperOptions>(builder.Configuration.GetSection(ZooKeeperOptions.SectionName));

        builder.Services.AddSuggestXAwsClients();

        var redisConnection = builder.Configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var configuration = ConfigurationOptions.Parse(redisConnection);
                // Never take a service down because the cache is unreachable —
                // SuggestionService in particular must degrade, not crash.
                configuration.AbortOnConnectFail = false;
                configuration.ConnectRetry = 5;
                return ConnectionMultiplexer.Connect(configuration);
            });
        }

        builder.Services.AddHealthChecks();

        // Identifies which service produced a log line once five of them are
        // interleaved in `docker compose logs`.
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        return builder;
    }

    /// <summary>Extra wiring for the services that expose an HTTP API.</summary>
    public static WebApplicationBuilder AddSuggestXApiDefaults(this WebApplicationBuilder builder)
    {
        // Controllers rather than minimal APIs, same trade JameX made:
        // ceremony for enforced structure ([ApiController] gives automatic
        // model-state validation, binding-source inference and consistent
        // ProblemDetails responses across five services for free).
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();

        // The Gateway proxies for the frontend, but hitting a service
        // directly from the Next.js origin during development is useful.
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins("http://localhost:3000", "http://localhost:8080")
            .AllowAnyHeader()
            .AllowAnyMethod()));

        return builder;
    }

    /// <summary>Endpoints every service exposes: liveness, readiness and API docs.</summary>
    public static WebApplication MapSuggestXDefaultEndpoints(this WebApplication app, string serviceName)
    {
        // Liveness: is the process up? Never touch a dependency here, or a
        // store blip restarts every healthy service that talks to it.
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive", service = serviceName }))
            .ExcludeFromDescription();

        // Readiness: should traffic be routed here? May check dependencies —
        // failing it removes the instance from rotation rather than killing it.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true
        }).ExcludeFromDescription();

        app.MapGet("/", () => Results.Ok(new
        {
            service = serviceName,
            docs = "/scalar",
            health = new[] { "/health/live", "/health/ready" }
        })).ExcludeFromDescription();

        // Attribute-routed controllers, but only for hosts that registered
        // MVC via AddSuggestXApiDefaults. Feature-detect rather than assume,
        // so a pure background worker calling only AddSuggestXServiceDefaults
        // never throws mapping controllers that were never registered.
        if (app.Services.GetService<IActionDescriptorCollectionProvider>() is not null)
            app.MapControllers();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options => options.WithTitle($"SuggestX {serviceName}"));
        }

        return app;
    }
}
