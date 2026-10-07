using Identity.Domain;
using Identity.Domain.Enums;

namespace Identity.Application.Skills.UpdateSkill;

/// <summary>Nullable fields mean "unchanged". Passing <see cref="Status"/> = INACTIVE deactivates the skill.</summary>
public sealed record UpdateSkillCommand(
    Guid SkillId,
    string? Name,
    string? Description,
    SkillStatus? Status);

/// <summary>
/// F-IDN-06: update a skill. Skills referenced by volunteers are deactivated, never deleted —
/// there is no delete endpoint for the catalog.
/// </summary>
public sealed class UpdateSkillCommandHandler(ISkillStore skillStore)
{
    public async Task<SkillDto> HandleAsync(
        UpdateSkillCommand command,
        CancellationToken cancellationToken = default)
    {
        var skill = await skillStore.GetByIdAsync(command.SkillId, cancellationToken)
            ?? throw new DomainException("SKILL_NOT_FOUND", "The skill does not exist.");

        skill.Update(command.Name, command.Description, command.Status);

        await skillStore.SaveChangesAsync(cancellationToken);

        return SkillDto.From(skill);
    }
}
