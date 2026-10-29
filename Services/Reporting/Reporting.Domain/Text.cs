namespace Reporting.Domain;

internal static class Text
{
    public static string Required(string? value, int maxLength, string code, string field)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrEmpty(cleaned) || cleaned.Length > maxLength)
        {
            throw DomainRuleException.Validation(code, $"{field} is required and at most {maxLength} characters.");
        }

        return cleaned;
    }

    public static string? Optional(string? value, int maxLength, string field)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (cleaned is { Length: var length } && length > maxLength)
        {
            throw DomainRuleException.Validation("FIELD_TOO_LONG", $"{field} must not exceed {maxLength} characters.");
        }

        return cleaned;
    }
}
