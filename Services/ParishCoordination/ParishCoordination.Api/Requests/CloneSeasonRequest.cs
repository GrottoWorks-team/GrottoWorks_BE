using System.ComponentModel.DataAnnotations;
using ParishCoordination.Application.Seasons.Commands.CloneSeason;

namespace ParishCoordination.Api.Requests;

public sealed class CloneSeasonAreaRequest
{
    public Guid SourceWorkAreaId { get; init; }

    [StringLength(150, ErrorMessage = "Area name must not exceed 150 characters.")]
    public string? Name { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? CommunityId { get; init; }
}

public sealed class CloneSeasonRequest : IValidatableObject
{
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

    /// <summary><c>NONE</c> (leave dates empty) or <c>SAME_DAY_OF_SEASON</c> (keep the offset).</summary>
    public string? DateShift { get; init; }

    public bool? CopyAssignments { get; init; }

    /// <summary><c>null</c> or empty copies every area of the source season.</summary>
    public IReadOnlyList<CloneSeasonAreaRequest>? Areas { get; init; }

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

        if (!string.IsNullOrWhiteSpace(DateShift) &&
            !string.Equals(DateShift, "NONE", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(DateShift, "SAME_DAY_OF_SEASON", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "Date shift must be NONE or SAME_DAY_OF_SEASON.",
                [nameof(DateShift)]);
        }

        if (Areas is not null)
        {
            for (var index = 0; index < Areas.Count; index++)
            {
                if (Areas[index] is null || Areas[index].SourceWorkAreaId == Guid.Empty)
                {
                    yield return new ValidationResult(
                        "Each area needs a sourceWorkAreaId.",
                        [nameof(Areas)]);
                    break;
                }
            }
        }
    }

    public CloneDateShift ParseDateShift() =>
        string.Equals(DateShift, "SAME_DAY_OF_SEASON", StringComparison.OrdinalIgnoreCase)
            ? CloneDateShift.SameDayOfSeason
            : CloneDateShift.None;
}
