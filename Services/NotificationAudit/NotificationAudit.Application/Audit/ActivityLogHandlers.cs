using BuildingBlocks.Web.Exceptions;
using NotificationAudit.Contracts.Audit;
using NotificationAudit.Domain.Audit;

namespace NotificationAudit.Application.Audit;

public sealed record ActivityLogFilter(
    Guid? ActorUserId,
    Guid? ParishId,
    Guid? SeasonId,
    string? Action,
    string? EntityType,
    Guid? EntityId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int Size);

public interface IActivityLogRepository
{
    Task<(IReadOnlyList<ActivityLog> Items, long Total)> GetPageAsync(ActivityLogFilter filter, CancellationToken cancellationToken);
    Task<ActivityLog?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityLog>> GetHistoryAsync(string entityType, Guid entityId, CancellationToken cancellationToken);
}

public sealed class ListActivityLogsHandler(IActivityLogRepository repository)
{
    public async Task<(IReadOnlyList<ActivityLogDto> Items, long Total)> HandleAsync(
        ActivityLogFilter filter,
        CancellationToken cancellationToken)
    {
        ValidateRange(filter.From, filter.To);
        var result = await repository.GetPageAsync(filter, cancellationToken);
        return (result.Items.Select(Map).ToList(), result.Total);
    }

    public static void ValidateRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
        {
            throw new DomainException("INVALID_DATE_RANGE", "'from' must be earlier than 'to'.", 400);
        }
    }

    internal static ActivityLogDto Map(ActivityLog log) => new(
        log.Id,
        log.ActorUserId,
        log.ParishId,
        log.SeasonId,
        log.Action,
        log.EntityType,
        log.EntityId,
        log.OldValue?.RootElement.Clone(),
        log.NewValue?.RootElement.Clone(),
        log.Reason,
        log.CorrelationId,
        log.CreatedAt);
}

public sealed class GetActivityLogHandler(IActivityLogRepository repository)
{
    public async Task<ActivityLogDto> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var log = await repository.GetAsync(id, cancellationToken);
        if (log is null) throw new DomainException("ACTIVITY_LOG_NOT_FOUND", "The activity log does not exist.", 404);
        return ListActivityLogsHandler.Map(log);
    }
}

public sealed class GetResourceHistoryHandler(IActivityLogRepository repository)
{
    public async Task<IReadOnlyList<StateChangeDto>> HandleAsync(
        string resourceType,
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        var logs = await repository.GetHistoryAsync(resourceType.Trim(), resourceId, cancellationToken);
        return logs.Select(log => new StateChangeDto(
            log.Id,
            log.Action,
            log.OldValue?.RootElement.Clone(),
            log.NewValue?.RootElement.Clone(),
            log.Reason,
            log.ActorUserId,
            log.CreatedAt)).ToList();
    }
}
