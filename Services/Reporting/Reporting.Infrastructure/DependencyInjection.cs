using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reporting.Application.Abstractions;
using Reporting.Application.Exports;
using Reporting.Application.Ingestion;
using Reporting.Application.ReadModels;
using Reporting.Application.Recognition;
using Reporting.Application.Scope;
using Reporting.Application.Users;
using Reporting.Infrastructure.Data;
using Reporting.Infrastructure.Exports;
using Reporting.Infrastructure.Identity;
using Reporting.Infrastructure.Observability;
using Reporting.Infrastructure.ReadModels;
using Reporting.Infrastructure.Recognition;
using Reporting.Infrastructure.Scope;

namespace Reporting.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ReportingDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'ReportingDatabase' is missing.");
        }

        services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IIntegrationEventPublisher, LoggingIntegrationEventPublisher>();
        services.AddScoped<IWorkAreaScopeStore, WorkAreaScopeStore>();
        services.AddScoped<ITaskProjectionStore, TaskProjectionStore>();
        services.AddScoped<IRecognitionCatalogStore, RecognitionCatalogStore>();
        services.AddScoped<ContributionStore>();
        services.AddScoped<IContributionStore>(provider => provider.GetRequiredService<ContributionStore>());
        services.AddScoped<IInboxStore>(provider => provider.GetRequiredService<ContributionStore>());
        services.AddScoped<IRecognitionQueryStore>(provider => provider.GetRequiredService<ContributionStore>());
        services.AddScoped<IReadModelStore, ReadModelStore>();

        // F-RPT-06 exports: jobs in report_export_job, rendered by the background worker.
        services.AddScoped<IReportExportStore, ReportExportStore>();
        services.AddSingleton<IReportFileRenderer, ReportFileRenderer>();
        services.AddSingleton<ExportSignal>();
        services.AddSingleton<IExportSignal>(provider => provider.GetRequiredService<ExportSignal>());
        if (configuration.GetValue("Exports:WorkerEnabled", true))
        {
            services.AddHostedService<ReportExportWorker>();
        }
        // F-IDN-09 names for rankings and reports; optional (best effort) so Reporting runs without Identity.
        if (Uri.TryCreate(configuration[IdentityClientOptions.BaseAddressConfigurationKey], UriKind.Absolute, out var identity) &&
            (identity.Scheme == Uri.UriSchemeHttp || identity.Scheme == Uri.UriSchemeHttps))
        {
            services.AddSingleton(new IdentityClientOptions(identity, configuration[IdentityClientOptions.InternalApiKeyConfigurationKey] ?? string.Empty));
            services.AddHttpContextAccessor();
            services.AddHttpClient<IUserDirectory, IdentityUserDirectoryClient>(client =>
                {
                    client.BaseAddress = identity;
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .AddStandardResilienceHandler(options =>
                {
                    // Names are optional: a slow or dead Identity must not slow reports down (NFR < 3 s).
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
                    options.Retry.MaxRetryAttempts = 1;
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
                });
        }
        else
        {
            services.AddSingleton<IUserDirectory, NoUserDirectory>();
        }

        return services;
    }
}
