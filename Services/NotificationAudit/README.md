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
| `000000000011` | `purchase.request.decided` |
| `000000000012` | `readiness.area.ready` |
| `000000000013` | `purchase.request.submitted` |
| `000000000014` | `purchase.delivery.confirmed` |
| `000000000015` | `material-report.submitted` |
| `000000000016` | `material-report.verified` |
| `000000000017` | `contribution.pledged` |
| `000000000018` | `contribution.receipt.confirmed` |
| `000000000019` | `contribution.donation.adjusted` |
| `000000000020` | `loan.offer.submitted` |
| `000000000021` | `support.transfer.confirmed` |
| `000000000022` | `recognition.entry.created` |
| `000000000023` | `readiness.item.failed` |
| `000000000024` | `readiness.area.reopened` |
| `000000000025` | `planning.work-area.assigned` |

Push and email preferences are persisted but providers are not connected yet. File access across business records is also deferred to each owning service integration.

Migration corrects the two legacy event aliases without changing rule IDs or the admin's enabled/disabled setting. A conflicting independently created canonical rule stops migration with a reconciliation message rather than deleting it. Seeder runs under a database advisory lock to avoid duplicates during concurrent startup.

`PUT /users/me/notification-preferences` uses operation ID `replaceNotificationPreferences`. Resource history accepts `from` inclusive and `to` exclusive, normalizes offsets to UTC and rejects empty/reversed ranges. Read operations use conditional database updates, preserving the first read timestamp; read-all captures its row IDs before updating so concurrently inserted/backdated notifications remain unread. Audit preserves allowlisted state changes and a bounded business reason with credential/private-link patterns redacted.

## Integration status

- The service has a transactional inbox and RabbitMQ consumers for TaskExecution, Identity, ParishCoordination, Resource, Readiness and Reporting business envelopes. They share schema validation, safe-context retention and audit ingestion.
- Direct assignments notify the Volunteer; applications notify the Leader; application decisions notify the Volunteer; direct declines notify the Leader. Workflow type is mandatory for assignment events, so ambiguous legacy messages remain pending rather than notifying the wrong person.
- Check-in requests notify the Leader; check-in and correction decisions notify the Volunteer. Volunteer cancellation and direct acceptance remain audit-only facts. Long-open attendance events notify the Volunteer and Leader once each.
- Events without enough recipient context are retained as `REQUIRES_CONTEXT` and never reported as delivered notifications.
- TaskExecution now has an outbox dispatcher, disabled by default until consumer/backfill integration is ready. Wiring other source publishers remains part of the later integration commits.
- Audit ingestion stores only an allowlist of non-secret payload fields. Tokens, passwords, private URLs, and arbitrary payload content are not copied.

Upstream queues are `notification-audit-{identity,planning,resource,readiness,reporting}-events`; Task uses `notification-audit-task-events`. `NotificationEvents:Enabled` controls their registration. Known notification events awaiting recipient/template integration stay `REQUIRES_CONTEXT`; ordinary audit-only events complete without pretending a notification was delivered. Runtime broker wiring, templates/context resolution and catalog alignment still require the next integration steps.

Inbox writes take a PostgreSQL transaction advisory lock per event ID. Only already processed copies of that event are ignored; unrelated unique/DB failures propagate for retry. Unresolved events retain a typed, redacted `safe_envelope` for later context resolution. Reprocessing the same context can finish a pending inbox without duplicating its audit row. Reusing an ID with conflicting context is rejected. Legacy inbox rows have no retained context until the original event is replayed; a context-resolution worker is still a subsequent integration step.

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
