# TaskExecution integration

## Support publication

`POST /api/v1/support-requests/{requestId}/responses` currently accepts staffing offers: `{ respondingCommunityId?, members: [{ positionId, volunteerId }], note? }`. Individual Volunteers can name only themselves. A community response verifies the current caller and every named Volunteer against Identity's authoritative community/parish/status and all position skill minima. Identity IDs are fetched in batches of 100 rather than one HTTP call per member. No skill override is accepted. Material offers are added in the separately planned Resource transfer integration.

Pending members do not create assignments or consume Task slots. A request lock and a partial unique index enforce one active position per person across responses. Composite foreign keys ensure each response/position belongs to the same request. `GET .../responses` is restricted to the assigned Leader; Support Request detail also returns its persisted responses.

`POST /api/v1/support-response-members/{memberId}/approve|reject` requires the assigned Leader and supports member version / `If-Match`. Approval revalidates active Volunteer, parish/community and all Task/position skills without override. Request and Task locks protect both capacities. The resulting `SUPPORT` assignment is already `ACCEPTED`; OPEN/NOT_STARTED/IN_PROGRESS/REVISION_REQUIRED Task status is preserved. Rejection requires a reason and creates no assignment. Member/response/request status, assignment, Task version and outbox facts commit together. Fulfillment counts accepted named members once; a MIXED request is not declared fulfilled from staffing approval alone. `support.member.reviewed` is an additive audit/decision event; integration and Notification recipients are completed separately before enabling backlog delivery.

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

The existing HTTP scope synchronization continues until the separately planned broker cutover.

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

The dispatcher publishes snapshots using their own strongly typed message contract. When `Outbox:DispatchEnabled=true`, a reconciliation worker scans existing Tasks at startup and every 30 minutes (`Outbox:ReconcileIntervalMinutes`, 1–1440). Keyset batches of 100 use repeatable-read transactions, so Task/assignment/material/parent state is coherent. Missing parent scopes are reported and retried on later scans. Broker cutover remains disabled by default.

Existing pending rows keep their retry backoff. Published technical snapshots are requeued without changing their event ID or stored envelope; business events are never replayed by reconciliation. Receivers must keep inbox/version checks. The worker shares the dispatch advisory lock and retries failed scans without affecting business HTTP requests.

Snapshots now include a complete `materials` array, including an empty array after clearing requirements. Legacy messages without that field carry `null`, meaning no material information rather than clear-all. Snapshot ID generation uses a new identity revision so reconciliation can send the complete representation without mutating older outbox envelopes. The save hook merges database rows and tracked material additions/updates/deletions even when the mutation did not load the material navigation.
