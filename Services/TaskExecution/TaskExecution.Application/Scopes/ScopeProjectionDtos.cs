using TaskExecution.Domain.Scopes;

namespace TaskExecution.Application.Scopes;

public sealed record SeasonScopeSnapshot(
    Guid SeasonId,
    Guid ParishId,
    DateOnly StartDate,
    DateOnly EndDate,
    SeasonScopeStatus Status,
    long Version);

public sealed record WorkAreaScopeSnapshot(
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    string Name,
    Guid? CommunityId,
    Guid? LeaderUserId,
    Guid? MaterialOfficerUserId,
    DateOnly? StartDate,
    DateOnly? Deadline,
    WorkAreaScopeStatus Status,
    long Version);

public sealed record SeasonScopeDto(
    Guid Id,
    Guid ParishId,
    DateOnly StartDate,
    DateOnly EndDate,
    SeasonScopeStatus Status,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static SeasonScopeDto From(SeasonScope scope) =>
        new(scope.SeasonId, scope.ParishId, scope.StartDate, scope.EndDate,
            scope.Status, scope.SourceVersion, scope.UpdatedAt);
}

public sealed record WorkAreaScopeDto(
    Guid Id,
    Guid SeasonId,
    Guid ParishId,
    string Name,
    Guid? CommunityId,
    Guid? LeaderUserId,
    Guid? MaterialOfficerUserId,
    DateOnly? StartDate,
    DateOnly? Deadline,
    WorkAreaScopeStatus Status,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static WorkAreaScopeDto From(WorkAreaScope scope) =>
        new(scope.WorkAreaId, scope.SeasonId, scope.ParishId, scope.Name,
            scope.CommunityId, scope.LeaderUserId, scope.MaterialOfficerUserId,
            scope.StartDate, scope.Deadline, scope.Status, scope.SourceVersion, scope.UpdatedAt);
}
