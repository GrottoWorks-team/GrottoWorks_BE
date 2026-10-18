using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Auth.PasswordReset;

public sealed record RequestPasswordResetCommand(string Email);

/// <summary>
/// F-IDN-04 <c>requestPasswordReset</c>. Always "accepted" (202) whether or not the email is registered,
/// so the endpoint cannot be used to discover accounts. Only ACTIVE accounts receive a code; a LOCKED or
/// INACTIVE account stays blocked (an admin unlock is required first). An account created by an admin
/// without a password can use this flow to set its first password.
/// </summary>
public sealed class RequestPasswordResetCommandHandler(
    IUserAccountStore userAccountStore,
    IPasswordResetTokenStore resetTokenStore,
    IOpaqueTokenGenerator tokenGenerator,
    IPasswordResetNotifier notifier,
    PasswordResetSettings settings)
{
    public async Task HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmail;
        try
        {
            normalizedEmail = AppUser.NormalizeEmail(command.Email);
        }
        catch (DomainException)
        {
            // Malformed input cannot match an account; answer exactly like an unknown email.
            return;
        }

        var user = await userAccountStore.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(Math.Max(1, settings.TokenMinutes));

        await resetTokenStore.InvalidateActiveForUserAsync(user.UserId, now, cancellationToken);

        var token = tokenGenerator.Generate();
        resetTokenStore.Add(PasswordResetToken.Create(user.UserId, token.Hash, now, expiresAt));
        await resetTokenStore.SaveChangesAsync(cancellationToken);

        await notifier.NotifyAsync(
            new PasswordResetNotification(user.UserId, user.Email, user.FullName, token.Plaintext, expiresAt),
            cancellationToken);
    }
}
