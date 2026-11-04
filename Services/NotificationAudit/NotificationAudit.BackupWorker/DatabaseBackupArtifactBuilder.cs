using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Npgsql;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.Files;
using NotificationAudit.Domain.SystemJobs;
using NotificationAudit.Infrastructure.Data;
using NotificationAudit.Infrastructure.Files;
using NotificationAudit.Infrastructure.SystemJobs;

namespace NotificationAudit.BackupWorker;

public sealed class DatabaseBackupArtifactBuilder(IOptions<DatabaseBackupOptions> backupOptions,
    IOptions<SystemJobOptions> jobOptions, IOptions<ObjectStorageOptions> storageOptions,
    NotificationAuditDbContext jobs, TimeProvider clock) : ISystemArtifactBuilder
{
    private sealed record RecoveryFile(Guid FileId, string ObjectKey, long SizeBytes, string? Checksum,
        Guid UploadedByUserId, string OriginalFileName, string ContentType, string Purpose, string Status,
        string? ReferenceType, Guid? ReferenceId, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
        Guid? AttachmentReservationId, string? AttachmentService, string? ReservedReferenceType, Guid? ReservedReferenceId);

    public async Task<GeneratedSystemArtifact> BuildAsync(SystemJob job, CancellationToken ct)
    {
        var options = backupOptions.Value;
        var selected = JsonSerializer.Deserialize<string[]>(job.ServicesJson)!;
        if (selected.Any(service => !SystemServiceCatalog.All.Contains(service)))
            throw new InvalidOperationException("Unknown backup source.");
        var keyText = Environment.GetEnvironmentVariable(options.EncryptionKeyEnvironmentVariable);
        byte[] key;
        try { key = Convert.FromBase64String(keyText ?? ""); }
        catch (FormatException) { throw new SystemArtifactBuildException("BACKUP_KEY_NOT_CONFIGURED", "{\"complete\":false,\"errorCode\":\"BACKUP_KEY_NOT_CONFIGURED\"}"); }
        if (key.Length != 32 || string.IsNullOrWhiteSpace(options.EncryptionKeyId))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new SystemArtifactBuildException("BACKUP_KEY_NOT_CONFIGURED", "{\"complete\":false,\"errorCode\":\"BACKUP_KEY_NOT_CONFIGURED\"}");
        }
        var entries = new List<object>();
        var files = new Dictionary<Guid, RecoveryFile>();
        var references = new HashSet<Guid>();
        var failed = false;
        string? manifestJson = null;
        var artifact = ArtifactStreams.CreateTemporaryFile();
        try
        {
            using var encrypted = new BackupEncryptionStream(artifact, key, options.EncryptionKeyId, ct);
            using (var archive = new ZipArchive(encrypted, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var service in selected)
                {
                    var startedAt = clock.GetUtcNow();
                    try
                    {
                        if (!options.Databases.TryGetValue(service, out var connectionString) || string.IsNullOrWhiteSpace(connectionString))
                            throw new InvalidOperationException("Backup source not configured.");
                        await using var connection = new NpgsqlConnection(connectionString);
                        await connection.OpenAsync(ct);
                        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                        await using var snapshotCommand = new NpgsqlCommand("SELECT pg_export_snapshot()", connection, transaction);
                        var snapshot = (string)(await snapshotCommand.ExecuteScalarAsync(ct))!;
                        await CaptureReferencesAsync(service, connection, transaction, references, files, ct);
                        var entryPath = $"databases/{service}.dump";
                        (long Bytes, string Sha256) dump;
                        await using (var destination = archive.CreateEntry(entryPath, CompressionLevel.NoCompression).Open())
                            dump = await PostgresSnapshotDump.WriteAsync(options, connectionString, snapshot, destination,
                                jobOptions.Value.MaximumSourceBytes, ct);
                        await transaction.CommitAsync(ct);
                        entries.Add(new { service, status = "COMPLETED", entry = entryPath, startedAt, finishedAt = clock.GetUtcNow(), bytes = dump.Bytes, sha256 = dump.Sha256, format = "pg_dump custom", snapshot = "Independent repeatable-read database snapshot" });
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        failed = true;
                        entries.Add(new { service, status = "FAILED", startedAt, finishedAt = clock.GetUtcNow(), errorCode = "DATABASE_BACKUP_FAILED" });
                    }
                }
                // Include live object metadata from the NotificationAudit dump snapshot plus any newer references
                // found in the other independent database snapshots. A missing reference fails the whole job.
                var current = await jobs.FileObjects.AsNoTracking()
                    .Where(file => file.Status == FileObjectStatus.Attached || file.Status == FileObjectStatus.Uploaded)
                    .Select(file => new RecoveryFile(file.Id, file.ObjectKey, file.SizeBytes, file.Checksum,
                        file.UploadedByUserId, file.OriginalFileName, file.ContentType, file.Purpose.ToString(), file.Status.ToString(),
                        file.ReferenceType, file.ReferenceId, file.CreatedAt, file.CompletedAt, file.AttachmentReservationId,
                        file.AttachmentService, file.ReservedReferenceType, file.ReservedReferenceId)).ToListAsync(ct);
                foreach (var item in current) files.TryAdd(item.FileId, item);
                foreach (var missing in references.Where(id => !files.ContainsKey(id)))
                {
                    failed = true;
                    entries.Add(new { kind = "FILE_OBJECT", fileId = missing, status = "FAILED", errorCode = "REFERENCED_FILE_METADATA_MISSING" });
                }
                var objectOptions = storageOptions.Value;
                using var client = new MinioClient().WithEndpoint(new Uri(objectOptions.InternalEndpoint).Host, new Uri(objectOptions.InternalEndpoint).Port)
                    .WithCredentials(objectOptions.AccessKey, objectOptions.SecretKey).WithSSL(new Uri(objectOptions.InternalEndpoint).Scheme == "https").Build();
                foreach (var file in files.Values.OrderBy(file => file.FileId))
                {
                    var startedAt = clock.GetUtcNow();
                    try
                    {
                        var path = $"objects/{file.FileId:N}";
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        long length = 0;
                        await using (var target = archive.CreateEntry(path, CompressionLevel.NoCompression).Open())
                        {
                            await client.GetObjectAsync(new GetObjectArgs().WithBucket(objectOptions.Bucket).WithObject(file.ObjectKey)
                                .WithCallbackStream(source =>
                                {
                                    var buffer = new byte[81920];
                                    int read;
                                    while ((read = source.Read(buffer)) > 0)
                                    {
                                        ct.ThrowIfCancellationRequested();
                                        if (read > jobOptions.Value.MaximumSourceBytes - length) throw new IOException("Object exceeds backup size limit.");
                                        length += read;
                                        hash.AppendData(buffer.AsSpan(0, read));
                                        target.Write(buffer, 0, read);
                                    }
                                }), ct);
                        }
                        var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                        if (length != file.SizeBytes || string.IsNullOrEmpty(file.Checksum) || !checksum.Equals(file.Checksum, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Recovery object checksum or size differs from committed metadata.");
                        entries.Add(new { kind = "FILE_OBJECT", fileId = file.FileId, objectKey = file.ObjectKey, bucket = objectOptions.Bucket,
                            entry = path, metadata = file, bytes = length, sha256 = checksum, status = "COMPLETED", startedAt, finishedAt = clock.GetUtcNow() });
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        failed = true;
                        entries.Add(new { kind = "FILE_OBJECT", fileId = file.FileId, objectKey = file.ObjectKey, status = "FAILED", errorCode = "RECOVERY_OBJECT_BACKUP_FAILED", startedAt, finishedAt = clock.GetUtcNow() });
                    }
                }
                manifestJson = JsonSerializer.Serialize(Manifest(job, options, entries, !failed));
                await using var manifest = archive.CreateEntry("manifest.json").Open();
                await manifest.WriteAsync(System.Text.Encoding.UTF8.GetBytes(manifestJson), ct);
            }
            encrypted.Complete();
            if (failed) throw new SystemArtifactBuildException("BACKUP_SOURCE_FAILED", manifestJson!);
            var sha256 = await ArtifactStreams.HashAsync(artifact, ct);
            return new GeneratedSystemArtifact(artifact, artifact.Length, sha256, "application/octet-stream", ".zip.enc", manifestJson!);
        }
        catch { await artifact.DisposeAsync(); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private object Manifest(SystemJob job, DatabaseBackupOptions options, List<object> sources, bool complete) => new
    {
        schemaVersion = 1, jobId = job.Id, kind = "SYSTEM_BACKUP", complete, createdAt = job.CreatedAt, finishedAt = clock.GetUtcNow(),
        encryption = new { format = "GWBAK001", algorithm = "AES-256-GCM chunked", keyId = options.EncryptionKeyId },
        consistency = "Independent database snapshots; not a simultaneous or cross-service transaction-consistent system snapshot.",
        recovery = "Decrypt and authenticate the entire ZIP before restoring custom database dumps and objects to their manifest bucket/objectKey.", sources
    };

    private static async Task CaptureReferencesAsync(string service, NpgsqlConnection connection, NpgsqlTransaction transaction,
        HashSet<Guid> references, Dictionary<Guid, RecoveryFile> files, CancellationToken ct)
    {
        // SQL identifiers are fixed in code, never supplied by a client or environment.
        var query = service switch
        {
            "TaskExecution" => "SELECT file_id FROM progress_evidence",
            "Resource" => "SELECT unnest(receipt_file_ids) FROM purchase",
            "Readiness" => "SELECT unnest(evidence_file_ids) FROM readiness_item",
            _ => null
        };
        if (query is not null)
        {
            await using var command = new NpgsqlCommand(query, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) references.Add(reader.GetGuid(0));
        }
        if (service == "NotificationAudit")
        {
            await using var command = new NpgsqlCommand("SELECT file_id, object_key, size_bytes, checksum, uploaded_by_user_id, original_file_name, content_type, purpose, status, reference_type, reference_id, created_at, completed_at, attachment_reservation_id, attachment_service, reserved_reference_type, reserved_reference_id FROM file_object WHERE status IN ('Uploaded','Attached')", connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var file = new RecoveryFile(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetGuid(10),
                    reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                    reader.IsDBNull(13) ? null : reader.GetGuid(13), reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15), reader.IsDBNull(16) ? null : reader.GetGuid(16));
                files[file.FileId] = file;
            }
        }
    }
}
