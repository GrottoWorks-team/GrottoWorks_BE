using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Auth.PasswordReset;

public sealed record ResetPasswordCommand(string Token, string NewPassword);

/// <summary>
/// F-IDN-04 <c>resetPassword</c>: a valid, unexpired, unused code sets a new password, is consumed,
/// and every refresh token of the account is revoked (API Contract 11: refresh tokens are revoked on
/// password reset). Unknown, used and expired codes share one error so codes cannot be probed.
/// </summary>
public sealed class ResetPasswordCommandHandler(
    IUserAccountStore userAccountStore,
    IPasswordResetTokenStore resetTokenStore,
    IRefreshTokenStore refreshTokenStore,
    IOpaqueTokenGenerator tokenGenerator,
    IPasswordHasher passwordHasher)
{
    public async Task HandleAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        PasswordPolicy.Validate(command.NewPassword);

        if (string.IsNullOrWhiteSpace(command.Token))
        {
            throw InvalidToken();
        }

        var now = DateTimeOffset.UtcNow;
        var token = await resetTokenStore.GetByHashAsync(tokenGenerator.Hash(command.Token.Trim()), cancellationToken);

        if (token is null || !token.IsUsable(now))
        {
            throw InvalidToken();
        }

        var user = await userAccountStore.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            throw InvalidToken();
        }

        // Single use even when two requests race with the same code.
        if (!await resetTokenStore.TryConsumeAsync(token, now, cancellationToken))
        {
            throw InvalidToken();
        }

        user.SetPasswordHash(passwordHasher.Hash(user, command.NewPassword), now);

        await resetTokenStore.InvalidateActiveForUserAsync(user.UserId, now, cancellationToken);
        await refreshTokenStore.RevokeAllForUserAsync(
            user.UserId,
            RefreshToken.ReasonPasswordReset,
            now,
            cancellationToken);

        // The stores share one unit of work (scoped DbContext): this persists the password,
        // the invalidated codes and the revoked refresh tokens together.
        await userAccountStore.SaveChangesAsync(cancellationToken);
    }

    private static DomainException InvalidToken() =>
        new("AUTH_RESET_TOKEN_INVALID", "The reset code is invalid or has expired. Request a new one.");
}
