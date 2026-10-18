using Identity.Domain.Enums;

namespace Identity.Application.Accounts;

/// <summary>Sortable fields of the user lists (API Contract 4.4 — unknown fields return 400).</summary>
public enum UserSortField
{
    DisplayName,
    Email,
    CreatedAt
}

/// <summary>
/// <c>UserFilter</c> of <c>listUsers</c> / <c>listParishUsers</c>. All filters are optional and combined
/// with AND; <see cref="ParishId"/> is forced to the caller's parish by <c>listParishUsers</c>.
/// </summary>
public sealed record UserSearchCriteria(
    int Page,
    int Size,
    Guid? ParishId = null,
    string? RoleCode = null,
    UserStatus? Status = null,
    Guid? CommunityId = null,
    Guid? SkillId = null,
    string? Search = null,
    UserSortField SortField = UserSortField.DisplayName,
    bool SortDescending = false);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalItems);
