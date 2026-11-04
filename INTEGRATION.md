# Integration and deployment checklist

The PLAN features are implemented in code. The 2026-11-04 deployment verification passed all 1,337 solution tests, migrated restored copies before the original Docker databases, and exercised the service flows below on isolated fixtures. All seven original service databases have no pending migrations, and all seven API readiness endpoints returned 200. Keep the machine-local acceptance reports and backups outside Git; see [verification tools](tools/Verification/README.md) for repeatable checks.

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

Request a backup with ADMIN `POST /api/v1/backups`, then `GET /api/v1/backups/{jobId}`. Download the encrypted artifact through its file reference. A missing key, unavailable source, missing referenced object or checksum mismatch fails the job. Verification completed an all-seven-service export and encrypted backup, authenticated decryption, restoration into seven separate databases, and checksum verification of 11 restored objects in a new private bucket. Repeat restore checks for later backups; one successful local restore does not replace an operational recovery schedule.

## Broker cutover and recovery

RabbitMQ is the planning-scope transport. Parish writes snapshots and business events into its transactional planning outbox; consumers retain inbox IDs and independently enforce parent/child source versions. Docker enables the publishers and consumers with the configured broker credentials. Reconciliation bootstraps existing data without incrementing business versions or mutating published envelopes. Delivery remains eventually consistent.

Acceptance verified global Season/Work Area parity against Task, Resource, Readiness and Reporting; duplicate, stale and equal-version snapshots; and durable queue/outbox recovery after stopping Task and RabbitMQ. Consumer-local timestamps remain unchanged on ignored snapshots. JSONB-normalized lifecycle envelopes are compared by JSON value, while genuinely different envelopes with reused IDs remain rejected. Legacy task-review events can recover missing context from Reporting's own projection; notification recipients are never treated as volunteers.

The old Parish→Task HTTP dispatcher, backfill, transport switches and Task's two internal PUT scope endpoints have been retired. A forward migration drops `task_scope_outbox` only after checking that current source snapshots were published. Historical migrations remain intact. Before applying that cleanup to another environment, back up Parish, deploy and enable consumers, verify full source/projection convergence, and stop old Parish writers. Existing internal HTTP operations for authoritative lookup, files, corrective tasks and support transfers remain in use; they are not exposed through Gateway. Separate temporary feeds in other services are not removed by this cutover.

Notification deadline data is derived from authoritative snapshots. Legacy snapshots without deadline do not invent one. Reconciliation emits the enhanced snapshot identity. Support audience resolution reads the complete season audience from Parish's authenticated internal API (`ParishClient:BaseAddress`), then uses Identity's directory for skill matching. It never treats a partial local planning projection as a complete audience. Source envelopes remain immutable after recipient resolution; upstream outages retain context for replay. Notifications respect in-app rules/preferences; push/email providers remain outside this PLAN's implementation scope.

## Observability and final acceptance

Set `OBSERVABILITY_ENABLED=true` and start `docker compose --profile observability up -d otel-collector`. OTLP is internal at `http://otel-collector:4317`. The bundled collector uses its debug exporter; configure the production backend/exporter independently. HTTP/RabbitMQ traces and metrics exclude API keys, tokens and payload bodies. Queue metrics use bounded service/queue labels; message IDs are not metric labels.

The isolated Docker acceptance exercised Gateway/internal authentication and ownership, file completion/attachment/download, Task attendance/progress/review, corrective creation/recheck, purchase approval/buyer/receipt completion, material Support withdrawal/receipt reconciliation, and community nomination acceptance/decline. Ledger and summary checks ensure transfers are counted once and Volunteers are not silently accepted. Export replay/conflict, backup decryption/restore and broker outage recovery also passed. The PostgreSQL rollback check deliberately caused a foreign-key failure and verified neither business rows nor planning outbox rows survived.

The shipped tools keep explicit mutation/restart gates and write reports outside Git. Run fault scenarios against separate databases/vhost/bucket. Runtime verification is deliberately bounded: it does not assert production load capacity, email/FCM delivery, collector/backend operation, or every possible crash point. Preserve pending/error events and diagnose them; do not purge queues to obtain a green report. Keep the original database volumes and pre-migration backups when cleaning session-owned verification resources.
