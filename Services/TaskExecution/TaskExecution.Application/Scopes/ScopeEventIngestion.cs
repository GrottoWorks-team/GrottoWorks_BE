using Identity.Contracts.Events;
using ParishCoordination.Contracts.Events;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.Application.Scopes;

public interface IScopeEventInbox
{
    Task ExecuteAsync(string consumer, Guid eventId, Func<Task> apply, CancellationToken cancellationToken);
}

public sealed class PlanningScopeIngestionHandler(IScopeEventInbox inbox,
    ApplySeasonScopeSnapshotHandler seasons, ApplyWorkAreaScopeSnapshotHandler areas)
{
    public async Task HandleAsync(PlanningScopeSnapshot snapshot, CancellationToken cancellationToken)
    {
        Validate(snapshot);
        await inbox.ExecuteAsync("planning-scope", snapshot.SnapshotId, async () =>
        {
            var season = snapshot.Season;
            await seasons.HandleAsync(new SeasonScopeSnapshot(season.SeasonId, season.ParishId,
                season.StartDate, season.EndDate, Parse<SeasonScopeStatus>(season.Status), season.Version), cancellationToken);
            if (snapshot.WorkArea is { } area)
                await areas.HandleAsync(new WorkAreaScopeSnapshot(area.WorkAreaId, area.SeasonId, area.ParishId,
                    area.Name, area.CommunityId, area.LeaderUserId, area.MaterialOfficerUserId,
                    area.StartDate, area.Deadline, Parse<WorkAreaScopeStatus>(area.Status), area.Version), cancellationToken);
        }, cancellationToken);
    }

    public static void Validate(PlanningScopeSnapshot snapshot)
    {
        if (snapshot.SnapshotId == Guid.Empty || snapshot.SourceVersion < 1 || snapshot.Season is null ||
            snapshot.Season.SeasonId == Guid.Empty || snapshot.Season.ParishId == Guid.Empty ||
            snapshot.Season.Version < 1 || snapshot.Season.EndDate <= snapshot.Season.StartDate)
            throw new FormatException("Invalid planning scope identity, version or season dates.");
        if (snapshot.ScopeType == "season")
        {
            if (snapshot.WorkArea is not null || snapshot.AggregateId != snapshot.Season.SeasonId ||
                snapshot.SourceVersion != snapshot.Season.Version)
                throw new FormatException("The season snapshot envelope does not match its payload.");
        }
        else if (snapshot.ScopeType == "work-area" && snapshot.WorkArea is { } area)
        {
            if (area.WorkAreaId != snapshot.AggregateId || area.WorkAreaId == Guid.Empty ||
                area.Version != snapshot.SourceVersion || area.SeasonId != snapshot.Season.SeasonId ||
                area.ParishId != snapshot.Season.ParishId)
                throw new FormatException("The work area snapshot belongs to another season or parish.");
            _ = Parse<WorkAreaScopeStatus>(area.Status);
        }
        else throw new FormatException("Unknown planning scope type.");
        _ = Parse<SeasonScopeStatus>(snapshot.Season.Status);
    }

    private static T Parse<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Replace("_", ""), true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new FormatException("Unsupported planning scope status.");
}

public interface IUserStatusProjectionStore
{
    Task<UserStatusProjection?> GetAsync(Guid userId, CancellationToken cancellationToken);
    void Add(UserStatusProjection projection);
    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed class IdentityStatusIngestionHandler(IScopeEventInbox inbox, IScopeSnapshotTransaction transaction,
    IUserStatusProjectionStore users)
{
    public Task HandleAsync(IdentityIntegrationEvent message, CancellationToken cancellationToken)
    {
        if (message.EventId == Guid.Empty || message.AggregateId == Guid.Empty || message.AggregateVersion < 1 || message.Version != 1)
            throw new FormatException("Invalid identity event envelope.");
        var status = message.EventType switch
        {
            "identity.user.locked" => "LOCKED",
            "identity.user.unlocked" or "identity.user.registered" => "ACTIVE",
            _ => null
        };
        if (status is null) return Task.CompletedTask;
        return inbox.ExecuteAsync("identity-status", message.EventId, async () =>
        {
            await transaction.ExecuteAsync("user", message.AggregateId, async () =>
            {
                var projection = await users.GetAsync(message.AggregateId, cancellationToken);
                if (projection is null)
                {
                    projection = UserStatusProjection.Create(message.AggregateId, message.ParishId, status, message.AggregateVersion);
                    users.Add(projection);
                }
                else projection.Apply(message.ParishId, status, message.AggregateVersion);
                await users.SaveAsync(cancellationToken);
                return true;
            }, cancellationToken);
        }, cancellationToken);
    }
}
