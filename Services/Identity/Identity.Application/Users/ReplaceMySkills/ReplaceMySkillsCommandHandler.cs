using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Application.Skills;
using Identity.Domain;
using Identity.Domain.Enums;

namespace Identity.Application.Users.ReplaceMySkills;

public sealed record ReplaceMySkillsCommand(IReadOnlyList<Guid> SkillIds);

/// <summary>
/// F-IDN-07 <c>replaceMySkills</c> (VOLUNTEER): the caller's declared skills become exactly the given set.
/// Every id must exist in the catalog; a newly declared skill must be ACTIVE, while a skill the volunteer
/// already had stays allowed after it was deactivated (deactivation must not silently rewrite profiles).
/// </summary>
public sealed class ReplaceMySkillsCommandHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore,
    ISkillStore skillStore)
{
    public async Task<UserProfileDto> HandleAsync(
        ReplaceMySkillsCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await userAccountStore.GetDetailsByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new DomainException("USER_NOT_FOUND", "The user no longer exists.");

        var requested = command.SkillIds.Distinct().ToList();
        var skills = await skillStore.GetByIdsAsync(requested, cancellationToken);
        var knownIds = skills.Select(skill => skill.SkillId).ToHashSet();

        if (requested.Any(skillId => !knownIds.Contains(skillId)))
        {
            throw new DomainException("VOLUNTEER_SKILL_UNKNOWN", "One or more skills do not exist.");
        }

        var alreadyDeclared = user.VolunteerSkills.Select(link => link.SkillId).ToHashSet();
        if (skills.Any(skill => skill.Status != SkillStatus.Active && !alreadyDeclared.Contains(skill.SkillId)))
        {
            throw new DomainException(
                "VOLUNTEER_SKILL_INACTIVE",
                "An inactive skill cannot be newly declared.");
        }

        // Duplicates in the request are reported by the domain rather than silently merged.
        user.ReplaceSkills(command.SkillIds, DateTimeOffset.UtcNow);

        await userAccountStore.SaveChangesAsync(cancellationToken);

        return UserProfileDto.From(user);
    }
}
