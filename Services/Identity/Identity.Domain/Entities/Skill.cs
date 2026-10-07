using Identity.Domain.Enums;

namespace Identity.Domain.Entities;

/// <summary>
/// Skill catalog entry (<c>skill</c>, ERD v3.5). Referenced skills are deactivated, never deleted (F-IDN-06).
/// </summary>
public sealed class Skill
{
    private Skill()
    {
    }

    private Skill(Guid skillId, string code, string name, string? description, SkillStatus status)
    {
        SkillId = skillId;
        Code = code;
        Name = name;
        Description = description;
        Status = status;
    }

    public static Skill Create(string code, string name, string? description = null)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedName = (name ?? string.Empty).Trim();

        if (normalizedName.Length is 0 or > 100)
        {
            throw new DomainException("SKILL_NAME_INVALID", "Skill name is required and must not exceed 100 characters.");
        }

        return new Skill(
            Guid.NewGuid(),
            normalizedCode,
            normalizedName,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SkillStatus.Active);
    }

    public static string NormalizeCode(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > 50)
        {
            throw new DomainException("SKILL_CODE_INVALID", "Skill code is required and must not exceed 50 characters.");
        }

        return normalized;
    }

    public Guid SkillId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public SkillStatus Status { get; private set; }

    public void Update(string? name, string? description, SkillStatus? status)
    {
        if (name is not null)
        {
            var trimmed = name.Trim();
            if (trimmed.Length is 0 or > 100)
            {
                throw new DomainException(
                    "SKILL_NAME_INVALID",
                    "Skill name is required and must not exceed 100 characters.");
            }

            Name = trimmed;
        }

        if (description is not null)
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }

        if (status is not null)
        {
            Status = status.Value;
        }
    }

    public void Deactivate() => Status = SkillStatus.Inactive;
}
