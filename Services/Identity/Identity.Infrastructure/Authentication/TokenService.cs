using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Authentication;

/// <summary>
/// Issues signed JWT access tokens (claims: <c>sub</c>, <c>role</c>, <c>parishId</c>, <c>communityId</c>
/// — the claim contract agreed with Lâm, see BuildingBlocks/README.md) and opaque refresh tokens
/// persisted only as a SHA-256 hash.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options) : IAuthenticationTokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedAccessToken IssueAccessToken(AppUser user, Guid? communityId)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new("role", user.Role?.Code ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (user.ParishId is { } parishId)
        {
            claims.Add(new Claim("parishId", parishId.ToString()));
        }

        if (communityId is { } resolvedCommunityId)
        {
            claims.Add(new Claim("communityId", resolvedCommunityId.ToString()));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public IssuedRefreshToken IssueRefreshToken(Guid userId, Guid? familyId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var plaintext = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        var refreshToken = RefreshToken.Create(
            userId,
            familyId ?? Guid.NewGuid(),
            HashRefreshToken(plaintext),
            now,
            now.AddDays(_options.RefreshTokenDays));

        return new IssuedRefreshToken(refreshToken, plaintext);
    }

    public string HashRefreshToken(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();
}
