using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Application.Communities;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Application.Seasons;
using ParishCoordination.Application.WorkAreas;
using ParishCoordination.Application.WorkAreaCategories;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Identity;
using ParishCoordination.Infrastructure.Repositories;
using ParishCoordination.Application.Users;
using ParishCoordination.Infrastructure.TaskScopeSync;
using Microsoft.Extensions.Http.Resilience;

namespace ParishCoordination.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ParishCoordinationDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ParishCoordinationDatabase' is missing.");
        }

        services.AddDbContext<ParishCoordinationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IParishRepository, ParishRepository>();
        services.AddScoped<ICommunityRepository, CommunityRepository>();
        services.AddScoped<ISeasonRepository, SeasonRepository>();
        services.AddScoped<IWorkAreaRepository, WorkAreaRepository>();
        services.AddScoped<IWorkAreaCategoryRepository, WorkAreaCategoryRepository>();
        services.AddScoped<IParishCoordinationDataSeeder, ParishCoordinationDataSeeder>();

        var syncOptions = TaskScopeSyncOptions.FromConfiguration(configuration);
        services.AddSingleton(syncOptions);
        if (syncOptions.Enabled)
        {
            services.AddScoped<TaskScopeBackfill>();
            services.AddHttpClient<ITaskScopeDelivery, HttpTaskScopeDelivery>(client =>
            {
                client.BaseAddress = syncOptions.BaseAddress;
                client.Timeout = Timeout.InfiniteTimeSpan;
            }).AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = syncOptions.RequestTimeout;
                options.AttemptTimeout.Timeout = syncOptions.RequestTimeout;
                options.CircuitBreaker.SamplingDuration = syncOptions.RequestTimeout * 3;
                // PUT retries belong to the durable outbox, not this HTTP pipeline.
                options.Retry.DisableForUnsafeHttpMethods();
            });
            services.AddHostedService<TaskScopeSyncWorker>();
        }

        var identityClientOptions = IdentityClientOptions.FromConfiguration(configuration);
        services.AddSingleton(identityClientOptions);
        services.AddHttpClient<IIdentityUserLookup, IdentityUserLookupClient>(client =>
            {
                client.BaseAddress = identityClientOptions.BaseAddress;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        return services;
    }
}
