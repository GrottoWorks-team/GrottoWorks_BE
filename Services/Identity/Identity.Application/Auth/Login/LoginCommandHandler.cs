using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password);

/// <summary>
/// F-IDN-02: authenticate and issue an access/refresh token pair.
/// A wrong email and a wrong password return the same error code so account existence is not disclosed.
/// </summary>
public sealed class LoginCommandHandler(
    IUserAccountStore userAccountStore,
    IRefreshTokenStore refreshTokenStore,
    IPasswordHasher passwordHasher,
    IAuthenticationTokenService tokenService)
{
    public async Task<TokenPairDto> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmail;
        try
        {
            normalizedEmail = AppUser.NormalizeEmail(command.Email);
        }
        catch (DomainException)
        {
            passwordHasher.VerifyDummy(command.Password);
            throw InvalidCredentials();
        }

        var user = await userAccountStore.GetByEmailAsync(normalizedEmail, cancellationToken);

        if (user?.PasswordHash is null)
        {
            // Same cost as a real check: response time must not reveal whether the email exists.
            passwordHasher.VerifyDummy(command.Password);
            throw InvalidCredentials();
        }

        var verification = passwordHasher.Verify(user, user.PasswordHash, command.Password);
        if (verification == PasswordVerification.Failed)
        {
            throw InvalidCredentials();
        }

        switch (user.Status)
        {
            case UserStatus.Locked:
                throw new DomainException(
                    "AUTH_ACCOUNT_LOCKED",
                    "This account is locked. Contact an administrator.");
            case UserStatus.Inactive:
                throw new DomainException(
                    "AUTH_ACCOUNT_INACTIVE",
                    "This account is inactive. Contact an administrator.");
        }

        var now = DateTimeOffset.UtcNow;

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            // Hash parameters were upgraded (e.g. a newer .NET iteration count): store the new hash.
            user.SetPasswordHash(passwordHasher.Hash(user, command.Password), now);
            await userAccountStore.SaveChangesAsync(cancellationToken);
        }

        var accessToken = tokenService.IssueAccessToken(user, user.VolunteerProfile?.CommunityId);
        var refreshToken = tokenService.IssueRefreshToken(user.UserId);

        refreshTokenStore.Add(refreshToken.Token);
        await refreshTokenStore.SaveChangesAsync(cancellationToken);

        return new TokenPairDto(
            accessToken.Value,
            refreshToken.Plaintext,
            (int)Math.Max(1, (accessToken.ExpiresAt - now).TotalSeconds),
            TokenPairDto.BearerTokenType);
    }

    private static DomainException InvalidCredentials() =>
        new("AUTH_INVALID_CREDENTIALS", "Email or password is incorrect.");
}
