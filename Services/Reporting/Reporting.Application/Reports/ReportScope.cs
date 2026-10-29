using BuildingBlocks.Security;
using Reporting.Application.Recognition;
using Reporting.Application.Scope;
using Reporting.Domain;
using Reporting.Domain.Scope;

namespace Reporting.Application.Reports;

public enum ReportType
{
    Tasks,
    Materials,
    Purchases,
    Contributions,
    Attendance,
    Readiness
}

/// <summary><c>ReportQuery</c>: season (required) and optional work area, community and date range (parish calendar).</summary>
public sealed record ReportQuery(Guid SeasonId, Guid? WorkAreaId = null, Guid? CommunityId = null, DateOnly? From = null, DateOnly? To = null)
{
    public bool InRange(DateTimeOffset? at)
    {
        if (From is null && To is null)
        {
            return true;
        }

        if (at is not { } value)
        {
            return false;
        }

        var day = ParishClock.Today(value);
        return (From is null || day >= From) && (To is null || day <= To);
    }
}

/// <summary>Who a report is built for: the caller, or the requester of an export job (built in the background).</summary>
public sealed record ReportRequester(Guid UserId, string Role, Guid ParishId);

/// <summary>The work areas a report or dashboard may show and how the scope was decided (BR-57).</summary>
public sealed record ResolvedScope(Guid ParishId, Guid SeasonId, string Scope, IReadOnlyList<WorkAreaScope> WorkAreas)
{
    public IReadOnlyList<Guid> Ids => WorkAreas.Select(area => area.WorkAreaId).ToList();

    /// <summary>Season-level data (e.g. money donations without a work area) is shown to the whole-parish scope only.</summary>
    public bool WholeParish => Scope == ReportScopeResolver.ParishScope;
}

/// <summary>
/// BR-57 / BR-67: PARISH sees every work area of its parish's season, LEADER the areas it leads, MO the areas it manages.
/// A season outside the caller's parish is 404; a work area filter outside the caller's scope is 404 too.
/// </summary>
public sealed class ReportScopeResolver(IWorkAreaScopeStore scopes, WorkAreaAccess access, ICurrentUser currentUser)
{
    public const string ParishScope = "PARISH";
    public const string LedScope = "WORK_AREAS_LED";
    public const string ManagedScope = "WORK_AREAS_MANAGED";

    private static readonly IReadOnlyDictionary<ReportType, string[]> Roles = new Dictionary<ReportType, string[]>
    {
        [ReportType.Tasks] = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader],
        [ReportType.Materials] = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer],
        [ReportType.Purchases] = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader, GrottoWorksRoles.MaterialOfficer],
        [ReportType.Contributions] = [GrottoWorksRoles.Parish, GrottoWorksRoles.MaterialOfficer],
        [ReportType.Attendance] = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader],
        [ReportType.Readiness] = [GrottoWorksRoles.Parish, GrottoWorksRoles.Leader]
    };

    public static IReadOnlyList<string> RolesFor(ReportType type) => Roles[type];

    /// <summary>The caller as a requester (parish required — BR-67).</summary>
    public ReportRequester Caller() => new(currentUser.UserId, currentUser.Role, access.RequireParish());

    public static void EnsureRole(ReportType type, ReportRequester requester)
    {
        if (!Roles[type].Contains(requester.Role))
        {
            throw DomainRuleException.Forbidden("REPORT_ACCESS_DENIED", $"Role {requester.Role} cannot read the {type.ToString().ToLowerInvariant()} report.");
        }
    }

    public Task<ResolvedScope> ResolveAsync(ReportQuery query, CancellationToken cancellationToken) => ResolveAsync(query, Caller(), cancellationToken);

    public async Task<ResolvedScope> ResolveAsync(ReportQuery query, ReportRequester requester, CancellationToken cancellationToken)
    {
        var parishId = requester.ParishId;
        var season = await scopes.ListByParishAsync(parishId, query.SeasonId, cancellationToken);
        if (season.Count == 0)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist or is outside your parish.");
        }

        var (scope, visible) = requester.Role switch
        {
            GrottoWorksRoles.Leader => (LedScope, season.Where(area => area.LeaderUserId == requester.UserId).ToList()),
            GrottoWorksRoles.MaterialOfficer => (ManagedScope, season.Where(area => area.MaterialOfficerUserId == requester.UserId).ToList()),
            _ => (ParishScope, season.ToList())
        };

        if (query.WorkAreaId is { } workAreaId)
        {
            visible = visible.Where(area => area.WorkAreaId == workAreaId).ToList();
            if (visible.Count == 0)
            {
                throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND", "The work area does not exist or is outside your scope.");
            }
        }

        if (query.CommunityId is { } communityId)
        {
            visible = visible.Where(area => area.CommunityId == communityId).ToList();
        }

        return new ResolvedScope(parishId, query.SeasonId, scope, visible.OrderBy(area => area.Name, StringComparer.CurrentCulture).ToList());
    }
}
