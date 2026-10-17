using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class UpdateWorkAreaRequest : IValidatableObject
{
    [StringLength(150, ErrorMessage = "Name must not exceed 150 characters.")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? Deadline { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is null &&
            Description is null &&
            CategoryId is null &&
            StartDate is null &&
            Deadline is null)
        {
            yield return new ValidationResult(
                "At least one field must be provided.",
                [
                    nameof(Name),
                    nameof(Description),
                    nameof(CategoryId),
                    nameof(StartDate),
                    nameof(Deadline)
                ]);
        }

        if (Name is not null && string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Name must not be empty.",
                [nameof(Name)]);
        }

        if (StartDate.HasValue &&
            Deadline.HasValue &&
            Deadline.Value < StartDate.Value)
        {
            yield return new ValidationResult(
                "Deadline must not be before start date.",
                [nameof(Deadline)]);
        }
    }
}
