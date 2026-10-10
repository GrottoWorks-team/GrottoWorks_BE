using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class CreateSeasonRequest : IValidatableObject
{
    public Guid? ParishId { get; init; }

    [Required(ErrorMessage = "Name is required.")]
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
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult("Name is required.", [nameof(Name)]);
        }

        if (!SeasonYear.HasValue)
        {
            yield return new ValidationResult("Season year is required.", [nameof(SeasonYear)]);
        }

        if (!StartDate.HasValue)
        {
            yield return new ValidationResult("Start date is required.", [nameof(StartDate)]);
        }

        if (!EndDate.HasValue)
        {
            yield return new ValidationResult("End date is required.", [nameof(EndDate)]);
        }

        if (StartDate.HasValue && EndDate.HasValue && EndDate.Value <= StartDate.Value)
        {
            yield return new ValidationResult(
                "End date must be after start date.",
                [nameof(EndDate)]);
        }
    }
}