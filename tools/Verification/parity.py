#!/usr/bin/env python3
"""Read-only planning parity for the original Docker databases or verification copies.

Prepared only by default; no subprocesses or files are touched without --execute.
Example (explicit read-only inspection):
  python tools/Verification/parity.py --execute --context original \
    --expected-cluster-id 7694293769844961318 --wait-seconds 120 \
    --output <new absolute JSON path outside Git>
Use --context verification for the exact verify_* copies. --container defaults to
grottoworks-postgres; no compose overlay is needed because the container is pinned
to its inspected ID. PostgreSQL is accessed only through docker exec and its local
Unix socket, never a host TCP connection. Override --postgres-user if necessary.

No API requests, credentials, provisioning, replay, restarts, SQL writes or fixes.
The cluster ID is verified before reading tables and with every subsequent query.
Each query uses REPEATABLE READ READ ONLY and statement_timeout; psql ignores rc
files and cannot prompt for a password. Uses only Python's standard library.

Schema version 1 report: source counts/integrity IDs; per-service stored field lists,
source/projection counts, missingIDs, unexpectedIDs, duplicateIDs, mismatchIDs and
fieldMismatches (IDs and field names only). No row values, logs, tokens or exception
messages are serialized. Both statuses normalize case/underscores, UUIDs canonicalize,
dates use ISO date values, and versions/ownership compare exactly. updated_at is
consumer-local; category/coordinates and season names are absent from these scope
projections, so they are not compared. No arbitrary expected seed/fixture counts.

--wait-seconds allows fresh comparisons until that convergence window expires;
--retry-interval controls read-only retries. Each sample has a hard budget of
8 * --timeout seconds, so a final in-flight sample may outlast the wait window by
at most that budget. A sample brackets all four projection reads with source reads.
Success requires source stability, source integrity and exact global parity.
An unstable last source sample is inconclusive, never asserted as a service defect.
Exit codes: 0 passed/prepared-only, 1 stable mismatch, 2 error/inconclusive.
"""
from __future__ import annotations

import argparse
from datetime import date, datetime, timezone
import json
import math
from pathlib import Path
import re
import subprocess
import time
import uuid


ROOT = Path(__file__).resolve().parents[2]
CLUSTER_ID = "7694293769844961318"
CONTEXTS = {
    "original": {"source": "parish_db", "task": "task_db", "resource": "resource_db",
                 "readiness": "readiness_db", "reporting": "reporting_db"},
    "verification": {"source": "verify_parish_db", "task": "verify_task_db", "resource": "verify_resource_db",
                     "readiness": "verify_readiness_db", "reporting": "verify_reporting_db"},
}
SEASON_COMMON = ("season_id", "season_status", "source_version")
AREA_COMMON = ("work_area_id", "season_id", "parish_id", "work_area_name", "community_id",
               "leader_user_id", "material_officer_user_id", "work_area_status", "source_version")
FIELDS = {
    name: {"seasons": SEASON_COMMON + (("parish_id", "start_date", "end_date") if name == "task" else ()),
           "areas": AREA_COMMON + (("start_date", "deadline") if name == "task" else ("season_status",))}
    for name in ("task", "resource", "readiness", "reporting")
}
SOURCE_SQL = """SELECT json_build_object(
    'seasons', (SELECT coalesce(json_agg(r ORDER BY season_id), '[]') FROM (
        SELECT season_id, parish_id, start_date, end_date, season_status,
               version AS source_version FROM christmas_season) r),
    'areas', (SELECT coalesce(json_agg(r ORDER BY work_area_id), '[]') FROM (
        SELECT a.work_area_id, a.season_id, s.parish_id, a.work_area_name,
               a.community_id, a.leader_user_id, a.material_officer_user_id,
               a.start_date, a.deadline, a.work_area_status, s.season_status,
               a.version AS source_version
        FROM work_area a LEFT JOIN christmas_season s USING (season_id)) r))"""


class InspectionFailed(Exception):
    """Only fixed non-sensitive diagnostic strings are permitted."""


def require(condition, message):
    if not condition:
        raise InspectionFailed(message)


def normalized(key, value):
    if value is None:
        return None
    if key.endswith("_status"):
        require(isinstance(value, str), "Invalid stored status type")
        return value.replace("_", "").upper()
    if key.endswith("_id"):
        return str(uuid.UUID(str(value)))
    if key in ("start_date", "end_date", "deadline"):
        return date.fromisoformat(value).isoformat()
    if key == "source_version":
        require(isinstance(value, int) and not isinstance(value, bool), "Invalid stored version type")
    return value


def normalize_rows(data):
    require(isinstance(data, dict) and set(data) == {"seasons", "areas"}, "Invalid scope query envelope")
    result = {}
    for kind, rows in data.items():
        require(isinstance(rows, list), "Invalid scope row collection")
        result[kind] = [{key: normalized(key, value) for key, value in row.items()} for row in rows]
    return result


