using System.Text.Json;

namespace TaskExecution.Contracts.Events;

public sealed record TaskIntegrationEvent(
    Guid EventId, string EventType, int Version, DateTimeOffset OccurredAt,
    string CorrelationId, Guid ActorId, Guid ParishId, Guid SeasonId,
    string AggregateType, Guid AggregateId, int AggregateVersion, JsonElement Payload);

public static class TaskEventTypes
{
    public const string AssignmentCreated = "task.assignment.created";
    public const string AssignmentResponded = "task.assignment.responded";
    public const string StatusChanged = "task.status.changed";
    public const string AttendanceRequested = "attendance.request.submitted";
    public const string AttendanceReviewed = "attendance.request.reviewed";
    public const string HoursApproved = "attendance.hours.approved";
    public const string SessionOpenTooLong = "attendance.session.open-too-long";
}
