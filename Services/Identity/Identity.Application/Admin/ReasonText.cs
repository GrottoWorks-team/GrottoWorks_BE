using Identity.Domain;

namespace Identity.Application.Admin;

/// <summary><c>ReasonCommand.reason</c>: required, 1–500 characters (API Contract 6, Common).</summary>
internal static class ReasonText
{
    public const int MaxLength = 500;

    public static string Require(string? reason)
    {
        var trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new DomainException(
                "REASON_INVALID",
                $"A reason of 1-{MaxLength} characters is required.");
        }

        return trimmed;
    }
}
