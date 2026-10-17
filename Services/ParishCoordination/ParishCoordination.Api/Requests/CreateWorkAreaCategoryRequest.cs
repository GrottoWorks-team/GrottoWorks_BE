using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class CreateWorkAreaCategoryRequest : IValidatableObject
{
    [Required(ErrorMessage = "Code is required.")]
    [StringLength(50, ErrorMessage = "Code must not exceed 50 characters.")]
    public string? Code { get; init; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name must not exceed 100 characters.")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            yield return new ValidationResult(
                "Code is required.",
                [nameof(Code)]);
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Name is required.",
                [nameof(Name)]);
        }
    }
}
