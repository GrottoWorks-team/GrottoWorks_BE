using System.ComponentModel.DataAnnotations;

namespace TaskExecution.Api.Requests;

public sealed class ReplaceCompletionCriteriaRequest
{
    [StringLength(4000)]
    public required string? CompletionCriteria { get; init; }
}
