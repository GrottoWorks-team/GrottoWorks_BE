using Identity.Domain.Enums;

namespace Identity.Application.Skills;

public sealed record SkillDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    SkillStatus Status)
{
    public static SkillDto From(Domain.Entities.Skill skill)
    {
        return new SkillDto(
            skill.SkillId,
            skill.Code,
            skill.Name,
            skill.Description,
            skill.Status);
    }
}
