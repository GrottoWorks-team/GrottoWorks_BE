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
            throw await RevokeFamilyAsReuseAsync(existing.FamilyId, now, cancellationToken);
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

        var nextToken = tokenService.IssueRefreshToken(user.UserId, existing.FamilyId);

        // Two concurrent requests with the same token: only one wins the atomic claim,
        // the loser is a reuse and revokes the whole family.
        if (!await refreshTokenStore.TryRotateAsync(existing, nextToken.Token, now, cancellationToken))
        {
            throw await RevokeFamilyAsReuseAsync(existing.FamilyId, now, cancellationToken);
        }

        var accessToken = tokenService.IssueAccessToken(user, user.VolunteerProfile?.CommunityId);

        return new TokenPairDto(
            accessToken.Value,
            nextToken.Plaintext,
            (int)Math.Max(1, (accessToken.ExpiresAt - now).TotalSeconds),
            TokenPairDto.BearerTokenType);
    }

    private async Task<DomainException> RevokeFamilyAsReuseAsync(
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await refreshTokenStore.RevokeFamilyAsync(
            familyId,
            RefreshToken.ReasonReuseDetected,
            now,
            cancellationToken);
        await refreshTokenStore.SaveChangesAsync(cancellationToken);

        return new DomainException(
            "AUTH_REFRESH_TOKEN_REUSED",
            "The refresh token was already used. The session has been revoked.");
    }
}