def index_rows(rows, identity):
    indexed, duplicates = {}, set()
    for row in rows:
        require(row.get(identity) is not None, "A scope row has no identity")
        key = row[identity]
        if key in indexed:
            duplicates.add(key)
        indexed[key] = row
    return indexed, sorted(duplicates)


def compare_rows(source, projection, kind, fields):
    identity = "season_id" if kind == "seasons" else "work_area_id"
    expected, source_duplicates = index_rows(source, identity)
    actual, duplicates = index_rows(projection, identity)
    differences = []
    for key in sorted(expected.keys() & actual.keys()):
        different_fields = [field for field in fields if field not in expected[key] or field not in actual[key]
                            or expected[key][field] != actual[key][field]]
        if different_fields:
            differences.append({"id": key, "fields": different_fields})
    missing, extra = sorted(expected.keys() - actual.keys()), sorted(actual.keys() - expected.keys())
    mismatch_ids = sorted(set(missing + extra + duplicates + source_duplicates + [row["id"] for row in differences]))
    return {"sourceCount": len(source), "projectionCount": len(projection), "sourceUniqueCount": len(expected),
            "projectionUniqueCount": len(actual), "countMatches": len(source) == len(projection),
            "comparedFields": list(fields), "missingIDs": missing, "unexpectedIDs": extra,
            "duplicateIDs": duplicates, "mismatchIDs": mismatch_ids, "fieldMismatches": differences,
            "matches": not mismatch_ids and len(source) == len(projection)}


class ParityReader:
    def __init__(self, args):
        self.args = args
        self.databases = CONTEXTS[args.context]
        self.container_id = None
        self.sample_deadline = None

    def command(self, argv, sql=None):
        timeout = self.args.timeout
        if self.sample_deadline is not None:
            timeout = min(timeout, self.sample_deadline - time.monotonic())
            require(timeout > 0, "Read-only sample time budget exhausted")
        try:
            result = subprocess.run(argv, input=sql, text=True, encoding="utf-8",
                                    capture_output=True, timeout=timeout, cwd=ROOT)
        except Exception:
            raise InspectionFailed("Read-only Docker inspection did not complete") from None
        require(result.returncode == 0, "Read-only Docker/PostgreSQL query failed")
        require(len(result.stdout) <= 32_000_000, "Scope query exceeded the inspection size limit")
        return result.stdout

    def prepare(self):
        # Inspect only non-sensitive metadata. Do not read/print container env or compose config.
        template = ('{"id":{{json .Id}},"running":{{json .State.Running}},'
                    '"service":{{json (index .Config.Labels "com.docker.compose.service")}}}')
        metadata = json.loads(self.command(["docker", "inspect", "--type", "container", "--format", template,
                                           self.args.container]))
        require(metadata.get("running") is True and metadata.get("service") == "postgres",
                "Target must be the running compose PostgreSQL container")
        require(re.fullmatch(r"[0-9a-f]{64}", metadata.get("id", "")) is not None, "Invalid inspected container identity")
        self.container_id = metadata["id"]
        self.sql(self.databases["source"], "SELECT true")

    def sql(self, database, query):
        require(database in set(self.databases.values()), "Database is not in the selected context allowlist")
        require(query.lstrip().startswith("SELECT ") and ";" not in query, "Only the fixed SELECT inspections are supported")
        # Fixed socket/port override any inherited PGHOST/PGPORT; -X ignores psql rc
        # commands. No password or connection string is put on argv or in the report.
        wrapped = ("BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;\n"
                   "SET LOCAL statement_timeout='" + str(max(1, int(self.args.timeout * 1000))) + "ms';\n"
                   "SELECT json_build_object('clusterId', (SELECT system_identifier::text FROM pg_control_system()),"
                   " 'data', (" + query + "));\nROLLBACK;\n")
        raw = self.command(["docker", "exec", "-i", self.container_id, "psql", "-X", "-qAt", "-w",
                            "--host=/var/run/postgresql", "--port=5432", "--username=" + self.args.postgres_user,
                            "--dbname=" + database, "-v", "ON_ERROR_STOP=1"], wrapped)
        payload = json.loads(raw)
        require(payload.get("clusterId") == self.args.expected_cluster_id,
                "PostgreSQL cluster identifier does not match the expected cluster")
        return payload["data"]

    def source(self):
        return normalize_rows(self.sql(self.databases["source"], SOURCE_SQL))

    def projection(self, name):
        fields = FIELDS[name]
        query = ("SELECT json_build_object('seasons', (SELECT coalesce(json_agg(r ORDER BY season_id), '[]') "
                 "FROM (SELECT " + ",".join(fields["seasons"]) + " FROM season_scope) r), "
                 "'areas', (SELECT coalesce(json_agg(r ORDER BY work_area_id), '[]') FROM (SELECT " +
                 ",".join(fields["areas"]) + " FROM work_area_scope) r))")
        return normalize_rows(self.sql(self.databases[name], query))

    def sample(self):
        self.sample_deadline = time.monotonic() + self.args.timeout * 8
        before = self.source()
        services = {}
        for name in FIELDS:
            projection = self.projection(name)
            kinds = {kind: compare_rows(before[kind], projection[kind], kind, FIELDS[name][kind])
                     for kind in ("seasons", "areas")}
            services[name] = {"database": self.databases[name], **kinds,
                              "matches": all(row["matches"] for row in kinds.values())}
        after = self.source()
        seasons, duplicate_seasons = index_rows(before["seasons"], "season_id")
        _, duplicate_areas = index_rows(before["areas"], "work_area_id")
        orphans = sorted(row["work_area_id"] for row in before["areas"] if row["season_id"] not in seasons)
        stable = before == after
        integrity = not duplicate_seasons and not duplicate_areas and not orphans
        matches = integrity and all(row["matches"] for row in services.values())
        return {"sourceStable": stable,
                "source": {"database": self.databases["source"], "seasonCount": len(before["seasons"]),
                           "areaCount": len(before["areas"]), "duplicateSeasonIDs": duplicate_seasons,
                           "duplicateAreaIDs": duplicate_areas, "orphanAreaIDs": orphans, "integrityMatches": integrity},
                "services": services, "outcome": "passed" if stable and matches else "mismatch" if stable else "inconclusive"}


