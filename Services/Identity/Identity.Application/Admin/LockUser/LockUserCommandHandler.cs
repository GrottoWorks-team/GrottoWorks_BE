using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Application.Auth;
using Identity.Contracts.Events;
using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Admin.LockUser;

public sealed record LockUserCommand(Guid UserId, long? Version, string Reason);

/// <summary>
/// F-IDN-08 <c>lockUser</c> (ADMIN): LOCKED accounts cannot sign in or refresh; every refresh token is
/// revoked (API Contract 11) and <c>identity.user.locked</c> is published so Task stops assigning the
/// user (edge case #24). Issued access tokens stay valid until expiry (≤ 15 min) until the Redis
/// denylist lands. The reason travels in the event for the activity log; Identity does not store it.
/// </summary>
public sealed class LockUserCommandHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore,
    IRefreshTokenStore refreshTokenStore,
    IIntegrationEventPublisher eventPublisher)
{
    public async Task<AdminUserDto> HandleAsync(
        LockUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var reason = ReasonText.Require(command.Reason);

        AdminGuards.EnsureNotSelf(
            currentUser,
            command.UserId,
            "USER_CANNOT_LOCK_SELF",
            "You cannot lock your own account.");

        var user = await AdminGuards.LoadUserAsync(userAccountStore, command.UserId, cancellationToken);
        user.EnsureVersion(command.Version);

        var now = DateTimeOffset.UtcNow;
        user.Lock(now);

        await refreshTokenStore.RevokeAllForUserAsync(user.UserId, RefreshToken.ReasonUserBlocked, now, cancellationToken);
        await userAccountStore.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(
            IdentityUserLocked.EventType,
            IdentityUserLocked.Version,
            new IdentityUserLocked(user.UserId, user.ParishId, reason, currentUser.UserId, now),
            "user",
            user.UserId,
            cancellationToken);

        return AdminUserDto.From(user);
    }
}
