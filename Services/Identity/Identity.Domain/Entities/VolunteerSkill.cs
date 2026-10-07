namespace Identity.Domain.Entities;

/// <summary>
/// Declared skill of a volunteer. Unique on <c>(volunteer_user_id, skill_id)</c> per ERD v3.5.
/// </summary>
public sealed class VolunteerSkill
{
    private VolunteerSkill()
    {
    }

    private VolunteerSkill(
        Guid volunteerSkillId,
        Guid volunteerUserId,
        Guid skillId,
        string? skillLevel,
        string? skillNote)
    {
        VolunteerSkillId = volunteerSkillId;
        VolunteerUserId = volunteerUserId;
        SkillId = skillId;
        SkillLevel = skillLevel;
        SkillNote = skillNote;
    }

    public static VolunteerSkill Create(
        Guid volunteerUserId,
        Guid skillId,
        string? skillLevel = null,
        string? skillNote = null)
    {
        if (volunteerUserId == Guid.Empty || skillId == Guid.Empty)
        {
            throw new DomainException(
                "VOLUNTEER_SKILL_INVALID",
                "A volunteer skill requires both a volunteer and a skill.");
        }

        return new VolunteerSkill(
            Guid.NewGuid(),
            volunteerUserId,
            skillId,
            string.IsNullOrWhiteSpace(skillLevel) ? null : skillLevel.Trim(),
            string.IsNullOrWhiteSpace(skillNote) ? null : skillNote.Trim());
    }

    public Guid VolunteerSkillId { get; private set; }

    public Guid VolunteerUserId { get; private set; }

    public Guid SkillId { get; private set; }

    public Skill? Skill { get; private set; }

    public string? SkillLevel { get; private set; }

    public string? SkillNote { get; private set; }
}
