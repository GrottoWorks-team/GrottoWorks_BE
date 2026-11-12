# TaskExecution integration

## Support publication

`GET /api/v1/seasons/{seasonId}/support-summary` is PARISH-only and enforces the Season parish. It returns request counts by status and responses grouped by their recorded `respondingCommunityId` (null remains unspecified/individual, never guessed from today's Identity profile). Member totals count named rows, not `responseQuantity` again: approved includes nominations, accepted excludes unconfirmed nominations, completed is a subset, rejected/cancelled remain historical. Material totals remain separate by source Work Area and material/unit; offered, approved, usable, unusable and pending-sync counts are distinct. Four grouped SQL queries plus scope verification share a repeatable-read snapshot, with no response-graph load or N+1. The Gateway Season path routes specifically to TaskExecution ahead of Parish's catch-all.

`POST /api/v1/support-requests/{requestId}/close` accepts `{ status: "CLOSED" | "CANCELLED", reason?, version? }`, with optional `If-Match`. Only the assigned Leader can close it. CLOSED requires fulfilled staffing and usable-material requirements. Cancellation follows BA's PLANNING/PUBLISHED transition and requires a reason; unresolved approvals, transfers awaiting receipt and existing approved/working participants cannot be discarded by this endpoint. Pending unapproved offers/nominations are cancelled, with history preserved. Closure audit fields and `support.request.closed` commit with the aggregate; no stock reversal or attendance deletion occurs.

Resource `support.transfer.confirmed` is consumed by `task-support-transfer-receipts` when broker consumption is enabled (`SupportTransfers:ConsumeEnabled` or the existing `PlanningEvents:ConsumeEnabled`). Receipt inbox, approval recovery, usable/unusable quantities, request fulfillment and the Task synchronization event commit together. Invalid schema goes to the error queue; transient failures retry. A 15-second background scan also reconciles all approved transfers with Resource's internal lookup, so delayed/missed delivery does not require inventing receipt quantities. Duplicate confirmations are no-ops; contradictory confirmations are rejected. Only usable quantity counts toward fulfillment, and existing staffing requirements remain in force for MIXED requests. No stock ledger is written by TaskExecution. The existing shared consumer registration also registers planning/identity consumers when broker consumption is enabled; publisher rollout/cutover still requires the documented integration checks.

`POST /api/v1/support-response-materials/{itemId}/approve` takes `{ approvedQuantity, version? }`; reject takes `{ reason, version? }`. Both require the assigned Leader and support `If-Match`/ETag. Approval first commits an immutable attempt, then calls Resource's idempotent internal transfer API. A confirmed withdrawal changes the offer to `APPROVED`, never directly to fulfilled. A durable insufficient-stock rejection returns 409 and permits a new explicit approval attempt. Timeout/unknown result returns 503 while retaining the intent; rejection or changing quantity is blocked until reconciliation settles it. The background worker scans pending approvals every 15 seconds, looking up outcomes before resending the same attempt. DTO `pendingSync` exposes in-flight approval or an approved transfer awaiting receipt. Resource must be deployed with the operation-outcome migration/API before using this flow. Receipt synchronization is a separate integration step.

`POST /api/v1/support-response-materials/{itemId}/reconcile` (LEADER or MO, added 13/11/2026) reconciles one transfer **synchronously** and returns `{ supportRequestId, status, version, item }`. `confirm-receipt` is hosted by Resource, so without it the request status only changed when `support.transfer.confirmed` was consumed or the 15-second scan ran (clients kept seeing `PUBLISHED` for up to 15 s). The client calls reconcile right after confirming; 503 `SUPPORT_TRANSFER_SYNC_PENDING` means Resource has not yet returned the authoritative receipt, and the event/scan remains the backstop.

`POST /api/v1/support-requests/{requestId}/responses` accepts `{ respondingCommunityId?, members: [{ positionId, volunteerId }], materials: [{ requestMaterialId, sourceWorkAreaId, offeredQuantity, note? }], note? }`; at least one array must be nonempty. Existing staffing payloads remain valid. Individual Volunteers can name only themselves. A community response verifies the current caller and every named Volunteer against Identity's authoritative community/parish/status and all position skill minima. Identity IDs are fetched in batches of 100 rather than one HTTP call per member. No skill override is accepted.

Material offers require the current MO of every source Work Area, with a different source/target in the same parish/season. Demand membership, quantity precision, duplicate lines and active Resource catalog/available stock are validated before saving either portion of a mixed response. Availability is advisory and does not reserve or move stock. Source scope lag/inconsistent upstream payload returns 503; insufficient unallocated stock returns 409. List/detail responses now return persisted material lines. Approval and receipt reconciliation are separate steps; pending/offered quantities never count as fulfilled.

Pending members do not create assignments or consume Task slots. A request lock and a partial unique index enforce one active position per person across responses. Composite foreign keys ensure each response/position belongs to the same request. `GET .../responses` is restricted to the assigned Leader; Support Request detail also returns its persisted responses.

`POST /api/v1/support-response-members/{memberId}/approve|reject` requires the assigned Leader and supports member version / `If-Match`. Approval revalidates active Volunteer, parish/community and all Task/position skills without override. Request and Task locks protect both capacities. Individual self-offers produce `ACCEPTED` assignments; community nominations produce `PENDING` assignments reserving capacity until the named Volunteer uses the existing assignment accept/decline API. Unconfirmed nominations do not count toward fulfillment. Member DTOs expose `requiresConfirmation`, `participationAccepted` and a separate decline reason. OPEN/NOT_STARTED/IN_PROGRESS/REVISION_REQUIRED Task status is preserved. Rejection requires a reason and creates no assignment. Member/response/request status, assignment, Task version and outbox facts commit together. A MIXED request is not declared fulfilled from staffing approval alone.

Nomination consent follows Lâm's reviewed BA decision. Migration preserves already recorded legacy assignment states without inventing a Volunteer consent timestamp or rewriting attendance/history; the new confirmation rule applies to new community nominations. Pending Support assignments appear in My Tasks/Leader assignment lists and schedule warnings. Acceptance/decline updates membership and fulfillment in the same Task transaction. Cancellation includes unconfirmed nominations.

Assignment responses and application reviews reload the tracked assignment after acquiring the Task lock. A request that waited for another decision must validate the current version/state rather than an EF identity-map copy read before the lock; Volunteer consent and fulfillment still commit together.

Task cancellation also cancels its approved Support Members and recalculates response/staffing fulfillment in the Task's transaction. Assignment/member IDs and original review metadata remain available; completed members and terminal request states are preserved. Aggregate concurrency prevents cancellation and review from silently overwriting each other. Receipt/material fulfillment remains owned by the separate Resource synchronization flow.

Support review outbox facts include the original PENDING state, resulting APPROVED/REJECTED state and rejection reason, so NotificationAudit can preserve an immutable decision trail with its existing allowlist/redaction. NotificationAudit handles these decisions once per event and retains published matching requirements pending candidate fan-out. This does not enable the producer dispatcher or claim broker runtime verification.

`POST /api/v1/support-requests/{requestId}/publish` is restricted to the assigned Leader and validates the current caller through Identity. It accepts optional body version / `If-Match`, returns the standard Support Request envelope and a new ETag. STAFFING requires positions, MATERIAL requires material lines, MIXED requires both. Positions accept nullable `taskId` during planning (including legacy data), but publication requires every position's Task to exist in the same Work Area and not be completed/cancelled. Task rows are checked in a single batch under share locks until publication commits.

Publication locks the Support Request, updates its state/version and records `support.request.published` in the same local transaction. The event includes skill/level, position/Task and material requirements without copying private notes. Delivery uses the existing outbox; candidate notification fan-out and staffing/material response workflows are separate integrations. Migration adds nullable Task references without guessing links for existing positions.

Assignment events carry `assignmentType` and the Work Area's `leaderUserId`, so consumers can distinguish a direct assignment, an application and its decision. Attendance requests carry the task and leader IDs. Both correction approval and rejection emit `attendance.request.reviewed` with `reviewType=CORRECTION`; approved-hour changes remain a separate event for reporting. These additive fields are captured with the business transaction, not resolved from mutable ownership later. Older payloads without workflow context require reconciliation rather than guessing the recipient.

Task events are recorded in `task_event_outbox` in the same database transaction as the business operation.

## RabbitMQ delivery

`Outbox:DispatchEnabled` defaults to `false`. Keep it disabled until downstream consumers accept full task snapshots and can create missing projections; existing consumers may otherwise ignore events for tasks they have never seen.

When enabled, configure `MessageBroker:Host`, `MessageBroker:UserName`, and `MessageBroker:Password` using environment variables or secrets. The worker polls every two seconds, publishes with the original event ID as the message ID, and marks a row published only after the publish call succeeds. Delivery is at least once: consumers must deduplicate by event ID.

A PostgreSQL transaction advisory lock coordinates dispatchers. A pending earlier event blocks only later events for the same task. Failed deliveries retry after 2, 5, 15, 60 and then 300 seconds. Published rows are retained for seven days; pending rows are never removed by cleanup. Logs contain event IDs and error types rather than payloads or credentials.

Planning scopes arrive permanently through the RabbitMQ planning-scope consumer (`PlanningEvents:ConsumeEnabled`). The legacy `PUT /internal/season-scopes/{seasonId}` and `PUT /internal/work-area-scopes/{workAreaId}` routes and request DTOs are removed. Scope application handlers, inbox/version checks, and transaction advisory locks remain; `GET /internal/work-area-task-counts` remains available for lifecycle verification.

## Required skill levels

Task and Support requirements use `BASIC < INTERMEDIATE < ADVANCED < EXPERT`, normalized with trim/uppercase. An unspecified volunteer level does not satisfy a non-null minimum. Migration `NormalizeRequiredSkillLevels` aborts with the unmapped values when legacy data uses another level; map those values explicitly before applying the migration. Blank legacy values become null. No values are guessed or downgraded.

Assignment/application validation reads declared levels from Identity's internal user view and checks each minimum. Identity must return matching `skillIds` and `skills` collections using the canonical catalog; missing/invalid upstream data returns `503` rather than bypassing validation. The existing explicit Leader override still records its reason and actor.

## Volunteer matching

`GET /api/v1/tasks/{taskId}/volunteer-matches` is restricted to the assigned Leader. Optional `communityId` filters the directory; `limit` defaults to 20, maximum 100. Results include active volunteers of the same parish meeting every required skill/minimum, excluding existing pending/accepted/working/completed assignments or applications for that Task. Schedule conflicts and declared availability remain advisory warnings.

Identity pages are fetched in batches of 100 (at most 10,000 source candidates; larger directories return `503`, never a silently truncated shortlist). Local schedule conflicts use one database query for the candidate batch. Known availability ranks first, then absence of known conflicts, same community, name and ID. Missing availability or Task start time is reported as undetermined.

Directory pages are cached for 60 seconds by default (`Matching:CacheSeconds`, 1–300) under parish/community/page keys. Configure `ConnectionStrings:Redis` (`redis:6379` in Docker); without it the service uses a bounded local memory cache. Cache operations have a 200 ms budget; failure bypasses caching for 30 seconds and falls back to Identity HTTP. Cached payloads are validated again against their tenant/filter. Assignment mutations always revalidate Identity directly and do not authorize from the matching cache.

## Task material requirements

`PUT /api/v1/tasks/{taskId}/materials` replaces all planning requirements for an `OPEN` Task managed by its Leader. Supply an array of `materialId`, `requiredQuantity`, `note`; an empty array clears the requirements. Material IDs are batch-validated against Resource's internal catalog. Missing/inactive references are rejected, decimal quantities follow `(12,2)`, and `If-Match`/ETag protect concurrent updates.

Task rows and `task.materials.changed` are saved in the same transaction. No-op replacement preserves version, row identity and event count. Broker delivery, Resource's material projection and backfill remain part of the subsequent integration commit; this API does not directly modify inventory, allocation or work-area requirements.

## Full task snapshots

Every saved Task change also stages a `TaskScopeSnapshot` in the same unit of work. This technical message includes parent Season/Work Area scopes and effective assignees (accepted, in progress, completed), allowing receivers to bootstrap a missing Task projection. Pending applications and unconfirmed direct assignments are excluded. Snapshot IDs are stable per Task/version; business event envelopes and IDs remain independent.

The dispatcher publishes snapshots using their own strongly typed message contract. When `Outbox:DispatchEnabled=true`, a reconciliation worker scans existing Tasks at startup and every 30 minutes (`Outbox:ReconcileIntervalMinutes`, 1–1440). Keyset batches of 100 use repeatable-read transactions, so Task/assignment/material/parent state is coherent. Missing parent scopes are reported and retried on later scans. Configure the broker explicitly for this permanent delivery path.

Existing pending rows keep their retry backoff. Published technical snapshots are requeued without changing their event ID or stored envelope; business events are never replayed by reconciliation. Receivers must keep inbox/version checks. The worker shares the dispatch advisory lock and retries failed scans without affecting business HTTP requests.

Snapshots now include a complete `materials` array, including an empty array after clearing requirements. Legacy messages without that field carry `null`, meaning no material information rather than clear-all. Snapshot ID generation uses a new identity revision so reconciliation can send the complete representation without mutating older outbox envelopes. The save hook merges database rows and tracked material additions/updates/deletions even when the mutation did not load the material navigation.


## Progress, review, special tasks, and durable evidence

Task progress is append-only: `GET/POST /api/v1/tasks/{taskId}/progress`. POST takes
`percentComplete` (integer 0–100), `note`, `evidence: [{fileId, caption}]`, and optional
`version`/`If-Match`. Up to ten distinct completed PROGRESS_EVIDENCE files owned by
the submitting volunteer may be attached. Only an active volunteer with an accepted
or in-progress assignment may submit. Progress at 100 does not automatically complete a Task.

`POST /submit` and `/resubmit` take `note`, `evidence`, and `version`; these create a
100-percent submission. First submit requires IN_PROGRESS; resubmit requires
REVISION_REQUIRED. Both emit `task.progress.submitted` with the responsible leader.
`GET /review-package` aggregates criteria, all progress/evidence, reviews, approved
attendance hours, and Resource material summary. Resource dependency failure is 503.
Leader `POST /approve` and `/request-revision` take `reason` and `version`; revision
requires a reason. Approval updates the selected submission approval, Task,
assignments, Support members, and outbox atomically. `task.completion.reviewed`
owns review notifications; the companion status event has
`notificationSuperseded=true` to prevent duplicate notifications.

Evidence download is `GET /api/v1/tasks/{taskId}/progress/{progressId}/evidence/{fileId}/download-url`.
It checks Task scope and exact evidence membership, finalizes the durable attachment
idempotently, then obtains a record-authorized URL from NotificationAudit.

## Start conditions (12/11/2026)

A Task can only be **started** (volunteer `POST /task-assignments/{id}/attendance/check-in-requests`
and the Leader's approval of that request) when all three hold:

| Condition | Error code (409) | Source |
|---|---|---|
| Start time reached (`startAt`) | `TASK_START_NOT_DUE` | Task aggregate |
| Full team accepted (`requiredVolunteerCount` ≤ accepted/in-progress/completed assignments) | `TASK_NOT_ENOUGH_MEMBERS` | `task_assignment` |
| Planned materials fully allocated (`task_material_plan` vs `material_allocation`) | `TASK_NOT_ENOUGH_MATERIALS` | Resource `GET /internal/tasks/{id}/material-summary` |
| Task not published yet (`OPEN`) | `TASK_NOT_PUBLISHED` | Task aggregate |
| Task already submitted/closed | `TASK_INVALID_STATE` | Task aggregate |

`GET /api/v1/tasks/{taskId}/start-eligibility` returns the same evaluation for clients
(`canStart`, `blockers[{code, detail}]`, `acceptedMembers`/`requiredMembers`,
`materialShortages[]`), so web and mobile can explain or disable the start action without
guessing. Tasks without material requirements skip the Resource call. A Resource outage is
**fail-open** (`materialsKnown = false`, no blocker) so a stock-service incident cannot freeze
the build; every other condition stays enforced.

## Assignment scope (13/11/2026)

A work area owns a **community**; the leader may only staff its tasks with volunteers of that
community:

| Action | Rule | Error code (409) |
|---|---|---|
| `POST /tasks/{taskId}/assignments` (direct assignment) | volunteer's `communityId` must equal the area's | `TASK_ASSIGNEE_OUTSIDE_AREA` |
| `POST /task-applications/{id}/approve` | same check when the application is approved | `TASK_ASSIGNEE_OUTSIDE_AREA` |
| `POST /tasks/{taskId}/applications` (volunteer self-registration) | a volunteer from another community may apply **only** when the area is asking for support | `TASK_APPLICATION_OUTSIDE_AREA` |
| `GET /tasks/{taskId}/volunteer-matches` | always scoped to the area's community (a caller-supplied `communityId` is ignored once the area has one) | — |

When the area is short of people the leader creates a support request (`POST /support-requests`,
positions linked to the task) and publishes it; volunteers approved for support positions become
assignments through the support flow (`ISupportAssignmentLifecycle`), which is not affected by
this check. `SupportRequestRepository.IsTaskRequestingSupportAsync` answers "is this task covered
by a PUBLISHED/PARTIALLY_FULFILLED request?" for both the application rule and the clients
(web/mobile only list other areas' tasks that are asking for help). Areas without a community
(parish-level areas) keep the previous behaviour: any volunteer of the parish.

The source saves attachment intents before remote reservation, reserves under the
Task mutation lock before saving progress, and retains these intents across rollback
or restart. A worker checks pending intents every 15 seconds, with a five-minute
initial recovery delay. It acquires the same Task lock before deciding whether to
finalize committed progress or release abandoned reservations. Failure is retried
with bounded backoff. Operations already released by recovery cannot later commit.
Configure `NotificationAuditClient:BaseAddress` (default `http://localhost:6107/`)
and the shared `InternalApi:ApiKey` (minimum 32 characters).

`POST /api/v1/purchase-requests/{requestId}/purchase-tasks` takes the flat planning
fields `name`, `description`, `completionCriteria`, `priority`, `estimatedMinutes`,
`startAt`, `deadline`. Work Area comes from the authoritative Resource snapshot;
Task has exactly one buyer slot. Creation requires APPROVED from Resource. Buyer
acceptance emits `task.purchase-task.assigned`. Resource
`purchase.delivery.confirmed` completes the purchase Task even when PR is still
PARTIALLY_RECEIVED. An inbox prevents duplicates; missing Task/assignment facts
fail for retry rather than acknowledge and discard. A keyset recovery scan every
30 seconds checks Resource confirmed purchases and completes missed deliveries.
`PurchaseEvents:ConsumeEnabled` enables broker ingestion; HTTP reconciliation is
registered independently. Manual review is unavailable for PURCHASE Tasks.

`POST /internal/tasks` is API-key protected, excluded from OpenAPI and requires
`Idempotency-Key`. Body is `{workAreaId, readinessItemId, requestedByUserId, name,
description, completionCriteria, priority, estimatedMinutes, requiredVolunteers,
startAt, deadline}`. The requesting user must be the active assigned Leader.
It creates OPEN CORRECTIVE Tasks with normal schedule validation. Both readiness
item and idempotency key are locked; identical retries return the saved response,
while changed input conflicts. Readiness callers must use this exact contract.

Progress and special-task migrations:
- `20261103220000_AddTaskProgressReview`
- `20261103220001_AddTaskFileAttachmentIntents`
- `20261103220002_AddSpecialTaskSynchronization`

Deployment verification on 2026-11-04 ran the solution tests and applied migrations
to restored database copies before the original Docker databases. Isolated HTTP
checks covered progress/review/evidence, Readiness corrective linkage, purchase
completion, material transfer reconciliation and explicit community nomination
consent. Broker checks covered duplicate/stale/equal-version snapshots and recovery
after Task/RabbitMQ outages. See the root `INTEGRATION.md` and
`tools/Verification/README.md` for configuration and repeatable verification.
