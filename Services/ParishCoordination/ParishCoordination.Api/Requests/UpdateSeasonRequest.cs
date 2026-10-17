using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class UpdateSeasonRequest : IValidatableObject
{
    [StringLength(150, ErrorMessage = "Name must not exceed 150 characters.")]
    public string? Name { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Season year must be a positive integer.")]
    public int? SeasonYear { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public string? Description { get; init; }

    [Range(typeof(decimal), "0", "999999999999.99", ErrorMessage = "Estimated budget must not be negative.")]
    public decimal? EstimatedBudget { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is null &&
            SeasonYear is null &&
            StartDate is null &&
            EndDate is null &&
            Description is null &&
            EstimatedBudget is null)
        {
            yield return new ValidationResult(
                "At least one field must be provided.",
                [
                    nameof(Name),
                    nameof(SeasonYear),
                    nameof(StartDate),
                    nameof(EndDate),
                    nameof(Description),
                    nameof(EstimatedBudget)
                ]);
        }

        if (Name is not null && string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Name must not be empty.",
                [nameof(Name)]);
        }

        if (StartDate.HasValue &&
            EndDate.HasValue &&
            EndDate.Value <= StartDate.Value)
        {
            yield return new ValidationResult(
                "End date must be after start date.",
                [nameof(EndDate)]);
        }
    }
}
