namespace Identity.Domain.Entities;

/// <summary>
/// Technical table backing F-IDN-04 (spec section 3, "bảng bổ sung"): a single-use, short-lived
/// password reset token. Only the SHA-256 hash is stored; the plaintext is sent to the user once.
/// </summary>
public sealed class PasswordResetToken
{
    private PasswordResetToken()
    {
    }

    private PasswordResetToken(
        Guid passwordResetTokenId,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        PasswordResetTokenId = passwordResetTokenId;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public static PasswordResetToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("PASSWORD_RESET_USER_REQUIRED", "A reset token must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("PASSWORD_RESET_HASH_REQUIRED", "A reset token hash is required.");
        }

        if (expiresAt <= createdAt)
        {
            throw new DomainException("PASSWORD_RESET_INVALID_TTL", "Reset token expiry must be after creation.");
        }

        return new PasswordResetToken(
            Guid.NewGuid(),
            userId,
            tokenHash,
            createdAt.ToUniversalTime(),
            expiresAt.ToUniversalTime());
    }

    public Guid PasswordResetTokenId { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Set when the token was consumed or superseded by a newer request — a token is usable once.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsed => UsedAt is not null;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsUsable(DateTimeOffset now) => !IsUsed && !IsExpired(now);

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now.ToUniversalTime();
}
