using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Pagination;

/// <summary>
/// <c>page</c>/<c>size</c>/<c>sort</c> query parser (F-PLT-03): defaults page=1 size=20,
/// rejects size &gt; 100 and sort fields outside the allow-list with 400.
/// </summary>
public sealed record PaginationRequest(int Page = 1, int Size = PaginationRequest.DefaultPageSize, string? Sort = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <param name="query">Request query collection.</param>
    /// <param name="allowedSortFields">
    /// Sort fields this endpoint understands (match is case-insensitive). An empty list rejects
    /// every <c>sort</c> value — callers must declare their sortable fields explicitly.
    /// </param>
    public static PaginationRequest FromQuery(IQueryCollection query, params string[] allowedSortFields)
    {
        var page = ParseIntParameter(query, "page", 1);
        if (page < 1)
        {
            throw new DomainException("INVALID_PAGE", "'page' must be a positive integer.", StatusCodes.Status400BadRequest);
        }

        var size = ParseIntParameter(query, "size", DefaultPageSize);
        if (size < 1)
        {
            throw new DomainException("INVALID_PAGE_SIZE", "'size' must be a positive integer.", StatusCodes.Status400BadRequest);
        }

        if (size > MaxPageSize)
        {
            throw new DomainException(
                "PAGE_SIZE_EXCEEDED",
                $"'size' must not exceed {MaxPageSize}.",
                StatusCodes.Status400BadRequest);
        }

        return new PaginationRequest(page, size, ParseSort(query, allowedSortFields));
    }

    /// <summary>
    /// API Contract 4.4: <c>sort=field,asc</c> or <c>sort=field,desc</c>, possibly repeated.
    /// Every value is validated (never silently ignored); this parser supports a single sort
    /// field per endpoint, so repeating a different field returns 400. Normalized as
    /// <c>"field,asc"</c> / <c>"field,desc"</c>.
    /// </summary>
    private static string? ParseSort(IQueryCollection query, string[] allowedSortFields)
    {
        string? sort = null;

        foreach (var rawValue in query["sort"])
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            var parts = rawValue.Trim().Split(',');
            if (parts.Length > 2)
            {
                throw new DomainException(
                    "INVALID_SORT_FIELD",
                    $"'sort' value '{rawValue.Trim()}' must be 'field' or 'field,asc|desc'.",
                    StatusCodes.Status400BadRequest);
            }

            var field = parts[0].Trim();
            var direction = parts.Length == 2 ? parts[1].Trim().ToLowerInvariant() : "asc";

            if (direction is not ("asc" or "desc"))
            {
                throw new DomainException(
                    "INVALID_SORT_DIRECTION",
                    $"'sort' direction '{direction}' must be 'asc' or 'desc'.",
                    StatusCodes.Status400BadRequest);
            }

            var match = allowedSortFields.FirstOrDefault(
                allowed => string.Equals(allowed, field, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                throw new DomainException(
                    "INVALID_SORT_FIELD",
                    $"'sort' field '{field}' is not supported.",
                    StatusCodes.Status400BadRequest);
            }

            var normalized = $"{match},{direction}";

            if (sort is null)
            {
                sort = normalized;
            }
            else if (!string.Equals(sort, normalized, StringComparison.Ordinal))
            {
                throw new DomainException(
                    "MULTIPLE_SORT_FIELDS",
                    "This endpoint supports at most one sort field.",
                    StatusCodes.Status400BadRequest);
            }
        }

        return sort;
    }

    private static int ParseIntParameter(IQueryCollection query, string name, int defaultValue)
    {
        var raw = query[name].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var value))
        {
            throw new DomainException(
                name == "page" ? "INVALID_PAGE" : "INVALID_PAGE_SIZE",
                $"'{name}' must be an integer.",
                StatusCodes.Status400BadRequest);
        }

        return value;
    }
}
