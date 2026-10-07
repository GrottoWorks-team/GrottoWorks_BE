using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Repositories;

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

        return services;
    }
}
