using System.ComponentModel.DataAnnotations;

namespace Resource.Api.Requests;

public sealed class ReportedLineRequest
{
    [Required(ErrorMessage = "Allocation item is required.")]
    public Guid? MaterialAllocationItemId { get; init; }

    public decimal? RemainingQuantity { get; init; }

    public decimal? DamagedQuantity { get; init; }

    public decimal? LostQuantity { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>TaskMaterialReportRequest</c> (F-RES-17): one line per allocation item of the task; missing quantities are 0.</summary>
public sealed class TaskMaterialReportRequest
{
    [Required(ErrorMessage = "Items are required.")]
    public List<ReportedLineRequest>? Items { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

public sealed class VerifiedLineRequest
{
    [Required(ErrorMessage = "Report item is required.")]
    public Guid? ReportItemId { get; init; }

    [Required(ErrorMessage = "Verified remaining quantity is required.")]
    public decimal? RemainingQuantity { get; init; }

    [Required(ErrorMessage = "Verified damaged quantity is required.")]
    public decimal? DamagedQuantity { get; init; }

    [Required(ErrorMessage = "Verified lost quantity is required.")]
    public decimal? LostQuantity { get; init; }
}

/// <summary><c>VerifyMaterialReportRequest</c> (F-RES-17): verified values for every report item.</summary>
public sealed class VerifyMaterialReportRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<VerifiedLineRequest>? Items { get; init; }
}
