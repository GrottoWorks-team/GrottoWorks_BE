using BuildingBlocks.Web.Exceptions;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Security;

/// <summary>Tenant isolation helper (F-PLT-04): every non-admin caller stays inside its own parish.</summary>
public static class ParishAccess
{
    public static void EnsureSameParish(ICurrentUser user, Guid? resourceParishId)
    {
        if (!user.IsAuthenticated)
        {
            throw new DomainException(
                "UNAUTHENTICATED",
                "Authentication is required.",
                StatusCodes.Status401Unauthorized);
        }

        if (string.Equals(user.Role, GrottoWorksRoles.Admin, StringComparison.Ordinal))
        {
            return;
        }

        if (resourceParishId is null || user.ParishId != resourceParishId)
        {
            throw new DomainException(
                "PARISH_ACCESS_DENIED",
                "The resource belongs to another parish.",
                StatusCodes.Status403Forbidden);
        }
    }
}
