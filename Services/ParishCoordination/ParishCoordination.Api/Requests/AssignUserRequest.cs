using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class AssignUserRequest : IValidatableObject
{
    public Guid? UserId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UserId.HasValue || UserId.Value == Guid.Empty)
        {
            yield return new ValidationResult(
                "User ID is required and must be a non-empty UUID.",
                [nameof(UserId)]);
        }
    }
}
