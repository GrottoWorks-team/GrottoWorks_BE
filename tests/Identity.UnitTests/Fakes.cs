using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Application.Auth;
using Identity.Application.Skills;
using Identity.Domain.Entities;
using RefreshTokenEntity = Identity.Domain.Entities.RefreshToken;

namespace Identity.UnitTests;

/// <summary>In-memory stores and services for unit tests (no database).</summary>
public sealed class FakeUserStore : IUserAccountStore
{
    private readonly Dictionary<string, AppUser> _byEmail = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, AppUser> _byId = new();
    private readonly Dictionary<string, Role> _rolesByCode = new(StringComparer.Ordinal);

    public List<AppUser> AddedUsers { get; } = [];

    public int SaveChangesCount { get; private set; }

    public void SeedRole(Role role) => _rolesByCode[role.Code] = role;

    public void Seed(AppUser user) => AddInternal(user);

    public Task<AppUser?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => Task.FromResult(_byEmail.GetValueOrDefault(normalizedEmail));

    public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_byId.GetValueOrDefault(userId));

    public Task<AppUser?> GetDetailsByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_byId.GetValueOrDefault(userId));

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => Task.FromResult(_byEmail.ContainsKey(normalizedEmail));

    public Task<Role?> GetRoleByCodeAsync(string roleCode, CancellationToken cancellationToken = default)
        => Task.FromResult(_rolesByCode.GetValueOrDefault(roleCode));

    public void Add(AppUser user)
    {
        AddedUsers.Add(user);
        AddInternal(user);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }

    private void AddInternal(AppUser user)
    {
        _byEmail[user.Email] = user;
        _byId[user.UserId] = user;
    }
}

public sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    private readonly Dictionary<string, RefreshToken> _byHash = new(StringComparer.Ordinal);
    private readonly List<RefreshToken> _all = [];
    private readonly Dictionary<Guid, string> _hashByTokenId = new();

    public List<RefreshToken> AddedTokens { get; } = [];

    public List<(Guid FamilyId, string Reason)> RevokedFamilies { get; } = [];

    public int SaveChangesCount { get; private set; }

    public void Seed(RefreshToken token)
    {
        AddInternal(token);
    }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Task.FromResult(_byHash.GetValueOrDefault(tokenHash));

    public void Add(RefreshTokenEntity refreshToken)
        => AddInternal(refreshToken);

    /// <summary>When true, the next rotation loses the race against a concurrent request.</summary>
    public bool LoseNextRotationRace { get; set; }

    public Task<bool> TryRotateAsync(
        RefreshTokenEntity current,
        RefreshTokenEntity next,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (LoseNextRotationRace || current.IsReuse)
        {
            LoseNextRotationRace = false;
            return Task.FromResult(false);
        }

        current.RotateWith(next.RefreshTokenId, now);
        AddInternal(next);
        SaveChangesCount++;
        return Task.FromResult(true);
    }

    public Task RevokeFamilyAsync(
        Guid familyId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        RevokedFamilies.Add((familyId, reason));
        foreach (var token in _all.Where(token => token.FamilyId == familyId))
        {
            token.Revoke(reason, now);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }

    private void AddInternal(RefreshToken token)
    {
        _byHash[token.TokenHash] = token;
        _all.Add(token);
        _hashByTokenId[token.RefreshTokenId] = token.TokenHash;
        AddedTokens.Add(token);
    }
}

public sealed class FakeSkillStore : ISkillStore
{
    private readonly List<Skill> _skills = [];
    private readonly Dictionary<string, Skill> _byCode = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Skill> _byId = new();

    public List<Skill> AddedSkills { get; } = [];

    public int SaveChangesCount { get; private set; }

    public void Seed(Skill skill)
    {
        _skills.Add(skill);
        _byCode[skill.Code] = skill;
        _byId[skill.SkillId] = skill;
    }

    public Task<IReadOnlyList<Skill>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Skill>>(_skills.ToList());

    public Task<Skill?> GetByIdAsync(Guid skillId, CancellationToken cancellationToken = default)
        => Task.FromResult(_byId.GetValueOrDefault(skillId));

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default)
        => Task.FromResult(_byCode.ContainsKey(normalizedCode));

    public void Add(Skill skill)
    {
        _skills.Add(skill);
        _byCode[skill.Code] = skill;
        _byId[skill.SkillId] = skill;
        AddedSkills.Add(skill);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Simple deterministic fake: hash = "hash:" + password. A stored "legacy:" + password hash verifies
/// as <see cref="PasswordVerification.SuccessRehashNeeded"/>.
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public int DummyVerifications { get; private set; }

    public string Hash(AppUser user, string password) => "hash:" + password;

    public PasswordVerification Verify(AppUser user, string passwordHash, string password)
    {
        if (string.Equals(passwordHash, "hash:" + password, StringComparison.Ordinal))
        {
            return PasswordVerification.Success;
        }

        return string.Equals(passwordHash, "legacy:" + password, StringComparison.Ordinal)
            ? PasswordVerification.SuccessRehashNeeded
            : PasswordVerification.Failed;
    }

    public void VerifyDummy(string password) => DummyVerifications++;
}

/// <summary>Allows every community unless <see cref="Allow"/> is false.</summary>
public sealed class FakeParishDirectory(bool allow = true) : IParishDirectory
{
    public bool Allow { get; } = allow;

    public List<(Guid CommunityId, Guid ParishId)> Checks { get; } = [];

    public Task<bool> CommunityBelongsToParishAsync(
        Guid communityId,
        Guid parishId,
        CancellationToken cancellationToken = default)
    {
        Checks.Add((communityId, parishId));
        return Task.FromResult(Allow);
    }
}

public sealed class FakeTokenService : IAuthenticationTokenService
{
    public IssuedAccessToken IssueAccessToken(AppUser user, Guid? communityId) =>
        new($"access-token-{user.UserId}-{Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddMinutes(15));

    public IssuedRefreshToken IssueRefreshToken(Guid userId, Guid? familyId = null)
    {
        var plaintext = $"refresh-{userId}-{Guid.NewGuid():N}";
        var token = RefreshToken.Create(
            userId,
            familyId ?? Guid.NewGuid(),
            HashRefreshToken(plaintext),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(14));

        return new IssuedRefreshToken(token, plaintext);
    }

    public string HashRefreshToken(string plaintext) => "rt-hash:" + plaintext;
}

public sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public sealed record PublishedEvent(
        string EventType,
        int Version,
        object? Payload,
        string AggregateType,
        Guid AggregateId);

    public List<PublishedEvent> Events { get; } = [];

    public Task PublishAsync<TPayload>(
        string eventType,
        int version,
        TPayload payload,
        string aggregateType,
        Guid aggregateId,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        Events.Add(new PublishedEvent(eventType, version, payload, aggregateType, aggregateId));
        return Task.CompletedTask;
    }
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid userId, string role = "VOLUNTEER", Guid? parishId = null, Guid? communityId = null)
    {
        UserId = userId;
        Role = role;
        ParishId = parishId;
        CommunityId = communityId;
    }

    public Guid UserId { get; }

    public string Role { get; }

    public Guid? ParishId { get; }

    public Guid? CommunityId { get; }

    public bool IsAuthenticated => true;
}