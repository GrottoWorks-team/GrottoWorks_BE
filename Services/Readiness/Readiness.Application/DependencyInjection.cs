using Microsoft.Extensions.DependencyInjection;
using Readiness.Application.Checklists;
using Readiness.Application.Scope;

namespace Readiness.Application;

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

        // F-RDY-02..06 checklists
        services.AddScoped<ChecklistLoader>();
        services.AddScoped<CreateReadinessCheckCommandHandler>();
        services.AddScoped<ChecklistDefinitionHandler>();
        services.AddScoped<InspectionCommandHandler>();
        services.AddScoped<AttachEvidenceCommandHandler>();
        services.AddScoped<ReadinessDecisionHandler>();
        services.AddScoped<GetSeasonReadinessQueryHandler>();
        services.AddScoped<CorrectiveTaskSync>();

        return services;
    }
}
