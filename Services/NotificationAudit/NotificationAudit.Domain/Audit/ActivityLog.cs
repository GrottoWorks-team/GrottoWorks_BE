using System.Text.Json;

namespace NotificationAudit.Domain.Audit;

public sealed class ActivityLog
{
    private ActivityLog() { }

    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? ParishId { get; private set; }
    public Guid? SeasonId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public JsonDocument? OldValue { get; private set; }
    public JsonDocument? NewValue { get; private set; }
    public string? Reason { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ActivityLog Create(
        Guid? actorUserId,
        Guid? parishId,
        Guid? seasonId,
        string action,
        string entityType,
        Guid entityId,
        JsonDocument? oldValue,
        JsonDocument? newValue,
        string? reason,
        Guid? correlationId,
        DateTimeOffset createdAt) => new()
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            ParishId = parishId,
            SeasonId = seasonId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            Reason = reason,
            CorrelationId = correlationId,
            CreatedAt = createdAt
        };
}
