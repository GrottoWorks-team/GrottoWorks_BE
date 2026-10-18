using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Contracts.Events;

namespace Identity.Application.Admin.UnlockUser;

public sealed record UnlockUserCommand(Guid UserId, long? Version, string Reason);

/// <summary>F-IDN-08 <c>unlockUser</c> (ADMIN): LOCKED → ACTIVE and <c>identity.user.unlocked</c>.</summary>
public sealed class UnlockUserCommandHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore,
    IIntegrationEventPublisher eventPublisher)
{
    public async Task<AdminUserDto> HandleAsync(
        UnlockUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ReasonText.Require(command.Reason);

        var user = await AdminGuards.LoadUserAsync(userAccountStore, command.UserId, cancellationToken);
        user.EnsureVersion(command.Version);

        var now = DateTimeOffset.UtcNow;
        user.Unlock(now);

        await userAccountStore.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(
            IdentityUserUnlocked.EventType,
            IdentityUserUnlocked.Version,
            new IdentityUserUnlocked(user.UserId, user.ParishId, currentUser.UserId, now),
            "user",
            user.UserId,
            cancellationToken);

        return AdminUserDto.From(user);
    }
}
