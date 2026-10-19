using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Application.Scopes;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Infrastructure.Scopes;

namespace TaskExecution.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TaskExecutionDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'TaskExecutionDatabase' is missing.");
        }

        services.AddDbContext<TaskExecutionDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IScopeProjectionStore, ScopeProjectionStore>();
        return services;
    }
}
