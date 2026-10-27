using Microsoft.Extensions.DependencyInjection;
using Reporting.Application.Ingestion;
using Reporting.Application.Recognition;
using Reporting.Application.Scope;

namespace Reporting.Application;

/// <summary>Plain handler classes registered as scoped services (no MediatR — plan.md "Pattern Application").</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Projections (work_area_scope, task_projection)
        services.AddScoped<ApplyWorkAreaSnapshotHandler>();
        services.AddScoped<ApplySeasonStatusHandler>();
        services.AddScoped<ApplyTaskSnapshotHandler>();
        services.AddScoped<WorkAreaAccess>();

        // F-RPT-01 contribution types and recognition rules
        services.AddScoped<ContributionTypeHandler>();
        services.AddScoped<RecognitionRuleHandler>();

        // F-RPT-02/03 contributions, points, recognition
        services.AddScoped<ContributionRecorder>();
        services.AddScoped<IntegrationEventHandler>();
        services.AddScoped<RecognitionQueryHandler>();

        return services;
    }
}
