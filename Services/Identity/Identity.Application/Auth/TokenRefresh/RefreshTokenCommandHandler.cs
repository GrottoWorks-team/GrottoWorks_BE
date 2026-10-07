using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Auth.TokenRefresh;

public sealed record RefreshTokenCommand(string RefreshToken);

/// <summary>
/// F-IDN-03: rotate the refresh token pair.
/// Reuse of an already-rotated/revoked token revokes the entire token family.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    IUserAccountStore userAccountStore,
    IRefreshTokenStore refreshTokenStore,
    IAuthenticationTokenService tokenService)
{
    public async Task<TokenPairDto> HandleAsync(
        RefreshTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            throw new DomainException("AUTH_INVALID_REFRESH_TOKEN", "A refresh token is required.");
        }

        var tokenHash = tokenService.HashRefreshToken(command.RefreshToken.Trim());
        var existing = await refreshTokenStore.GetByHashAsync(tokenHash, cancellationToken)
            ?? throw new DomainException(
                "AUTH_INVALID_REFRESH_TOKEN",
                "The refresh token is invalid.");

        if (existing.IsReuse)
        {
            await refreshTokenStore.RevokeFamilyAsync(
                existing.FamilyId,
                RefreshToken.ReasonReuseDetected,
                now,
                cancellationToken);
            await refreshTokenStore.SaveChangesAsync(cancellationToken);

            throw new DomainException(
                "AUTH_REFRESH_TOKEN_REUSED",
                "The refresh token was already used. The session has been revoked.");
        }

        if (existing.IsExpired(now))
        {
            existing.Revoke(RefreshToken.ReasonExpired, now);
            await refreshTokenStore.SaveChangesAsync(cancellationToken);

            throw new DomainException(
                "AUTH_REFRESH_TOKEN_EXPIRED",
                "The refresh token has expired. Sign in again.");
        }

        var user = await userAccountStore.GetByIdAsync(existing.UserId, cancellationToken)
            ?? throw new DomainException("AUTH_INVALID_REFRESH_TOKEN", "The refresh token is invalid.");

        if (user.Status != UserStatus.Active)
        {
            await refreshTokenStore.RevokeFamilyAsync(
                existing.FamilyId,
                RefreshToken.ReasonUserBlocked,
                now,
                cancellationToken);
            await refreshTokenStore.SaveChangesAsync(cancellationToken);

            throw new DomainException(
                user.Status == UserStatus.Locked ? "AUTH_ACCOUNT_LOCKED" : "AUTH_ACCOUNT_INACTIVE",
                user.Status == UserStatus.Locked
                    ? "This account is locked. Contact an administrator."
                    : "This account is inactive. Contact an administrator.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var accessToken = tokenService.IssueAccessToken(user, user.VolunteerProfile?.CommunityId);
        var nextToken = tokenService.IssueRefreshToken(user.UserId, existing.FamilyId);

        existing.RotateWith(nextToken.Token.RefreshTokenId, nowUtc);
        refreshTokenStore.Add(nextToken.Token);
        await refreshTokenStore.SaveChangesAsync(cancellationToken);

        return new TokenPairDto(
            accessToken.Value,
            nextToken.Plaintext,
            (int)Math.Max(1, (accessToken.ExpiresAt - nowUtc).TotalSeconds),
            TokenPairDto.BearerTokenType);
    }
}
