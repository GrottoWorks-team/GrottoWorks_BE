using System.ComponentModel.DataAnnotations;

namespace TaskExecution.Api.Requests;

public sealed class ReasonCommandRequest : IValidatableObject
{
    [Range(1,int.MaxValue)] public int? Version { get; init; }
    [Required, StringLength(500,MinimumLength=1)] public string? Reason { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(Reason)) yield return new("Reason must not be blank.", [nameof(Reason)]);
    }
}
