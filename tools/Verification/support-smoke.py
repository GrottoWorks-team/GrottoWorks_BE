#!/usr/bin/env python3
"""Isolated Support acceptance; explicit execution and mutation flags are required.

Stdlib + adjacent smoke.Harness. Default mode prints a plan without reading dotenv,
running subprocesses, making requests, or writing reports. Execution creates retained
fixtures; never starts/stops services, builds, or changes infrastructure configuration.

Run from the repository with --execute --allow-mutations --test-context verify20261103
--env-file .env --output <new absolute JSON path outside Git>. Supply the complete
compose file set using repeated --compose-file when using an isolated overlay.
Defaults: Gateway localhost:6102, HTTP timeout 20s, projection wait 180s, budget 900s.
Explicit login HTTP 429 responses may retry for 180s, waiting 60s between attempts
as required by Gateway's Retry-After policy. No timeout or business mutation retries.
Requires SEED_ADMIN_PASSWORD, working storage/purchase flow, planning/identity/task
consumers, Resource support commands, Task support approval worker and receipt
consumer/reconciliation worker. No outages or fault injection are performed.
"""
from __future__ import annotations

import argparse
import base64
from datetime import datetime, timedelta
import hashlib
import json
from pathlib import Path
import secrets
import subprocess
import time
from urllib.parse import unquote, urlsplit
from urllib.request import Request
import uuid

from smoke import CheckFailed, Harness, base_url, load_settings, require, state, version


ROOT = Path(__file__).resolve().parents[2]
CONTEXT = "verify20261103"
DATABASES = {
    "identity.api": ("IdentityDatabase", "verify_identity_db"),
    "parishcoordination.api": ("ParishCoordinationDatabase", "verify_parish_db"),
    "taskexecution.api": ("TaskExecutionDatabase", "verify_task_db"),
    "resource.api": ("ResourceDatabase", "verify_resource_db"),
    "readiness.api": ("ReadinessDatabase", "verify_readiness_db"),
    "reporting.api": ("ReportingDatabase", "verify_reporting_db"),
    "notificationaudit.api": ("NotificationAuditDatabase", "verify_notify_db"),
}


