using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Accounts;

public sealed class UserAccountStore(IdentityDbContext dbContext) : IUserAccountStore
{
    public async Task<AppUser?> GetByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .Include(user => user.Role)
            .Include(user => user.VolunteerProfile)
            .FirstOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
    }

    public async Task<AppUser?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .Include(user => user.Role)
            .Include(user => user.VolunteerProfile)
            .FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);
    }

    public async Task<AppUser?> GetDetailsByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .Include(user => user.Role)
            .Include(user => user.VolunteerProfile)
            .Include(user => user.VolunteerSkills)
            .Include(user => user.VolunteerAvailabilities)
            .AsSplitQuery()
            .FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken);
    }

    public Task<Role?> GetRoleByCodeAsync(
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        // Tracked on purpose: the role instance is attached to a new AppUser on Add.
        return dbContext.Roles.FirstOrDefaultAsync(role => role.Code == roleCode, cancellationToken);
    }

    public void Add(AppUser user)
    {
        dbContext.Users.Add(user);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.IsUniqueViolation(DbUpdateExceptionExtensions.AppUserEmailIndex))
        {
            // Lost the race against a concurrent registration with the same email.
            throw new DomainException(
                "AUTH_EMAIL_ALREADY_EXISTS",
                "An account with this email already exists.");
        }
    }
}
