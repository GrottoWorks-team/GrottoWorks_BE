using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Data;

public interface IIdentityDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// F-IDN-01: idempotent seed of the five roles and the default ADMIN account.
/// The admin password comes from configuration (<c>Seed:AdminPassword</c>) and is never logged.
/// </summary>
public sealed class IdentityDataSeeder(
    IdentityDbContext dbContext,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<IdentityDataSeeder> logger)
    : IIdentityDataSeeder
{
    private static readonly (string Code, string Name, string Description)[] SeedRoles =
    [
        (RoleCodes.Admin, "System Administrator", "Manages accounts, roles, catalogs, logs, export and backup."),
        (RoleCodes.Parish, "Parish Christmas Committee", "Coordinates the whole season: planning, monitoring, reports."),
        (RoleCodes.Leader, "Work Area Leader", "Plans and approves work inside assigned work areas."),
        (RoleCodes.MaterialOfficer, "Material Officer", "Manages materials, purchases, donations and allocations."),
        (RoleCodes.Volunteer, "Volunteer", "Executes assigned or applied tasks, attendance and progress.")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        var existingCodes = (await dbContext.Roles
                .AsNoTracking()
                .Select(role => role.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var (code, name, description) in SeedRoles)
        {
            if (existingCodes.Contains(code))
            {
                continue;
            }

            dbContext.Roles.Add(Role.Create(code, name, description));
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {RoleCount} roles.", added);
        }
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var adminEmail = AppUser.NormalizeEmail(
            configuration["Seed:AdminEmail"] ?? "admin@grottoworks.local");

        if (await dbContext.Users.AnyAsync(user => user.Email == adminEmail, cancellationToken))
        {
            return;
        }

        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Seed:AdminPassword is not configured — the default ADMIN account was not created.");
            return;
        }

        var adminRole = await dbContext.Roles
            .FirstOrDefaultAsync(role => role.Code == RoleCodes.Admin, cancellationToken)
            ?? throw new InvalidOperationException("The ADMIN role is missing after role seeding.");

        var now = DateTimeOffset.UtcNow;
        var admin = AppUser.Create(
            "System Administrator",
            adminEmail,
            phone: null,
            parishId: null, // BR-67: only ADMIN has no parish.
            adminRole,
            now);

        admin.SetPasswordHash(passwordHasher.Hash(admin, password), now);

        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Seeded the default ADMIN account {Email}. Change its password immediately outside local development.",
            adminEmail);
    }
}
