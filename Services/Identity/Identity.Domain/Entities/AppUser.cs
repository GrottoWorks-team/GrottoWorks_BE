using Identity.Domain.Enums;

namespace Identity.Domain.Entities;

/// <summary>
/// Aggregate root for an account. Owns the invariants of <c>app_user</c> (ERD v3.5):
/// normalized unique email, exactly one role, parish required for every role except ADMIN (BR-67).
/// </summary>
public sealed class AppUser
{
    /// <summary>API Contract: User.displayName is 1–120 characters (DB column allows 150).</summary>
    public const int MaxFullNameLength = 120;

    private AppUser()
    {
    }

    public static AppUser Create(
        string fullName,
        string email,
        string? phone,
        Guid? parishId,
        Role role,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(role);

        var trimmedFullName = (fullName ?? string.Empty).Trim();
        if (trimmedFullName.Length is 0 or > MaxFullNameLength)
        {
            throw new DomainException(
                "USER_FULL_NAME_INVALID",
                $"Full name is required and must not exceed {MaxFullNameLength} characters.");
        }

        var normalizedEmail = NormalizeEmail(email);

        EnsureParishRule(parishId, role.Code);

        return new AppUser
        {
            UserId = Guid.NewGuid(),
            RoleId = role.RoleId,
            Role = role,
            ParishId = parishId,
            FullName = trimmedFullName,
            Email = normalizedEmail,
            Phone = NormalizePhone(phone),
            PasswordHash = null,
            Status = UserStatus.Active,
            CreatedAt = createdAtUtc
        };
    }

    /// <summary>Lower-cased, trimmed email used for the unique index and lookups.</summary>
    public static string NormalizeEmail(string? email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length is 0 or > 255 ||
            normalized.Split('@') is not { Length: 2 } parts ||
            parts[0].Length == 0 ||
            parts[1].Length < 3 ||
            !parts[1].Contains('.'))
        {
            throw new DomainException(
                "AUTH_EMAIL_INVALID",
                "Enter a valid email address.");
        }

        return normalized;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Role? Role { get; private set; }

    /// <summary>Tenant scope. Stored as a cross-service reference only — no FK to the Parish service (spec 2.1).</summary>
    public Guid? ParishId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? PasswordHash { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public VolunteerProfile? VolunteerProfile { get; private set; }

    public List<VolunteerSkill> VolunteerSkills { get; private set; } = [];

    public List<VolunteerAvailability> VolunteerAvailabilities { get; private set; } = [];

    /// <summary>Only an ACTIVE account may authenticate (F-IDN-02).</summary>
    public bool CanAuthenticate => Status == UserStatus.Active && PasswordHash is not null;

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("AUTH_PASSWORD_HASH_REQUIRED", "Password hash is required.");
        }

        PasswordHash = passwordHash;
        UpdatedAt = now;
    }

    public void ChangeRole(Role role, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(role);

        EnsureParishRule(ParishId, role.Code);

        // One role per account (ERD v3.5).
        Role = role;
        RoleId = role.RoleId;
        UpdatedAt = now;
    }

    public void ChangeParish(Guid? parishId, DateTimeOffset now)
    {
        EnsureParishRule(parishId, Role?.Code);

        ParishId = parishId;
        UpdatedAt = now;
    }

    public void UpdateProfile(string? fullName, string? phone, DateTimeOffset now)
    {
        if (fullName is not null)
        {
            var trimmed = fullName.Trim();
            if (trimmed.Length is 0 or > MaxFullNameLength)
            {
                throw new DomainException(
                    "USER_FULL_NAME_INVALID",
                    $"Full name is required and must not exceed {MaxFullNameLength} characters.");
            }

            FullName = trimmed;
        }

        if (phone is not null)
        {
            Phone = NormalizePhone(phone);
        }

        UpdatedAt = now;
    }

    public void Lock(DateTimeOffset now)
    {
        Status = UserStatus.Locked;
        UpdatedAt = now;
    }

    public void Unlock(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        Status = UserStatus.Inactive;
        UpdatedAt = now;
    }

    /// <summary>Creates the one-to-one volunteer profile on first update (F-IDN-05).</summary>
    public void CreateProfile(Guid communityId, string? introduction = null, string? availabilityNote = null)
    {
        VolunteerProfile ??= VolunteerProfile.Create(UserId, communityId, introduction, availabilityNote);
    }

    /// <summary>BR-67: every account except ADMIN belongs to a parish; an empty uuid is not a parish.</summary>
    private static void EnsureParishRule(Guid? parishId, string? roleCode)
    {
        if (parishId == Guid.Empty || (parishId is null && roleCode != RoleCodes.Admin))
        {
            throw new DomainException(
                "USER_PARISH_REQUIRED",
                "A parish is required for every account except ADMIN.");
        }
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var trimmed = phone.Trim();
        if (trimmed.Length > 30)
        {
            throw new DomainException("USER_PHONE_INVALID", "Phone number must not exceed 30 characters.");
        }

        return trimmed;
    }
}
