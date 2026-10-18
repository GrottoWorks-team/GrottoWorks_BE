using Microsoft.Extensions.DependencyInjection;
using Resource.Application.Catalog;
using Resource.Application.Scope;
using Resource.Application.Requirements;

namespace Resource.Application;

/// <summary>
/// Plain handler classes registered as scoped services (no MediatR — plan.md "Pattern Application").
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // F-RES-02 material catalog
        services.AddScoped<ListMaterialCategoriesQueryHandler>();
        services.AddScoped<CreateMaterialCategoryCommandHandler>();
        services.AddScoped<UpdateMaterialCategoryCommandHandler>();
        services.AddScoped<ListMaterialsQueryHandler>();
        services.AddScoped<CreateMaterialCommandHandler>();
        services.AddScoped<UpdateMaterialCommandHandler>();

        // F-RES-01 work-area scope projection
        services.AddScoped<ApplyWorkAreaSnapshotHandler>();
        services.AddScoped<ApplySeasonStatusHandler>();
        services.AddScoped<WorkAreaAccess>();

        // F-RES-03 material requirements
        services.AddScoped<ListMaterialRequirementsQueryHandler>();
        services.AddScoped<CreateMaterialRequirementCommandHandler>();
        services.AddScoped<UpdateMaterialRequirementCommandHandler>();

        return services;
    }
}
