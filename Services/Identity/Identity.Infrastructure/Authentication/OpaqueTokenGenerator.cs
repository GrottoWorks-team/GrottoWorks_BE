using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Authentication;

/// <summary>256-bit random URL-safe token; persisted only as a lower-case hex SHA-256 hash.</summary>
public sealed class OpaqueTokenGenerator : IOpaqueTokenGenerator
{
    public OpaqueToken Generate()
    {
        var plaintext = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new OpaqueToken(plaintext, Hash(plaintext));
    }

    public string Hash(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();
}
