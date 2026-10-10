using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Application.Communities.Commands.CreateCommunity;
using ParishCoordination.Application.Communities.Queries.GetCommunities;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
using ParishCoordination.Application.Seasons.Queries.GetSeasons;
using ParishCoordination.Application.Parishes.Commands.UpdateParish;
using ParishCoordination.Application.Parishes.Queries.GetParish;
using ParishCoordination.Application.Parishes.Queries.GetParishes;

namespace ParishCoordination.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetCommunitiesQueryHandler>();
        services.AddScoped<CreateCommunityCommandHandler>();
        services.AddScoped<CreateParishCommandHandler>();
        services.AddScoped<UpdateParishCommandHandler>();
        services.AddScoped<GetParishQueryHandler>();
        services.AddScoped<GetParishesQueryHandler>();
        services.AddScoped<GetSeasonsQueryHandler>();

        return services;
    }
}
