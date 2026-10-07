using Identity.Domain;

namespace Identity.Application.Skills.CreateSkill;

public sealed record CreateSkillCommand(string Code, string Name, string? Description);

/// <summary>F-IDN-06: add a skill (ADMIN only). The code is unique and normalized to upper case.</summary>
public sealed class CreateSkillCommandHandler(ISkillStore skillStore)
{
    public async Task<SkillDto> HandleAsync(
        CreateSkillCommand command,
        CancellationToken cancellationToken = default)
    {
        var code = Domain.Entities.Skill.NormalizeCode(command.Code);

        if (await skillStore.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(
                "SKILL_CODE_ALREADY_EXISTS",
                "A skill with this code already exists.");
        }

        var skill = Domain.Entities.Skill.Create(code, command.Name, command.Description);

        skillStore.Add(skill);
        await skillStore.SaveChangesAsync(cancellationToken);

        return SkillDto.From(skill);
    }
}
