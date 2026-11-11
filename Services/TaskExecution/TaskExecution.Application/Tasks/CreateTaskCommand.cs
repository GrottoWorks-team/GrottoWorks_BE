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
    int EstimatedMinutes,
    int RequiredVolunteers,
    DateTimeOffset? StartAt,
    DateTimeOffset Deadline,
    /// <summary>Loại task; mặc định `Work`. Dùng `Corrective` cho việc khắc phục readiness (BR-66).</summary>
    TaskExecution.Domain.Tasks.TaskType Type = TaskExecution.Domain.Tasks.TaskType.Work);

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

        TaskScheduleRules.EnsureFitsScope(command.StartAt, command.Deadline, workArea, season);

        var task = WorkTask.CreateOpen(
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
            DateTimeOffset.UtcNow,
            command.Type);

        tasks.Add(task);
        await tasks.SaveChangesAsync(cancellationToken);
        return TaskDto.From(task);
    }

}
