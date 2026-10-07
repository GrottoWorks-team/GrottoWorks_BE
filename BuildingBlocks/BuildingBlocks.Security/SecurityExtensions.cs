using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Security;

/// <summary>JWT bearer + current user registration (F-PLT-04).</summary>
public static class SecurityExtensions
{
    /// <summary>
    /// Validates the access token issued by Identity: HMAC-SHA256, issuer/audience/key from the
    /// <c>Jwt</c> config section, literal claim names (<c>MapInboundClaims = false</c>,
    /// <c>NameClaimType = "sub"</c>, <c>RoleClaimType = "role"</c>) per BuildingBlocks README §1.
    /// </summary>
    public static IServiceCollection AddGrottoWorksSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        string jwtSectionName = "Jwt")
    {
        var jwtSection = configuration.GetSection(jwtSectionName);
        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException(
                $"Configuration section '{jwtSectionName}' is missing 'SigningKey'.");

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"'{jwtSectionName}:SigningKey' must be at least 32 characters.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSection["Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>Registers <see cref="ICurrentUser"/> over the authenticated HttpContext.</summary>
    public static IServiceCollection AddGrottoWorksCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }

    /// <summary>
    /// Role policy helper (F-PLT-04). Uses the <c>role</c> claim configured by
    /// <see cref="AddGrottoWorksSecurity"/>, so <c>RequireGrottoWorksRole(GrottoWorksRoles.Leader)</c>
    /// works without extra claim mapping.
    /// </summary>
    public static AuthorizationPolicyBuilder RequireGrottoWorksRole(
        this AuthorizationPolicyBuilder builder,
        params string[] roles) =>
        builder.RequireRole(roles);
}
