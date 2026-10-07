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

        string? sort = query["sort"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sort))
        {
            sort = sort.Trim();
            var match = allowedSortFields.FirstOrDefault(
                field => string.Equals(field, sort, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                throw new DomainException(
                    "INVALID_SORT_FIELD",
                    $"'sort' field '{sort}' is not supported.",
                    StatusCodes.Status400BadRequest);
            }

            sort = match;
        }
        else
        {
            sort = null;
        }

        return new PaginationRequest(page, size, sort);
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