def output_path(path):
    require(path is not None and path.is_absolute() and path.suffix.lower() == ".json",
            "A new absolute JSON report path is required")
    result = path.resolve()
    require(not result.is_relative_to(ROOT) and not any((parent / ".git").exists() for parent in result.parents),
            "Parity reports must be outside every Git worktree")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--execute", action="store_true", help="Allow read-only Docker/PostgreSQL inspection")
    parser.add_argument("--context", choices=tuple(CONTEXTS), required=True)
    parser.add_argument("--container", default="grottoworks-postgres")
    parser.add_argument("--expected-cluster-id", default=CLUSTER_ID)
    parser.add_argument("--postgres-user", default="postgres")
    parser.add_argument("--wait-seconds", type=float, default=0)
    parser.add_argument("--retry-interval", type=float, default=2)
    parser.add_argument("--timeout", type=float, default=15)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = {"schemaVersion": 1, "mode": "read-only", "context": args.context,
              "expectedClusterId": args.expected_cluster_id, "outcome": "error", "attempts": 0}
    reserved_output = None
    try:
        require(re.fullmatch(r"[0-9]{1,20}", args.expected_cluster_id) is not None, "Expected cluster ID must be numeric")
        require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*", args.container) is not None, "Invalid container selector")
        require(re.fullmatch(r"[A-Za-z_][A-Za-z0-9_-]*", args.postgres_user) is not None, "Invalid PostgreSQL role selector")
        require(all(math.isfinite(value) for value in (args.wait_seconds, args.retry_interval, args.timeout)) and
                args.wait_seconds >= 0 and args.retry_interval > 0 and args.timeout > 0, "Invalid convergence time limits")
        if not args.execute:
            print(json.dumps({**report, "outcome": "prepared-only", "databases": CONTEXTS[args.context],
                              "remoteOperations": 0, "reportWritten": False}, indent=2))
            return 0
        destination = output_path(args.output)
        destination.parent.mkdir(parents=True, exist_ok=True)
        with destination.open("x", encoding="utf-8") as stream:
            json.dump({**report, "outcome": "running"}, stream)
        reserved_output = destination
        started = time.monotonic()
        report["startedAt"] = datetime.now(timezone.utc).isoformat()
        reader = ParityReader(args)
        reader.prepare()
        report["clusterVerified"] = True
        report["containerId"] = reader.container_id
        deadline = time.monotonic() + args.wait_seconds
        while True:
            report["attempts"] += 1
            sample = reader.sample()
            report.update(sample)
            report["elapsedSeconds"] = round(time.monotonic() - started, 3)
            if sample["outcome"] == "passed" or time.monotonic() >= deadline:
                break
            time.sleep(min(args.retry_interval, max(0, deadline - time.monotonic())))
    except InspectionFailed as error:
        report.update(outcome="error", reason=str(error))
    except KeyboardInterrupt:
        report.update(outcome="error", reason="Read-only parity inspection interrupted")
    except Exception:
        report.update(outcome="error", reason="Parity inspection failed; sensitive diagnostics suppressed")
    if reserved_output is not None:
        try:
            reserved_output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        except Exception:
            print('{"outcome":"error","reason":"Cannot write external parity report"}')
            return 2
    print(json.dumps({key: report[key] for key in ("outcome", "context", "attempts", "reason") if key in report}, indent=2))
    return 0 if report["outcome"] == "passed" else 1 if report["outcome"] == "mismatch" else 2


if __name__ == "__main__":
    raise SystemExit(main())
