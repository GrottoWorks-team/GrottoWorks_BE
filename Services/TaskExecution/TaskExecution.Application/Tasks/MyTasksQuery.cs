using BuildingBlocks.Security;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Tasks;

public sealed record MyTasksFilter(TaskState? Status, TaskType? TaskType, Guid? SeasonId, int Page, int Size, string? Sort);

public interface IMyTasksStore
{
    Task<(IReadOnlyList<WorkTask> Items, long Total)> GetAsync(Guid volunteerId, Guid parishId,
        MyTasksFilter filter, CancellationToken cancellationToken);
}

public sealed class MyTasksQueryHandler(IMyTasksStore store, ICurrentUser user)
{
    public async Task<(IReadOnlyList<TaskDto> Items, long Total)> HandleAsync(MyTasksFilter filter, CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || user.Role != GrottoWorksRoles.Volunteer || !user.ParishId.HasValue)
            throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED", "A volunteer parish is required.");
        var result = await store.GetAsync(user.UserId, user.ParishId.Value, filter, cancellationToken);
        return (result.Items.Select(TaskDto.From).ToList(), result.Total);
    }
}
