# Deployment verification tools

These are opt-in administrative checks, not application dependencies. They must
never be started by an API, migration, test runner or production container.
Business services continue to own and access only their own database.

Run from the repository root. Python scripts require Python 3.10+ (standard
library only). The console requires the solution's .NET SDK and restored packages.
Keep credentials in the ignored local `.env` or process environment. Reports,
database dumps and decrypted archives belong outside Git.

## Read-only model and migration checks

Build the console with `dotnet build tools/Verification/Verification.csproj`.
Run `dotnet tools/Verification/bin/Debug/net10.0/Verification.dll model
<Service> <database>` to verify the frozen EF migration snapshot matches the
current model. `schema` checks actual columns, nullability and index names;
`migrate` applies pending migrations and requires none remain afterwards.

Database operations require `VERIFY_POSTGRES_PASSWORD` and
`VERIFY_POSTGRES_CLUSTER_ID`. Obtain the latter explicitly from the intended
container using `SELECT system_identifier::text FROM pg_control_system()`.
`VERIFY_POSTGRES_HOST`, `VERIFY_POSTGRES_PORT` and `VERIFY_POSTGRES_USER` default
to `localhost`, `15432`, `postgres`. The cluster check prevents accidentally
migrating a native PostgreSQL instance on port 5432. Always back up existing
databases and verify migrations against restored copies first.

`rollback ParishCoordination verify_parish_db` deliberately submits an invalid
internal foreign key through EF, then verifies that both business and planning
outbox rows rolled back. This command is restricted to that verification copy.

## Isolated HTTP and broker acceptance

`smoke.py`, `broker.py` and `support-smoke.py` default to preparation only. Read
their `--help` before opting in. Configure the complete compose file set and
separate `verify_*` databases, RabbitMQ vhost `verify20261103` and a private test
bucket. Their administrative SQL checks are restricted to these named copies.

- `smoke.py`: Gateway authentication, ownership, file verification/attachment,
  Task/attendance/review, Readiness/corrective linkage, purchases and staffing.
- `broker.py`: exact source/projection convergence, duplicate/stale/equal-version
  delivery, durable queue/outbox recovery across Task and RabbitMQ outages.
  It stops/starts only those two explicitly allowlisted services.
- `support-smoke.py`: real material transfer/reconciliation and separate
  acceptance/decline of community nominations.
- `jobs.py`: seven-source export, idempotency, encrypted backup, authenticated
  decryption and manifest inspection. It retains downloaded artifacts outside Git.
- `parity.py`: read-only global Season/Work Area comparison for the original
  Docker databases or the verification copies. It checks the container's cluster
  identity, source stability, counts, IDs, versions, ownership and stored fields.

Do not run outage checks concurrently with HTTP scenarios. Fixtures are created
through public APIs and retained in the copies until the operator saves the
reports and removes the isolated environment. A report containing skipped checks
does not prove those flows have passed.

## Recovery checks

`decrypt <encrypted-file> <new-output-file> <expected-key-id>` uses
`GROTTOWORKS_BACKUP_KEY`; it refuses to overwrite an existing output. Restore
database dumps into newly created databases, then use `schema` to validate them.
`restore-objects <plaintext-archive> <new-bucket>` requires
`VERIFY_STORAGE_SECRET_KEY` (or legacy `VERIFY_MINIO_PASSWORD`; the access key
`VERIFY_STORAGE_ACCESS_KEY`/`VERIFY_MINIO_USER` defaults to `grottoworks`), permits
only a `grottoworks-restore-*` bucket, and checks each restored object against its
manifest SHA-256. Default target is local MinIO (`VERIFY_STORAGE_ENDPOINT`, default
`http://localhost:9000`), where the bucket must not exist yet. For Cloudflare R2 set
`VERIFY_STORAGE_PROVIDER=R2` and the R2 endpoint; the bucket must be created empty in
the dashboard first (see `docs/ops/cloudflare-r2.md`).

`replay-reporting-errors` and `replay-lifecycle-errors` are deliberately restricted
to the isolated vhost and two known queue pairs. They preserve original message
IDs/envelopes, require publisher confirmation, and acknowledge the error message
only after successful publication. Run only after deploying the corresponding
consumer fix. Never purge an error queue to make acceptance appear green.

Cleanup must enumerate exact session-owned containers, databases, buckets and
files. Preserve existing volumes, business pending/error events, audit history,
backup archives and the encryption key. Never use a global Docker prune.
