using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Application.Auth;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Admin.UpdateUser;

/// <summary>
/// PATCH semantics: <c>null</c> means unchanged. <see cref="Status"/> accepts ACTIVE/INACTIVE only —
/// LOCKED goes through <c>lockUser</c>/<c>unlockUser</c> so the reason and event are never skipped.
/// The role is changed through <c>assignUserRole</c>.
/// </summary>
public sealed record UpdateUserCommand(
    Guid UserId,
    long? Version,
    string? Email,
    string? DisplayName,
    string? Phone,
    Guid? ParishId,
    UserStatus? Status);

/// <summary>F-IDN-08 <c>updateUser</c> (ADMIN).</summary>
public sealed class UpdateUserCommandHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore,
    IRefreshTokenStore refreshTokenStore,
    IParishDirectory parishDirectory)
{
    public async Task<AdminUserDto> HandleAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await AdminGuards.LoadUserAsync(userAccountStore, command.UserId, cancellationToken);
        user.EnsureVersion(command.Version);

        var now = DateTimeOffset.UtcNow;

        if (command.Email is not null)
        {
            var email = AppUser.NormalizeEmail(command.Email);
            if (!string.Equals(email, user.Email, StringComparison.Ordinal))
            {
                if (await userAccountStore.EmailExistsAsync(email, cancellationToken))
                {
                    throw new DomainException(
                        "AUTH_EMAIL_ALREADY_EXISTS",
                        "An account with this email already exists.");
                }

                user.ChangeEmail(email, now);
            }
        }

        if (command.DisplayName is not null || command.Phone is not null)
        {
            user.UpdateProfile(command.DisplayName, command.Phone, now);
        }

        if (command.ParishId is { } parishId && parishId != user.ParishId)
        {
            await ChangeParishAsync(user, parishId, now, cancellationToken);
        }

        var revokeSessions = false;
        switch (command.Status)
        {
            case null:
                break;
            case UserStatus.Locked:
                throw new DomainException(
                    "USER_STATUS_USE_LOCK",
                    "Use the lock endpoint to lock an account.");
            case UserStatus.Inactive when user.Status != UserStatus.Inactive:
                AdminGuards.EnsureNotSelf(
                    currentUser,
                    user.UserId,
                    "USER_CANNOT_DEACTIVATE_SELF",
                    "You cannot deactivate your own account.");
                user.Deactivate(now);
                revokeSessions = true;
                break;
            case UserStatus.Active when user.Status != UserStatus.Active:
                user.Activate(now);
                break;
        }

        if (revokeSessions)
        {
            await refreshTokenStore.RevokeAllForUserAsync(
                user.UserId,
                RefreshToken.ReasonUserBlocked,
                now,
                cancellationToken);
        }

        await userAccountStore.SaveChangesAsync(cancellationToken);

        return AdminUserDto.From(user);
    }

    private async Task ChangeParishAsync(
        AppUser user,
        Guid parishId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await AdminGuards.EnsureParishExistsAsync(parishDirectory, parishId, cancellationToken);

        // The volunteer profile keeps a community of the old parish; moving the account would put a
        // foreign community into its JWT communityId claim (Contract 1: no scope expansion).
        if (user.VolunteerProfile is { } profile &&
            !await parishDirectory.CommunityBelongsToParishAsync(profile.CommunityId, parishId, cancellationToken))
        {
            throw new DomainException(
                "USER_COMMUNITY_OUTSIDE_PARISH",
                "The user's community does not belong to the new parish. Update the community first.");
        }

        user.ChangeParish(parishId, now);
    }
}
