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

    private decimal? latitude;
    private decimal? longitude;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLatitude { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLongitude { get; private set; }

    [Range(typeof(decimal), "-90", "90")]
    public decimal? Latitude
    {
        get => latitude;
        init { latitude = value; HasLatitude = true; }
    }

    [Range(typeof(decimal), "-180", "180")]
    public decimal? Longitude
    {
        get => longitude;
        init { longitude = value; HasLongitude = true; }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is null &&
            Description is null &&
            CategoryId is null &&
            StartDate is null &&
            Deadline is null && !HasLatitude && !HasLongitude)
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
