using Identity.Application.Accounts;

namespace Identity.Application.Admin.ListUsers;

/// <summary>F-IDN-08 <c>listUsers</c> (ADMIN): every account of the system, filtered and paged.</summary>
public sealed class ListUsersQueryHandler(IUserAccountStore userAccountStore)
{
    public async Task<PagedResult<AdminUserDto>> HandleAsync(
        UserSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var result = await userAccountStore.SearchAsync(criteria, cancellationToken);

        return new PagedResult<AdminUserDto>(
            result.Items.Select(AdminUserDto.From).ToList(),
            result.TotalItems);
    }
}
