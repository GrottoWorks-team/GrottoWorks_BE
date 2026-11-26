using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class CloneSeasonAreaRequest
{
    [Required(ErrorMessage = "sourceWorkAreaId is required.")]
    public Guid? SourceWorkAreaId { get; init; }

    [StringLength(200)]
    public string? Name { get; init; }
}

/// <summary><c>CloneSeasonRequest</c> (26/11/2026) — mirrors the FE dialog; null/empty <see cref="Areas"/> copies all.</summary>
public sealed class CloneSeasonRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; init; }

    [Range(2000, 2100, ErrorMessage = "seasonYear is out of range.")]
    public int SeasonYear { get; init; }

    [Required(ErrorMessage = "startDate is required.")]
    public DateOnly? StartDate { get; init; }

    [Required(ErrorMessage = "endDate is required.")]
    public DateOnly? EndDate { get; init; }

    [StringLength(2000)]
    public string? Description { get; init; }

    public decimal? EstimatedBudget { get; init; }

    public string? DateShift { get; init; }

    public bool CopyAssignments { get; init; } = true;

    public List<CloneSeasonAreaRequest>? Areas { get; init; }

    public Guid? ParishId { get; init; }
}
