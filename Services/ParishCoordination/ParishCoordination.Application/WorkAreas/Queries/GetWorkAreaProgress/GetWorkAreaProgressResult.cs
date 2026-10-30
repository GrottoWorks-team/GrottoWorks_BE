using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreas.Queries.GetWorkAreaProgress;

public enum GetWorkAreaProgressOutcome
{
    Success,
    WorkAreaNotFound,
    SeasonNotFound,
    TaskExecutionUnavailable
}

public sealed record WorkAreaProgressDto(
    Guid WorkAreaId,
    Guid SeasonId,
    WorkAreaStatus WorkAreaStatus,
    long TotalTasks,
    long CancelledTasks,
    long EligibleTasks,
    long CompletedTasks,
    long OverdueTasks,
    DateTimeOffset CalculatedAt);

public sealed record GetWorkAreaProgressResult(
    GetWorkAreaProgressOutcome Outcome,
    WorkAreaProgressDto? Progress = null)
{
    public static GetWorkAreaProgressResult Success(WorkAreaProgressDto progress) =>
        new(GetWorkAreaProgressOutcome.Success, progress);

    public static GetWorkAreaProgressResult WorkAreaNotFound() =>
        new(GetWorkAreaProgressOutcome.WorkAreaNotFound);

    public static GetWorkAreaProgressResult SeasonNotFound() =>
        new(GetWorkAreaProgressOutcome.SeasonNotFound);

    public static GetWorkAreaProgressResult TaskExecutionUnavailable() =>
        new(GetWorkAreaProgressOutcome.TaskExecutionUnavailable);
}
