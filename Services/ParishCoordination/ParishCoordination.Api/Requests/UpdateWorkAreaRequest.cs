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
    private decimal? mapX;
    private decimal? mapY;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLatitude { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLongitude { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasMapX { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasMapY { get; private set; }

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

    [Range(typeof(decimal), "0", "1")]
    public decimal? MapX
    {
        get => mapX;
        init { mapX = value; HasMapX = true; }
    }

    [Range(typeof(decimal), "0", "1")]
    public decimal? MapY
    {
        get => mapY;
        init { mapY = value; HasMapY = true; }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HasMapX != HasMapY || MapX.HasValue != MapY.HasValue)
        {
            yield return new ValidationResult(
                "Send both map coordinates as numbers to set a pin, or both as null to remove it.",
                [nameof(MapX), nameof(MapY)]);
        }

        if (Name is null &&
            Description is null &&
            CategoryId is null &&
            StartDate is null &&
            Deadline is null && !HasLatitude && !HasLongitude && !HasMapX && !HasMapY)
        {
            yield return new ValidationResult(
                "At least one field must be provided.",
                [
                    nameof(Name),
                    nameof(Description),
                    nameof(CategoryId),
                    nameof(StartDate),
                    nameof(Deadline),
                    nameof(MapX),
                    nameof(MapY)
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
