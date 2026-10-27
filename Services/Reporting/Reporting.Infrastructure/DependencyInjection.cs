using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reporting.Application.Abstractions;
using Reporting.Application.Exports;
using Reporting.Application.Ingestion;
using Reporting.Application.ReadModels;
using Reporting.Application.Recognition;
using Reporting.Application.Scope;
using Reporting.Infrastructure.Data;
using Reporting.Infrastructure.Exports;
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
        return services;
    }
}
