namespace Identity.Application.Auth;

/// <summary>TokenPair schema (API Contract section 5, User tag).</summary>
public sealed record TokenPairDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType)
{
    public const string BearerTokenType = "Bearer";
}
