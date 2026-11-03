using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Reporting.Infrastructure.Data;

namespace Reporting.Infrastructure.SystemExport;

/// <summary>Schema version 1: explicit table/column allowlist. New columns are never exported implicitly.</summary>
public sealed class SystemExportSource(ReportingDbContext db)
{
    public static IReadOnlyDictionary<string, string[]> Columns { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>
    {
        ["contribution"] = ["contribution_id", "contributed_by_user_id", "donation_id", "season_id", "parish_id", "work_area_id", "contribution_type_id", "contribution_description", "contribution_date", "reference_type", "reference_id", "donor_user_id"],
        ["contribution_type"] = ["contribution_type_id", "contribution_code", "contribution_name", "contribution_description", "contribution_status"],
        ["recognition_point"] = ["recognition_point_id", "awarded_to_user_id", "season_id", "recognition_rule_id", "contribution_id", "awarded_points", "reason", "created_at"],
        ["recognition_rule"] = ["recognition_rule_id", "contribution_type_id", "rule_name", "rule_version", "rule_points", "rule_description", "rule_status", "effective_from", "effective_to"],
        ["rm_attendance"] = ["attendance_id", "volunteer_user_id", "task_id", "work_area_id", "season_id", "parish_id", "approved_hours", "approved_at"],
        ["rm_donation"] = ["donation_id", "donor_id", "donor_user_id", "season_id", "parish_id", "work_area_id", "donation_type", "is_pledged", "pledged_amount", "pledged_quantity", "received_amount", "received_usable_quantity", "received_unusable_quantity", "receipts", "first_received_at", "updated_at"],
        ["rm_material_line"] = ["work_area_id", "material_id", "season_id", "parish_id", "material_code", "material_name", "unit", "required_quantity", "confirmed_available", "shortage", "pledged_quantity", "donated_usable", "donated_unusable", "purchased_usable", "purchased_unusable", "updated_at"],
        ["rm_purchase_request"] = ["purchase_request_id", "work_area_id", "season_id", "parish_id", "requested_by_user_id", "request_status", "estimated_total", "actual_cost", "deliveries", "submitted_at", "decided_at", "updated_at"],
        ["rm_readiness_area"] = ["work_area_id", "readiness_checklist_id", "season_id", "parish_id", "checklist_status", "failed_items", "failed_mandatory_items", "reopen_count", "ready_at", "updated_at"],
        ["rm_readiness_history"] = ["readiness_history_id", "work_area_id", "readiness_checklist_id", "season_id", "parish_id", "entry_kind", "item_name", "is_required", "entry_note", "actor_user_id", "occurred_at"],
    });

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), 65536, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "begin", service = "Reporting", schemaVersion = 1, startedAt }).AsMemory(), cancellationToken);
        var counts = new Dictionary<string, long>();
        foreach (var (table, columns) in Columns)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandTimeout = 600;
            // Identifiers come exclusively from the source-code allowlist above.
            var projection = string.Join(", ", columns.Select(column => $"\"{column}\""));
            command.CommandText = $"SELECT row_to_json(export_row)::text FROM (SELECT {projection} FROM \"{table}\") export_row";
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            long count = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = reader.GetString(0);
                await writer.WriteLineAsync(($"{{\"type\":\"row\",\"table\":\"{table}\",\"data\":{row}}}").AsMemory(), cancellationToken);
                count++;
            }
            counts.Add(table, count);
        }
        await transaction.CommitAsync(cancellationToken);
        // A consumer MUST reject a response without this footer, even if HTTP already returned 200.
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "end", service = "Reporting", schemaVersion = 1, complete = true, startedAt, finishedAt = DateTimeOffset.UtcNow, counts }).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
