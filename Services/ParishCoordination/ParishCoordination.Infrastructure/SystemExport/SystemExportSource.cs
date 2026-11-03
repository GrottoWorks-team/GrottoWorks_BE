using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.SystemExport;

/// <summary>Schema version 1: explicit table/column allowlist. New columns are never exported implicitly.</summary>
public sealed class SystemExportSource(ParishCoordinationDbContext db)
{
    public static IReadOnlyDictionary<string, string[]> Columns { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>
    {
        ["christmas_season"] = ["season_id", "parish_id", "created_by_user_id", "season_name", "season_year", "start_date", "end_date", "season_status", "season_description", "estimated_budget", "created_at", "updated_at", "version"],
        ["community"] = ["community_id", "parish_id", "community_name", "community_description", "community_status", "created_at", "updated_at", "version"],
        ["parish"] = ["parish_id", "parish_name", "parish_address", "parish_description", "parish_status", "created_at", "updated_at", "version"],
        ["work_area"] = ["latitude", "longitude", "work_area_id", "season_id", "community_id", "work_area_category_id", "leader_user_id", "material_officer_user_id", "work_area_name", "work_area_description", "start_date", "deadline", "work_area_status", "created_at", "updated_at", "version"],
        ["work_area_category"] = ["work_area_category_id", "category_code", "category_name", "category_description", "category_status"],
    });

    public async Task WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        using var writer = new StreamWriter(destination, new UTF8Encoding(false), 65536, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "begin", service = "ParishCoordination", schemaVersion = 1, startedAt }).AsMemory(), cancellationToken);
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
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "end", service = "ParishCoordination", schemaVersion = 1, complete = true, startedAt, finishedAt = DateTimeOffset.UtcNow, counts }).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
