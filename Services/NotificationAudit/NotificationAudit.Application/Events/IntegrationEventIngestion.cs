using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotificationAudit.Application.Events;

public sealed record IncomingIntegrationEvent(
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
    DateTimeOffset OccurredAt,
    string? Reason = null,
    JsonDocument? SafeOldValue = null);

public sealed record EventIngestionPlan(
    Guid EventId,
    string EventType,
    string Status,
    IReadOnlyList<NotificationCandidate> Notifications,
    AuditCandidate Audit,
    IncomingIntegrationEvent? SafeContext = null);

public interface INotificationEventStore
{
    Task StoreAsync(EventIngestionPlan plan, CancellationToken cancellationToken);
}

public sealed class InvalidNotificationEventException(string message) : Exception(message);

public sealed partial class IntegrationEventIngestionHandler(INotificationEventStore store)
{
    public async Task HandleAsync(IncomingIntegrationEvent message, CancellationToken cancellationToken)
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
        IncomingIntegrationEvent message,
        out bool requiresContext)
    {
        requiresContext = false;
        var payload = message.Payload;
        var taskId = GetGuid(payload, "taskId") ?? message.AggregateId;

        if (message.EventType is "task.assignment.created" or "task.assignment.responded" or
            "attendance.request.submitted" or "attendance.request.reviewed")
            return MapTaskWorkflow(message, out requiresContext);

        if (message.EventType == "planning.work-area.assigned")
            return MapWorkAreaAssignment(message, out requiresContext);
        if (message.EventType == "support.member.reviewed")
            return MapSupportDecision(message, out requiresContext);
        if (message.EventType is "support.material.approved" or "support.material.reviewed")
            return MapSupportMaterialDecision(message, out requiresContext);

        if (message.EventType is "purchase.request.submitted" or "purchase.request.decided" or
            "readiness.item.failed" or "recognition.entry.created")
            return MapUpstreamWorkflow(message, out requiresContext);

        if (message.EventType == "task.status.changed" && GetString(payload, "status") is "COMPLETED" or "REVISION_REQUIRED")
        {
            // Review-result recipient context is added with the Task review workflow; do not lose these notifications.
            requiresContext = true;
            return [];
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

        // These are notification events; until their recipient mapping is resolved, retain the safe context.
        requiresContext = message.EventType is "support.request.published" or "planning.work-area.assigned" or "planning.season.activated" or
            "planning.season.completed" or "planning.season.archived" or "purchase.request.submitted" or
            "purchase.request.decided" or "purchase.delivery.confirmed" or "contribution.pledged" or
            "contribution.receipt.confirmed" or "contribution.donation.adjusted" or "loan.offer.submitted" or
            "readiness.item.failed" or "readiness.area.ready" or "readiness.area.reopened" or
            "recognition.entry.created" or "support.transfer.confirmed" or "material-report.submitted" or "material-report.verified";
        return [];
    }

    private static AuditCandidate MapAudit(IncomingIntegrationEvent message)
    {
        var guidFields = new[]
        {
            "taskId", "workAreaId", "assignmentId", "attendanceRequestId", "attendanceSessionId",
            "volunteerUserId", "leaderUserId", "seasonId", "parishId", "communityId", "materialOfficerUserId",
            "purchaseRequestId", "supportRequestId", "supportResponseId", "readinessChecklistId", "itemId", "userId",
            "requestedByUserId", "decidedByUserId", "confirmedByUserId", "buyerUserId", "awardedToUserId",
            "recognitionPointId", "donationId", "donorId", "borrowedItemId", "offeredByUserId", "reportedByUserId",
            "verifiedByUserId", "materialReportId", "reopenedByUserId", "readinessItemId", "materialId",
            "sourceWorkAreaId", "targetWorkAreaId", "requesterUserId", "approvedByUserId", "recipientUserId",
            "requestId", "attendanceId", "correctionId", "supportMemberId", "supportResponseMaterialId", "approvalOperationId"
        };
        var safeValues = new Dictionary<string, JsonElement>();
        if (message.EventType == "support.request.published") AddSafeSupportRequirements(message.Payload, safeValues);
        if (message.EventType is "support.material.approved" or "support.material.reviewed" or "support.transfer.synchronized")
            AddSafeSupportQuantities(message.Payload, safeValues);
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
            "RECEIVED", "PARTIALLY_RECEIVED", "TRANSFERRED", "DIRECT", "APPLICATION", "SUPPORT"
        };
        foreach (var field in new[] { "decision", "status", "previousStatus", "newStatus", "assignmentType" })
        {
            var value = GetString(message.Payload, field);
            if (value is not null && states.Contains(value)) safeValues[field] = JsonSerializer.SerializeToElement(value);
        }
        var reviewType = GetString(message.Payload, "reviewType");
        if (reviewType is "CHECK_IN" or "CORRECTION") safeValues["reviewType"] = JsonSerializer.SerializeToElement(reviewType);
        foreach (var field in new[] { "communityChanged", "leaderChanged", "materialOfficerChanged", "mandatory", "requiresConfirmation" })
        {
            if (message.Payload.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                safeValues[field] = value.Clone();
        }
        var previousStatus = GetString(message.Payload, "previousStatus");
        var newStatus = GetString(message.Payload, "newStatus");
        if (!safeValues.ContainsKey("status") && newStatus is not null && states.Contains(newStatus))
            safeValues["status"] = JsonSerializer.SerializeToElement(newStatus);
        var reason = SanitizeReason(GetString(message.Payload, "reason"));
        if (reason is not null) safeValues["reason"] = JsonSerializer.SerializeToElement(reason);

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
            message.OccurredAt,
            reason,
            previousStatus is not null && states.Contains(previousStatus)
                ? JsonSerializer.SerializeToDocument(new { status = previousStatus }) : null);
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

    private static string? SanitizeReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > 2000) text = text[..2000];
        var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        text = Regex.Replace(text, @"https?://\S+", "[URL_REMOVED]", options);
        text = Regex.Replace(text, @"\bBearer\s+[a-z0-9._~+/-]+=*", "Bearer [REDACTED]", options);
        return Regex.Replace(text, @"\b(password|token|secret|api[-_]?key)\s*[:=]\s*[^\s,;]+", "$1=[REDACTED]", options);
    }
}
