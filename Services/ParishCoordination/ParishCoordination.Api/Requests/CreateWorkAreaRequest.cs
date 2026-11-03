using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class CreateWorkAreaRequest : IValidatableObject
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(150, ErrorMessage = "Name must not exceed 150 characters.")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? CommunityId { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? Deadline { get; init; }

    [Range(typeof(decimal), "-90", "90")]
    public decimal? Latitude { get; init; }

    [Range(typeof(decimal), "-180", "180")]
    public decimal? Longitude { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Name is required.",
                [nameof(Name)]);
        }
    }
}
