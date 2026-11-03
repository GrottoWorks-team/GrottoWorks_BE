namespace TaskExecution.Domain;

public static class SkillLevels
{
    public static string? Normalize(string? value, string errorCode = "SKILL_LEVEL_INVALID")
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var level = value.Trim().ToUpperInvariant();
        if (level is not ("BASIC" or "INTERMEDIATE" or "ADVANCED" or "EXPERT"))
            throw DomainRuleException.Validation(errorCode, "Skill level must be BASIC, INTERMEDIATE, ADVANCED or EXPERT.");
        return level;
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