class SupportSmoke(Harness):
    def __init__(self, args, settings, report):
        super().__init__(args, settings)
        self.prefix = "support-" + self.run_id
        self.report = report
        self.current_stage = "setup"
        self.expires = time.monotonic() + args.max_seconds
        self.compose = ["docker", "compose", "--project-directory", str(ROOT)]
        for file in args.compose_file:
            self.compose += ["-f", str(file.resolve())]
        self.compose += ["--env-file", str(args.env_file.resolve())]
        report.update(runId=self.run_id, fixtureIds=self.ids, checks=self.checks, observations={})

    def remaining(self):
        remaining = self.expires - time.monotonic()
        require(remaining > 0, "Support acceptance runtime budget exhausted")
        return remaining

    def transport(self, method, path, role=None, body=None, headers=None, direct=None):
        login = method == "POST" and path == "/api/v1/auth/login" and role is None and direct is None
        retry_deadline = min(self.expires, time.monotonic() + self.args.login_retry_timeout)
        while True:
            timeout = self.args.timeout
            try:
                self.args.timeout = min(timeout, self.remaining())
                status, payload = super().transport(method, path, role, body, headers, direct)
            finally:
                self.args.timeout = timeout
            if not login or status != 429:
                return status, payload
            # Gateway rejects this request before Identity executes it. The shared
            # transport intentionally omits response headers; its fixed Gateway policy
            # sends Retry-After: 60, so honor that full delay rather than guessing 10s.
            # Transport exceptions propagate immediately: they never enter this retry.
            delay = 60
            require(time.monotonic() + delay < retry_deadline,
                    "Login rate limit did not recover within the bounded retry window")
            self.checks.append({"stage": self.current_stage, "check": "Gateway login rate limit wait",
                                "httpStatus": 429, "retryDelaySeconds": delay, "outcome": "retrying"})
            wake_at = time.monotonic() + delay
            while time.monotonic() < wake_at:
                time.sleep(max(0, min(1, wake_at - time.monotonic(), self.remaining())))

    def wait_read(self, *args, **kwargs):
        timeout = self.args.projection_timeout
        try:
            self.args.projection_timeout = min(timeout, self.remaining())
            return super().wait_read(*args, **kwargs)
        finally:
            self.args.projection_timeout = timeout

    def call(self, *args, **kwargs):
        try:
            return super().call(*args, **kwargs)
        finally:
            # Server-originated error strings/field names are not trusted report data.
            for check in self.checks:
                check.pop("problemCode", None)
                check.pop("validationFields", None)

    def command(self, argv, query=None):
        result = subprocess.run(argv, cwd=ROOT, input=query, text=True, capture_output=True,
                                timeout=min(self.args.timeout, self.remaining()))
        require(result.returncode == 0, "Read-only isolation inspection failed")
        return result.stdout

    def preflight(self):
        gateway = urlsplit(self.gateway)
        require(gateway.hostname in ("localhost", "127.0.0.1"), "Gateway must be the local isolated stack")
        for service, (key, database) in DATABASES.items():
            container = self.command(self.compose + ["ps", "-q", service]).strip()
            require(bool(container) and len(container.splitlines()) == 1, "Expected one running API container")
            row = json.loads(self.command(["docker", "inspect", container]))[0]
            require(row["State"]["Running"], "An API container is not running")
            env = dict(item.split("=", 1) for item in row["Config"]["Env"] if "=" in item)
            connection = dict(item.strip().lower().split("=", 1) for item in
                              env.get("ConnectionStrings__" + key, "").split(";") if "=" in item)
            host = urlsplit(env.get("MessageBroker__Host", ""))
            require(connection.get("database") == database and connection.get("host") == "postgres"
                    and host.hostname == "rabbitmq" and unquote(host.path).strip("/") == CONTEXT,
                    "API is outside the exact verification database/vhost context")
            if service == "parishcoordination.api":
                require(env.get("ScopeDelivery__BrokerPrimaryEnabled", "").lower() == "true" and
                        env.get("ScopeDelivery__HttpRollbackEnabled", "false").lower() == "false",
                        "Broker-primary scope delivery without HTTP rollback is required")
        container = self.command(self.compose + ["ps", "-q", "yarpapigateway"]).strip()
        require(bool(container) and len(container.splitlines()) == 1, "Expected one running Gateway")
        row = json.loads(self.command(["docker", "inspect", container]))[0]
        mappings = row["NetworkSettings"]["Ports"].get("8080/tcp") or []
        require(any(int(item["HostPort"]) == (gateway.port or 80) for item in mappings),
                "Gateway origin does not match the inspected stack")
        env = dict(item.split("=", 1) for item in row["Config"]["Env"] if "=" in item)
        for cluster, destination in (("identity", "identity.api"), ("parish", "parishcoordination.api"),
                                     ("task", "taskexecution.api"), ("resource", "resource.api"),
                                     ("notificationaudit", "notificationaudit.api")):
            require(env.get("ReverseProxy__Clusters__" + cluster + "__Destinations__destination1__Address", "").rstrip("/")
                    == "http://" + destination + ":8080", "Gateway destination is outside the inspected stack")
        self.checks.append({"check": "exact verify database and vhost isolation", "outcome": "passed"})

    def account(self, alias, role, community=None):
        password = "Support1!" + secrets.token_urlsafe(24)
        email = self.prefix + "-" + alias.lower().replace("_", "-") + "@example.test"
        created = self.call("create fixture account " + alias, "POST", "/api/v1/admin/users", "ADMIN",
            {"email": email, "displayName": self.prefix + " " + alias, "password": password,
             "role": role, "parishId": self.ids["parish"]}, expected=(201,))
        self.ids["user_" + alias] = str(uuid.UUID(created["id"]))
        saved = (self.tokens.get(role), self.users.get(role))
        self.login(role, email, password)
        token, profile = self.tokens[role], self.users[role]
        if saved[0] is not None:
            self.tokens[role], self.users[role] = saved
        self.tokens[alias], self.users[alias] = token, profile
        if community:
            self.call("join responding community " + alias, "PATCH", "/api/v1/users/me", alias,
                      {"communityId": community})

    def fixtures(self):
        self.current_stage = "fixture"
        self.admin()
        parish = self.call("create own parish", "POST", "/api/v1/parishes", "ADMIN",
                           {"name": self.prefix}, expected=(201,))
        self.ids["parish"] = parish["id"]
        for name in ("source", "target"):
            community = self.call("create " + name + " community", "POST",
                f"/api/v1/parishes/{parish['id']}/communities", "ADMIN",
                {"name": self.prefix + " " + name}, expected=(201,))
            self.ids[name + "Community"] = community["id"]
        for role in ("PARISH", "LEADER", "MO"):
            self.account(role, role)
        self.account("TARGET_MO", "MO")
        for alias in ("VOLUNTEER", "NOMINEE", "DECLINER"):
            self.account(alias, "VOLUNTEER", self.ids["sourceCommunity"])
        start = (self.now - timedelta(days=1)).date().isoformat()
        end = (self.now + timedelta(days=7)).date().isoformat()
        season = self.call("create season", "POST", "/api/v1/seasons", "ADMIN",
            {"parishId": parish["id"], "name": self.prefix, "seasonYear": self.now.year,
             "startDate": start, "endDate": end, "estimatedBudget": 100000}, expected=(201,))
        self.ids["season"] = season["id"]
        for name, officer in (("source", "MO"), ("target", "TARGET_MO")):
            area = self.call("create " + name + " before activation", "POST",
                f"/api/v1/seasons/{season['id']}/work-areas", "ADMIN",
                {"name": self.prefix + " " + name, "communityId": self.ids[name + "Community"],
                 "startDate": start, "deadline": end}, expected=(201,))
            self.ids[name + "Area"] = area["id"]
            for suffix, owner in (("leader", "LEADER"), ("material-officer", officer)):
                area = self.call("assign " + name + " " + suffix, "PUT",
                    f"/api/v1/work-areas/{area['id']}/{suffix}", "ADMIN", {"userId": self.users[owner]["id"]},
                    headers={"If-Match": f'"{area["version"]}"'})
            require(area.get("leaderId") == self.users["LEADER"]["id"] and
                    area.get("materialOfficerId") == self.users[officer]["id"], "Fixture ownership did not persist")
        self.call("activate after both work areas exist", "POST",
                  f"/api/v1/seasons/{season['id']}/activate", "ADMIN", version(season))
        # Wait only on reads. No creation/update command is retried on timeout.
        for name, officer in (("source", "MO"), ("target", "TARGET_MO")):
            area_id = self.ids[name + "Area"]
            self.wait_read("Task " + name + " projection", f"/api/v1/work-areas/{area_id}/tasks", "LEADER",
                           predicate=lambda data: isinstance(data, list))
            self.wait_read("Resource " + name + " ownership", f"/api/v1/work-areas/{area_id}/material-requirements", officer)
        self.wait_active_scopes()

    def wait_active_scopes(self):
        season = str(uuid.UUID(self.ids["season"]))
        source_season = self.call("read authoritative activated season", "GET", "/api/v1/seasons/" + season, "ADMIN")
        state(source_season, "ACTIVE")
        season_version = version(source_season)["version"]
        parish = str(uuid.UUID(self.ids["parish"]))
        leader = str(uuid.UUID(self.users["LEADER"]["id"]))
        expected, predicates = [], []
        for name, officer in (("source", "MO"), ("target", "TARGET_MO")):
            area_id = str(uuid.UUID(self.ids[name + "Area"]))
            area = self.call("read authoritative " + name + " ownership", "GET",
                             "/api/v1/work-areas/" + area_id, "ADMIN")
            area_version = version(area)["version"]
            officer_id = str(uuid.UUID(self.users[officer]["id"]))
            community = str(uuid.UUID(self.ids[name + "Community"]))
            require(area.get("leaderId") == leader and area.get("materialOfficerId") == officer_id,
                    "Source fixture ownership changed before projection readiness")
            expected.append({"workAreaId": area_id, "sourceVersion": area_version,
                             "leaderId": leader, "materialOfficerId": officer_id})
            predicates.append("(a.work_area_id='" + area_id + "' AND a.source_version=" + str(area_version) +
                              " AND a.leader_user_id='" + leader + "' AND a.material_officer_user_id='" +
                              officer_id + "' AND a.community_id='" + community + "')")
        self.report["observations"]["expectedScopes"] = {"seasonVersion": season_version, "areas": expected}
        for database in ("verify_task_db", "verify_resource_db"):
            deadline = time.monotonic() + self.args.projection_timeout
            while True:
                # Row existence and same-parish GET access do not establish write ownership.
                # Wait for each independent area assignment version and the parent version.
                query = ("BEGIN READ ONLY; SET LOCAL statement_timeout='10s'; SELECT count(*) "
                    "FROM work_area_scope a JOIN season_scope s USING(season_id) WHERE a.season_id='" + season +
                    "' AND a.parish_id='" + parish + "' AND s.season_status='ACTIVE' AND s.source_version=" +
                    str(season_version) + (" AND a.season_status='ACTIVE'" if database == "verify_resource_db" else "") +
                    " AND (" + " OR ".join(predicates) + "); ROLLBACK;")
                result = self.command(self.compose + ["exec", "-T", "postgres", "psql", "-X", "-qAt",
                    "-U", self.settings.get("POSTGRES_USER", "postgres"), "-d", database, "-v", "ON_ERROR_STOP=1"], query)
                if result.strip() == "2":
                    break
                require(time.monotonic() < deadline, "Exact fixture versions and ownership did not converge")
                time.sleep(1)
            self.checks.append({"stage": self.current_stage, "check": database + " exact versions and write ownership",
                                "outcome": "passed"})

    def receipt_upload(self):
        self.current_stage = "purchase-receipt"
        data = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6vVsAAAAASUVORK5CYII=")
        checksum = hashlib.sha256(data).hexdigest()
        upload = self.call("request purchase receipt upload", "POST", "/api/v1/files/upload-requests", "VOLUNTEER",
            {"fileName": self.prefix + ".png", "contentType": "image/png", "sizeBytes": len(data),
             "purpose": "PURCHASE_RECEIPT", "checksum": checksum}, expected=(201,))
        storage = base_url(self.settings.get("MINIO_PUBLIC_ENDPOINT", "http://localhost:9000"))
        parsed = urlsplit(upload["uploadUrl"])
        require(parsed.scheme + "://" + parsed.netloc == storage, "Unexpected upload origin")
        request = Request(upload["uploadUrl"], method=upload["method"], headers=upload["headers"], data=data)
        with self.opener.open(request, timeout=min(self.args.timeout, self.remaining())) as response:
            require(response.code in (200, 204), "Receipt object upload failed")
        completed = self.call("finalize purchase receipt", "POST", f"/api/v1/files/{upload['fileId']}/complete",
                              "VOLUNTEER", {"checksum": checksum})
        self.uploads["PURCHASE_RECEIPT"] = completed["id"]
        self.ids["receiptFile"] = completed["id"]

    def inventory(self, area, officer):
        rows = self.call("read own material stock", "GET", "/api/v1/inventory?workAreaId=" + area +
                         "&materialId=" + self.ids["material"], officer)
        require(isinstance(rows, list), "Inventory response is not a row list")
        matches = [row for row in rows if row.get("workAreaId") == area and row.get("materialId") == self.ids["material"]]
        require(len(matches) == 1, "Expected one own material balance")
        return matches[0]

    def response_material(self, responses):
        return next((item for response in (responses or []) for item in response.get("materials", [])
                     if item.get("id") == self.ids["supportMaterial"]), None)

    def material_support(self):
        self.current_stage = "material-support"
        self.ids["workArea"] = self.ids["sourceArea"]
        self.receipt_upload()
        self.current_stage = "purchase"
        self.purchase()  # Shared real approval -> buyer assignment -> receipt -> stock -> Task completion.
        self.current_stage = "material-support"
        stock = self.inventory(self.ids["sourceArea"], "MO")
        require(stock["confirmedAvailable"] == 1 and stock["unallocated"] == 1, "Purchase did not establish transferable stock")
        self.ids["workArea"] = self.ids["targetArea"]
        self.call("target material requirement", "POST",
            f"/api/v1/work-areas/{self.ids['targetArea']}/material-requirements", "TARGET_MO",
            {"materialId": self.ids["material"], "requiredQuantity": 1}, expected=(201,))
        request = self.call("create material support request", "POST", "/api/v1/support-requests", "LEADER",
            {"seasonId": self.ids["season"], "workAreaId": self.ids["targetArea"], "supportType": "MATERIAL",
             "description": self.prefix, "deadline": (self.now + timedelta(days=1)).date().isoformat(),
             "positions": [], "materials": [{"materialId": self.ids["material"], "requiredQuantity": 1}]}, expected=(201,))
        self.ids["materialSupportRequest"] = request["id"]
        path = "/api/v1/support-requests/" + request["id"]
        published = self.call("publish material support", "POST", path + "/publish", "LEADER", version(request))
        offer = {"members": [], "materials": [{"requestMaterialId": published["materials"][0]["id"],
                 "sourceWorkAreaId": self.ids["sourceArea"], "offeredQuantity": 1}], "note": self.prefix}
        self.call("target MO cannot offer another MO's stock", "POST", path + "/responses", "TARGET_MO", offer, expected=(403,))
        response = self.call("actual source MO offers stock", "POST", path + "/responses", "MO", offer, expected=(201,))
        item = response["materials"][0]
        self.ids["supportMaterial"] = item["id"]
        self.call("leader approves actual MO offer", "POST", f"/api/v1/support-response-materials/{item['id']}/approve",
                  "LEADER", {"approvedQuantity": 1, **version(item)})
        responses_path = path + "/responses"
        rows = self.wait_read("material withdrawal reconciled", responses_path, "LEADER", predicate=lambda rows:
            (self.response_material(rows) or {}).get("status") == "APPROVED")
        approved = self.response_material(rows)
        require(approved["approvedQuantity"] == 1 and approved["pendingSync"] is True, "Approval omitted its pending receipt state")
        require(self.inventory(self.ids["sourceArea"], "MO")["confirmedAvailable"] == 0, "Approved transfer did not withdraw source stock")
        receipt_path = f"/api/v1/support-response-materials/{item['id']}/confirm-receipt"
        body = {"receivedUsableQuantity": 1, "receivedUnusableQuantity": 0, "note": self.prefix}
        self.call("source MO cannot confirm target receipt", "POST", receipt_path, "MO", body, expected=(403,))
        # Resource's transfer xmin differs from Task item's version. Public receipt API permits
        # an omitted version; never pass Task's version as if it were Resource's token.
        received = self.call("target MO confirms Resource transfer", "POST", receipt_path, "TARGET_MO", body)
        state(received, "RECEIVED")
        rows = self.wait_read("Resource receipt reconciles into Task", responses_path, "LEADER", predicate=lambda rows:
            (self.response_material(rows) or {}).get("status") == "TRANSFERRED" and
            (self.response_material(rows) or {}).get("pendingSync") is False)
        synced = self.response_material(rows)
        # PostgreSQL timestamps retain microseconds; a pre-persistence response may
        # contain one extra .NET tick digit. Compare at database timestamp precision.
        confirmed_at = datetime.fromisoformat(received["receivedConfirmedAt"].replace("Z", "+00:00"))
        synced_at = datetime.fromisoformat(synced["receivedConfirmedAt"].replace("Z", "+00:00"))
        require(synced["receivedUsableQuantity"] == 1 and synced["receivedUnusableQuantity"] == 0 and
                synced["receivedConfirmedBy"] == self.users["TARGET_MO"]["id"] and
                abs(synced_at - confirmed_at) <= timedelta(microseconds=1) and
                synced["sourceWorkAreaId"] == self.ids["sourceArea"], "Task receipt differs from Resource confirmation")
        self.call("receipt cannot be confirmed twice", "POST", receipt_path, "TARGET_MO",
                  {**body, **version(received)}, expected=(409,))
        require(self.inventory(self.ids["targetArea"], "TARGET_MO")["confirmedAvailable"] == 1, "Target stock was not received exactly once")
        self.report["observations"]["material"] = {"resourceReceipt": received, "taskMaterial": synced}
        for name in ("source", "target"):
            history = self.call("verify unique " + name + " transfer ledger", "GET",
                f"/api/v1/materials/{self.ids['material']}/allocation-history?workAreaId={self.ids[name + 'Area']}", "LEADER")
            transfers = [row for row in history if row.get("referenceId") == item["id"]]
            # The ledger stores outflows with a negative quantity; offer/receipt DTOs use absolute quantities.
            require(len(transfers) == 1 and transfers[0]["quantity"] == (-1 if name == "source" else 1) and transfers[0]["transactionType"] ==
                    ("TRANSFER_OUT" if name == "source" else "TRANSFER_IN"), "Transfer ledger is missing or duplicated")

    def assignments(self):
        return self.call("read nomination assignments", "GET", f"/api/v1/tasks/{self.ids['nominationTask']}/assignments", "LEADER")

    def community_consent(self):
        self.current_stage = "community-consent"
        body = self.task_body("community nomination")
        body["requiredVolunteers"] = 2
        task = self.call("create task for community nominations", "POST",
            f"/api/v1/work-areas/{self.ids['targetArea']}/tasks", "LEADER", body, expected=(201,))
        self.ids["nominationTask"] = task["id"]
        request = self.call("create staffing request", "POST", "/api/v1/support-requests", "LEADER",
            {"seasonId": self.ids["season"], "workAreaId": self.ids["targetArea"], "supportType": "STAFFING",
             "description": self.prefix, "deadline": (self.now + timedelta(days=1)).date().isoformat(),
             "positions": [{"name": "Community participants", "requiredVolunteerCount": 2, "skills": [], "taskId": task["id"]}],
             "materials": []}, expected=(201,))
        self.ids["staffingSupportRequest"] = request["id"]
        path = "/api/v1/support-requests/" + request["id"]
        published = self.call("publish staffing request", "POST", path + "/publish", "LEADER", version(request))
        response = self.call("community member nominates two other volunteers", "POST", path + "/responses", "VOLUNTEER",
            {"respondingCommunityId": self.ids["sourceCommunity"], "members": [
                {"positionId": published["positions"][0]["id"], "volunteerId": self.users[alias]["id"]}
                for alias in ("NOMINEE", "DECLINER")], "materials": [], "note": self.prefix}, expected=(201,))
        for member in response["members"]:
            approved = self.call("leader approves community nomination", "POST",
                f"/api/v1/support-response-members/{member['id']}/approve", "LEADER", version(member))
            require(approved["requiresConfirmation"] is True and approved["participationAccepted"] is False
                    and approved.get("assignmentId"), "Nomination bypassed volunteer consent")
        pending = self.assignments()
        require(len(pending) == 2 and all(row["status"] == "PENDING" and row["assignmentType"] == "SUPPORT"
                                       for row in pending), "Nominated assignments must await consent")
        for alias, action in (("NOMINEE", "accept"), ("DECLINER", "decline")):
            assignment = next(row for row in pending if row["volunteerId"] == self.users[alias]["id"])
            route = f"/api/v1/task-assignments/{assignment['id']}/{action}"
            body = {**version(assignment), **({"reason": "Unavailable for nominated support"} if action == "decline" else {})}
            self.call("nominator cannot consent for nominee", "POST", route, "VOLUNTEER", body, expected=(403,))
            result = self.call("nominee explicitly " + action, "POST", route, alias, body)
            state(result, "ACCEPTED" if action == "accept" else "DECLINED")
        rows = self.call("verify consent in support response", "GET", path + "/responses", "LEADER")
        members = {item["volunteerId"]: item for row in rows for item in row["members"]}
        accepted, declined = members[self.users["NOMINEE"]["id"]], members[self.users["DECLINER"]["id"]]
        require(accepted["participationAccepted"] is True and accepted["status"] == "APPROVED" and
                declined["participationAccepted"] is False and declined["status"] == "CANCELLED" and
                declined.get("participationReason") == "Unavailable for nominated support", "Consent did not reconcile to support members")
        summary = self.call("Parish consent summary", "GET", f"/api/v1/seasons/{self.ids['season']}/support-summary", "PARISH")
        community = next((row for row in summary["communities"] if row["respondingCommunityId"] == self.ids["sourceCommunity"]), None)
        require(community is not None and community["offeredMembers"] == 2 and community["acceptedMembers"] == 1
                and community["awaitingConfirmationMembers"] == 0 and community["cancelledMembers"] == 1,
                "Community summary does not match explicit consent decisions")
        self.report["observations"]["consent"] = {"acceptedMember": accepted, "declinedMember": declined,
                                                    "communitySummary": community}

    def scenario(self):
        self.preflight()
        self.fixtures()
        self.material_support()
        self.community_consent()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--allow-mutations", action="store_true")
    parser.add_argument("--test-context")
    parser.add_argument("--env-file", type=Path, default=ROOT / ".env")
    parser.add_argument("--compose-file", type=Path, action="append", default=[])
    parser.add_argument("--gateway", default="http://localhost:6102")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--timeout", type=float, default=20)
    parser.add_argument("--projection-timeout", type=float, default=180)
    parser.add_argument("--max-seconds", type=float, default=900)
    parser.add_argument("--login-retry-timeout", type=float, default=180,
                        help="Budget for explicit login 429 retries; each retry waits Gateway's fixed 60s")
    args = parser.parse_args()
    if not args.execute:
        print(json.dumps({"outcome": "prepared-only", "testContext": CONTEXT, "networkRequests": 0,
            "mutationsExecuted": 0, "stages": ["read-only isolation preflight", "two areas before activation",
                "shared Harness purchase and source stock", "actual source MO offer and leader approval",
                "Resource receipt to Task reconciliation", "community nomination acceptance and decline"],
            "readyFor": "Explicit execution on the isolated stack after broker checks"}, indent=2))
        return 0
    report = {"outcome": "failed", "testContext": CONTEXT}
    output = None
    try:
        require(args.allow_mutations and args.test_context == CONTEXT, "Execute requires mutation opt-in and exact isolated context")
        require(args.timeout > 0 and args.projection_timeout > 0 and args.max_seconds > 0 and
                args.login_retry_timeout > 0, "Timeouts must be positive")
        require(args.output is not None and args.output.is_absolute() and args.output.suffix.lower() == ".json",
                "A new absolute external JSON report path is required")
        candidate = args.output.resolve()
        require(not candidate.is_relative_to(ROOT) and not any((parent / ".git").exists() for parent in candidate.parents),
                "JSON reports must be outside every Git worktree")
        candidate.parent.mkdir(parents=True, exist_ok=True)
        with candidate.open("x", encoding="utf-8") as stream:
            json.dump({"outcome": "running", "testContext": CONTEXT}, stream)
        output = candidate
        settings = load_settings(args.env_file)
        require(bool(settings.get("SEED_ADMIN_PASSWORD")), "SEED_ADMIN_PASSWORD is required")
        settings["SMOKE_ADMIN_PASSWORD"] = settings["SEED_ADMIN_PASSWORD"]
        settings["SMOKE_ADMIN_EMAIL"] = settings.get("SEED_ADMIN_EMAIL", "admin@grottoworks.local")
        settings["SMOKE_GATEWAY_URL"] = base_url(args.gateway)
        client = SupportSmoke(args, settings, report)
        client.scenario()
        report["outcome"] = "passed"
    except CheckFailed as error:
        report["reason"] = str(error)
    except KeyboardInterrupt:
        report["reason"] = "Interrupted; own fixture IDs retained in report"
    except Exception:
        report["reason"] = "Support acceptance operation failed; sensitive diagnostics suppressed"
    if output:
        try:
            output.write_text(json.dumps(report, indent=2), encoding="utf-8")
        except Exception:
            print('{"outcome":"failed","reason":"Cannot write external report"}')
            return 1
    print(json.dumps({"outcome": report["outcome"], "reason": report.get("reason")}, indent=2))
    return 0 if report["outcome"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
