using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class AssignWorkAreaCommunityRequest : IValidatableObject
{
    public Guid? CommunityId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!CommunityId.HasValue || CommunityId.Value == Guid.Empty)
        {
            yield return new ValidationResult(
                "Community ID is required and must be a non-empty UUID.",
                [nameof(CommunityId)]);
        }
    }
}
