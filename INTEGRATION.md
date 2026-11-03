# Integration and deployment checklist

The remaining Desktop PLAN features are implemented in code. Unit tests accompany the changes; they were not executed. Build success is not runtime acceptance. No container rebuild, migration, export or backup was executed during implementation.

## Service flows

- TaskExecution: progress/evidence, submission/resubmission, review package, approval/revision, approved purchase tasks and automatic receipt completion, internal idempotent corrective creation. Evidence reservation precedes the business write; durable recovery finalizes committed references and releases abandoned ones. Record-authorized downloads are available to permitted participants/reviewers.
- Resource/Readiness: purchase receipt and checklist evidence reservations, recovery and authorized downloads. Resource exposes authoritative PR/delivery/material summaries. Readiness persists corrective creation requests before HTTP and reconciles the link after restart. Neither service silently accepts unverified files when configuration is absent.
- ParishCoordination: Task/Readiness inboxes, retained missing-parent events, reconciliation of task counts before readiness review, Work Area lifecycle, automatic Season ACTIVE/READY aggregation and atomic completion of READY areas.
- NotificationAudit: planning/review/Support notifications, retained-context recovery, skill-matched Support audiences, deadline snapshots and once-per-recipient/deadline-generation reminders. System exports use each owning service's authenticated JSONL source. Backups run in a separate worker and contain encrypted custom-format database dumps plus verified recovery objects.
- BuildingBlocks: shared OpenTelemetry registration; HTTP and MassTransit tracing; publish confirmation, correlation propagation and publish/consume outcome metrics. Each of the three services exposes its own outbox/inbox/retry/job backlog through a local database probe. Missing database access reports probe unavailability, not a fabricated empty queue.
- Gateway: explicit shared-prefix routes for Task purchase/review/evidence and system jobs; JWT policies, upload/job rate limits, ProblemDetails for throttling, exposed ETag/Location/Retry-After and no internal proxy route.

## Local Docker configuration

Keep the existing `.env` secrets and add the names in `.env.example`. `SYSTEM_EXPORT_API_KEY` must be a separate random value of at least 32 characters. Ordinary internal calls continue to use `INTERNAL_API_KEY`. Never commit `.env` or real keys.

The normal `docker compose up -d --build` deploys APIs and the export worker. It does not execute an export or backup automatically. A system export is requested by ADMIN with `POST /api/v1/system-data-exports`, an `Idempotency-Key` UUID and optional `{ "services": ["Identity", "TaskExecution"] }`. Omit services for all seven. The completion notification contains a file reference; use the existing owner-protected download API. Any failed source makes the job fail with a manifest, never a successful partial export.

The backup worker is intentionally isolated behind the `backup` profile. Set distinct `BACKUP_DB_PASSWORD`, `BACKUP_JOB_DB_PASSWORD`, a base64 32-byte `GROTTOWORKS_BACKUP_KEY`, and a persistent `BACKUP_KEY_ID`. Retain the encryption key securely for recovery. The business API receives no source database passwords or encryption key.

After service migrations, provision the two worker roles using `docker/postgres/backup-role.sql` as the database administrator. The script reads passwords from environment, not SQL source. For example, with the secrets already loaded into the current PowerShell environment:

```powershell
Get-Content docker/postgres/backup-role.sql | docker compose exec -e BACKUP_DB_PASSWORD -e BACKUP_JOB_DB_PASSWORD -T postgres psql -U postgres
docker compose --profile backup up -d --build notificationaudit.backup
```

The source role has read-only access to the seven named databases. The job role can update job metadata and append artifact/notification/audit metadata in notify_db. Reapply default grants using the actual migration-owner role in environments where each service uses a distinct database owner. The worker has no public port. Its temporary files are private; the Docker profile uses a bounded tmpfs. Increase that capacity deliberately for large backups. Artifacts default to seven-day retention. Each database has its own consistent snapshot; the manifest explicitly does not claim a simultaneous distributed snapshot.

Request a backup with ADMIN `POST /api/v1/backups`, then `GET /api/v1/backups/{jobId}`. Download the encrypted artifact through its file reference. A missing key, unavailable source, missing referenced object or checksum mismatch fails the job. Confirm restoration in an isolated environment before relying on backups operationally; this implementation session did not execute backup or restore.

## Broker cutover and recovery

Publishers/consumers have individual switches. Docker enables event ingestion and the new durable publishers, but `BROKER_SCOPE_PRIMARY=false` keeps the existing Parish→Task HTTP delivery until acceptance. Broker scope backfill must be enabled for Resource/Readiness/Reporting/Notification snapshots before treating broker projections as authoritative.

1. Deploy all new consumers and apply migrations through the usual deployment process.
2. Set `BROKER_SCOPE_PRIMARY=true`, `HTTP_SCOPE_ROLLBACK=false`; redeploy Parish. The scope worker selects one transport, not concurrent HTTP and broker delivery.
3. Verify existing/new Seasons, Work Areas, users and Tasks bootstrap all projections; verify duplicate/out-of-order replay, missing parents, broker outage/restart and backlog recovery.
4. Verify Task progress review, corrective linkage, buyer acceptance and delivery completion; verify archived scopes reject writes and reopened readiness returns Season to ACTIVE.
5. If required, set `HTTP_SCOPE_ROLLBACK=true` while retaining `TaskScopeSync:Enabled`; this disables broker scope dispatch and restores durable HTTP delivery. Business event outboxes remain independent.
6. Remove the HTTP adapter only after runtime acceptance. It is retained in this change as the PLAN's rollback path, not a second primary transport.

Notification deadline data is derived from authoritative snapshots. Legacy snapshots without deadline do not invent one. Reconciliation emits the enhanced snapshot identity. Support audience resolution reads the complete season audience from Parish's authenticated internal API (`ParishClient:BaseAddress`), then uses Identity's directory for skill matching. It never treats a partial local planning projection as a complete audience. Source envelopes remain immutable after recipient resolution; upstream outages retain context for replay. Notifications respect in-app rules/preferences; push/email providers remain outside this PLAN's implementation scope.

## Observability and final acceptance

Set `OBSERVABILITY_ENABLED=true` and start `docker compose --profile observability up -d otel-collector`. OTLP is internal at `http://otel-collector:4317`. The bundled collector uses its debug exporter; configure the production backend/exporter independently. HTTP/RabbitMQ traces and metrics exclude API keys, tokens and payload bodies. Queue metrics use bounded service/queue labels; message IDs are not metric labels.

Before accepting deployment, run the tests yourself, inspect migrations against a database copy, check all public operation/role metadata and Gateway routing, reject `/internal/**` through Gateway, and exercise timeout/crash recovery for evidence, outbox/inbox, purchase and Support. Exercise job replay with same/different payload, worker lease loss/restart, failed sources, expired artifact access and an isolated encrypted restore. No runtime result is claimed here.
