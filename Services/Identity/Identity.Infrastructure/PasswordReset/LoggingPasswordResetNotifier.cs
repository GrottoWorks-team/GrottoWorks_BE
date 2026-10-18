using Identity.Application.Abstractions;
using Identity.Application.Auth.PasswordReset;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.PasswordReset;

/// <summary>
/// INTERIM delivery channel for F-IDN-04 until an email sender exists (NotificationAudit, Lâm).
/// By default it logs only that a code was issued — never the code (BA 15: no tokens in logs).
/// With <c>PasswordReset:ExposeTokenInLogs = true</c> (local development only) the code is logged so the
/// flow can be tested end to end.
/// </summary>
public sealed class LoggingPasswordResetNotifier(
    PasswordResetSettings settings,
    ILogger<LoggingPasswordResetNotifier> logger)
    : IPasswordResetNotifier
{
    public Task NotifyAsync(PasswordResetNotification notification, CancellationToken cancellationToken = default)
    {
        if (settings.ExposeTokenInLogs)
        {
            logger.LogWarning(
                "DEV ONLY — password reset code for user {UserId}: {ResetCode} (expires {ExpiresAt:o}). " +
                "Disable PasswordReset:ExposeTokenInLogs outside local development.",
                notification.UserId,
                notification.Token,
                notification.ExpiresAt);
        }
        else
        {
            logger.LogInformation(
                "Password reset code issued for user {UserId} (expires {ExpiresAt:o}); no delivery channel configured yet.",
                notification.UserId,
                notification.ExpiresAt);
        }

        return Task.CompletedTask;
    }
}
