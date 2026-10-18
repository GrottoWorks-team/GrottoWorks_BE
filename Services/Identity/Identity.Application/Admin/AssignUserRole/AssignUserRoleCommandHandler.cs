using Identity.Application.Abstractions;
using Identity.Application.Accounts;

namespace Identity.Application.Admin.AssignUserRole;

/// <summary>
/// <c>RoleAssignmentRequest</c>: the single role of the account and, when it needs one, the parish
/// (BR-67 — e.g. promoting a parish-less ADMIN to LEADER must name the parish in the same call).
/// </summary>
public sealed record AssignUserRoleCommand(Guid UserId, long? Version, string Role, Guid? ParishId);

/// <summary>
/// F-IDN-08 <c>assignUserRole</c> (ADMIN): one role per account, replaced atomically together with the
/// parish. An administrator cannot change their own role, which also guarantees at least one ADMIN
/// remains. The new role reaches the JWT on the next login/refresh (access tokens live ≤ 15 min).
/// </summary>
public sealed class AssignUserRoleCommandHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore,
    IParishDirectory parishDirectory)
{
    public async Task<AdminUserDto> HandleAsync(
        AssignUserRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        AdminGuards.EnsureNotSelf(
            currentUser,
            command.UserId,
            "USER_CANNOT_CHANGE_OWN_ROLE",
            "You cannot change the role of your own account.");

        var user = await AdminGuards.LoadUserAsync(userAccountStore, command.UserId, cancellationToken);
        user.EnsureVersion(command.Version);

        var role = await AdminGuards.LoadRoleAsync(userAccountStore, command.Role, cancellationToken);

        if (command.ParishId is { } parishId && parishId != user.ParishId)
        {
            await AdminGuards.EnsureParishExistsAsync(parishDirectory, parishId, cancellationToken);

            if (user.VolunteerProfile is { } profile &&
                !await parishDirectory.CommunityBelongsToParishAsync(profile.CommunityId, parishId, cancellationToken))
            {
                throw new Domain.DomainException(
                    "USER_COMMUNITY_OUTSIDE_PARISH",
                    "The user's community does not belong to the new parish. Update the community first.");
            }
        }

        user.AssignRole(role, command.ParishId, DateTimeOffset.UtcNow);

        await userAccountStore.SaveChangesAsync(cancellationToken);

        return AdminUserDto.From(user);
    }
}
