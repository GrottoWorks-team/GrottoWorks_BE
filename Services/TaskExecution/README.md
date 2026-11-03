# TaskExecution integration

Task events are recorded in `task_event_outbox` in the same database transaction as the business operation.

## RabbitMQ delivery

`Outbox:DispatchEnabled` defaults to `false`. Keep it disabled until downstream consumers accept full task snapshots and can create missing projections; existing consumers may otherwise ignore events for tasks they have never seen.

When enabled, configure `MessageBroker:Host`, `MessageBroker:UserName`, and `MessageBroker:Password` using environment variables or secrets. The worker polls every two seconds, publishes with the original event ID as the message ID, and marks a row published only after the publish call succeeds. Delivery is at least once: consumers must deduplicate by event ID.

A PostgreSQL transaction advisory lock coordinates dispatchers. A pending earlier event blocks only later events for the same task. Failed deliveries retry after 2, 5, 15, 60 and then 300 seconds. Published rows are retained for seven days; pending rows are never removed by cleanup. Logs contain event IDs and error types rather than payloads or credentials.

The existing HTTP scope synchronization continues until the separately planned broker cutover.
