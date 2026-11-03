# NotificationAudit

NotificationAudit owns in-app notifications, notification preferences and rules, activity logs, and private file metadata.

## Stable notification rules

Rule IDs are stable so the administration UI can update a rule without discovering it from another service. IDs use the prefix `71000000-0000-0000-0000-` and the suffixes below:

| Suffix | Event type |
|---|---|
| `000000000001` | `task.assignment.created` |
| `000000000002` | `task.assignment.responded` |
| `000000000003` | `task.status.changed` |
| `000000000004` | `attendance.request.submitted` |
| `000000000005` | `attendance.request.reviewed` |
| `000000000006` | `attendance.hours.approved` |
| `000000000007` | `attendance.session.open-too-long` |
| `000000000008` | `planning.season.activated` |
| `000000000009` | `planning.season.completed` |
| `000000000010` | `planning.season.archived` |
| `000000000011` | `resource.purchase-request.decided` |
| `000000000012` | `readiness.inspection.completed` |

Push and email preferences are persisted but providers are not connected yet. File access across business records is also deferred to each owning service integration.

## Integration status

- The service has a transactional inbox and a RabbitMQ consumer for `TaskIntegrationEvent`.
- Direct assignment, attendance review, and long-open attendance events have complete recipient context and can create in-app notifications.
- Events without enough recipient context are retained as `REQUIRES_CONTEXT` and never reported as delivered notifications.
- TaskExecution now has an outbox dispatcher, disabled by default until consumer/backfill integration is ready. Wiring other source publishers remains part of the later integration commits.
- Audit ingestion stores only an allowlist of non-secret payload fields. Tokens, passwords, private URLs, and arbitrary payload content are not copied.

## File finalization and deletion

Completion copies the pending upload to a fresh private key, then inspects that exact copy before committing metadata. The pending object is retained until the database commit succeeds, so a failed save can be retried. Repeated completion of an uploaded file returns its existing reference. Inspection reads at most 20 MiB, checks the actual byte count and honors cancellation rather than trusting the object-size header.

Deletion commits a metadata tombstone before removing the object. Storage failures return `503`; retrying deletion retries removal even when the tombstone is already present. A failed database commit leaves the live object untouched.

Private copies abandoned by failed metadata writes/validation and pending objects left by failed cleanup still need the storage-retention cleanup worker in the integration follow-up. They are never returned as completed file references.

## Internal attachment protocol

API-key-protected endpoints, excluded from public OpenAPI and Gateway:

- `POST /internal/files/{fileId}/attachment-reservations`
- `POST /internal/files/{fileId}/attachment-reservations/finalize`
- `POST /internal/files/{fileId}/attachment-reservations/release`
- `POST /internal/files/{fileId}/record-download-url`

Reserve with a stable operation ID, expected uploader/purpose, owning service and business record. Reservation blocks public deletion. The owning service must save the business record plus its finalize intent atomically, then retry finalization using the same operation ID. Release only after establishing that the business operation failed; finalized attachments cannot be released through this protocol. Reservations do not expire automatically because a delayed finalize must not expose committed evidence to deletion.

The record-download endpoint accepts only an attached file matching the exact service/reference. Its internal caller must first authorize the user against that business record and verify membership of the file. Public download remains uploader-only. Source-side intent workers and record-authorized public download flows are implemented separately; these endpoints alone do not complete evidence integration.
