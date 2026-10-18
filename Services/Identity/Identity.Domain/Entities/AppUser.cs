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

    public const int MaxSkills = 50;

    public const int MaxAvailabilitySlots = 100;

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

    /// <summary>
    /// Optimistic concurrency token (API Contract 4.5) mapped to PostgreSQL <c>xmin</c>, so no extra column
    /// is needed in <c>app_user</c>. Exposed to ADMIN clients as <c>version</c>; a stale value returns 412.
    /// </summary>
    public uint Version { get; private set; }

    public VolunteerProfile? VolunteerProfile { get; private set; }

    public List<VolunteerSkill> VolunteerSkills { get; private set; } = [];

    public List<VolunteerAvailability> VolunteerAvailabilities { get; private set; } = [];

    /// <summary>Only an ACTIVE account may authenticate (F-IDN-02).</summary>
    public bool CanAuthenticate => Status == UserStatus.Active && PasswordHash is not null;

    /// <summary>Optimistic concurrency guard: <c>null</c> means the client did not send a version.</summary>
    public void EnsureVersion(long? expectedVersion)
    {
        if (expectedVersion is { } expected && expected != Version)
        {
            throw new DomainException(
                "USER_VERSION_MISMATCH",
                "The account was changed by someone else. Reload it and try again.");
        }
    }

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

    /// <summary>F-IDN-08: an administrator locks an ACTIVE or INACTIVE account. Locking twice is a state conflict.</summary>
    public void Lock(DateTimeOffset now)
    {
        if (Status == UserStatus.Locked)
        {
            throw new DomainException("USER_ALREADY_LOCKED", "The account is already locked.");
        }

        Status = UserStatus.Locked;
        UpdatedAt = now;
    }

    /// <summary>F-IDN-08: only a LOCKED account can be unlocked; it returns to ACTIVE.</summary>
    public void Unlock(DateTimeOffset now)
    {
        if (Status != UserStatus.Locked)
        {
            throw new DomainException("USER_NOT_LOCKED", "The account is not locked.");
        }

        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (Status == UserStatus.Locked)
        {
            throw new DomainException(
                "USER_LOCKED_USE_UNLOCK",
                "A locked account must be unlocked before its status can be changed.");
        }

        Status = UserStatus.Inactive;
        UpdatedAt = now;
    }

    /// <summary>INACTIVE → ACTIVE through updateUser. A LOCKED account goes through <see cref="Unlock"/> instead.</summary>
    public void Activate(DateTimeOffset now)
    {
        if (Status == UserStatus.Locked)
        {
            throw new DomainException(
                "USER_LOCKED_USE_UNLOCK",
                "A locked account must be unlocked before its status can be changed.");
        }

        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>F-IDN-08: an administrator corrects the login email (normalized, uniqueness checked by the handler).</summary>
    public void ChangeEmail(string email, DateTimeOffset now)
    {
        Email = NormalizeEmail(email);
        UpdatedAt = now;
    }

    /// <summary>Changes role and parish together so BR-67 is checked against the final pair, not an intermediate one.</summary>
    public void AssignRole(Role role, Guid? parishId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(role);

        var resultingParishId = parishId ?? ParishId;
        EnsureParishRule(resultingParishId, role.Code);

        Role = role;
        RoleId = role.RoleId;
        ParishId = resultingParishId;
        UpdatedAt = now;
    }

    /// <summary>
    /// F-IDN-07 <c>replaceMySkills</c>: the declared skill set becomes exactly <paramref name="skillIds"/>.
    /// Links that stay are kept as-is (their level/note survive); unique (user, skill) holds by construction.
    /// </summary>
    public void ReplaceSkills(IReadOnlyCollection<Guid> skillIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(skillIds);

        if (skillIds.Any(skillId => skillId == Guid.Empty))
        {
            throw new DomainException("VOLUNTEER_SKILL_INVALID", "Skill ids must not be empty.");
        }

        var requested = skillIds.ToHashSet();
        if (requested.Count != skillIds.Count)
        {
            throw new DomainException("VOLUNTEER_SKILL_DUPLICATE", "Each skill can be declared only once.");
        }

        if (requested.Count > MaxSkills)
        {
            throw new DomainException(
                "VOLUNTEER_SKILL_LIMIT",
                $"A volunteer can declare at most {MaxSkills} skills.");
        }

        VolunteerSkills.RemoveAll(link => !requested.Contains(link.SkillId));

        var existing = VolunteerSkills.Select(link => link.SkillId).ToHashSet();
        foreach (var skillId in requested.Where(skillId => !existing.Contains(skillId)))
        {
            VolunteerSkills.Add(VolunteerSkill.Create(UserId, skillId));
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// F-IDN-07 <c>replaceMyAvailability</c>: replaces every structured slot. Each slot must satisfy
    /// <c>available_to &gt; available_from</c> (BR-70, DB check) and slots must not overlap, so matching
    /// (BR-15) never sees ambiguous ranges.
    /// </summary>
    public void ReplaceAvailability(IReadOnlyList<AvailabilitySlot> slots, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (slots.Count > MaxAvailabilitySlots)
        {
            throw new DomainException(
                "AVAILABILITY_LIMIT",
                $"At most {MaxAvailabilitySlots} availability slots can be declared.");
        }

        var created = slots
            .Select(slot => VolunteerAvailability.Create(UserId, slot.AvailableFrom, slot.AvailableTo, slot.Note))
            .OrderBy(slot => slot.AvailableFrom)
            .ToList();

        for (var index = 1; index < created.Count; index++)
        {
            if (created[index].AvailableFrom < created[index - 1].AvailableTo)
            {
                throw new DomainException(
                    "AVAILABILITY_OVERLAP",
                    "Availability slots must not overlap.");
            }
        }

        VolunteerAvailabilities.Clear();
        VolunteerAvailabilities.AddRange(created);
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
