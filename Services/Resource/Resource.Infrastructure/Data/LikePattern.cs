namespace Resource.Infrastructure.Data;

/// <summary>Escapes LIKE wildcards (escape char backslash) so user text matches literally.</summary>
internal static class LikePattern
{
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
