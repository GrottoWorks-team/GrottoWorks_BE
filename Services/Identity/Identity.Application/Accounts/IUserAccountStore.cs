using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Accounts;

/// <summary>
/// Account store abstraction. Deliberately narrow — not a generic repository wrapper (skill §33).
/// </summary>
public interface IUserAccountStore
{
    Task<AppUser?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>Loads the user with <c>Role</c> and <c>VolunteerProfile</c>.</summary>
    Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Loads the user with profile, skills and availability (for the profile API).</summary>
    Task<AppUser?> GetDetailsByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<Role?> GetRoleByCodeAsync(string roleCode, CancellationToken cancellationToken = default);

    /// <summary>Loads the given users with role, profile and skills; unknown ids are absent (F-IDN-09).</summary>
    Task<IReadOnlyList<AppUser>> GetDetailsByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>Filtered, sorted, paged read of users with role, profile and skills (F-IDN-08, F-IDN-10).</summary>
    Task<PagedResult<AppUser>> SearchAsync(UserSearchCriteria criteria, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken = default);

    void Add(AppUser user);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
