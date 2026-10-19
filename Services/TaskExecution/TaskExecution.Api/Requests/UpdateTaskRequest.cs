using System.ComponentModel.DataAnnotations;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Api.Requests;

public sealed class UpdateTaskRequest : IValidatableObject
{
    [StringLength(160)]
    public string? Name { get; init; }

    [StringLength(2000)]
    public string? Description { get; init; }

    [StringLength(4000)]
    public string? CompletionCriteria { get; init; }

    public TaskPriority? Priority { get; init; }

    [Range(1, 599999)]
    public int? EstimatedMinutes { get; init; }

    [Range(1, int.MaxValue)]
    public int? RequiredVolunteers { get; init; }

    public DateTimeOffset? StartAt { get; init; }
    public DateTimeOffset? Deadline { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is null && Description is null && CompletionCriteria is null &&
            Priority is null && EstimatedMinutes is null && RequiredVolunteers is null &&
            StartAt is null && Deadline is null)
        {
            yield return new ValidationResult("At least one planning field must be provided.");
        }

        if (Name is not null && string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult("Name must not be empty.", [nameof(Name)]);
        }
    }
}
