using System.ComponentModel.DataAnnotations;
using Reporting.Domain.Scope;

namespace Reporting.Api.Requests;

/// <summary>
/// Interim feed of the work-area projection until ParishCoordination publishes
/// <c>planning.work-area.*</c>. Same fields the event payload must carry.
/// </summary>
public sealed class WorkAreaSnapshotRequest
{
    [Required] public Guid? SeasonId { get; init; }

    [Required] public Guid? ParishId { get; init; }

    [Required]
    [StringLength(150)]
    public string? Name { get; init; }

    [Required] public WorkAreaStatus? Status { get; init; }

    [Required] public SeasonStatus? SeasonStatus { get; init; }

    public Guid? CommunityId { get; init; }

    public Guid? LeaderUserId { get; init; }

    public Guid? MaterialOfficerUserId { get; init; }

    /// <summary>Source aggregate version; older snapshots are ignored.</summary>
    [Range(0, long.MaxValue)]
    public long Version { get; init; }
}

public sealed class SeasonStatusRequest
{
    [Required] public SeasonStatus? Status { get; init; }
}

/// <summary>Interim feed of <c>task_projection</c> until TaskExecution publishes <c>task.*</c> events.</summary>
public sealed class TaskSnapshotRequest
{
    [Required] public Guid? WorkAreaId { get; init; }

    [Required]
    [StringLength(200)]
    public string? Name { get; init; }

    [Required] public ProjectedTaskType? Type { get; init; }

    [Required] public ProjectedTaskStatus? Status { get; init; }

    public Guid? PurchaseRequestId { get; init; }

    /// <summary>Volunteers with an ACCEPTED assignment.</summary>
    public List<Guid>? AssigneeUserIds { get; init; }

    [Range(0, long.MaxValue)]
    public long Version { get; init; }
}
