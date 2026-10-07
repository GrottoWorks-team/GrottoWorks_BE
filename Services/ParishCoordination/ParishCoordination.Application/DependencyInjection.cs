using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
using ParishCoordination.Application.Parishes.Commands.UpdateParish;
using ParishCoordination.Application.Parishes.Queries.GetParish;
using ParishCoordination.Application.Parishes.Queries.GetParishes;

namespace ParishCoordination.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateParishCommandHandler>();
        services.AddScoped<UpdateParishCommandHandler>();
        services.AddScoped<GetParishQueryHandler>();
        services.AddScoped<GetParishesQueryHandler>();

        return services;
    }
}
