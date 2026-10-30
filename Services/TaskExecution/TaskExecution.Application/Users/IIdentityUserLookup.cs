using BuildingBlocks.Security;
using TaskExecution.Domain;

namespace TaskExecution.Application.Users;

public sealed record IdentityUser(Guid UserId, string DisplayName, string Status, string Role,
    Guid? ParishId, Guid? CommunityId, IReadOnlyList<Guid> SkillIds);

public interface IIdentityUserLookup
{
    Task<IdentityUser?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class ActiveUserAccess(IIdentityUserLookup users, ICurrentUser currentUser)
{
    public async Task<IdentityUser> EnsureActiveAsync(Guid userId, Guid parishId, string role,
        CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(userId, cancellationToken)
            ?? throw DomainRuleException.NotFound("USER_NOT_FOUND", "The user does not exist.");
        if (user.Status != "ACTIVE") throw new DomainRuleException("USER_NOT_ACTIVE", "The user is not active.");
        if (user.ParishId != parishId) throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED", "The user belongs to another parish.");
        if (user.Role != role) throw new DomainRuleException("USER_ROLE_MISMATCH", "The user has an incompatible role.");
        return user;
    }

    public Task<IdentityUser> EnsureCallerAsync(Guid parishId, CancellationToken cancellationToken = default) =>
        EnsureActiveAsync(currentUser.UserId, parishId, currentUser.Role, cancellationToken);
}
