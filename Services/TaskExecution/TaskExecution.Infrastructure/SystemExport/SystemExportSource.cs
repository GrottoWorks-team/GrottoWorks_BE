using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.Infrastructure.SystemExport;

/// <summary>Schema version 1: explicit table/column allowlist. New columns are never exported implicitly.</summary>
public sealed class SystemExportSource(TaskExecutionDbContext db)
{
    public static IReadOnlyDictionary<string, string[]> Columns { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>
    {
        ["attendance"] = ["attendance_id", "task_assignment_id", "check_in_request_id", "check_in_at", "check_out_at", "working_hours", "approved_minutes", "attendance_status", "version"],
        ["attendance_correction"] = ["attendance_correction_id", "attendance_id", "requested_by_user_id", "reviewed_by_user_id", "original_check_in_at", "original_check_out_at", "requested_check_in_at", "requested_check_out_at", "session_version", "correction_reason", "correction_status", "review_note", "reviewed_at", "created_at", "version"],
        ["attendance_request"] = ["attendance_request_id", "task_assignment_id", "requested_by_user_id", "attendance_id", "request_type", "requested_at", "request_status", "request_note", "reviewed_by_user_id", "review_note", "reviewed_at", "version"],
        ["support_position_skill"] = ["support_request_position_id", "skill_id", "required_skill_level"],
        ["support_request"] = ["support_request_id", "season_id", "work_area_id", "requested_by_user_id", "support_type", "request_description", "deadline", "request_status", "created_at", "updated_at", "version", "closed_by_user_id", "closed_at", "close_reason"],
        ["support_request_material"] = ["support_request_material_id", "support_request_id", "material_id", "required_quantity", "material_note"],
        ["support_request_position"] = ["support_request_position_id", "support_request_id", "position_name", "required_volunteer_count", "position_note", "task_id"],
        ["support_response"] = ["support_response_id", "support_request_id", "submitted_by_user_id", "volunteer_user_id", "responding_community_id", "response_status", "response_quantity", "response_note", "created_at", "version"],
        ["support_response_material"] = ["support_response_material_id", "support_response_id", "support_request_id", "support_request_material_id", "source_work_area_id", "material_id", "offered_quantity", "approved_quantity", "received_usable_quantity", "received_unusable_quantity", "material_status", "approved_by_user_id", "approved_at", "received_confirmed_by_user_id", "received_confirmed_at", "material_note", "version", "approval_operation_id", "approval_requested_quantity", "approval_requested_by", "approval_requested_at", "approval_correlation_id", "approval_error_code", "review_reason"],
        ["support_response_member"] = ["support_response_member_id", "support_response_id", "support_request_id", "support_request_position_id", "volunteer_user_id", "member_status", "task_assignment_id", "version", "reviewed_by_user_id", "reviewed_at", "review_reason", "requires_confirmation", "participation_accepted", "participation_reason"],
        ["task"] = ["task_id", "work_area_id", "created_by_user_id", "task_type", "purchase_request_id", "task_name", "task_description", "completion_criteria", "task_priority", "estimated_minutes", "required_volunteers", "start_at", "deadline", "task_status", "version", "cancellation_reason", "cancelled_by_user_id", "cancelled_at", "created_at", "updated_at"],
        ["task_assignment"] = ["task_assignment_id", "task_id", "volunteer_user_id", "assignment_type", "assignment_status", "assignment_note", "response_reason", "requested_at", "assigned_at", "approved_at", "rejected_at", "assigned_by_user_id", "override_capacity", "override_skill_mismatch", "override_reason", "version"],
        ["task_material"] = ["task_material_id", "task_id", "material_id", "required_quantity", "material_note"],
        ["task_skill"] = ["task_skill_id", "task_id", "skill_id", "required_skill_level", "required_volunteer_count"],
        ["task_progress"] = ["task_progress_id", "task_id", "submitted_by_user_id", "progress_percent", "progress_note", "submitted_at", "is_submission", "approved_by_user_id", "approved_at"],
        ["progress_evidence"] = ["progress_evidence_id", "task_progress_id", "file_id", "evidence_caption", "uploaded_at"],
        ["task_completion_review"] = ["review_id", "task_id", "task_progress_id", "reviewed_by_user_id", "approved", "reason", "reviewed_at"],
    });

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), 65536, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "begin", service = "TaskExecution", schemaVersion = 1, startedAt }).AsMemory(), cancellationToken);
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
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "end", service = "TaskExecution", schemaVersion = 1, complete = true, startedAt, finishedAt = DateTimeOffset.UtcNow, counts }).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
