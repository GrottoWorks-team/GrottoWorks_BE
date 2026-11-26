namespace Identity.Domain.Entities;

public static class SkillLevels
{
    /// <summary>Mức mặc định khi khai báo kỹ năng mà không nêu cấp bậc (26/11/2026).</summary>
    public const string Default = "BASIC";

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized is not ("BASIC" or "INTERMEDIATE" or "ADVANCED" or "EXPERT"))
            throw new DomainException("VOLUNTEER_SKILL_LEVEL_INVALID", "Skill level must be BASIC, INTERMEDIATE, ADVANCED or EXPERT.");
        return normalized;
    }

    public static bool Meets(string? actual, string? required)
    {
        var expected = Normalize(required);
        if (expected is null) return true;
        return Rank(Normalize(actual)) >= Rank(expected);
    }

    private static int Rank(string? level) => level switch
    {
        "BASIC" => 1,
        "INTERMEDIATE" => 2,
        "ADVANCED" => 3,
        "EXPERT" => 4,
        _ => 0
    };
}

public sealed record VolunteerSkillInput(Guid SkillId, string? SkillLevel);
