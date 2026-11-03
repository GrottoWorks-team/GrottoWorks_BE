using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NotificationAudit.Infrastructure.Data;

namespace NotificationAudit.Infrastructure.SystemExport;

/// <summary>Schema version 1: explicit table/column allowlist. New columns are never exported implicitly.</summary>
public sealed class SystemExportSource(NotificationAuditDbContext db)
{
    public static IReadOnlyDictionary<string, string[]> Columns { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>
    {
        ["activity_log"] = ["activity_log_id", "actor_user_id", "parish_id", "season_id", "action", "entity_type", "entity_id", "reason", "correlation_id", "created_at"],
        ["file_object"] = ["file_id", "uploaded_by_user_id", "object_key", "original_file_name", "content_type", "size_bytes", "checksum", "purpose", "status", "reference_type", "reference_id", "attachment_reservation_id", "attachment_service", "reserved_reference_type", "reserved_reference_id", "created_at", "completed_at"],
        ["notification"] = ["notification_id", "recipient_user_id", "event_type", "title", "body", "reference_type", "reference_id", "is_read", "created_at", "read_at"],
        ["notification_preference"] = ["user_id", "in_app_enabled", "push_enabled", "email_enabled", "updated_at"],
        ["notification_rule"] = ["notification_rule_id", "event_type", "in_app_enabled", "version", "updated_at"],
        ["system_job"] = ["id", "owner_user_id", "kind", "services_json", "status", "created_at", "next_attempt_at", "started_at", "finished_at", "attempts", "artifact_file_id", "artifact_object_key", "artifact_expires_at", "error_code", "correlation_id"],
    });

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), 65536, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "begin", service = "NotificationAudit", schemaVersion = 1, startedAt }).AsMemory(), cancellationToken);
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
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "end", service = "NotificationAudit", schemaVersion = 1, complete = true, startedAt, finishedAt = DateTimeOffset.UtcNow, counts }).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
