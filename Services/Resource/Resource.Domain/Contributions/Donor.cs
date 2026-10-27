namespace Resource.Domain.Contributions;

/// <summary>
/// <c>donor</c> (ERD v3.6.2, F-RES-12): a person or group who gives money or materials. External donors have no
/// account; a volunteer is linked by <see cref="UserId"/> (unique). Donors are never merged automatically by name,
/// phone or email. <see cref="ParishId"/> is not in the logical ERD — it keeps donor contact details inside the
/// parish that recorded them (tenant scope, spec §2.1).
/// </summary>
public sealed class Donor
{
    public const int MaxNameLength = 150;
    public const int MaxPhoneLength = 30;
    public const int MaxEmailLength = 255;
    public const int MaxAddressLength = 255;
    public const int MaxNoteLength = 2000;

    private Donor()
    {
    }

    public Guid DonorId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid? UserId { get; private set; }

    public string DonorName { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public string? Note { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.</summary>
    public uint Version { get; private set; }

    public static Donor Create(
        Guid parishId,
        Guid? userId,
        string? donorName,
        string? phone,
        string? email,
        string? address,
        string? note,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        if (parishId == Guid.Empty)
        {
            throw DomainRuleException.Validation("DONOR_PARISH_REQUIRED", "A donor belongs to a parish.");
        }

        return new Donor
        {
            DonorId = Guid.NewGuid(),
            ParishId = parishId,
            UserId = userId,
            DonorName = Name(donorName),
            Phone = Optional(phone, MaxPhoneLength, "DONOR_PHONE_INVALID", "Phone"),
            Email = CleanEmail(email),
            Address = Optional(address, MaxAddressLength, "DONOR_ADDRESS_INVALID", "Address"),
            Note = Optional(note, MaxNoteLength, "DONOR_NOTE_INVALID", "Note"),
            CreatedByUserId = createdByUserId,
            CreatedAt = now.ToUniversalTime()
        };
    }

    public void EnsureVersion(long? expected)
    {
        if (expected is { } value && value != Version)
        {
            throw new DomainRuleException(
                "DONOR_VERSION_MISMATCH",
                "The donor was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }

    /// <summary>PATCH semantics: <c>null</c> = unchanged; an empty optional field clears it; the name cannot be cleared.</summary>
    public void Update(string? donorName, string? phone, string? email, string? address, string? note, DateTimeOffset now)
    {
        if (donorName is not null)
        {
            DonorName = Name(donorName);
        }

        if (phone is not null)
        {
            Phone = Optional(phone, MaxPhoneLength, "DONOR_PHONE_INVALID", "Phone");
        }

        if (email is not null)
        {
            Email = CleanEmail(email);
        }

        if (address is not null)
        {
            Address = Optional(address, MaxAddressLength, "DONOR_ADDRESS_INVALID", "Address");
        }

        if (note is not null)
        {
            Note = Optional(note, MaxNoteLength, "DONOR_NOTE_INVALID", "Note");
        }

        UpdatedAt = now.ToUniversalTime();
    }

    private static string Name(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            throw DomainRuleException.Validation("DONOR_NAME_INVALID", $"Donor name is required and at most {MaxNameLength} characters.");
        }

        return name;
    }

    private static string? CleanEmail(string? value)
    {
        var email = Optional(value, MaxEmailLength, "DONOR_EMAIL_INVALID", "Email");
        if (email is not null && (email.IndexOf('@') is <= 0 || email.EndsWith('@') || email.Contains(' ')))
        {
            throw DomainRuleException.Validation("DONOR_EMAIL_INVALID", "Email is not a valid address.");
        }

        return email?.ToLowerInvariant();
    }

    private static string? Optional(string? value, int maxLength, string code, string field)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (cleaned is { Length: var length } && length > maxLength)
        {
            throw DomainRuleException.Validation(code, $"{field} must not exceed {maxLength} characters.");
        }

        return cleaned;
    }
}
