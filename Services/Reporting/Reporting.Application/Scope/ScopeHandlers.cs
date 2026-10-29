using Reporting.Domain;
using Reporting.Domain.Scope;

namespace Reporting.Application.Scope;

public sealed record SeasonStatusChange(Guid SeasonId, SeasonStatus Status);

public sealed record WorkAreaScopeDto(
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    string Name,
    WorkAreaStatus Status,
    SeasonStatus SeasonStatus,
    Guid? CommunityId,
    Guid? LeaderUserId,
    Guid? MaterialOfficerUserId,
    long Version)
{
    public static WorkAreaScopeDto From(WorkAreaScope scope) =>
        new(scope.WorkAreaId, scope.SeasonId, scope.ParishId, scope.Name, scope.Status, scope.SeasonStatus,
            scope.CommunityId, scope.LeaderUserId, scope.MaterialOfficerUserId, scope.SourceVersion);
}

/// <summary>
/// Applies a work-area snapshot to the projection (spec §2.2). Called today by the internal sync endpoint and,
/// once ParishCoordination publishes <c>planning.work-area.*</c>, by the MassTransit consumer — same code path,
/// idempotent (stale versions are ignored).
/// </summary>
public sealed class ApplyWorkAreaSnapshotHandler(IWorkAreaScopeStore store)
{
    public async Task<(WorkAreaScopeDto Scope, bool Applied)> HandleAsync(
        WorkAreaSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await store.GetAsync(snapshot.WorkAreaId, cancellationToken);

        if (existing is null)
        {
            var created = WorkAreaScope.FromSnapshot(snapshot, now);
            store.Add(created);
            await store.SaveChangesAsync(cancellationToken);
            return (WorkAreaScopeDto.From(created), true);
        }

        var applied = existing.Apply(snapshot, now);
        if (applied)
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return (WorkAreaScopeDto.From(existing), applied);
    }
}

/// <summary><c>planning.season.activated/completed/archived</c> → season state of every projected work area.</summary>
public sealed class ApplySeasonStatusHandler(IWorkAreaScopeStore store)
{
    public async Task<int> HandleAsync(SeasonStatusChange change, CancellationToken cancellationToken = default)
    {
        var scopes = await store.ListBySeasonAsync(change.SeasonId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var scope in scopes)
        {
            scope.ApplySeasonStatus(change.Status, now);
        }

        await store.SaveChangesAsync(cancellationToken);
        return scopes.Count;
    }
}

/// <summary>Reads and checks the work-area scope of the caller (spec §2.1 "Authorization": role + scope).</summary>
public sealed class WorkAreaAccess(IWorkAreaScopeStore store, BuildingBlocks.Security.ICurrentUser currentUser)
{
    /// <summary>Any authenticated user of the same parish (ADMIN: any parish). Other parishes see 404.</summary>
    public async Task<WorkAreaScope> ForReadAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        var scope = await store.GetAsync(workAreaId, cancellationToken);
        if (scope is null || !IsVisible(scope))
        {
            throw DomainRuleException.NotFound("WORK_AREA_NOT_FOUND", "The work area does not exist or is outside your parish.");
        }

        return scope;
    }

    /// <summary>The leader assigned to this work area, while the season is not archived (BR-29, BR-07).</summary>
    public async Task<WorkAreaScope> ForLeaderWriteAsync(Guid workAreaId, CancellationToken cancellationToken = default)
    {
        var scope = await ForReadAsync(workAreaId, cancellationToken);

        if (scope.LeaderUserId != currentUser.UserId)
        {
            throw DomainRuleException.Forbidden(
                "WORK_AREA_SCOPE_DENIED",
                "Only the leader assigned to this work area can decide on it.");
        }

        EnsureWritable(scope);
        return scope;
    }

    public static void EnsureWritable(WorkAreaScope scope)
    {
        if (scope.IsReadOnly)
        {
            throw new DomainRuleException("SEASON_READ_ONLY", "The season is archived; its data is read-only.");
        }
    }

    public bool IsVisible(WorkAreaScope scope) =>
        currentUser.Role == BuildingBlocks.Security.GrottoWorksRoles.Admin || scope.ParishId == currentUser.ParishId;

    /// <summary>The caller's parish (ADMIN may pass any parish explicitly).</summary>
    public Guid RequireParish(Guid? requestedParishId = null)
    {
        if (currentUser.Role == BuildingBlocks.Security.GrottoWorksRoles.Admin && requestedParishId is { } any)
        {
            return any;
        }

        return currentUser.ParishId
            ?? throw DomainRuleException.Forbidden("PARISH_SCOPE_REQUIRED", "Your account is not attached to a parish.");
    }
}
