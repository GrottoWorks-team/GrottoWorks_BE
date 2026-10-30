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
- TaskExecution currently stores integration events in its own outbox but has no broker dispatcher yet. Publishers from TaskExecution and other source services must be connected in later integration commits.
- Audit ingestion stores only an allowlist of non-secret payload fields. Tokens, passwords, private URLs, and arbitrary payload content are not copied.
