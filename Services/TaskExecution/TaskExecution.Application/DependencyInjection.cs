using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Application.Scopes;

namespace TaskExecution.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ApplySeasonScopeSnapshotHandler>();
        services.AddScoped<ApplyWorkAreaScopeSnapshotHandler>();
        return services;
    }
}
