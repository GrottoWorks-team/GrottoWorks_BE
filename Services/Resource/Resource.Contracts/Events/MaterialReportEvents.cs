namespace Resource.Contracts.Events;

/// <summary>One allocation item in a material report. Verified fields are null until the officer verified.</summary>
public sealed record MaterialReportLine(
    Guid ReportItemId,
    Guid MaterialAllocationItemId,
    Guid MaterialId,
    decimal AllocatedQuantity,
    decimal Remaining,
    decimal Damaged,
    decimal Lost);

/// <summary>
/// <c>material-report.submitted</c> (API Contract v1.3.2 §8) — publisher Resource (Vũ), consumers NotificationAudit
/// (tell <see cref="MaterialOfficerUserId"/> to verify) and Reporting. Quantities are the volunteer's reported values.
/// </summary>
public sealed record MaterialReportSubmitted(
    Guid TaskMaterialReportId,
    Guid TaskId,
    Guid WorkAreaId,
    Guid ParishId,
    Guid ReportedBy,
    Guid? MaterialOfficerUserId,
    IReadOnlyList<MaterialReportLine> Items,
    DateTimeOffset ReportedAt)
{
    public const string EventType = "material-report.submitted";

    public const int Version = 1;
}

/// <summary>
/// <c>material-report.verified</c> (API Contract v1.3.2 §8) — publisher Resource (Vũ), consumers NotificationAudit and
/// Reporting. Quantities are the verified values; verified remaining went back to the work area's stock (BR-49),
/// damaged and lost did not (BR-50). Used = allocated − remaining − damaged − lost.
/// </summary>
public sealed record MaterialReportVerified(
    Guid TaskMaterialReportId,
    Guid TaskId,
    Guid WorkAreaId,
    Guid ParishId,
    Guid ReportedBy,
    Guid VerifiedBy,
    IReadOnlyList<MaterialReportLine> Items,
    DateTimeOffset VerifiedAt)
{
    public const string EventType = "material-report.verified";

    public const int Version = 1;
}

/// <summary>One allocation item still out with a cancelled task.</summary>
public sealed record AllocationRecallLine(Guid MaterialAllocationItemId, Guid MaterialId, decimal AllocatedQuantity);

/// <summary>
/// <c>material.allocation.recall-required</c> — publisher Resource (Vũ), consumer NotificationAudit (tell
/// <see cref="MaterialOfficerUserId"/> to collect the materials). Raised once when a task that holds allocated materials is
/// cancelled: the officer (or an assignee) files the material report of the cancelled task so the verified remainder
/// goes back to the work area's stock (BR-44 as extended for cancelled tasks).
/// </summary>
public sealed record MaterialAllocationRecallRequired(
    Guid TaskId,
    Guid WorkAreaId,
    Guid SeasonId,
    Guid ParishId,
    Guid? MaterialOfficerUserId,
    IReadOnlyList<AllocationRecallLine> Items,
    DateTimeOffset CancelledAt)
{
    public const string EventType = "material.allocation.recall-required";

    public const int Version = 1;
}
