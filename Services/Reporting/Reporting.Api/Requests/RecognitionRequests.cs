using System.ComponentModel.DataAnnotations;
using Reporting.Domain.Recognition;

namespace Reporting.Api.Requests;

/// <summary><c>CreateContributionType</c> (F-RPT-01).</summary>
public sealed class CreateContributionTypeRequest
{
    [Required(ErrorMessage = "Code is required.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Code must be 1–50 characters.")]
    public string? Code { get; init; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be 1–100 characters.")]
    public string? Name { get; init; }

    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }
}

/// <summary><c>UpdateContributionType</c>: only the fields sent change; the code is permanent.</summary>
public sealed class UpdateContributionTypeRequest
{
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be 1–100 characters.")]
    public string? Name { get; init; }

    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }

    public ContributionTypeStatus? Status { get; init; }
}

/// <summary><c>CreateRecognitionRule</c>: a new rule, or the next version of <c>previousRuleId</c> (BR-68).</summary>
public sealed class CreateRecognitionRuleRequest
{
    public Guid? ContributionTypeId { get; init; }

    [StringLength(150, MinimumLength = 1, ErrorMessage = "Rule name must be 1–150 characters.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Points are required.")]
    [Range(1, 10_000, ErrorMessage = "Points must be between 1 and 10000.")]
    public int? Points { get; init; }

    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }

    [Required(ErrorMessage = "effectiveFrom is required.")]
    public DateOnly? EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    public Guid? PreviousRuleId { get; init; }
}

/// <summary><c>ReasonCommand</c>.</summary>
public sealed class ReasonCommandRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason must be 1–500 characters.")]
    public string? Reason { get; init; }
}
