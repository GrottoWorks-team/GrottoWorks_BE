namespace Identity.Application.Abstractions;

/// <summary>Plaintext goes to the user once; only <see cref="Hash"/> is persisted.</summary>
public sealed record OpaqueToken(string Plaintext, string Hash);

/// <summary>
/// Cryptographically random, URL-safe single-use tokens (password reset, F-IDN-04).
/// Implemented in Infrastructure with <c>RandomNumberGenerator</c> + SHA-256.
/// </summary>
public interface IOpaqueTokenGenerator
{
    OpaqueToken Generate();

    string Hash(string plaintext);
}
