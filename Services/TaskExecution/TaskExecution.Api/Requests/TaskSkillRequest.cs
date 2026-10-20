using System.ComponentModel.DataAnnotations;

namespace TaskExecution.Api.Requests;

public sealed class TaskSkillRequest : IValidatableObject
{
    public Guid SkillId { get; init; }

    [StringLength(30)]
    public string? RequiredSkillLevel { get; init; }

    [Range(1, int.MaxValue)]
    public int? RequiredVolunteerCount { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SkillId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Skill ID must not be empty.",
                [nameof(SkillId)]);
        }
    }
}
