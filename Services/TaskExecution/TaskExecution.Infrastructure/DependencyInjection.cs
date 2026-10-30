using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Infrastructure.Data;
using TaskExecution.Infrastructure.Scopes;
using TaskExecution.Infrastructure.Tasks;

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
        services.AddScoped<IScopeSnapshotTransaction, ScopeSnapshotTransaction>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<TaskExecution.Application.Events.ITaskEventOutbox, TaskExecution.Infrastructure.Events.TaskEventOutbox>();
        return services;
    }
}
