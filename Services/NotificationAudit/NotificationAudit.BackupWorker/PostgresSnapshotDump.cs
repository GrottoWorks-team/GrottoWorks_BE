using System.Data;
using System.Diagnostics;
using Npgsql;
using NotificationAudit.Infrastructure.SystemJobs;

namespace NotificationAudit.BackupWorker;

public static class PostgresSnapshotDump
{
    public static ProcessStartInfo CreateStartInfo(DatabaseBackupOptions options, string connectionString, string snapshot)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Host) ||
            string.IsNullOrWhiteSpace(connection.Username) || connection.Multiplexing)
            throw new InvalidOperationException("A dedicated backup database connection is required.");
        var start = new ProcessStartInfo(options.PgDumpPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Never put credentials or connection strings in command-line arguments or error output.
        start.ArgumentList.Add("--format=custom");
        start.ArgumentList.Add("--no-password");
        start.ArgumentList.Add("--snapshot=" + snapshot);
        // Remove inherited libpq connection settings; only the allowlisted configured target can be used.
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("PG", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password ?? "";
        start.Environment["PGSSLMODE"] = connection.SslMode switch
        {
            SslMode.VerifyFull => "verify-full", SslMode.VerifyCA => "verify-ca",
            _ => connection.SslMode.ToString().ToLowerInvariant()
        };
        if (!string.IsNullOrWhiteSpace(connection.RootCertificate)) start.Environment["PGSSLROOTCERT"] = connection.RootCertificate;
        start.Environment["PGAPPNAME"] = "grottoworks-isolated-backup";
        start.Environment["PGCONNECT_TIMEOUT"] = "15";
        return start;
    }

    public static async Task<(long Bytes, string Sha256)> WriteAsync(DatabaseBackupOptions options, string connectionString,
        string snapshot, Stream destination, long limit, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(options.DumpTimeoutMinutes, 1, 180)));
        using var process = new Process { StartInfo = CreateStartInfo(options, connectionString, snapshot) };
        if (!process.Start()) throw new IOException("Could not start pg_dump.");
        var drain = DrainErrorsAsync(process.StandardError, timeout.Token);
        try
        {
            var content = await ArtifactStreams.CopyHashedAsync(process.StandardOutput.BaseStream, destination, limit, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await drain;
            if (process.ExitCode != 0 || content.Bytes == 0) throw new IOException("Database dump failed.");
            return content;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            try { await drain; } catch (OperationCanceledException) { }
        }
    }

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken ct)
    {
        // Drain bounded chunks to avoid process-pipe deadlocks. Raw stderr may contain connection secrets.
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), ct) > 0) { }
    }
}
