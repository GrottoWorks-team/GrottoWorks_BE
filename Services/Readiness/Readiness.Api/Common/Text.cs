namespace Readiness.Api.Common;

internal static class Text
{
    /// <summary>Blank optional text means "not provided".</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
