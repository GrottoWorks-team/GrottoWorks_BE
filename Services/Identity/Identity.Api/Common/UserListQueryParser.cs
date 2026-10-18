using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Enums;

namespace Identity.Api.Common;

/// <summary>
/// Parses <c>UserFilter</c> + paging (API Contract 4.4): page ≥ 1, size 1–100 (default 20),
/// <c>sort=displayName|email|createdAt[,asc|desc]</c>. Unknown query parameters, filter values and sort
/// fields return 400 instead of being ignored.
/// </summary>
public static class UserListQueryParser
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly HashSet<string> CommonKeys =
        new(["page", "size", "sort", "role", "status", "communityId", "skillId", "q"], StringComparer.OrdinalIgnoreCase);

    public static UserSearchCriteria Parse(IQueryCollection query, bool allowParishFilter)
    {
        foreach (var key in query.Keys)
        {
            var known = CommonKeys.Contains(key) ||
                        (allowParishFilter && string.Equals(key, "parishId", StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                throw BadRequest("INVALID_FILTER_FIELD", $"Unknown query parameter '{key}'.");
            }
        }

        var page = ParseInt(query, "page", 1);
        if (page < 1)
        {
            throw BadRequest("INVALID_PAGE", "'page' must be a positive integer.");
        }

        var size = ParseInt(query, "size", DefaultPageSize);
        if (size is < 1 or > MaxPageSize)
        {
            throw BadRequest("INVALID_PAGE_SIZE", $"'size' must be between 1 and {MaxPageSize}.");
        }

        var (sortField, descending) = ParseSort(query["sort"].ToString());

        string? role = null;
        if (Single(query, "role") is { } rawRole)
        {
            role = rawRole.Trim().ToUpperInvariant();
            if (!RoleCodes.IsValid(role))
            {
                throw BadRequest("INVALID_FILTER_VALUE", $"'role' must be one of: {string.Join(", ", RoleCodes.All)}.");
            }
        }

        UserStatus? status = null;
        if (Single(query, "status") is { } rawStatus)
        {
            status = rawStatus.Trim().ToUpperInvariant() switch
            {
                "ACTIVE" => UserStatus.Active,
                "LOCKED" => UserStatus.Locked,
                "INACTIVE" => UserStatus.Inactive,
                _ => throw BadRequest("INVALID_FILTER_VALUE", "'status' must be ACTIVE, LOCKED or INACTIVE.")
            };
        }

        var search = Single(query, "q")?.Trim();
        if (search is { Length: > 100 })
        {
            throw BadRequest("INVALID_FILTER_VALUE", "'q' must not exceed 100 characters.");
        }

        return new UserSearchCriteria(
            page,
            size,
            ParishId: allowParishFilter ? ParseGuid(query, "parishId") : null,
            RoleCode: role,
            Status: status,
            CommunityId: ParseGuid(query, "communityId"),
            SkillId: ParseGuid(query, "skillId"),
            Search: string.IsNullOrEmpty(search) ? null : search,
            SortField: sortField,
            SortDescending: descending);
    }

    private static (UserSortField Field, bool Descending) ParseSort(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (UserSortField.DisplayName, false);
        }

        if (raw.Contains(';') || raw.Split(',') is not { Length: 1 or 2 } parts)
        {
            throw BadRequest("INVALID_SORT_FIELD", "'sort' must be 'field' or 'field,asc|desc' (one field).");
        }

        var field = parts[0].Trim().ToLowerInvariant() switch
        {
            "displayname" => UserSortField.DisplayName,
            "email" => UserSortField.Email,
            "createdat" => UserSortField.CreatedAt,
            _ => throw BadRequest("INVALID_SORT_FIELD", $"'sort' field '{parts[0].Trim()}' is not supported.")
        };

        var direction = parts.Length == 2 ? parts[1].Trim().ToLowerInvariant() : "asc";
        return direction switch
        {
            "asc" => (field, false),
            "desc" => (field, true),
            _ => throw BadRequest("INVALID_SORT_DIRECTION", "'sort' direction must be 'asc' or 'desc'.")
        };
    }

    private static string? Single(IQueryCollection query, string key)
    {
        var values = query[key];
        if (values.Count > 1)
        {
            throw BadRequest("INVALID_FILTER_VALUE", $"'{key}' can be given only once.");
        }

        var value = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int ParseInt(IQueryCollection query, string key, int defaultValue)
    {
        var raw = Single(query, key);
        if (raw is null)
        {
            return defaultValue;
        }

        return int.TryParse(raw, out var value)
            ? value
            : throw BadRequest("INVALID_PAGE", $"'{key}' must be an integer.");
    }

    private static Guid? ParseGuid(IQueryCollection query, string key)
    {
        var raw = Single(query, key);
        if (raw is null)
        {
            return null;
        }

        return Guid.TryParse(raw, out var value)
            ? value
            : throw BadRequest("INVALID_FILTER_VALUE", $"'{key}' must be a UUID.");
    }

    private static DomainException BadRequest(string code, string message) => new(code, message);
}
