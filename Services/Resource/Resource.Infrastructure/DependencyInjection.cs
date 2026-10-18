using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Infrastructure.Catalog;
using Resource.Application.Scope;
using Resource.Infrastructure.Scope;
using Resource.Application.Requirements;
using Resource.Infrastructure.Requirements;
using Resource.Application.Inventory;
using Resource.Infrastructure.Inventory;
using Resource.Application.Purchases;
using Resource.Infrastructure.Purchases;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Observability;

namespace Resource.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ResourceDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'ResourceDatabase' is missing.");
        }

        services.AddDbContext<ResourceDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IIntegrationEventPublisher, LoggingIntegrationEventPublisher>();
        services.AddScoped<IMaterialCatalogStore, MaterialCatalogStore>();
        services.AddScoped<IWorkAreaScopeStore, WorkAreaScopeStore>();
        services.AddScoped<IMaterialRequirementStore, MaterialRequirementStore>();
        services.AddScoped<IInventoryStore, InventoryStore>();
        services.AddScoped<IPurchaseRequestStore, PurchaseRequestStore>();

        return services;
    }
}
