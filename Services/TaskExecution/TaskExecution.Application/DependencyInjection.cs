using Microsoft.Extensions.DependencyInjection;

namespace TaskExecution.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services;
}
