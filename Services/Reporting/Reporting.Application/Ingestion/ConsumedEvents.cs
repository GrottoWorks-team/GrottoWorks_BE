namespace Reporting.Application.Ingestion;

// Consumer-side payloads of the TaskExecution events (Lâm). attendance.hours.approved follows what Task publishes
// (TaskExecution.Contracts TaskIntegrationEvent). Older task.completion.reviewed payloads omit recognition context;
// ingestion resolves that context from the envelope and Reporting's own task projection.

/// <summary><c>task.completion.reviewed</c>: <see cref="Decision"/> APPROVED counts once per volunteer (BR-54).</summary>
public sealed record TaskCompletionReviewed(
    Guid TaskId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    string? TaskName,
    string? TaskType,
    string Decision,
    IReadOnlyList<Guid>? VolunteerUserIds,
    Guid ReviewedByUserId,
    DateTimeOffset ReviewedAt)
{
    public const string EventType = "task.completion.reviewed";
    public const string Approved = "APPROVED";
}

/// <summary>
/// <c>attendance.hours.approved</c> as TaskExecution publishes it (check-out, or an approved correction of the same
/// attendance): minutes in the payload, parish / season in the envelope, the task as aggregate id. <see cref="WorkAreaId"/>
/// is not sent yet; Reporting falls back to its task projection. Only approved time counts (BR-21).
/// </summary>
public sealed record TaskHoursApproved(
    Guid AttendanceId,
    Guid? AssignmentId,
    Guid VolunteerUserId,
    DateTimeOffset? CheckInAt,
    DateTimeOffset? CheckOutAt,
    int ApprovedMinutes,
    int? PreviousApprovedMinutes,
    Guid? CorrectionId,
    Guid? WorkAreaId)
{
    public const string EventType = "attendance.hours.approved";
}

/// <summary>Approved hours with the envelope context resolved.</summary>
public sealed record ApprovedHours(
    Guid AttendanceId,
    Guid TaskId,
    Guid? WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid VolunteerUserId,
    decimal Hours,
    DateTimeOffset ApprovedAt);

/// <summary><c>support.member.completed</c>: an approved support member finished the work in another community's area.</summary>
public sealed record SupportMemberCompleted(
    Guid SupportResponseMemberId,
    Guid SupportRequestId,
    Guid VolunteerUserId,
    Guid TargetWorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    DateTimeOffset CompletedAt)
{
    public const string EventType = "support.member.completed";
}
