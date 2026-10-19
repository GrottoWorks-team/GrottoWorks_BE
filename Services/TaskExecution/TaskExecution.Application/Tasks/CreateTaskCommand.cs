using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public sealed record CreateTaskCommand(
    Guid WorkAreaId,
    string Name,
    string? Description,
    string? CompletionCriteria,
    TaskPriority Priority,
    int? EstimatedMinutes,
    int RequiredVolunteers,
    DateTimeOffset? StartAt,
    DateTimeOffset? Deadline);

public sealed class CreateTaskCommandHandler(
    ITaskRepository tasks,
    TaskScopeAccess scopeAccess,
    ICurrentUser currentUser)
{
    public async Task<TaskDto> HandleAsync(
        CreateTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        var (workArea, season) = await scopeAccess.GetWorkAreaForLeaderWriteAsync(
            command.WorkAreaId,
            cancellationToken);

        EnsureTaskScheduleFitsScope(command.StartAt, command.Deadline, workArea, season);

        var task = WorkTask.CreateDraft(
            Guid.NewGuid(),
            command.WorkAreaId,
            currentUser.UserId,
            command.Name,
            command.Description,
            command.CompletionCriteria,
            command.Priority,
            command.EstimatedMinutes,
            command.RequiredVolunteers,
            command.StartAt,
            command.Deadline,
            DateTimeOffset.UtcNow);

        tasks.Add(task);
        await tasks.SaveChangesAsync(cancellationToken);
        return TaskDto.From(task);
    }

    private static void EnsureTaskScheduleFitsScope(
        DateTimeOffset? startAt,
        DateTimeOffset? deadline,
        WorkAreaScope workArea,
        SeasonScope season)
    {
        EnsureDateInsideSeason(startAt, season);
        EnsureDateInsideSeason(deadline, season);

        if (startAt.HasValue && workArea.StartDate.HasValue &&
            DateOnly.FromDateTime(startAt.Value.UtcDateTime) < workArea.StartDate.Value)
        {
            throw DomainRuleException.Validation(
                "TASK_DATE_OUT_OF_WORK_AREA",
                "Task start time is before the work area starts.");
        }

        if (deadline.HasValue && workArea.Deadline.HasValue &&
            DateOnly.FromDateTime(deadline.Value.UtcDateTime) > workArea.Deadline.Value)
        {
            throw DomainRuleException.Validation(
                "TASK_DATE_OUT_OF_WORK_AREA",
                "Task deadline is after the work area deadline.");
        }
    }

    private static void EnsureDateInsideSeason(DateTimeOffset? value, SeasonScope season)
    {
        if (!value.HasValue)
        {
            return;
        }

        var date = DateOnly.FromDateTime(value.Value.UtcDateTime);
        if (date < season.StartDate || date > season.EndDate)
        {
            throw DomainRuleException.Validation(
                "TASK_DATE_OUT_OF_SEASON",
                "Task schedule must be within the owning season.");
        }
    }
}
