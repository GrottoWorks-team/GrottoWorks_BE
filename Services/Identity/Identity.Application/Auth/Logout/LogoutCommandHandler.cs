using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken);

/// <summary>F-IDN-03: revoke the presented refresh token family. Idempotent — an unknown token still returns success.</summary>
public sealed class LogoutCommandHandler(
    IRefreshTokenStore refreshTokenStore,
    IAuthenticationTokenService tokenService)
{
    public async Task HandleAsync(
        LogoutCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            throw new DomainException("AUTH_INVALID_REFRESH_TOKEN", "A refresh token is required.");
        }

        var tokenHash = tokenService.HashRefreshToken(command.RefreshToken.Trim());
        var existing = await refreshTokenStore.GetByHashAsync(tokenHash, cancellationToken);

        if (existing is null)
        {
            return;
        }

        await refreshTokenStore.RevokeFamilyAsync(
            existing.FamilyId,
            RefreshToken.ReasonLoggedOut,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await refreshTokenStore.SaveChangesAsync(cancellationToken);
    }
}
