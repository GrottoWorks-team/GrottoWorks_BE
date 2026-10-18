using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resource.Application.Abstractions;
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

        return services;
    }
}
