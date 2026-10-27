using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Reporting.Application.Exports;
using Reporting.Application.Reports;
using Reporting.Domain.Exports;

namespace Reporting.Infrastructure.Exports;

/// <summary>
/// Renders <c>ReportData</c> to xlsx (ClosedXML), pdf (QuestPDF, Community licence) or csv. Every format starts with the
/// report context required by BA 12.2 / BR-57 (parish, season, generation time, scope, filters), then the table, the
/// totals and the notes. Times are shown in Vietnam time.
/// </summary>
public sealed class ReportFileRenderer : IReportFileRenderer
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    static ReportFileRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public RenderedFile Render(ReportDataDto report, ExportFormat format) =>
        format switch
        {
            ExportFormat.Xlsx => new RenderedFile(Xlsx(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
            ExportFormat.Pdf => new RenderedFile(Pdf(report), "application/pdf", "pdf"),
            _ => new RenderedFile(Csv(report), "text/csv; charset=utf-8", "csv")
        };

    private static IReadOnlyList<(string Label, string Value)> Context(ReportDataDto report) =>
    [
        ("Báo cáo", Title(report.ReportType)),
        ("Giáo xứ", report.ParishId.ToString()),
        ("Season", report.SeasonId.ToString()),
        ("Thời gian tạo", Format(report.GeneratedAt)),
        ("Người tạo", report.GeneratedBy.ToString()),
        ("Phạm vi dữ liệu", report.Scope),
        ("Bộ lọc", Filters(report.Filters))
    ];

    private static byte[] Xlsx(ReportDataDto report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");
        var row = 1;
        foreach (var (label, value) in Context(report))
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = value;
            row++;
        }

        row++;
        var header = row;
        for (var column = 0; column < report.Columns.Count; column++)
        {
            sheet.Cell(header, column + 1).Value = Header(report.Columns[column]);
        }

        sheet.Row(header).Style.Font.Bold = true;
        foreach (var data in report.Rows)
        {
            row++;
            for (var column = 0; column < report.Columns.Count; column++)
            {
                var cell = sheet.Cell(row, column + 1);
                switch (data.GetValueOrDefault(report.Columns[column].Key))
                {
                    case null:
                        break;
                    case decimal number:
                        cell.Value = number;
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    case long number:
                        cell.Value = number;
                        break;
                    case DateTimeOffset at:
                        cell.Value = at.ToOffset(Vietnam).DateTime;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        break;
                    case var other:
                        cell.Value = Text(other);
                        break;
                }
            }
        }

        if (report.Rows.Count > 0)
        {
            sheet.Range(header, 1, row, report.Columns.Count).SetAutoFilter();
        }

        row += 2;
        sheet.Cell(row, 1).Value = "Tổng";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        foreach (var (key, value) in report.Totals)
        {
            row++;
            sheet.Cell(row, 1).Value = key;
            sheet.Cell(row, 2).Value = Text(value);
        }

        row++;
        foreach (var note in report.Notes)
        {
            row++;
            sheet.Cell(row, 1).Value = note;
        }

        sheet.Columns().AdjustToContents(1, Math.Min(row, 500));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] Pdf(ReportDataDto report) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Column(column =>
            {
                column.Item().Text(Title(report.ReportType)).FontSize(14).Bold();
                foreach (var (label, value) in Context(report).Skip(1))
                {
                    column.Item().Text(text =>
                    {
                        text.Span($"{label}: ").SemiBold();
                        text.Span(value);
                    });
                }
            });
            page.Content().PaddingVertical(8).Column(column =>
            {
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in report.Columns)
                        {
                            columns.RelativeColumn();
                        }
                    });
                    table.Header(header =>
                    {
                        foreach (var reportColumn in report.Columns)
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text(Header(reportColumn)).SemiBold();
                        }
                    });
                    foreach (var data in report.Rows)
                    {
                        foreach (var reportColumn in report.Columns)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(2)
                                .Text(Text(data.GetValueOrDefault(reportColumn.Key)));
                        }
                    }
                });
                column.Item().PaddingTop(8).Text("Tổng").Bold();
                foreach (var (key, value) in report.Totals)
                {
                    column.Item().Text($"{key}: {Text(value)}");
                }

                foreach (var note in report.Notes)
                {
                    column.Item().PaddingTop(2).Text(note).Italic();
                }
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();

    private static byte[] Csv(ReportDataDto report)
    {
        var csv = new StringBuilder();
        foreach (var (label, value) in Context(report))
        {
            csv.AppendLine($"{Escape(label)},{Escape(value)}");
        }

        csv.AppendLine();
        csv.AppendLine(string.Join(',', report.Columns.Select(column => Escape(Header(column)))));
        foreach (var row in report.Rows)
        {
            csv.AppendLine(string.Join(',', report.Columns.Select(column => Escape(Text(row.GetValueOrDefault(column.Key))))));
        }

        csv.AppendLine();
        foreach (var (key, value) in report.Totals)
        {
            csv.AppendLine($"{Escape(key)},{Escape(Text(value))}");
        }

        foreach (var note in report.Notes)
        {
            csv.AppendLine(Escape(note));
        }

        // BOM so Excel opens Vietnamese text as UTF-8.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Title(ReportType type) =>
        type switch
        {
            ReportType.Tasks => "Báo cáo task",
            ReportType.Materials => "Báo cáo vật tư",
            ReportType.Purchases => "Báo cáo mua sắm",
            ReportType.Contributions => "Báo cáo đóng góp",
            ReportType.Attendance => "Báo cáo giờ làm",
            _ => "Báo cáo readiness"
        };

    private static string Header(ReportColumnDto column) => column.Unit is { } unit && unit != "unit" ? $"{column.Label} ({unit})" : column.Label;

    private static string Filters(ReportFiltersDto filters)
    {
        var parts = new List<string>();
        if (filters.WorkAreaId is { } area)
        {
            parts.Add($"workAreaId={area}");
        }

        if (filters.CommunityId is { } community)
        {
            parts.Add($"communityId={community}");
        }

        if (filters.From is { } from)
        {
            parts.Add($"from={from:yyyy-MM-dd}");
        }

        if (filters.To is { } to)
        {
            parts.Add($"to={to:yyyy-MM-dd}");
        }

        return parts.Count == 0 ? "(không)" : string.Join("; ", parts);
    }

    private static string Format(DateTimeOffset at) => at.ToOffset(Vietnam).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (UTC+7)";

    private static string Text(object? value) =>
        value switch
        {
            null => string.Empty,
            bool flag => flag ? "Có" : "Không",
            DateTimeOffset at => Format(at),
            DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    /// <summary>RFC 4180 quoting; a leading = + - @ is prefixed with ' so spreadsheets do not run it as a formula.</summary>
    private static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
