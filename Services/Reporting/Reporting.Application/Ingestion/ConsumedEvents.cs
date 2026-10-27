namespace Reporting.Application.Ingestion;

// Consumer-side payloads of the TaskExecution events (Lâm) Reporting needs. TaskExecution.Contracts has no event records
// yet; these shapes are the proposal in BuildingBlocks README §5.5 — keep both in sync until Task owns the records.

/// <summary><c>task.completion.reviewed</c>: <see cref="Decision"/> APPROVED counts once per volunteer (BR-54).</summary>
public sealed record TaskCompletionReviewed(
    Guid TaskId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    string? TaskName,
    string? TaskType,
    string Decision,
    IReadOnlyList<Guid> VolunteerUserIds,
    Guid ReviewedByUserId,
    DateTimeOffset ReviewedAt)
{
    public const string EventType = "task.completion.reviewed";
    public const string Approved = "APPROVED";
}

/// <summary><c>attendance.hours.approved</c>: only approved time counts (BR-21).</summary>
public sealed record AttendanceHoursApproved(
    Guid AttendanceId,
    Guid TaskId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid VolunteerUserId,
    decimal ApprovedHours,
    Guid ApprovedByUserId,
    DateTimeOffset ApprovedAt)
{
    public const string EventType = "attendance.hours.approved";
}

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
