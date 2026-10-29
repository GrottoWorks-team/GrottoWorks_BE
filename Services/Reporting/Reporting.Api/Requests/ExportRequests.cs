using System.ComponentModel.DataAnnotations;
using Reporting.Application.Reports;
using Reporting.Domain.Exports;

namespace Reporting.Api.Requests;

/// <summary><c>CreateReportExport</c> (F-RPT-06): which report, which format and the same filters as <c>ReportQuery</c>.</summary>
public sealed class CreateReportExportRequest
{
    [Required(ErrorMessage = "reportType is required.")]
    public ReportType? ReportType { get; init; }

    /// <summary>XLSX (default), PDF or CSV.</summary>
    public ExportFormat? Format { get; init; }

    [Required(ErrorMessage = "seasonId is required.")]
    public Guid? SeasonId { get; init; }

    public Guid? WorkAreaId { get; init; }

    public Guid? CommunityId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}
