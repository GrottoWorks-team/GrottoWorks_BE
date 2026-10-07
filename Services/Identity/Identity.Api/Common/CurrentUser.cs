using System.Security.Claims;
using Identity.Application.Abstractions;

namespace Identity.Api.Common;

/// <summary>
/// Reads the claims of the validated JWT (claim contract: <c>sub</c>, <c>role</c>, <c>parishId</c>,
/// <c>communityId</c> — inbound claim mapping is disabled, so literal names are used).
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid UserId => ParseGuid(Find("sub") ?? Find(ClaimTypes.NameIdentifier)) ?? Guid.Empty;

    public string Role => Find("role") ?? Find(ClaimTypes.Role) ?? string.Empty;

    public Guid? ParishId => ParseGuid(Find("parishId"));

    public Guid? CommunityId => ParseGuid(Find("communityId"));

    private string? Find(string claimType) =>
        Principal?.FindFirst(claimType)?.Value;

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;
}
