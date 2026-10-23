using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class AssignWorkAreaCommunityRequest : IValidatableObject
{
    private Guid? communityId;
    private bool communityIdWasProvided;

    public Guid? CommunityId
    {
        get => communityId;
        init
        {
            communityId = value;
            communityIdWasProvided = true;
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!communityIdWasProvided)
        {
            yield return new ValidationResult(
                "The communityId property is required.",
                [nameof(CommunityId)]);
        }

        if (CommunityId.HasValue && CommunityId.Value == Guid.Empty)
        {
            yield return new ValidationResult(
                "Community ID must be a non-empty UUID when provided.",
                [nameof(CommunityId)]);
        }
    }
}
