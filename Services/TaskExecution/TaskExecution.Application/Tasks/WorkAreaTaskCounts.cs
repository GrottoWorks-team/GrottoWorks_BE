namespace TaskExecution.Application.Tasks;

public sealed record WorkAreaTaskCountValues(
    Guid WorkAreaId,
    long TotalTasks,
    long CancelledTasks,
    long CompletedTasks,
    long OverdueTasks);

public sealed record WorkAreaTaskCountDto(
    Guid WorkAreaId,
    long TotalTasks,
    long CancelledTasks,
    long EligibleTasks,
    long CompletedTasks,
    long OverdueTasks,
    DateTimeOffset CalculatedAt);

public interface IWorkAreaTaskCountReader
{
    Task<IReadOnlyList<WorkAreaTaskCountValues>> GetAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        DateTimeOffset calculatedAt,
        CancellationToken cancellationToken = default);
}

public sealed class GetWorkAreaTaskCountsQueryHandler(
    IWorkAreaTaskCountReader reader,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<WorkAreaTaskCountDto>> HandleAsync(
        IReadOnlyList<Guid> workAreaIds,
        CancellationToken cancellationToken = default)
    {
        var calculatedAt = timeProvider.GetUtcNow();
        var counts = await reader.GetAsync(workAreaIds, calculatedAt, cancellationToken);
        var countsByWorkArea = counts.ToDictionary(row => row.WorkAreaId);

        var result = new List<WorkAreaTaskCountDto>(workAreaIds.Count);
        foreach (var workAreaId in workAreaIds)
        {
            if (!countsByWorkArea.TryGetValue(workAreaId, out var row))
            {
                result.Add(new WorkAreaTaskCountDto(
                    workAreaId,
                    TotalTasks: 0,
                    CancelledTasks: 0,
                    EligibleTasks: 0,
                    CompletedTasks: 0,
                    OverdueTasks: 0,
                    calculatedAt));
                continue;
            }

            result.Add(new WorkAreaTaskCountDto(
                row.WorkAreaId,
                row.TotalTasks,
                row.CancelledTasks,
                row.TotalTasks - row.CancelledTasks,
                row.CompletedTasks,
                row.OverdueTasks,
                calculatedAt));
        }

        return result;
    }
}
