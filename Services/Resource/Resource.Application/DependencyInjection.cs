using Microsoft.Extensions.DependencyInjection;

namespace Resource.Application;

/// <summary>
/// Plain handler classes registered as scoped services (no MediatR — plan.md "Pattern Application").
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
