using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Admin;

/// <summary>Shared lookups and checks of the ADMIN user-management handlers (F-IDN-08).</summary>
internal static class AdminGuards
{
    public static async Task<AppUser> LoadUserAsync(
        IUserAccountStore store,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await store.GetDetailsByIdAsync(userId, cancellationToken)
            ?? throw new DomainException("USER_NOT_FOUND", "The user does not exist.");
    }

    public static async Task<Role> LoadRoleAsync(
        IUserAccountStore store,
        string? roleCode,
        CancellationToken cancellationToken)
    {
        var normalized = (roleCode ?? string.Empty).Trim().ToUpperInvariant();

        if (!RoleCodes.IsValid(normalized))
        {
            throw new DomainException(
                "ROLE_UNKNOWN",
                $"Role must be one of: {string.Join(", ", RoleCodes.All)}.");
        }

        return await store.GetRoleByCodeAsync(normalized, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Role {normalized} is missing. Run the Identity data seeder (F-IDN-01).");
    }

    public static async Task EnsureParishExistsAsync(
        IParishDirectory parishDirectory,
        Guid? parishId,
        CancellationToken cancellationToken)
    {
        if (parishId is not { } id || id == Guid.Empty)
        {
            return; // BR-67 (parish required unless ADMIN) is enforced by the domain.
        }

        if (!await parishDirectory.ParishExistsAsync(id, cancellationToken))
        {
            throw new DomainException("USER_PARISH_UNKNOWN", "The parish does not exist.");
        }
    }

    /// <summary>An administrator cannot lock, deactivate or demote the account they are signed in with.</summary>
    public static void EnsureNotSelf(ICurrentUser currentUser, Guid targetUserId, string code, string message)
    {
        if (currentUser.UserId == targetUserId)
        {
            throw new DomainException(code, message);
        }
    }
}
