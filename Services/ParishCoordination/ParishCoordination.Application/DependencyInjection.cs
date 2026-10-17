using Microsoft.Extensions.DependencyInjection;
using ParishCoordination.Application.Communities.Commands.CreateCommunity;
using ParishCoordination.Application.Communities.Commands.UpdateCommunity;
using ParishCoordination.Application.Communities.Queries.GetCommunities;
using ParishCoordination.Application.Parishes.Commands.CreateParish;
using ParishCoordination.Application.Seasons.Queries.GetSeasons;
using ParishCoordination.Application.Seasons.Queries.GetSeason;
using ParishCoordination.Application.WorkAreas.Queries.GetWorkArea;
using ParishCoordination.Application.WorkAreas.Queries.ListWorkAreas;
using ParishCoordination.Application.WorkAreas.Commands.CreateWorkArea;
using ParishCoordination.Application.WorkAreas.Commands.AssignWorkAreaCommunity;
using ParishCoordination.Application.WorkAreas.Commands.UpdateWorkArea;
using ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;
using ParishCoordination.Application.WorkAreaCategories.Commands.CreateWorkAreaCategory;
using ParishCoordination.Application.WorkAreaCategories.Commands.UpdateWorkAreaCategory;
using ParishCoordination.Application.Seasons.Commands.CreateSeason;
using ParishCoordination.Application.Seasons.Commands.UpdateSeason;
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
        services.AddScoped<UpdateCommunityCommandHandler>();
        services.AddScoped<CreateParishCommandHandler>();
        services.AddScoped<UpdateParishCommandHandler>();
        services.AddScoped<GetParishQueryHandler>();
        services.AddScoped<GetParishesQueryHandler>();
        services.AddScoped<GetSeasonsQueryHandler>();
        services.AddScoped<GetSeasonQueryHandler>();
        services.AddScoped<ListWorkAreasQueryHandler>();
        services.AddScoped<GetWorkAreaQueryHandler>();
        services.AddScoped<CreateWorkAreaCommandHandler>();
        services.AddScoped<AssignWorkAreaCommunityCommandHandler>();
        services.AddScoped<UpdateWorkAreaCommandHandler>();
        services.AddScoped<CreateSeasonCommandHandler>();
        services.AddScoped<UpdateSeasonCommandHandler>();
        services.AddScoped<ListWorkAreaCategoriesQueryHandler>();
        services.AddScoped<CreateWorkAreaCategoryCommandHandler>();
        services.AddScoped<UpdateWorkAreaCategoryCommandHandler>();

        return services;
    }
}
