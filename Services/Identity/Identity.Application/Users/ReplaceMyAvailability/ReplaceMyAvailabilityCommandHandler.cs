using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Users.ReplaceMyAvailability;

public sealed record ReplaceMyAvailabilityCommand(IReadOnlyList<AvailabilitySlot> Slots);

/// <summary>
/// F-IDN-07 <c>replaceMyAvailability</c> (VOLUNTEER): replaces every structured availability slot.
/// BR-70: <c>availableTo &gt; availableFrom</c>; slots must not overlap. An empty list clears availability.
/// </summary>
public sealed class ReplaceMyAvailabilityCommandHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore)
{
    public async Task<UserProfileDto> HandleAsync(
        ReplaceMyAvailabilityCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await userAccountStore.GetDetailsByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new DomainException("USER_NOT_FOUND", "The user no longer exists.");

        user.ReplaceAvailability(command.Slots, DateTimeOffset.UtcNow);

        await userAccountStore.SaveChangesAsync(cancellationToken);

        return UserProfileDto.From(user);
    }
}
