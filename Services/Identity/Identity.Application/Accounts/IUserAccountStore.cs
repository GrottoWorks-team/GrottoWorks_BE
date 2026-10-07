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

    void Add(AppUser user);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
