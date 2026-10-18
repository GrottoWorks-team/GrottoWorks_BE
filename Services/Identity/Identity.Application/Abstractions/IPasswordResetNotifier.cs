namespace Identity.Application.Abstractions;

/// <summary>Everything a delivery channel (email/SMS) needs to send the reset code. Never log <see cref="Token"/>.</summary>
public sealed record PasswordResetNotification(
    Guid UserId,
    string Email,
    string DisplayName,
    string Token,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Delivers the password reset code to the account owner (F-IDN-04).
/// INTERIM: no delivery channel exists yet (NotificationAudit, Lâm, has no email sender) — the
/// infrastructure implementation only logs, and exposes the code in logs solely when
/// <c>PasswordReset:ExposeTokenInLogs</c> is enabled for local development.
/// </summary>
public interface IPasswordResetNotifier
{
    Task NotifyAsync(PasswordResetNotification notification, CancellationToken cancellationToken = default);
}
