namespace Identity.Domain.Entities;

/// <summary>
/// Structured availability slot used by volunteer matching (BR-15, BR-70).
/// </summary>
public sealed class VolunteerAvailability
{
    private VolunteerAvailability()
    {
    }

    private VolunteerAvailability(
        Guid volunteerAvailabilityId,
        Guid volunteerUserId,
        DateTimeOffset availableFrom,
        DateTimeOffset availableTo,
        string? availabilityNote)
    {
        VolunteerAvailabilityId = volunteerAvailabilityId;
        VolunteerUserId = volunteerUserId;
        AvailableFrom = availableFrom;
        AvailableTo = availableTo;
        AvailabilityNote = availabilityNote;
    }

    public static VolunteerAvailability Create(
        Guid volunteerUserId,
        DateTimeOffset availableFrom,
        DateTimeOffset availableTo,
        string? availabilityNote = null)
    {
        if (volunteerUserId == Guid.Empty)
        {
            throw new DomainException("AVAILABILITY_USER_REQUIRED", "An availability slot must belong to a volunteer.");
        }

        // BR-70 / DB check volunteer_availability_valid_range.
        if (availableTo <= availableFrom)
        {
            throw new DomainException(
                "AVAILABILITY_INVALID_RANGE",
                "Available to must be after available from.");
        }

        return new VolunteerAvailability(
            Guid.NewGuid(),
            volunteerUserId,
            availableFrom,
            availableTo,
            string.IsNullOrWhiteSpace(availabilityNote) ? null : availabilityNote.Trim());
    }

    public Guid VolunteerAvailabilityId { get; private set; }

    public Guid VolunteerUserId { get; private set; }

    public DateTimeOffset AvailableFrom { get; private set; }

    public DateTimeOffset AvailableTo { get; private set; }

    public string? AvailabilityNote { get; private set; }

    public void Move(DateTimeOffset availableFrom, DateTimeOffset availableTo, string? availabilityNote)
    {
        if (availableTo <= availableFrom)
        {
            throw new DomainException(
                "AVAILABILITY_INVALID_RANGE",
                "Available to must be after available from.");
        }

        AvailableFrom = availableFrom;
        AvailableTo = availableTo;

        if (availabilityNote is not null)
        {
            AvailabilityNote = string.IsNullOrWhiteSpace(availabilityNote) ? null : availabilityNote.Trim();
        }
    }
}
