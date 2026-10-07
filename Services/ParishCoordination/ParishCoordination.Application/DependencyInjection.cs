using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Application.Parishes.CreateParish;
using ParishCoordination.Application.Parishes.GetParishes;

namespace ParishCoordination.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateParishCommandHandler>();
        services.AddScoped<GetParishesQueryHandler>();

        return services;
    }
}
