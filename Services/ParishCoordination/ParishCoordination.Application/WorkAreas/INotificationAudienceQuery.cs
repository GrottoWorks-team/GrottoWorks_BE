namespace ParishCoordination.Application.WorkAreas;

public sealed record WorkAreaAudience(Guid WorkAreaId, Guid SeasonId, Guid ParishId,
    Guid? LeaderUserId, Guid? MaterialOfficerUserId);

public interface INotificationAudienceQuery
{
    Task<IReadOnlyList<WorkAreaAudience>?> GetAsync(Guid parishId, Guid seasonId, CancellationToken ct);
}
