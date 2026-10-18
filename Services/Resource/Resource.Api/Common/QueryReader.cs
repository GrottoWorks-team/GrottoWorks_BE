using Resource.Domain;

namespace Resource.Api.Common;

/// <summary>
/// Strict query-string reader (API Contract 4.4): page ≥ 1, size 1–100 (default 20), one
/// <c>sort=field[,asc|desc]</c> from an allow-list, and unknown parameters → 400 instead of being ignored.
/// </summary>
public sealed class QueryReader
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IQueryCollection _query;

    public QueryReader(IQueryCollection query, params string[] allowedKeys)
    {
        _query = query;
        var allowed = new HashSet<string>(allowedKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in query.Keys)
        {
            if (!allowed.Contains(key))
            {
                throw BadRequest("INVALID_FILTER_FIELD", $"Unknown query parameter '{key}'.");
            }
        }
    }

    public (int Page, int Size) Paging()
    {
        var page = Int("page") ?? 1;
        if (page < 1)
        {
            throw BadRequest("INVALID_PAGE", "'page' must be a positive integer.");
        }

        var size = Int("size") ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            throw BadRequest("INVALID_PAGE_SIZE", $"'size' must be between 1 and {MaxPageSize}.");
        }

        return (page, size);
    }

    /// <summary>Returns the matched field (canonical spelling from <paramref name="fields"/>) and direction.</summary>
    public (string Field, bool Descending) Sort(string defaultField, params string[] fields)
    {
        var raw = String("sort");
        if (raw is null)
        {
            return (defaultField, false);
        }

        var parts = raw.Split(',');
        if (parts.Length > 2)
        {
            throw BadRequest("INVALID_SORT_FIELD", "'sort' must be 'field' or 'field,asc|desc'.");
        }

        var field = fields.FirstOrDefault(candidate => string.Equals(candidate, parts[0].Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw BadRequest("INVALID_SORT_FIELD", $"'sort' field '{parts[0].Trim()}' is not supported.");

        var direction = parts.Length == 2 ? parts[1].Trim().ToLowerInvariant() : "asc";
        return direction switch
        {
            "asc" => (field, false),
            "desc" => (field, true),
            _ => throw BadRequest("INVALID_SORT_DIRECTION", "'sort' direction must be 'asc' or 'desc'.")
        };
    }

    public string? String(string key, int maxLength = 100)
    {
        var values = _query[key];
        if (values.Count > 1)
        {
            throw BadRequest("INVALID_FILTER_VALUE", $"'{key}' can be given only once.");
        }

        var value = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= maxLength
            ? value
            : throw BadRequest("INVALID_FILTER_VALUE", $"'{key}' must not exceed {maxLength} characters.");
    }

    public Guid? Guid(string key)
    {
        var raw = String(key);
        if (raw is null)
        {
            return null;
        }

        return System.Guid.TryParse(raw, out var value)
            ? value
            : throw BadRequest("INVALID_FILTER_VALUE", $"'{key}' must be a UUID.");
    }

    public DateTimeOffset? DateTime(string key)
    {
        var raw = String(key);
        if (raw is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : throw BadRequest("INVALID_FILTER_VALUE", $"'{key}' must be an ISO 8601 date-time.");
    }

    /// <summary>Parses SNAKE_CASE_UPPER enum values (e.g. <c>ACTIVE</c>, <c>OPENING_STOCK</c>).</summary>
    public TEnum? Enum<TEnum>(string key)
        where TEnum : struct, System.Enum
    {
        var raw = String(key);
        if (raw is null)
        {
            return null;
        }

        var pascal = string.Concat(raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

        return System.Enum.TryParse<TEnum>(pascal, ignoreCase: false, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw BadRequest(
                "INVALID_FILTER_VALUE",
                $"'{key}' must be one of: {string.Join(", ", System.Enum.GetNames<TEnum>().Select(ToSnakeUpper))}.");
    }

    private int? Int(string key)
    {
        var raw = String(key);
        if (raw is null)
        {
            return null;
        }

        return int.TryParse(raw, out var value)
            ? value
            : throw BadRequest(key == "size" ? "INVALID_PAGE_SIZE" : "INVALID_PAGE", $"'{key}' must be an integer.");
    }

    private static string ToSnakeUpper(string name) =>
        string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + character : character.ToString())).ToUpperInvariant();

    private static DomainRuleException BadRequest(string code, string message) =>
        new(code, message, DomainErrorKind.BadRequest);
}
