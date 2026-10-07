namespace Identity.Domain.Entities;

/// <summary>
/// Technical table backing F-IDN-03: hashed rotating refresh token.
/// One family per login; rotation links tokens through <see cref="ReplacedByTokenId"/>,
/// and presenting an already-rotated token revokes the whole family (reuse detection).
/// </summary>
public sealed class RefreshToken
{
    public const string ReasonRotated = "rotated";
    public const string ReasonReplaced = "replaced";
    public const string ReasonReuseDetected = "reuse_detected";
    public const string ReasonLoggedOut = "logged_out";
    public const string ReasonExpired = "expired";
    public const string ReasonUserBlocked = "user_blocked";

    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid refreshTokenId,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        RefreshTokenId = refreshTokenId;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public static RefreshToken Create(
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("REFRESH_TOKEN_USER_REQUIRED", "A refresh token must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("REFRESH_TOKEN_HASH_REQUIRED", "A refresh token hash is required.");
        }

        if (expiresAt <= issuedAt)
        {
            throw new DomainException("REFRESH_TOKEN_INVALID_TTL", "Refresh token expiry must be after issue.");
        }

        return new RefreshToken(Guid.NewGuid(), userId, familyId, tokenHash, issuedAt, expiresAt);
    }

    public Guid RefreshTokenId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>All tokens issued by one login share a family.</summary>
    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsRotated => ReplacedByTokenId is not null;

    /// <summary>Presentation of a rotated or revoked token means the token leaked (F-IDN-03).</summary>
    public bool IsReuse => IsRevoked || IsRotated;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsUsable(DateTimeOffset now) => !IsReuse && !IsExpired(now);

    public void RotateWith(Guid nextTokenId, DateTimeOffset now)
    {
        ReplacedByTokenId = nextTokenId;
        RevokedAt ??= now;
        RevokedReason ??= ReasonReplaced;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        RevokedAt ??= now;
        RevokedReason ??= reason;
    }
}
