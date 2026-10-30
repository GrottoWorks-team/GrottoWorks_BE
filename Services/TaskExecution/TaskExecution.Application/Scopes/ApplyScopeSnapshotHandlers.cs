using TaskExecution.Domain.Scopes;

namespace TaskExecution.Application.Scopes;

public sealed class ApplySeasonScopeSnapshotHandler(IScopeProjectionStore store, IScopeSnapshotTransaction transaction)
{
    public Task<(SeasonScopeDto Scope, bool Applied)> HandleAsync(
        SeasonScopeSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync("season", snapshot.SeasonId,
            () => ApplyAsync(snapshot, cancellationToken), cancellationToken);

    private async Task<(SeasonScopeDto Scope, bool Applied)> ApplyAsync(
        SeasonScopeSnapshot snapshot, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await store.GetSeasonAsync(snapshot.SeasonId, cancellationToken);
        if (existing is null)
        {
            var created = SeasonScope.Create(
                snapshot.SeasonId,
                snapshot.ParishId,
                snapshot.StartDate,
                snapshot.EndDate,
                snapshot.Status,
                snapshot.Version,
                now);

            store.Add(created);
            await store.SaveChangesAsync(cancellationToken);
            return (SeasonScopeDto.From(created), true);
        }

        var applied = existing.Apply(
            snapshot.ParishId,
            snapshot.StartDate,
            snapshot.EndDate,
            snapshot.Status,
            snapshot.Version,
            now);

        if (applied)
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return (SeasonScopeDto.From(existing), applied);
    }
}

public sealed class ApplyWorkAreaScopeSnapshotHandler(IScopeProjectionStore store, IScopeSnapshotTransaction transaction)
{
    public Task<(WorkAreaScopeDto Scope, bool Applied)> HandleAsync(
        WorkAreaScopeSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync("work-area", snapshot.WorkAreaId,
            () => ApplyAsync(snapshot, cancellationToken), cancellationToken);

    private async Task<(WorkAreaScopeDto Scope, bool Applied)> ApplyAsync(
        WorkAreaScopeSnapshot snapshot, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await store.GetWorkAreaAsync(snapshot.WorkAreaId, cancellationToken);
        if (existing is null)
        {
            var created = WorkAreaScope.Create(
                snapshot.WorkAreaId,
                snapshot.SeasonId,
                snapshot.ParishId,
                snapshot.Name,
                snapshot.CommunityId,
                snapshot.LeaderUserId,
                snapshot.MaterialOfficerUserId,
                snapshot.StartDate,
                snapshot.Deadline,
                snapshot.Status,
                snapshot.Version,
                now);

            store.Add(created);
            await store.SaveChangesAsync(cancellationToken);
            return (WorkAreaScopeDto.From(created), true);
        }

        var applied = existing.Apply(
            snapshot.SeasonId,
            snapshot.ParishId,
            snapshot.Name,
            snapshot.CommunityId,
            snapshot.LeaderUserId,
            snapshot.MaterialOfficerUserId,
            snapshot.StartDate,
            snapshot.Deadline,
            snapshot.Status,
            snapshot.Version,
            now);

        if (applied)
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return (WorkAreaScopeDto.From(existing), applied);
    }
}
