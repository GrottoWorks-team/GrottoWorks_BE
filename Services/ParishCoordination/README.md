# ParishCoordination integration

The existing `TaskScopeSync` HTTP delivery remains enabled until the planned RabbitMQ cutover.

`PlanningEvents:DispatchEnabled` defaults to false. When enabled, business events and technical planning snapshots use the transactional planning outbox. A reconciliation worker scans existing Seasons, then Work Areas, at startup and every 30 minutes (`PlanningEvents:ReconcileIntervalMinutes`, 1–1440). Keyset batches of 100 use repeatable-read transactions and the same advisory lock as the dispatcher.

Published technical snapshots are requeued at the same source version with the original event ID/envelope; pending rows retain retry backoff. Reconciliation does not replay business events or increment domain versions. Each child snapshot embeds its parent Season. Consumers must retain their inbox and monotonic-version checks. This worker does not disable the HTTP dispatcher or enable RabbitMQ automatically.
