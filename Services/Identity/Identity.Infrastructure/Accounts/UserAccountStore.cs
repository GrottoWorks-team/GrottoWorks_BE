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

    public async Task<IReadOnlyList<AppUser>> GetDetailsByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Users
            .AsNoTracking()
            .Include(user => user.Role)
            .Include(user => user.VolunteerProfile)
            .Include(user => user.VolunteerSkills)
            .AsSplitQuery()
            .Where(user => userIds.Contains(user.UserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AppUser>> SearchAsync(
        UserSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsNoTracking().AsQueryable();

        if (criteria.ParishId is { } parishId)
        {
            query = query.Where(user => user.ParishId == parishId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.RoleCode))
        {
            var roleCode = criteria.RoleCode;
            query = query.Where(user => user.Role!.Code == roleCode);
        }

        if (criteria.Status is { } status)
        {
            query = query.Where(user => user.Status == status);
        }

        if (criteria.CommunityId is { } communityId)
        {
            query = query.Where(user => user.VolunteerProfile!.CommunityId == communityId);
        }

        if (criteria.SkillId is { } skillId)
        {
            query = query.Where(user => user.VolunteerSkills.Any(link => link.SkillId == skillId));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var pattern = "%" + EscapeLikePattern(criteria.Search.Trim()) + "%";
            query = query.Where(user =>
                EF.Functions.ILike(user.FullName, pattern, "\\") ||
                EF.Functions.ILike(user.Email, pattern, "\\"));
        }

        var totalItems = await query.LongCountAsync(cancellationToken);

        IOrderedQueryable<AppUser> ordered = (criteria.SortField, criteria.SortDescending) switch
        {
            (UserSortField.Email, false) => query.OrderBy(user => user.Email),
            (UserSortField.Email, true) => query.OrderByDescending(user => user.Email),
            (UserSortField.CreatedAt, false) => query.OrderBy(user => user.CreatedAt),
            (UserSortField.CreatedAt, true) => query.OrderByDescending(user => user.CreatedAt),
            (_, false) => query.OrderBy(user => user.FullName),
            (_, true) => query.OrderByDescending(user => user.FullName)
        };

        var items = await ordered
            .ThenBy(user => user.UserId)
            .Skip((criteria.Page - 1) * criteria.Size)
            .Take(criteria.Size)
            .Include(user => user.Role)
            .Include(user => user.VolunteerProfile)
            .Include(user => user.VolunteerSkills)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedResult<AppUser>(items, totalItems);
    }

    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);
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
        catch (DbUpdateConcurrencyException exception)
            when (exception.Entries.Any(entry => entry.Entity is AppUser))
        {
            // xmin changed between load and save: another request updated the account (API Contract 4.5).
            throw new DomainException(
                "USER_VERSION_MISMATCH",
                "The account was changed by someone else. Reload it and try again.");
        }
    }

    /// <summary>Escapes LIKE wildcards so a search for "50%" matches the literal text.</summary>
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
