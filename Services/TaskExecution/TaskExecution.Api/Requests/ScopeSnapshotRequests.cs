using System.ComponentModel.DataAnnotations;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.Api.Requests;

public sealed record SeasonScopeSnapshotRequest(
    [property: Required] Guid? ParishId,
    [property: Required] DateOnly? StartDate,
    [property: Required] DateOnly? EndDate,
    [property: Required] SeasonScopeStatus? Status,
    [property: Range(1, long.MaxValue)] long Version);

public sealed record WorkAreaScopeSnapshotRequest(
    [property: Required] Guid? SeasonId,
    [property: Required] Guid? ParishId,
    [property: Required, StringLength(150, MinimumLength = 1)] string Name,
    Guid? CommunityId,
    Guid? LeaderUserId,
    Guid? MaterialOfficerUserId,
    DateOnly? StartDate,
    DateOnly? Deadline,
    [property: Required] WorkAreaScopeStatus? Status,
    [property: Range(1, long.MaxValue)] long Version);
