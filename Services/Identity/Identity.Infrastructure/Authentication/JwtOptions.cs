namespace Identity.Infrastructure.Authentication;

/// <summary>
/// Strongly typed JWT settings (skill §35). <see cref="SigningKey"/> is supplied through
/// configuration/providers (environment variable <c>Jwt__SigningKey</c> in deployed environments) — never committed.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public required string SigningKey { get; init; }

    /// <summary>Access tokens are short-lived: 15 minutes (spec F-IDN-03).</summary>
    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 14;
}
