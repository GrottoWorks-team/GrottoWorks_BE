using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary><paramref name="Plaintext"/> is returned to the client once; only the hash is persisted.</summary>
public sealed record IssuedRefreshToken(RefreshToken Token, string Plaintext);

/// <summary>
/// Issues access tokens (JWT) and refresh tokens. Implemented in Infrastructure;
/// the signing key never reaches the Application layer.
/// </summary>
public interface IAuthenticationTokenService
{
    IssuedAccessToken IssueAccessToken(AppUser user, Guid? communityId);

    IssuedRefreshToken IssueRefreshToken(Guid userId, Guid? familyId = null);

    string HashRefreshToken(string plaintext);
}
