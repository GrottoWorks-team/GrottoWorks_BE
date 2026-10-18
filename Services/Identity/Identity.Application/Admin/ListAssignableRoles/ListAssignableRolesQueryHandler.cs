using Identity.Application.Accounts;

namespace Identity.Application.Admin.ListAssignableRoles;

/// <summary>F-IDN-08 <c>listAssignableRoles</c> (ADMIN): the five seeded roles (one role per account).</summary>
public sealed class ListAssignableRolesQueryHandler(IUserAccountStore userAccountStore)
{
    public async Task<IReadOnlyList<RoleDto>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var roles = await userAccountStore.ListRolesAsync(cancellationToken);

        return roles
            .OrderBy(role => RoleOrder(role.Code))
            .ThenBy(role => role.Code, StringComparer.Ordinal)
            .Select(RoleDto.From)
            .ToList();
    }

    /// <summary>Canonical order ADMIN, PARISH, LEADER, MO, VOLUNTEER; unknown codes last.</summary>
    private static int RoleOrder(string code)
    {
        for (var index = 0; index < Domain.RoleCodes.All.Count; index++)
        {
            if (string.Equals(Domain.RoleCodes.All[index], code, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
