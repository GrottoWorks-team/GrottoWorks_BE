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
    JsonElement Payload,
    int SchemaVersion = 1,
    long? AggregateVersion = null);

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
    AuditCandidate Audit,
    IncomingTaskEvent? SafeContext = null);

public interface INotificationEventStore
{
    Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken);
}

public sealed class InvalidNotificationEventException(string message) : Exception(message);

public sealed class TaskEventIngestionHandler(INotificationEventStore store)
{
    public async Task HandleAsync(IncomingTaskEvent message, CancellationToken cancellationToken)
    {
        if (message.EventId == Guid.Empty || message.AggregateId == Guid.Empty || message.OccurredAt == default ||
            message.SchemaVersion != 1 || message.AggregateVersion is <= 0 || message.Payload.ValueKind != JsonValueKind.Object ||
            !ValidLabel(message.EventType, 100) || !ValidLabel(message.AggregateType, 50))
        {
            throw new InvalidNotificationEventException("The integration event has no identity or event type.");
        }

        message = message with { OccurredAt = message.OccurredAt.ToUniversalTime() };
        var notifications = MapNotifications(message, out var requiresContext);
        var audit = MapAudit(message);
        var plan = new EventIngestionPlan(
            message.EventId,
            message.EventType,
            requiresContext ? "REQUIRES_CONTEXT" : "PROCESSED",
            notifications,
            audit,
            message with { Payload = audit.SafeNewValue.RootElement.Clone(), CorrelationId = audit.CorrelationId?.ToString() ?? "" });
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
            if (!volunteerId.HasValue || decision is not ("APPROVED" or "REJECTED"))
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
        var guidFields = new[]
        {
            "taskId", "workAreaId", "assignmentId", "attendanceRequestId", "attendanceSessionId",
            "volunteerUserId", "leaderUserId", "seasonId", "parishId", "communityId", "materialOfficerUserId",
            "purchaseRequestId", "supportRequestId", "supportResponseId", "readinessChecklistId", "itemId", "userId"
        };
        var safeValues = new Dictionary<string, JsonElement>();
        foreach (var field in guidFields)
        {
            if (GetGuid(message.Payload, field) is { } id)
            {
                safeValues[field] = JsonSerializer.SerializeToElement(id);
            }
        }

        var states = new HashSet<string>(StringComparer.Ordinal)
        {
            "OPEN", "NOT_STARTED", "IN_PROGRESS", "SUBMITTED", "REVISION_REQUIRED", "COMPLETED", "CANCELLED",
            "PENDING", "ACCEPTED", "DECLINED", "APPROVED", "REJECTED", "PLANNING", "ACTIVE", "INACTIVE", "LOCKED",
            "READY", "ARCHIVED", "PLANNED", "READINESS_REVIEW", "NEEDS_CORRECTION", "PARTIALLY_FULFILLED", "FULFILLED", "CLOSED",
            "RECEIVED", "PARTIALLY_RECEIVED", "DIRECT", "APPLICATION", "SUPPORT"
        };
        foreach (var field in new[] { "decision", "status", "previousStatus", "newStatus", "assignmentType" })
        {
            var value = GetString(message.Payload, field);
            if (value is not null && states.Contains(value)) safeValues[field] = JsonSerializer.SerializeToElement(value);
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
        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed) && parsed != Guid.Empty ? parsed : null;
    }

    private static string? GetString(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ValidLabel(string value, int maximumLength) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
}
