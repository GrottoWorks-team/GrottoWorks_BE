using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Infrastructure.SystemExport;

namespace NotificationAudit.Infrastructure.SystemJobs;

public static class SystemJobRegistration
{
    public static IServiceCollection AddSystemJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSystemJobExecution(configuration);
        services.AddScoped<SystemJobStore>();
        services.AddScoped<ISystemJobStore>(provider => provider.GetRequiredService<SystemJobStore>());
        services.AddScoped<ISystemArtifactAccess>(provider => provider.GetRequiredService<SystemJobStore>());
        services.AddScoped<SystemJobHandler>();
        services.AddScoped<SystemExportSource>();
        services.AddScoped<ISystemArtifactBuilder, SystemExportArtifactBuilder>();
        services.AddHttpClient("SystemExport", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });
        if (configuration.GetValue("SystemJobs:ExportWorkerEnabled", false)) services.AddHostedService<SystemExportWorker>();
        if (configuration.GetValue("SystemJobs:CleanupEnabled", false)) services.AddHostedService<SystemObjectCleanupWorker>();
        return services;
    }

    public static IServiceCollection AddSystemJobExecution(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SystemJobOptions>().Bind(configuration.GetSection("SystemJobs"))
            .Validate(options => options.MaximumAttempts is >= 1 and <= 10 && options.RetentionDays is >= 1 and <= 365 &&
                options.MaximumSourceBytes > 0 && options.SourceTimeoutMinutes is >= 1 and <= 180,
                "System job retry, retention, timeout or byte limits are invalid.").ValidateOnStart();
        services.AddScoped<ISystemJobExecutionStore, SystemJobExecutionStore>();
        services.AddScoped<ISystemArtifactStorage, SystemArtifactStorage>();
        services.AddScoped<ExecuteSystemJob>();
        return services;
    }
}
