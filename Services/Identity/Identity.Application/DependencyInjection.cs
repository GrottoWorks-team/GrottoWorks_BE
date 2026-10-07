using Identity.Application.Auth.Login;
using Identity.Application.Auth.Logout;
using Identity.Application.Auth.TokenRefresh;
using Identity.Application.Auth.RegisterUser;
using Identity.Application.Skills.CreateSkill;
using Identity.Application.Skills.ListSkills;
using Identity.Application.Skills.UpdateSkill;
using Identity.Application.Users.GetMyProfile;
using Identity.Application.Users.UpdateMyProfile;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Application;

/// <summary>
/// Handler registration follows the ParishCoordination pattern: plain handler classes resolved as
/// scoped services (no MediatR — see plan.md, "Pattern Application").
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterUserCommandHandler>();
        services.AddScoped<LoginCommandHandler>();
        services.AddScoped<RefreshTokenCommandHandler>();
        services.AddScoped<LogoutCommandHandler>();
        services.AddScoped<GetMyProfileQueryHandler>();
        services.AddScoped<UpdateMyProfileCommandHandler>();
        services.AddScoped<ListSkillsQueryHandler>();
        services.AddScoped<CreateSkillCommandHandler>();
        services.AddScoped<UpdateSkillCommandHandler>();

        return services;
    }
}
