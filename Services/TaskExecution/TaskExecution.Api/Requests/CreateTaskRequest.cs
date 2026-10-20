using System.ComponentModel.DataAnnotations;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Api.Requests;

public sealed record CreateTaskRequest(
    [property: Required, StringLength(160, MinimumLength = 1)] string Name,
    [property: StringLength(2000)] string? Description,
    [property: StringLength(4000)] string? CompletionCriteria,
    [property: Required] TaskPriority? Priority,
    [property: Range(1, 599999)] int? EstimatedMinutes,
    [property: Range(1, int.MaxValue)] int RequiredVolunteers,
    DateTimeOffset? StartAt,
    DateTimeOffset? Deadline);
