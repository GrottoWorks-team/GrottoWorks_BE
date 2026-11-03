namespace TaskExecution.Contracts.Events;

// Technical replication contract; business events remain TaskIntegrationEvent.
public sealed record TaskScopeSnapshot(
    Guid EventId, int SchemaVersion, DateTimeOffset CapturedAt,
    Guid TaskId, Guid WorkAreaId, string Name, string TaskType, string Status,
    Guid? PurchaseRequestId, IReadOnlyList<Guid> AssigneeUserIds, long SourceVersion,
    TaskSeasonSnapshot Season, TaskWorkAreaSnapshot WorkArea,
    IReadOnlyList<TaskMaterialSnapshot>? Materials = null);

public sealed record TaskMaterialSnapshot(Guid MaterialId, decimal RequiredQuantity, string? Note);

public sealed record TaskSeasonSnapshot(Guid SeasonId, Guid ParishId,
    DateOnly StartDate, DateOnly EndDate, string Status, long SourceVersion);

public sealed record TaskWorkAreaSnapshot(Guid WorkAreaId, Guid SeasonId, Guid ParishId,
    string Name, Guid? CommunityId, Guid? LeaderUserId, Guid? MaterialOfficerUserId,
    DateOnly? StartDate, DateOnly? Deadline, string Status, long SourceVersion);
