namespace Identity.Domain.Entities;

/// <summary>
/// One-to-one volunteer details attached to an account (<c>volunteer_profile</c>, ERD v3.5).
/// <c>community_id</c> is a cross-service reference to the Parish service — no FK across services.
/// </summary>
public sealed class VolunteerProfile
{
    private VolunteerProfile()
    {
    }

    private VolunteerProfile(Guid volunteerUserId, Guid communityId, string? introduction, string? availabilityNote)
    {
        VolunteerUserId = volunteerUserId;
        CommunityId = communityId;
        Introduction = introduction;
        AvailabilityNote = availabilityNote;
    }

    public static VolunteerProfile Create(
        Guid volunteerUserId,
        Guid communityId,
        string? introduction = null,
        string? availabilityNote = null)
    {
        if (volunteerUserId == Guid.Empty)
        {
            throw new DomainException("PROFILE_USER_REQUIRED", "A volunteer profile must belong to a user.");
        }

        if (communityId == Guid.Empty)
        {
            throw new DomainException(
                "PROFILE_COMMUNITY_REQUIRED",
                "A community is required to create a volunteer profile.");
        }

        return new VolunteerProfile(
            volunteerUserId,
            communityId,
            Normalize(introduction, 2000),
            Normalize(availabilityNote, 500));
    }

    public Guid VolunteerUserId { get; private set; }

    public Guid CommunityId { get; private set; }

    public string? Introduction { get; private set; }

    public string? AvailabilityNote { get; private set; }

    public void Update(Guid? communityId, string? introduction, string? availabilityNote)
    {
        if (communityId is { } resolvedCommunityId)
        {
            if (resolvedCommunityId == Guid.Empty)
            {
                throw new DomainException(
                    "PROFILE_COMMUNITY_REQUIRED",
                    "A community is required for a volunteer profile.");
            }

            CommunityId = resolvedCommunityId;
        }

        if (introduction is not null)
        {
            Introduction = Normalize(introduction, 2000);
        }

        if (availabilityNote is not null)
        {
            AvailabilityNote = Normalize(availabilityNote, 500);
        }
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException(
                "PROFILE_TEXT_TOO_LONG",
                $"Text must not exceed {maxLength} characters.");
        }

        return trimmed;
    }
}
