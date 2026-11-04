#!/usr/bin/env python3
"""Prepare-only by default. Execution requires explicit mutation/restart flags.

Python 3.10+, stdlib and the adjacent smoke helper. Never provisions infrastructure.
Supply the isolated compose overlay with repeated --compose-file options.
Execution requires --execute --allow-service-restarts --test-context verify20261103
and an absolute --output JSON path outside every Git worktree. Fixtures are retained.
"""
from __future__ import annotations

import argparse
import base64
import copy
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import secrets
import subprocess
import time
from urllib.parse import quote, unquote, urlsplit
from urllib.request import Request
import uuid

from smoke import CheckFailed, Harness, base_url, load_settings, require


ROOT = Path(__file__).resolve().parents[2]
CONTEXT = "verify20261103"
EXCHANGE = "ParishCoordination.Contracts.Events:PlanningScopeSnapshot"
URN = "urn:message:ParishCoordination.Contracts.Events:PlanningScopeSnapshot"
DATABASES = {
    "identity.api": ("IdentityDatabase", "verify_identity_db"),
    "parishcoordination.api": ("ParishCoordinationDatabase", "verify_parish_db"),
    "taskexecution.api": ("TaskExecutionDatabase", "verify_task_db"),
    "resource.api": ("ResourceDatabase", "verify_resource_db"),
    "readiness.api": ("ReadinessDatabase", "verify_readiness_db"),
    "reporting.api": ("ReportingDatabase", "verify_reporting_db"),
    "notificationaudit.api": ("NotificationAuditDatabase", "verify_notify_db"),
}
TARGETS = {
    "task": ("verify_task_db", "scope_event_inbox", "planning-scope"),
    "resource": ("verify_resource_db", "scope_replication_inbox", "resource-planning"),
    "readiness": ("verify_readiness_db", "scope_replication_inbox", "readiness-planning"),
}
SEASON_FIELDS = "season_id,season_status,source_version"
AREA_FIELDS = ("work_area_id,season_id,parish_id,work_area_name,community_id,"
               "leader_user_id,material_officer_user_id,work_area_status,source_version")


def uid(value):
    return str(uuid.UUID(value))


