# System exports and encrypted backups

Implementation follows the reviewed API contract v1.3.4 and Desktop PLAN.md. No runtime export, backup, migration or test execution was performed during implementation.

## Small feature groups / changed paths

1. **Service export sources (one independent group per service)**: each of Identity, ParishCoordination, TaskExecution, Resource, Readiness, Reporting and NotificationAudit has two new files:
   - `<Service>.Infrastructure/SystemExport/SystemExportSource.cs`: explicit SQL table/column allowlist, streaming JSONL, read-only repeatable-read transaction, schema/version identity and terminal row-count footer.
   - `<Service>.Api/SystemExport/SystemExportEndpoints.cs`: internal authenticated endpoint extension. No existing other-service file was modified for these sources.
2. **System jobs / system data export (NotificationAudit)**:
   - Domain/SystemJobs/SystemJob.cs; Contracts/SystemJobs/SystemJobContracts.cs; Application/SystemJobs/*.
   - Infrastructure/SystemJobs/* excluding BackupEncryption.cs; Api/Endpoints/SystemJobEndpoints.cs.
   - Migration `20261104090000_AddSystemJobs.cs`, model snapshot, Infrastructure DI/project dependencies and Api/Program.cs.
   - Domain/Files/FileObject.cs and Application/Files/FileUpload.cs for generated artifact metadata, ADMIN owner checks and expiring downloads.
3. **Encrypted database and object backup**:
   - New project `Services/NotificationAudit/NotificationAudit.BackupWorker/NotificationAudit.BackupWorker.csproj`, Program.cs, DatabaseBackupOptions.cs, DatabaseBackupWorker.cs, PostgresSnapshotDump.cs, DatabaseBackupArtifactBuilder.cs.
   - Infrastructure/SystemJobs/BackupEncryption.cs. Dockerfile is parent-owned.
4. **Tests, written and build-checked only**:
   - tests/NotificationAudit.UnitTests/SystemJobTests.cs
   - tests/NotificationAudit.UnitTests/SystemJobExecutionTests.cs
   - tests/NotificationAudit.UnitTests/SystemArtifactTests.cs
   - tests/NotificationAudit.UnitTests/SystemExportContractTests.cs
   - tests/NotificationAudit.UnitTests/SystemArtifactAccessTests.cs
   - tests/NotificationAudit.UnitTests/NotificationAudit.UnitTests.csproj (backup worker reference; embeds the seven source allowlists).

Existing Events/notification mapping/reminder changes and new Deadlines files are parent-owned. This work wires the parent's `services.AddNotificationCompletion(configuration)` and `NotificationDeadlineModel.Configure(modelBuilder)` but does not edit those implementation files.

## Registered internal sources

These registration calls are wired in each API startup, without exposing `/internal/**` at Gateway:

```csharp
// using Identity.Api.SystemExport;
app.MapSystemExportEndpoints();
// using ParishCoordination.Api.SystemExport;
app.MapSystemExportEndpoints();
// using TaskExecution.Api.SystemExport;
app.MapSystemExportEndpoints();
// using Resource.Api.SystemExport;
app.MapSystemExportEndpoints();
// using Readiness.Api.SystemExport;
app.MapSystemExportEndpoints();
// using Reporting.Api.SystemExport;
app.MapSystemExportEndpoints();
```

Each API already registers its own DbContext; no extra source DI call is needed. NotificationAudit's own source, public system jobs, observability, parent's notification completion, and infrastructure registrations are already wired. Do not register them twice. The new backup project must be added to the solution by the parent; this task does not edit the solution.

## Public contract

| Method/path | operationId | Role |
|---|---|---|
| POST /api/v1/system-data-exports | createSystemDataExport | ADMIN |
| POST /api/v1/backups | startBackup | ADMIN |
| GET /api/v1/backups/{jobId} | getBackupStatus | ADMIN + job owner |

Creation body: `{ "services": ["Identity", "Resource"] }`; omitted/null services selects all seven; empty, unknown or duplicate names returns 422. Required `Idempotency-Key` is a nonempty UUID. Requests return 202 with job resource and standard correlation envelope. Job states are QUEUED/RUNNING/COMPLETED/FAILED. Keys are retained indefinitely with their job, exceeding the 24-hour minimum. Reordered/case-normalized equivalent service selections replay; changed selections conflict with 409.

No fourth export status API was added. Completion notification references the generated FILE ID; clients use the existing authenticated `GET /api/v1/files/{fileId}/download-url`. Backup status also returns fileId and manifest. ADMIN ownership is rechecked in the application and file access path, including expiration. Generated artifacts cannot be removed through delete-unattached-file. Signed URLs do not outlive artifact retention; requests within the final minute are rejected rather than receiving a longer minimum storage URL.

## Configuration and deployment

Every service source requires `SystemExport:ApiKey` (at least 32 characters), supplied by environment/secrets. Internal `GET /internal/system-export` requires `X-System-Export-Key`. Authentication fails closed when unconfigured. Source keys and URLs are never returned in exported data or manifests. No broad EF entity serialization or `SELECT *` is used in export sources; authentication tables/columns, event payloads, private download URLs and unstructured audit state are excluded. File object keys are metadata, not signed URLs, and are included.

NotificationAudit API:

- `SystemJobs:ExportWorkerEnabled=true` enables durable export processing (default false).
- `SystemJobs:CleanupEnabled=true` enables object and retention reconciliation (default false).
- `SystemJobs:MaximumAttempts=3`, `RetentionDays=7`, `MaximumSourceBytes=2147483648`, `SourceTimeoutMinutes=15`.
- `SystemJobs:Sources:<Service>:BaseUrl` and `SystemJobs:Sources:<Service>:ApiKey` for each remote source. The NotificationAudit source is read locally using its own DbContext.
- Existing `ObjectStorage:{InternalEndpoint,PublicEndpoint,AccessKey,SecretKey,Bucket}`.
- Existing `ConnectionStrings:NotificationAuditDatabase`.

The worker intentionally does not call AddInfrastructure, start API endpoints, consume broker messages, create buckets or apply migrations. Its host configuration is isolated from the API:

- `ConnectionStrings:NotificationAuditDatabase`: job database credentials, with rights described below.
- `DatabaseBackup:Databases:<Service>`: worker-only Npgsql connection string for each allowlisted service database. Do not provide these to NotificationAudit.Api.
- `DatabaseBackup:PgDumpPath=pg_dump`; install a compatible PostgreSQL client in the parent-owned worker image.
- `DatabaseBackup:DumpTimeoutMinutes=30` (bounded 1â€“180).
- `DatabaseBackup:EncryptionKeyId`: non-secret, externally managed key/version identifier, required.
- `DatabaseBackup:EncryptionKeyEnvironmentVariable=GROTTOWORKS_BACKUP_KEY`: environment variable holding a base64 32-byte random AES key. Required; keep old key versions for retained backups. Never put keys into request bodies, committed settings or artifacts.
- `SystemJobs` limits/retention as above, and existing `ObjectStorage` settings. It needs read access to recovery objects and write/read access to artifact keys in the private bucket.

Job worker database role requires SELECT/INSERT/UPDATE on system_job; SELECT/INSERT on file_object; INSERT on notification and activity_log. API cleanup additionally needs UPDATE on file_object. Database backup readers need SELECT on source tables (including sequences needed by pg_dump), permission to export/import their snapshot, and pg_dump-compatible read privileges. No database credentials appear in command-line arguments: libpq receives only explicit worker target settings via child environment. Raw stderr is drained without persistence/logging. Use deployment-specific role SQL owned by the parent.

## Durability and artifact behavior

- Creation uses a PostgreSQL transaction-scoped advisory lock per owner/kind/key plus a unique index. Business job and request audit commit together.
- Claims use `FOR UPDATE SKIP LOCKED`, a fresh fencing token and a five-minute lease. A separate DbContext renews every 30 seconds; lost renewal cancels work. Restart recovers expired RUNNING jobs. Both source failures and repeated worker crashes exhaust bounded attempts. Retry delays grow exponentially, capped at 15 minutes.
- Completion locks and checks the current token and lease, then commits job result, file metadata, notification and completion audit together. Stale workers cannot publish. Uploaded objects are not immediately deleted after ambiguous DB commit errors.
- Successful export ZIP contains one `<Service>.jsonl` for every requested service plus `manifest.json`. The worker validates source identity, schema, terminal completion and row counts. A failed/truncated source fails the whole job, persists safe per-source errors in its manifest, and never presents partial data as success.
- Backup uses custom-format pg_dump for each selected database, importing the corresponding exported repeatable-read snapshot. Capture starts/ends and checksums are per database. This is explicitly **not a simultaneous, cross-service transaction-consistent snapshot**.
- Recovery objects are actually copied into the backup ZIP, not merely listed. Their SHA-256 and byte size must equal committed file metadata; missing metadata, missing objects, size/checksum mismatches, or any failed database fail the entire job. Business references from task evidence, purchase receipts and readiness evidence are collected from each dump's snapshot and checked. The archive also includes live NotificationAudit objects and manifests their bucket/object key. Prior live system artifacts are included too; size limits can intentionally fail a growing archive rather than silently omit them.
- The entire ZIP is encrypted while written. Plaintext pg_dump output and object contents stream directly into ZIP entries and AES-GCM encryption; they are not staged on disk. Final encrypted files use random, private, delete-on-close temporary files. The versioned `GWBAK001` format uses independent 1 MiB AES-256-GCM frames, authenticated header/frame index/length, unique nonce prefix per artifact and mandatory authenticated EOF. It detects wrong keys, tampering, reordering, truncation and appended bytes. KeyId is in the header; the key is not.
- A controlled restore must use BackupEncryptionStream.DecryptAsync with the retained key version, authenticate the **entire** file into protected staging, then restore dumps and manifest objects. Discard all plaintext if decryption fails. Decryption is a library, not an exposed public restore endpoint.
- Upload verifies stored length and SHA-256 before publishing metadata.
- Cleaner only examines known `pending/`, `completed/` and `system-jobs/` prefixes after a 24-hour grace, locks the corresponding metadata/job row, preserves current leases and referenced live objects, tombstones expired metadata before deletion, and retries on later sweeps after storage failures. Cleanup does not guess whether an attachment reservation can be released. Expired artifacts are inaccessible at retention expiry even before a sweep physically removes them.

## Validation limits

Only project compilation is authorized in this task. Unit tests were written but NEVER executed. No exports/backups, migrations, Docker or whole-solution build were run. Parent coordinates final solution validation, deployment settings, Gateway policies, Docker packaging and commits. PostgreSQL advisory-lock/concurrent-worker behavior, real object-store failures, pg_dump/restore compatibility, and a full encrypted restore require the separate authorized integration checklist.

## Exact implementation paths

Parent-owned Docker, Gateway, notification Events/Deadlines and role-script changes are intentionally excluded.

```text
Services/Identity/Identity.Api/SystemExport/SystemExportEndpoints.cs
Services/Identity/Identity.Infrastructure/SystemExport/SystemExportSource.cs
Services/NotificationAudit/NotificationAudit.Api/Endpoints/SystemJobEndpoints.cs
Services/NotificationAudit/NotificationAudit.Api/Program.cs
Services/NotificationAudit/NotificationAudit.Api/SystemExport/SystemExportEndpoints.cs
Services/NotificationAudit/NotificationAudit.Application/Files/FileUpload.cs
Services/NotificationAudit/NotificationAudit.Application/SystemJobs/SystemJobExecution.cs
Services/NotificationAudit/NotificationAudit.Application/SystemJobs/SystemJobHandler.cs
Services/NotificationAudit/NotificationAudit.BackupWorker/DatabaseBackupArtifactBuilder.cs
Services/NotificationAudit/NotificationAudit.BackupWorker/DatabaseBackupOptions.cs
Services/NotificationAudit/NotificationAudit.BackupWorker/DatabaseBackupWorker.cs
Services/NotificationAudit/NotificationAudit.BackupWorker/NotificationAudit.BackupWorker.csproj
Services/NotificationAudit/NotificationAudit.BackupWorker/PostgresSnapshotDump.cs
Services/NotificationAudit/NotificationAudit.BackupWorker/Program.cs
Services/NotificationAudit/NotificationAudit.Contracts/SystemJobs/SystemJobContracts.cs
Services/NotificationAudit/NotificationAudit.Domain/Files/FileObject.cs
Services/NotificationAudit/NotificationAudit.Domain/SystemJobs/SystemJob.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/Data/Migrations/20261104090000_AddSystemJobs.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/Data/Migrations/NotificationAuditDbContextModelSnapshot.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/DependencyInjection.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/NotificationAudit.Infrastructure.csproj
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemExport/SystemExportSource.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/ArtifactStreams.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/BackupEncryption.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemArtifactStorage.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemExportArtifactBuilder.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemJobConfiguration.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemJobExecutionStore.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemJobOptions.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemJobRegistration.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemJobStore.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemJobWorker.cs
Services/NotificationAudit/NotificationAudit.Infrastructure/SystemJobs/SystemObjectCleanupWorker.cs
Services/ParishCoordination/ParishCoordination.Api/SystemExport/SystemExportEndpoints.cs
Services/ParishCoordination/ParishCoordination.Infrastructure/SystemExport/SystemExportSource.cs
Services/Readiness/Readiness.Api/SystemExport/SystemExportEndpoints.cs
Services/Readiness/Readiness.Infrastructure/SystemExport/SystemExportSource.cs
Services/Reporting/Reporting.Api/SystemExport/SystemExportEndpoints.cs
Services/Reporting/Reporting.Infrastructure/SystemExport/SystemExportSource.cs
Services/Resource/Resource.Api/SystemExport/SystemExportEndpoints.cs
Services/Resource/Resource.Infrastructure/SystemExport/SystemExportSource.cs
Services/TaskExecution/TaskExecution.Api/SystemExport/SystemExportEndpoints.cs
Services/TaskExecution/TaskExecution.Infrastructure/SystemExport/SystemExportSource.cs
tests/NotificationAudit.UnitTests/NotificationAudit.UnitTests.csproj
tests/NotificationAudit.UnitTests/SystemArtifactAccessTests.cs
tests/NotificationAudit.UnitTests/SystemArtifactTests.cs
tests/NotificationAudit.UnitTests/SystemExportContractTests.cs
tests/NotificationAudit.UnitTests/SystemJobExecutionTests.cs
tests/NotificationAudit.UnitTests/SystemJobTests.cs
```
