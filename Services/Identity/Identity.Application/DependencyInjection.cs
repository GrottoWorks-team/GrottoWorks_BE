using Identity.Application.Admin.AssignUserRole;
using Identity.Application.Admin.CreateUser;
using Identity.Application.Admin.ListAssignableRoles;
using Identity.Application.Admin.ListUsers;
using Identity.Application.Admin.LockUser;
using Identity.Application.Admin.UnlockUser;
using Identity.Application.Admin.UpdateUser;
using Identity.Application.Auth.Login;
using Identity.Application.Auth.PasswordReset;
using Identity.Application.Auth.Logout;
using Identity.Application.Auth.TokenRefresh;
using Identity.Application.Auth.RegisterUser;
using Identity.Application.Skills.CreateSkill;
using Identity.Application.Skills.ListSkills;
using Identity.Application.Skills.UpdateSkill;
using Identity.Application.Users.GetInternalUsers;
using Identity.Application.Users.GetMyProfile;
using Identity.Application.Users.ListParishUsers;
using Identity.Application.Users.ReplaceMyAvailability;
using Identity.Application.Users.ReplaceMySkills;
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

        // S2: F-IDN-04 password reset
        services.AddScoped<RequestPasswordResetCommandHandler>();
        services.AddScoped<ResetPasswordCommandHandler>();

        // S2: F-IDN-07 volunteer skills & availability
        services.AddScoped<ReplaceMySkillsCommandHandler>();
        services.AddScoped<ReplaceMyAvailabilityCommandHandler>();

        // S2: F-IDN-08 admin user management
        services.AddScoped<ListUsersQueryHandler>();
        services.AddScoped<CreateUserCommandHandler>();
        services.AddScoped<UpdateUserCommandHandler>();
        services.AddScoped<LockUserCommandHandler>();
        services.AddScoped<UnlockUserCommandHandler>();
        services.AddScoped<AssignUserRoleCommandHandler>();
        services.AddScoped<ListAssignableRolesQueryHandler>();

        // S2: F-IDN-09 internal lookup, F-IDN-10 parish users
        services.AddScoped<GetInternalUsersQueryHandler>();
        services.AddScoped<ListParishUsersQueryHandler>();

        return services;
    }
}
