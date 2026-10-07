using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Skills.ListSkills;

public sealed record ListSkillsQuery(Domain.Enums.SkillStatus? Status = null);

/// <summary>F-IDN-06: list the skill catalog for authenticated users.</summary>
public sealed class ListSkillsQueryHandler(ISkillStore skillStore)
{
    public async Task<IReadOnlyList<SkillDto>> HandleAsync(
        ListSkillsQuery query,
        CancellationToken cancellationToken = default)
    {
        var skills = await skillStore.GetAllAsync(cancellationToken);

        return skills
            .Where(skill => query.Status is null || skill.Status == query.Status)
            .OrderBy(skill => skill.Name)
            .ThenBy(skill => skill.SkillId)
            .Select(SkillDto.From)
            .ToList();
    }
}
