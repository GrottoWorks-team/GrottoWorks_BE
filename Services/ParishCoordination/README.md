# ParishCoordination integration

`planning.work-area.assigned` preserves the Work Area snapshot and adds `communityChanged`, `leaderChanged`, and `materialOfficerChanged`. Flags compare original and current assignments inside the business transaction. A Community change does not imply either role holder was reassigned; consumers must not infer changes from current role IDs. Technical scope snapshots remain unchanged.

Planning scope delivery uses RabbitMQ permanently. The legacy `TaskScopeSync` HTTP module and rollback switches are removed.

`PlanningEvents:DispatchEnabled` defaults to false. When enabled, business events and technical planning snapshots use the transactional planning outbox. A reconciliation worker scans existing Seasons, then Work Areas, at startup and every 30 minutes (`PlanningEvents:ReconcileIntervalMinutes`, 1–1440). Keyset batches of 100 use repeatable-read transactions and the same advisory lock as the dispatcher.

Published technical snapshots are requeued at the same source version with the original event ID/envelope; pending rows retain retry backoff. Reconciliation does not replay business events or increment domain versions. Each child snapshot embeds its parent Season. Consumers must retain their inbox and monotonic-version checks. Configure the broker and enable `PlanningEvents:DispatchEnabled` for delivery; there is no HTTP scope fallback.

The forward `RemoveTaskScopeHttpOutbox` migration drops the retired HTTP outbox only after this database has published planning scope snapshots for every current Season and Work Area version. If the guard fails, run reconciliation and drain the planning outbox using the preceding broker-enabled release, then retry. Historical migrations retain their frozen outbox schema. Rolling the migration back restores only the empty retired table, not its delivery history.
