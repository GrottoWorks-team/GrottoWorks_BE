using Identity.Domain.Entities;

namespace Identity.Application.Auth.PasswordReset;

/// <summary>Password reset token store (technical table, F-IDN-04).</summary>
public interface IPasswordResetTokenStore
{
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Marks every unused token of the user as used, so only the newest code works.</summary>
    Task InvalidateActiveForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims an unused token. Returns <c>false</c> when a concurrent request consumed it
    /// first — the token is single-use even under a race.
    /// </summary>
    Task<bool> TryConsumeAsync(PasswordResetToken token, DateTimeOffset now, CancellationToken cancellationToken = default);

    void Add(PasswordResetToken token);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