class BrokerAcceptance:
    def __init__(self, args, report):
        self.args, self.report = args, report
        self.settings = load_settings(args.env_file)
        # The shared helper handles gateway transport, redirects and authentication.
        self.settings["SMOKE_GATEWAY_URL"] = args.gateway
        self.client = Harness(args, self.settings)
        self.client.prefix = "broker-" + self.client.run_id
        self.client.current_stage = "broker"
        self.compose = ["docker", "compose", "--project-directory", str(ROOT)]
        for file in args.compose_file:
            self.compose += ["-f", str(file.resolve())]
        self.compose += ["--env-file", str(args.env_file.resolve())]
        self.origin = base_url(args.rabbit_url)
        user = self.settings.get("RABBITMQ_DEFAULT_USER", "grottoworks")
        password = self.settings.get("RABBITMQ_DEFAULT_PASS", "grottoworks")
        self.auth = "Basic " + base64.b64encode((user + ":" + password).encode()).decode()
        self.stopped = set()
        self.report.update(runId=self.client.run_id, fixtures={}, checks=[], publications=[],
                           recovery=[], httpChecks=[], comparisons=[], outboxSnapshots=[])

    def command(self, argv, input_text=None):
        # No shell, no command text/output/exception details in reports.
        result = subprocess.run(argv, cwd=ROOT, input=input_text, text=True,
                                capture_output=True, timeout=self.args.timeout)
        require(result.returncode == 0, "Docker or PostgreSQL operation failed")
        return result.stdout

    def sql(self, database, query):
        require(database in {item[1] for item in DATABASES.values()}, "Database is not allowlisted")
        require(query.lstrip().startswith("SELECT "), "Only SELECT inspection is supported")
        raw = self.command(self.compose + ["exec", "-T", "postgres", "psql", "-X", "-qAt",
            "-U", self.settings.get("POSTGRES_USER", "postgres"), "-d", database,
            "-v", "ON_ERROR_STOP=1"],
            "BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;\n"
            "SET LOCAL statement_timeout='10s';\n" + query + ";\nROLLBACK;\n")
        return json.loads(raw)

    def rabbit(self, method, path, body=None):
        request = Request(self.origin + "/api/" + path, method=method,
            headers={"Authorization": self.auth, "Content-Type": "application/json"},
            data=json.dumps(body).encode() if body is not None else None)
        try:
            with self.client.opener.open(request, timeout=self.args.timeout) as response:
                raw = response.read(4_000_001)
                require(len(raw) <= 4_000_000, "Broker response exceeded inspection limit")
                return json.loads(raw) if raw else None
        except CheckFailed:
            raise
        except Exception:
            raise CheckFailed("Rabbit management request failed") from None

    def wait(self, label, predicate):
        deadline = time.monotonic() + self.args.projection_timeout
        while True:
            if predicate():
                self.report["checks"].append({"check": label, "outcome": "passed"})
                return
            require(time.monotonic() < deadline, "Timed out: " + label)
            time.sleep(1)

    def preflight(self):
        # Inspect actual running containers, not just compose's intended configuration.
        gateway = urlsplit(base_url(self.args.gateway))
        broker = urlsplit(self.origin)
        require(gateway.hostname in ("localhost", "127.0.0.1") and
                broker.hostname in ("localhost", "127.0.0.1"),
                "Verification HTTP origins must use the local compose stack")
        for service, origin in (("yarpapigateway", gateway), ("rabbitmq", broker)):
            container = self.command(self.compose + ["ps", "-q", service]).strip()
            require(bool(container) and len(container.splitlines()) == 1, "Expected one local infrastructure container")
            details = json.loads(self.command(["docker", "inspect", container]))[0]
            port = "8080/tcp" if service == "yarpapigateway" else "15672/tcp"
            mappings = details["NetworkSettings"]["Ports"].get(port) or []
            require(any(int(mapping["HostPort"]) == (origin.port or (443 if origin.scheme == "https" else 80))
                        for mapping in mappings), "HTTP origin does not match the inspected compose service")
            if service == "yarpapigateway":
                env = dict(value.split("=", 1) for value in details["Config"]["Env"] if "=" in value)
                for cluster, destination in (("identity", "identity.api"), ("parish", "parishcoordination.api")):
                    require(env.get("ReverseProxy__Clusters__" + cluster +
                                    "__Destinations__destination1__Address", "").rstrip("/") ==
                            "http://" + destination + ":8080", "Gateway destination is outside the verified services")
        for service, (key, database) in DATABASES.items():
            container = self.command(self.compose + ["ps", "-q", service]).strip()
            require(bool(container) and len(container.splitlines()) == 1,
                    "Expected one running API container per service")
            details = json.loads(self.command(["docker", "inspect", container]))[0]
            require(details["State"]["Running"], "An API is not running")
            env = dict(value.split("=", 1) for value in details["Config"]["Env"] if "=" in value)
            connection = dict(pair.strip().lower().split("=", 1)
                              for pair in env.get("ConnectionStrings__" + key, "").split(";") if "=" in pair)
            require(connection.get("database") == database and connection.get("host") == "postgres",
                    "API is not bound to its exact isolated verification database")
            host = urlsplit(env.get("MessageBroker__Host", ""))
            require(unquote(host.path).strip("/") == CONTEXT and host.hostname == "rabbitmq",
                    "API is not bound to the isolated verification vhost")
            if service == "parishcoordination.api":
                require(env.get("PlanningEvents__DispatchEnabled", "").lower() == "true"
                        and not any(key.startswith(("ScopeDelivery__", "TaskScopeSync__")) for key in env),
                        "Permanent broker delivery must be enabled without legacy HTTP settings")
        exchange = self.rabbit("GET", "exchanges/" + CONTEXT + "/" + quote(EXCHANGE, safe=""))
        require(exchange.get("durable") is True, "Planning exchange must be durable")
        for name in TARGETS:
            queue = self.queue(name)
            require(queue.get("durable") is True and not queue.get("auto_delete")
                    and queue.get("consumers", 0) > 0, "Planning queues must be durable and have consumers")
        self.report["checks"].append({"check": "running API database/vhost isolation and durable topology", "outcome": "passed"})

    def queue(self, name):
        return self.rabbit("GET", "queues/" + CONTEXT + "/" + name + "-planning-scopes")

    def api(self, label, method, path, body=None, expected=(200,), headers=None):
        # Do not retain server-supplied problem codes/field names in the public report.
        status, payload = self.client.transport(method, path, "ADMIN", body, headers)
        require(status in expected, "Gateway operation failed: " + label)
        self.report["httpChecks"].append({"check": label, "httpStatus": status})
        return payload.get("data", payload) if isinstance(payload, dict) else payload

    def fixtures(self):
        password = self.settings.get("SEED_ADMIN_PASSWORD")
        require(bool(password), "SEED_ADMIN_PASSWORD is required")
        self.client.login("ADMIN", self.settings.get("SEED_ADMIN_EMAIL", "admin@grottoworks.local"), password)
        prefix = self.client.prefix
        parish = self.api("create parish", "POST", "/api/v1/parishes",
                          {"name": prefix, "description": "Isolated broker acceptance"}, (201,))
        self.parish = uid(parish["id"])
        self.report["fixtures"]["parish"] = self.parish
        self.owners = {}
        for role in ("LEADER", "MO"):
            for index in (0, 1):
                user = self.api("create ownership fixture", "POST", "/api/v1/admin/users",
                    {"email": f"{prefix}-{role.lower()}-{index}@example.test", "displayName": prefix,
                     "password": "Broker1!" + secrets.token_urlsafe(24), "role": role,
                     "parishId": self.parish}, (201,))
                self.owners[role + str(index)] = uid(user["id"])
        self.report["fixtures"]["owners"] = self.owners
        community = self.api("create community", "POST", f"/api/v1/parishes/{self.parish}/communities",
                             {"name": prefix}, (201,))
        self.report["fixtures"]["community"] = uid(community["id"])
        now = datetime.now(timezone.utc)
        start, end = (now - timedelta(days=1)).date(), (now + timedelta(days=7)).date()
        season = self.api("create season", "POST", "/api/v1/seasons",
            {"parishId": self.parish, "name": prefix, "seasonYear": now.year,
             "startDate": start.isoformat(), "endDate": end.isoformat(), "estimatedBudget": 1000}, (201,))
        self.season = uid(season["id"])
        self.report["fixtures"]["season"] = self.season
        area = self.api("create work area", "POST", f"/api/v1/seasons/{self.season}/work-areas",
            {"name": prefix, "communityId": community["id"], "startDate": start.isoformat(),
             "deadline": end.isoformat()}, (201,))
        self.area = uid(area["id"])
        self.report["fixtures"]["workArea"] = self.area
        self.converge("initial fixture replication")
        self.old_season = self.snapshot(self.season)
        self.old_area = self.snapshot(self.area)
        season = self.api("update source season", "PATCH", "/api/v1/seasons/" + self.season,
            {"endDate": (end + timedelta(days=1)).isoformat()},
            headers={"If-Match": f'"{season["version"]}"'})
        self.assign(0)
        self.api("update source work area", "PATCH", "/api/v1/work-areas/" + self.area,
                 {"name": prefix + " updated"}, headers=self.area_etag())
        self.converge("updated versions and ownership across all source rows")

    def area_etag(self):
        area = self.api("read own work area", "GET", "/api/v1/work-areas/" + self.area)
        return {"If-Match": f'"{area["version"]}"'}

    def assign(self, index):
        for role, suffix in (("LEADER", "leader"), ("MO", "material-officer")):
            area = self.api("update source ownership", "PUT", f"/api/v1/work-areas/{self.area}/{suffix}",
                            {"userId": self.owners[role + str(index)]}, headers=self.area_etag())
            field = "leaderId" if role == "LEADER" else "materialOfficerId"
            require(area.get(field) == self.owners[role + str(index)], "Source ownership assignment did not persist")

    def source(self):
        return self.sql("verify_parish_db", "SELECT json_build_object('seasons',"
            "(SELECT coalesce(json_agg(x ORDER BY season_id),'[]') FROM "
            "(SELECT season_id,parish_id,start_date,end_date,season_status,version AS source_version "
            "FROM christmas_season) x),'areas',"
            "(SELECT coalesce(json_agg(x ORDER BY work_area_id),'[]') FROM "
            "(SELECT a.work_area_id,a.season_id,s.parish_id,a.work_area_name,a.community_id,"
            "a.leader_user_id,a.material_officer_user_id,a.start_date,a.deadline,a.work_area_status,"
            "s.season_status,a.version AS source_version FROM work_area a "
            "JOIN christmas_season s USING(season_id)) x))")

    def projection(self, name):
        season_fields = SEASON_FIELDS + (",parish_id,start_date,end_date" if name == "task" else "")
        area_fields = AREA_FIELDS + (",start_date,deadline" if name == "task" else ",season_status")
        return self.sql(TARGETS[name][0], "SELECT json_build_object('seasons',"
            "(SELECT coalesce(json_agg(x ORDER BY season_id),'[]') FROM (SELECT " + season_fields +
            " FROM season_scope) x),'areas',(SELECT coalesce(json_agg(x ORDER BY work_area_id),'[]') "
            "FROM (SELECT " + area_fields + " FROM work_area_scope) x))")

    def matches(self, source, projected, name):
        for kind, identity in (("seasons", "season_id"), ("areas", "work_area_id")):
            expected = {row[identity]: row for row in source[kind]}
            actual = projected[kind]
            if len(actual) != len(expected) or len({row[identity] for row in actual}) != len(actual):
                return False
            for row in actual:
                if row[identity] not in expected or any(self.wire_value(key, expected[row[identity]].get(key)) != self.wire_value(key, value)
                                                       for key, value in row.items()):
                    return False
        return True

    @staticmethod
    def wire_value(key, value):
        # Parish's existing EF converter stores enum names without underscores;
        # replicated snapshots use the public SCREAMING_SNAKE_CASE vocabulary.
        if key == "work_area_status" and isinstance(value, str):
            return value.replace("_", "").upper()
        return value

    def converge(self, label):
        def check():
            source = self.source()
            projections = {name: self.projection(name) for name in TARGETS}
            if not all(self.matches(source, rows, name) for name, rows in projections.items()):
                return False
            # Avoid accepting a comparison across a concurrent source mutation.
            if source != self.source():
                return False
            self.report["comparison"] = {"sourceSeasonCount": len(source["seasons"]),
                "sourceWorkAreaCount": len(source["areas"]), "allSourceRowsCompared": True,
                "databases": [item[0] for item in TARGETS.values()]}
            self.report["comparisons"].append({"phase": label, "source": source, "projections": projections})
            return True
        self.wait(label, check)

    def snapshot(self, aggregate):
        rows = self.sql("verify_parish_db", "SELECT coalesce(json_agg(x),'[]') FROM "
            "(SELECT envelope,published_at,source_version FROM planning_event_outbox "
            "WHERE event_type='scope.snapshot' AND aggregate_id='" + uid(aggregate) +
            "' ORDER BY source_version DESC LIMIT 1) x")
        require(len(rows) == 1, "Own durable fixture snapshot is missing")
        snapshot = rows[0]["envelope"]
        require(snapshot["season"]["parishId"] == self.parish and
                snapshot["aggregateId"] in (self.season, self.area), "Snapshot is outside own fixture")
        self.report["outboxSnapshots"].append({"snapshotId": snapshot["snapshotId"],
            "aggregateId": snapshot["aggregateId"], "sourceVersion": rows[0]["source_version"],
            "published": rows[0]["published_at"] is not None})
        return snapshot

    def receipt(self, name, snapshot_id):
        database, table, consumer = TARGETS[name]
        return self.sql(database, "SELECT count(*) FROM " + table + " WHERE consumer='" + consumer +
                        "' AND event_id='" + uid(snapshot_id) + "'")

    def publish(self, snapshot, label, fresh_snapshot=False):
        message = copy.deepcopy(snapshot)
        if fresh_snapshot:
            message["snapshotId"] = str(uuid.uuid4())
        message_id = str(uuid.uuid4())
        envelope = {"messageId": message_id, "messageType": [URN], "message": message,
                    "sentTime": datetime.now(timezone.utc).isoformat(), "headers": {}}
        result = self.rabbit("POST", "exchanges/" + CONTEXT + "/" + quote(EXCHANGE, safe="") + "/publish",
            {"properties": {"content_type": "application/vnd.masstransit+json", "delivery_mode": 2,
                            "message_id": message_id}, "routing_key": "",
             "payload_encoding": "string", "payload": json.dumps(envelope)})
        require(result.get("routed") is True, "Replay was not routed")
        self.report["publications"].append({"kind": label, "messageId": message_id,
            "snapshotId": message["snapshotId"], "sourceVersion": message["sourceVersion"]})
        return message["snapshotId"]

    def drained(self):
        return all(q.get("messages_ready", -1) == 0 and q.get("messages_unacknowledged", -1) == 0
                   for q in (self.queue(name) for name in TARGETS))

    def own_state(self):
        # Includes updated_at and every projected column, so ignored events cannot silently rewrite rows.
        result = {}
        for name, (database, _, _) in TARGETS.items():
            result[name] = self.sql(database, "SELECT json_build_object('seasons',"
                "(SELECT json_agg(s) FROM season_scope s WHERE season_id='" + self.season +
                "'),'areas',(SELECT json_agg(a) FROM work_area_scope a WHERE work_area_id='" + self.area + "'))")
        return result

    def replays(self):
        self.wait("planning queues drained before replay", self.drained)
        before = self.own_state()
        for aggregate, stale in ((self.season, self.old_season), (self.area, self.old_area)):
            latest = self.snapshot(aggregate)
            self.wait("original snapshot inbox receipt", lambda: all(
                self.receipt(name, latest["snapshotId"]) == 1 for name in TARGETS))
            require(stale["sourceVersion"] < latest["sourceVersion"], "Stale fixture version must be lower")
            self.publish(latest, "duplicate-new-transport-id")
            stale_id = self.publish(stale, "stale-new-snapshot-and-transport-id", True)
            self.wait("stale snapshot processed by every inbox", lambda: all(
                self.receipt(name, stale_id) == 1 for name in TARGETS))
            # A fresh logical duplicate proves version guarding independently of snapshot-ID deduplication.
            barrier_id = self.publish(latest, "equal-version-new-snapshot-and-transport-id", True)
            self.wait("equal-version snapshot processed by every inbox", lambda: all(
                self.receipt(name, barrier_id) == 1 for name in TARGETS))
            self.wait("replay queues drained", self.drained)
            for name in TARGETS:
                require(self.receipt(name, latest["snapshotId"]) == 1, "Duplicate inbox receipt detected")
        after = self.own_state()
        self.report.setdefault("replayComparisons", []).append({"before": before, "after": after})
        require(before == after, "Replay changed or duplicated an own fixture projection")
        self.converge("no regression or duplicate projection after replay")

    def service(self, action, service):
        require(self.args.execute and self.args.allow_service_restarts and self.args.test_context == CONTEXT,
                "Service restart authorization is missing")
        require(service in ("taskexecution.api", "rabbitmq") and action in ("stop", "start"),
                "Service action is not allowlisted")
        # Mark before stop: a timeout may happen after Docker has performed the operation.
        if action == "stop":
            self.stopped.add(service)
        self.command(self.compose + [action, service])
        if action == "start":
            self.stopped.discard(service)
        self.report["recovery"].append({"service": service, "action": action, "outcome": "passed"})

    def broker_ready(self):
        try:
            self.rabbit("GET", "vhosts/" + CONTEXT)
            return True
        except CheckFailed:
            return False

    def published(self, aggregate, expected):
        return self.sql("verify_parish_db", "SELECT count(*) FROM planning_event_outbox "
            "WHERE event_type='scope.snapshot' AND aggregate_id='" + uid(aggregate) +
            "' AND source_version=" + str(int(expected)) + " AND published_at IS NOT NULL") == 1

    def recovery(self):
        self.service("stop", "taskexecution.api")
        self.wait("Task consumer disconnected", lambda: self.queue("task").get("consumers") == 0)
        task_before = self.own_state()["task"]
        self.assign(1)
        queued = self.snapshot(self.area)
        self.wait("outbox published while Task is offline", lambda: self.published(self.area, queued["sourceVersion"]))
        self.wait("durable Task queue retained offline update", lambda: self.queue("task").get("messages_ready", 0) > 0)
        require(self.own_state()["task"] == task_before, "Task projection changed while its consumer was stopped")
        queued_count = self.queue("task")["messages_ready"]
        self.service("stop", "rabbitmq")
        area = self.api("update source during broker outage", "PATCH", "/api/v1/work-areas/" + self.area,
                        {"name": self.client.prefix + " recovered"}, headers=self.area_etag())
        pending = self.snapshot(self.area)
        require(pending["sourceVersion"] == area["version"] and
                not self.published(self.area, pending["sourceVersion"]),
                "Broker outage did not retain an unpublished durable snapshot")
        self.service("start", "rabbitmq")
        self.wait("broker management recovered", self.broker_ready)
        self.wait("queued Task update survived broker restart", lambda:
                  self.queue("task").get("messages_ready", 0) >= queued_count)
        self.wait("durable outbox dispatched after broker restart", lambda:
                  self.published(self.area, pending["sourceVersion"]))
        self.service("start", "taskexecution.api")
        self.wait("Task consumer reconnected", lambda: self.queue("task").get("consumers", 0) > 0)
        self.wait("Task processed both offline snapshots", lambda: all(
            self.receipt("task", item["snapshotId"]) == 1 for item in (queued, pending)))
        self.converge("all source versions and ownership recovered after outages")

    def run(self):
        try:
            self.preflight()
            self.fixtures()
            self.replays()
            self.recovery()
            self.replays()
        finally:
            # Best effort restore only services stopped by this run, broker before Task.
            for service in ("rabbitmq", "taskexecution.api"):
                if service in self.stopped:
                    try:
                        self.service("start", service)
                    except Exception:
                        self.report["recovery"].append({"service": service, "action": "start", "outcome": "failed"})
            require(not self.stopped, "Service restoration failed; operator action required")


