using System.Text.Json;

namespace NotificationAudit.Application.Events;

public sealed record IncomingTaskEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    Guid ActorId,
    Guid ParishId,
    Guid SeasonId,
    string AggregateType,
    Guid AggregateId,
    JsonElement Payload);

public sealed record NotificationCandidate(
    Guid RecipientUserId,
    string Title,
    string Body,
    string? ReferenceType,
    Guid? ReferenceId);

public sealed record AuditCandidate(
    Guid? ActorUserId,
    Guid? ParishId,
    Guid? SeasonId,
    string Action,
    string EntityType,
    Guid EntityId,
    JsonDocument SafeNewValue,
    Guid? CorrelationId,
    DateTimeOffset OccurredAt);

public sealed record EventIngestionPlan(
    Guid EventId,
    string EventType,
    string Status,
    IReadOnlyList<NotificationCandidate> Notifications,
    AuditCandidate Audit);

public interface INotificationEventStore
{
    Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken);
}

public sealed class InvalidNotificationEventException(string message) : Exception(message);

public sealed class TaskEventIngestionHandler(INotificationEventStore store)
{
    public async Task HandleAsync(IncomingTaskEvent message, CancellationToken cancellationToken)
    {
        if (message.EventId == Guid.Empty || string.IsNullOrWhiteSpace(message.EventType))
        {
            throw new InvalidNotificationEventException("The integration event has no identity or event type.");
        }

        var notifications = MapNotifications(message, out var requiresContext);
        var audit = MapAudit(message);
        var plan = new EventIngestionPlan(
            message.EventId,
            message.EventType,
            requiresContext ? "REQUIRES_CONTEXT" : "PROCESSED",
            notifications,
            audit);
        await store.StoreAsync(plan, cancellationToken);
    }

    public static IReadOnlyList<NotificationCandidate> MapNotifications(
        IncomingTaskEvent message,
        out bool requiresContext)
    {
        requiresContext = false;
        var payload = message.Payload;
        var taskId = GetGuid(payload, "taskId") ?? message.AggregateId;

        if (message.EventType == "task.assignment.created")
        {
            var volunteerId = GetGuid(payload, "volunteerUserId");
            if (!volunteerId.HasValue)
            {
                requiresContext = true;
                return [];
            }
            return [new NotificationCandidate(volunteerId.Value, "New task assignment", "You have received a new task assignment.", "TASK", taskId)];
        }

        if (message.EventType == "attendance.request.reviewed")
        {
            var volunteerId = GetGuid(payload, "volunteerUserId");
            var decision = GetString(payload, "decision");
            if (!volunteerId.HasValue || string.IsNullOrWhiteSpace(decision))
            {
                requiresContext = true;
                return [];
            }
            var approved = decision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase);
            return [new NotificationCandidate(
                volunteerId.Value,
                approved ? "Check-in approved" : "Check-in rejected",
                approved ? "Your attendance request was approved." : "Your attendance request was rejected.",
                "TASK",
                taskId)];
        }

        if (message.EventType == "attendance.session.open-too-long")
        {
            var volunteerId = GetGuid(payload, "volunteerUserId");
            var leaderId = GetGuid(payload, "leaderUserId");
            if (!volunteerId.HasValue || !leaderId.HasValue)
            {
                requiresContext = true;
                return [];
            }
            return new[] { volunteerId.Value, leaderId.Value }.Distinct().Select(userId =>
                new NotificationCandidate(
                    userId,
                    "Attendance session still open",
                    "An attendance session has remained open beyond the configured threshold.",
                    "TASK",
                    taskId)).ToList();
        }

        return [];
    }

    private static AuditCandidate MapAudit(IncomingTaskEvent message)
    {
        var allowedFields = new[]
        {
            "taskId", "workAreaId", "assignmentId", "attendanceRequestId", "attendanceSessionId",
            "volunteerUserId", "leaderUserId", "decision", "status"
        };
        var safeValues = new Dictionary<string, JsonElement>();
        foreach (var field in allowedFields)
        {
            if (message.Payload.ValueKind == JsonValueKind.Object && message.Payload.TryGetProperty(field, out var value))
            {
                safeValues[field] = value.Clone();
            }
        }

        Guid? correlationId = Guid.TryParse(message.CorrelationId, out var parsedCorrelation) ? parsedCorrelation : null;
        return new AuditCandidate(
            message.ActorId == Guid.Empty ? null : message.ActorId,
            message.ParishId == Guid.Empty ? null : message.ParishId,
            message.SeasonId == Guid.Empty ? null : message.SeasonId,
            message.EventType,
            message.AggregateType,
            message.AggregateId,
            JsonSerializer.SerializeToDocument(safeValues),
            correlationId,
            message.OccurredAt);
    }

    private static Guid? GetGuid(JsonElement payload, string property)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static string? GetString(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
