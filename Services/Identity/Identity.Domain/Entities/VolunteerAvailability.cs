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
        AvailableFrom = availableFrom.ToUniversalTime();
        AvailableTo = availableTo.ToUniversalTime();
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

        var note = string.IsNullOrWhiteSpace(availabilityNote) ? null : availabilityNote.Trim();
        if (note is { Length: > MaxNoteLength })
        {
            throw new DomainException(
                "AVAILABILITY_NOTE_TOO_LONG",
                $"Availability note must not exceed {MaxNoteLength} characters.");
        }

        // timestamptz columns only accept UTC offsets through Npgsql; all server times are UTC (spec 2.1).
        return new VolunteerAvailability(
            Guid.NewGuid(),
            volunteerUserId,
            availableFrom.ToUniversalTime(),
            availableTo.ToUniversalTime(),
            note);
    }

    public const int MaxNoteLength = 500;

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

        AvailableFrom = availableFrom.ToUniversalTime();
        AvailableTo = availableTo.ToUniversalTime();

        if (availabilityNote is not null)
        {
            AvailabilityNote = string.IsNullOrWhiteSpace(availabilityNote) ? null : availabilityNote.Trim();
        }
    }
}