def outside_git(path):
    require(path.is_absolute() and path.suffix.lower() == ".json", "Output must be an absolute JSON path")
    resolved = path.resolve()
    require(not resolved.is_relative_to(ROOT), "Report must be outside the repository")
    require(not any((parent / ".git").exists() for parent in (resolved.parent, *resolved.parents)),
            "Report must be outside every Git worktree")
    return resolved


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--allow-service-restarts", action="store_true")
    parser.add_argument("--test-context")
    parser.add_argument("--env-file", type=Path, default=ROOT / ".env")
    parser.add_argument("--compose-file", type=Path, action="append", default=[])
    parser.add_argument("--gateway", default="http://localhost:6102")
    parser.add_argument("--rabbit-url", default="http://localhost:15672")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--timeout", type=int, default=45)
    parser.add_argument("--projection-timeout", type=int, default=180)
    args = parser.parse_args()
    if not args.execute:
        print(json.dumps({"outcome": "prepared-only", "context": CONTEXT,
            "databases": [item[1] for item in DATABASES.values()],
            "requiredFlags": ["--execute", "--allow-service-restarts", "--test-context " + CONTEXT,
                              "--output <absolute JSON path outside Git>"],
            "stages": ["read-only isolation preflight", "ADMIN gateway fixtures and updates",
                       "all-source exact projection comparison", "duplicate and stale durable snapshot replay",
                       "Task consumer outage", "broker restart and durable outbox/queue recovery"],
            "execution": "Explicit execution and restart flags required; no operations performed"}, indent=2))
        return 0
    report = {"outcome": "failed", "context": CONTEXT}
    output = None
    report_reserved = False
    try:
        require(args.allow_service_restarts and args.test_context == CONTEXT,
                "Execution requires restart opt-in and the exact verification context")
        require(args.output is not None, "An external JSON report path is required")
        output = outside_git(args.output)
        require(args.timeout > 0 and args.projection_timeout > 0, "Timeouts must be positive")
        # Establish report writability before any remote operation.
        output.parent.mkdir(parents=True, exist_ok=True)
        with output.open("x", encoding="utf-8") as stream:
            report_reserved = True
            json.dump({"outcome": "running", "context": CONTEXT}, stream)
        acceptance = BrokerAcceptance(args, report)
        acceptance.run()
        report["outcome"] = "passed"
    except CheckFailed as error:
        report["reason"] = str(error)  # All messages above and in shared helper are fixed text.
    except KeyboardInterrupt:
        report["reason"] = "Interrupted"
    except Exception:
        report["reason"] = "Verification operation failed; sensitive diagnostics suppressed"
    if output is not None and report_reserved:
        try:
            output.write_text(json.dumps(report, indent=2), encoding="utf-8")
        except Exception:
            print('{"outcome":"failed","reason":"Cannot write external verification report"}')
            return 1
    print(json.dumps({"outcome": report["outcome"], "reason": report.get("reason")}, indent=2))
    return 0 if report["outcome"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
