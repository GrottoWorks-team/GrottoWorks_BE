using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Identity.Infrastructure.Data;

namespace Identity.Infrastructure.SystemExport;

/// <summary>Schema version 1: explicit table/column allowlist. New columns are never exported implicitly.</summary>
public sealed class SystemExportSource(IdentityDbContext db)
{
    public static IReadOnlyDictionary<string, string[]> Columns { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>
    {
        ["app_user"] = ["status_version", "user_id", "role_id", "parish_id", "full_name", "email", "phone", "user_status", "created_at", "updated_at"],
        ["role"] = ["role_id", "role_code", "role_name", "role_description"],
        ["skill"] = ["skill_id", "skill_code", "skill_name", "skill_description", "skill_status"],
        ["volunteer_availability"] = ["volunteer_availability_id", "volunteer_user_id", "available_from", "available_to", "availability_note"],
        ["volunteer_profile"] = ["volunteer_user_id", "community_id", "introduction", "availability_note"],
        ["volunteer_skill"] = ["volunteer_skill_id", "volunteer_user_id", "skill_id", "skill_level", "skill_note"],
    });

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), 65536, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "begin", service = "Identity", schemaVersion = 1, startedAt }).AsMemory(), cancellationToken);
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
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "end", service = "Identity", schemaVersion = 1, complete = true, startedAt, finishedAt = DateTimeOffset.UtcNow, counts }).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
