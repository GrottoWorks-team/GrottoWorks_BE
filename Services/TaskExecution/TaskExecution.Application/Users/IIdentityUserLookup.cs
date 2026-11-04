using BuildingBlocks.Security;
using TaskExecution.Domain;

namespace TaskExecution.Application.Users;

public sealed record IdentityUser(Guid UserId, string DisplayName, string Status, string Role,
    Guid? ParishId, Guid? CommunityId, IReadOnlyList<Guid> SkillIds, IReadOnlyList<IdentitySkill>? Skills = null,
    IReadOnlyList<IdentityAvailability>? Availability = null);

public sealed record IdentitySkill(Guid SkillId, string? SkillLevel);
public sealed record IdentityAvailability(DateTimeOffset AvailableFrom, DateTimeOffset AvailableTo);

public static class VolunteerSkills
{
    public static bool Meets(IdentityUser volunteer, Guid skillId, string? minimumLevel)
    {
        if (!volunteer.SkillIds.Contains(skillId)) return false;
        if (minimumLevel is null) return true;
        var declaration = volunteer.Skills?.SingleOrDefault(skill => skill.SkillId == skillId);
        return SkillLevels.Meets(declaration?.SkillLevel, minimumLevel);
    }
}

public interface IIdentityUserLookup
{
    Task<IdentityUser?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IIdentityUserBatchLookup
{
    Task<IReadOnlyDictionary<Guid,IdentityUser>> FindManyAsync(IReadOnlyCollection<Guid> userIds,CancellationToken cancellationToken=default);
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
