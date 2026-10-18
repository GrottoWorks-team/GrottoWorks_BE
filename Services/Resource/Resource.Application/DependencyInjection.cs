using Microsoft.Extensions.DependencyInjection;
using Resource.Application.Catalog;

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

        return services;
    }
}
