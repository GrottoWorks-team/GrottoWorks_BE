using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ApplySeasonScopeSnapshotHandler>();
        services.AddScoped<ApplyWorkAreaScopeSnapshotHandler>();
        services.AddScoped<TaskScopeAccess>();
        services.AddScoped<GetTasksQueryHandler>();
        services.AddScoped<GetTaskQueryHandler>();
        services.AddScoped<CreateTaskCommandHandler>();
        return services;
    }
}
