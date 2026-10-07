using System.ComponentModel.DataAnnotations;
using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Api.Requests;

public sealed class UpdateParishRequest : IValidatableObject
{
    [StringLength(150, ErrorMessage = "Name must not exceed 150 characters.")]
    public string? Name { get; init; }

    [StringLength(255, ErrorMessage = "Address must not exceed 255 characters.")]
    public string? Address { get; init; }

    public string? Description { get; init; }

    public ParishStatus? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is null && Address is null && Description is null && Status is null)
        {
            yield return new ValidationResult(
                "At least one field must be provided.",
                [nameof(Name), nameof(Address), nameof(Description), nameof(Status)]);
        }

        if (Name is not null && string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Name must not be empty.",
                [nameof(Name)]);
        }
    }
}
