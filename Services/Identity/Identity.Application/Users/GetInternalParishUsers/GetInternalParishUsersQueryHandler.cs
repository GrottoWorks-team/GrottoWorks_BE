using Identity.Application.Accounts;
using Identity.Application.Users.GetInternalUsers;
using Identity.Domain;
using Identity.Domain.Enums;

namespace Identity.Application.Users.GetInternalParishUsers;

public sealed record GetInternalParishUsersQuery(Guid ParishId, string? Role, int Page, int Size);

/// <summary>
/// Service-to-service directory (26/11/2026): ACTIVE users of one parish in one role. NotificationAudit uses it to fan
/// the "purchase request awaiting decision" notification out to the parish committee, which now approves purchases.
/// </summary>
public sealed class GetInternalParishUsersQueryHandler(IUserAccountStore store)
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<InternalUserDto>> HandleAsync(
        GetInternalParishUsersQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.ParishId == Guid.Empty)
        {
            throw new DomainException("INTERNAL_PARISH_USERS_PARISH_REQUIRED", "A parish is required.");
        }

        var role = string.IsNullOrWhiteSpace(query.Role) ? RoleCodes.Parish : query.Role.Trim().ToUpperInvariant();
        if (!RoleCodes.IsValid(role))
        {
            throw new DomainException("INTERNAL_PARISH_USERS_ROLE_INVALID", $"'role' must be one of: {string.Join(", ", RoleCodes.All)}.");
        }

        if (query.Page < 1 || query.Size is < 1 or > MaxPageSize || (long)(query.Page - 1) * query.Size > int.MaxValue)
        {
            throw new DomainException("INTERNAL_PARISH_USERS_PAGE_INVALID",
                $"Page must be positive and size must be between 1 and {MaxPageSize}.");
        }

        var result = await store.SearchAsync(
            new UserSearchCriteria(query.Page, query.Size, query.ParishId, role, UserStatus.Active),
            cancellationToken);
        return new PagedResult<InternalUserDto>(result.Items.Select(InternalUserDto.From).ToList(), result.TotalItems);
    }
}
