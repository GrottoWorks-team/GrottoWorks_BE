namespace Resource.Domain.Catalog;

/// <summary>Normalization shared by catalog entities (codes upper-case, trimmed text).</summary>
internal static class CatalogText
{
    public static string Code(string? value, int maxLength, string errorCode)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0 || normalized.Length > maxLength || normalized.Any(char.IsWhiteSpace))
        {
            throw DomainRuleException.Validation(
                errorCode,
                $"Code is required, must not contain spaces and must not exceed {maxLength} characters.");
        }

        return normalized;
    }

    public static string Required(string? value, int maxLength, string errorCode, string field)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw DomainRuleException.Validation(
                errorCode,
                $"{field} is required and must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    public static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
