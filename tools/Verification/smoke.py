#!/usr/bin/env python3
"""Opt-in API smoke verification. Python 3.10+, standard library only.

Without --execute this prints a plan and makes no network requests.
Business APIs always use the gateway; direct service calls are GET /internal only.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import time
from datetime import datetime, timedelta, timezone
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener
import uuid


STAGES = ("security", "fixture", "evidence", "task", "readiness", "corrective", "purchase", "support")
DEPENDENCIES = {
    "security": (), "fixture": (), "evidence": ("fixture",), "task": ("evidence",),
    "readiness": ("fixture",), "corrective": ("readiness",),
    "purchase": ("evidence",), "support": ("fixture",),
}


class CheckFailed(Exception):
    """Only fixed, non-sensitive messages belong in this exception."""


class StageSkipped(Exception):
    pass


def load_settings(env_file: Path | None) -> dict[str, str]:
    """Read simple dotenv assignments without executing shell or expanding values."""
    values = {}
    if env_file:
        for line in env_file.read_text(encoding="utf-8-sig").splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("export "):
                line = line[7:]
            key, separator, value = line.partition("=")
            if not separator or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", key.strip()):
                continue
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                value = value[1:-1]
            else:
                value = re.split(r"\s+#", value, maxsplit=1)[0].rstrip()
            values[key.strip()] = value
    values.update(os.environ)
    return values


def base_url(value: str) -> str:
    parts = urlsplit(value)
    if (parts.scheme not in ("http", "https") or not parts.hostname or parts.username
            or parts.password or parts.query or parts.fragment or parts.path not in ("", "/")):
        raise CheckFailed("API base URLs must be HTTP(S) origins without credentials or paths")
    return value.rstrip("/")


def selected_stages(value: str) -> list[str]:
    requested = set(value.split(","))
    if requested - set(STAGES):
        raise CheckFailed("Unknown stage selection")
    def include(stage):
        for dependency in DEPENDENCIES[stage]:
            if dependency not in requested:
                requested.add(dependency)
                include(dependency)
    for stage in tuple(requested):
        include(stage)
    return [stage for stage in STAGES if stage in requested]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise CheckFailed(message)


def state(row: dict, expected: str) -> None:
    require(row.get("status") == expected, "Unexpected lifecycle state")


def version(row: dict) -> dict:
    value = row.get("version", row.get("checkVersion"))
    require(isinstance(value, int) and value > 0, "Response omitted a valid version")
    return {"version": value}


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None  # Never forward bearer tokens, internal keys, or signed URLs.


class Harness:
    def __init__(self, args, settings):
        self.args, self.settings = args, settings
        self.gateway = base_url(settings.get("SMOKE_GATEWAY_URL", "http://localhost:8080"))
        self.internal = {
            name: base_url(settings[key]) for name, key in (
                ("task", "SMOKE_TASK_INTERNAL_URL"), ("resource", "SMOKE_RESOURCE_INTERNAL_URL"))
            if settings.get(key)
        }
        self.key = settings.get("SMOKE_INTERNAL_API_KEY", settings.get("INTERNAL_API_KEY", ""))
        self.run_id = uuid.uuid4().hex[:16]
        self.prefix = "smoke-" + self.run_id
        self.tokens, self.users, self.ids = {}, {}, {}
        self.opener = build_opener(NoRedirect(), ProxyHandler({}))
        self.checks, self.stage_results = [], []
        self.current_stage = "setup"
        self.now = datetime.now(timezone.utc)
        self.deadline = (self.now + timedelta(days=2)).isoformat()
        self.evidence = []
        self.uploads = {}

    def transport(self, method, path, role=None, body=None, headers=None, direct=None):
        if direct:
            require(method == "GET" and path.startswith("/internal/"), "Direct calls must be internal reads")
            origin = self.internal[direct]
        else:
            require(path.startswith("/api/v1/") or path.startswith("/internal/"), "Unsupported API path")
            origin = self.gateway
        request_headers = {"Accept": "application/json", "X-Correlation-ID": self.prefix}
        if role:
            require(role in self.tokens, "Required account is not authenticated")
            request_headers["Authorization"] = "Bearer " + self.tokens[role]
        if body is not None:
            request_headers["Content-Type"] = "application/json"
        request_headers.update(headers or {})
        request = Request(origin + path, method=method, headers=request_headers,
                          data=json.dumps(body).encode() if body is not None else None)
        try:
            response = self.opener.open(request, timeout=self.args.timeout)
        except HTTPError as error:
            response = error
        except (URLError, TimeoutError, socket.timeout, OSError):
            raise CheckFailed("HTTP transport failed; check stack connectivity") from None
        with response:
            raw = response.read(2_000_001)
            require(len(raw) <= 2_000_000, "Response exceeded the inspection limit")
            try:
                payload = json.loads(raw) if raw else None
            except (ValueError, UnicodeError):
                payload = None
            return response.code, payload

    def call(self, label, method, path, role=None, body=None, expected=(200,), code=None, **kwargs):
        started = time.monotonic()
        result = {"stage": self.current_stage, "check": label, "method": method}
        self.checks.append(result)
        try:
            status_code, payload = self.transport(method, path, role, body, **kwargs)
            result["httpStatus"] = status_code
            if isinstance(payload, dict) and payload.get("code"):
                result["problemCode"] = payload["code"]
            if isinstance(payload, dict) and isinstance(payload.get("errors"), dict):
                result["validationFields"] = list(payload["errors"])
            require(status_code in expected, "Unexpected HTTP status")
            if code:
                require(isinstance(payload, dict) and payload.get("code") == code,
                        "Unexpected problem code")
            result["outcome"] = "passed"
            return payload.get("data", payload) if isinstance(payload, dict) else payload
        except CheckFailed as error:
            result.update(outcome="failed", reason=str(error))
            raise
        finally:
            result["durationMs"] = round((time.monotonic() - started) * 1000)

    def wait_read(self, label, path, role, predicate=lambda data: True, direct=None):
        """Retry only reads for eventual projections; never retry a mutation on timeout."""
        started = time.monotonic()
        attempts, status_code = 0, None
        while True:
            attempts += 1
            try:
                status_code, payload = self.transport("GET", path, role, direct=direct,
                    headers={"X-Internal-Api-Key": self.key} if direct else None)
                data = payload.get("data") if isinstance(payload, dict) else None
                if status_code == 200 and predicate(data):
                    self.checks.append({"stage": self.current_stage, "check": label, "method": "GET",
                        "outcome": "passed", "httpStatus": 200, "attempts": attempts})
                    return data
                if status_code not in (200, 403, 404, 503):
                    break
            except CheckFailed:
                pass
            if time.monotonic() - started >= self.args.projection_timeout:
                break
            time.sleep(min(1, self.args.projection_timeout))
        self.checks.append({"stage": self.current_stage, "check": label, "method": "GET",
            "outcome": "failed", "httpStatus": status_code, "attempts": attempts})
        raise CheckFailed("Projection did not become observable within the wait limit")

    def login(self, role, email, password):
        token = self.call("login " + role, "POST", "/api/v1/auth/login",
                          body={"email": email, "password": password})
        require(isinstance(token, dict) and isinstance(token.get("accessToken"), str),
                "Login did not return an access token")
        self.tokens[role] = token["accessToken"]
        profile = self.call("profile " + role, "GET", "/api/v1/users/me", role)
        require(profile.get("role") == role, "Authenticated account has the wrong role")
        self.users[role] = profile

    def admin(self):
        if "ADMIN" not in self.tokens:
            email, password = self.settings.get("SMOKE_ADMIN_EMAIL"), self.settings.get("SMOKE_ADMIN_PASSWORD")
            require(bool(email and password), "SMOKE_ADMIN_EMAIL and SMOKE_ADMIN_PASSWORD are required")
            self.login("ADMIN", email, password)

    def security(self):
        self.call("gateway anonymous denied", "GET", "/api/v1/users/me", expected=(401,))
        self.call("gateway invalid bearer denied", "GET", "/api/v1/users/me",
                  headers={"Authorization": "Bearer smoke-invalid-token"}, expected=(401,))
        self.admin()
        for headers in ({}, {"X-Internal-Api-Key": self.key}):
            self.call("gateway internal route absent", "GET", "/internal/work-area-task-counts?workAreaIds=" + str(uuid.uuid4()),
                      "ADMIN", headers=headers, expected=(404,))
        if "task" not in self.internal:
            self.checks.append({"stage": "security", "check": "direct internal authentication",
                                "outcome": "skipped", "reason": "SMOKE_TASK_INTERNAL_URL not set"})
            return
        path = "/internal/work-area-task-counts?workAreaIds=" + str(uuid.uuid4())
        for label, headers in (("no key", {}), ("wrong key", {"X-Internal-Api-Key": "invalid-smoke-key"})):
            self.call("direct internal denies " + label, "GET", path, headers=headers,
                      direct="task", expected=(401,), code="INTERNAL_AUTH_REQUIRED")
        if self.key:
            self.call("direct internal accepts configured key", "GET", path, direct="task",
                      headers={"X-Internal-Api-Key": self.key})
        else:
            self.checks.append({"stage": "security", "check": "direct internal valid key",
                                "outcome": "skipped", "reason": "Internal API key not configured"})

    def fixture(self):
        self.admin()
        parish = self.call("create isolated parish", "POST", "/api/v1/parishes", "ADMIN",
                           {"name": self.prefix, "description": "Disposable API smoke fixture"}, expected=(201,))
        self.ids["parish"] = parish["id"]
        for role in ("PARISH", "LEADER", "MO", "VOLUNTEER"):
            email = f"{self.prefix}-{role.lower()}@example.test"
            password = "Smoke1!" + secrets.token_urlsafe(24)
            account = self.call("create fixture account " + role, "POST", "/api/v1/admin/users", "ADMIN",
                {"email": email, "displayName": self.prefix + " " + role, "password": password,
                 "role": role, "parishId": parish["id"]}, expected=(201,))
            self.ids["user_" + role] = account["id"]
            self.login(role, email, password)
        for role in ("PARISH", "LEADER", "MO", "VOLUNTEER"):
            self.call("admin roles denied to " + role, "GET", "/api/v1/admin/users", role, expected=(403,))
        community = self.call("create fixture community", "POST",
            f"/api/v1/parishes/{parish['id']}/communities", "PARISH", {"name": self.prefix}, expected=(201,))
        self.ids["community"] = community["id"]
        start = (self.now - timedelta(days=1)).date().isoformat()
        end = (self.now + timedelta(days=7)).date().isoformat()
        season = self.call("create fixture season", "POST", "/api/v1/seasons", "PARISH",
            {"parishId": parish["id"], "name": self.prefix, "seasonYear": self.now.year,
             "startDate": start, "endDate": end, "estimatedBudget": 100000}, expected=(201,))
        self.ids["season"] = season["id"]
        area = self.call("create fixture work area with coordinates", "POST",
            f"/api/v1/seasons/{season['id']}/work-areas", "PARISH",
            {"name": self.prefix, "communityId": community["id"], "startDate": start,
             "deadline": end, "latitude": 10.762622, "longitude": 106.660172}, expected=(201,))
        self.ids["workArea"] = area["id"]
        path = "/api/v1/work-areas/" + area["id"]
        for role, suffix in (("LEADER", "leader"), ("MO", "material-officer")):
            area = self.call("assign work area " + role, "PUT", path + "/" + suffix, "PARISH",
                {"userId": self.users[role]["id"]}, headers={"If-Match": f'"{area["version"]}"'})
        require(area.get("leaderId") == self.users["LEADER"]["id"], "Leader assignment did not persist")
        require(area.get("materialOfficerId") == self.users["MO"]["id"], "MO assignment did not persist")
        self.call("invalid latitude rejected", "PATCH", path, "PARISH", {"latitude": 91}, expected=(422,))
        area = self.call("patch coordinate pair", "PATCH", path, "PARISH",
            {"latitude": 10.77, "longitude": 106.67}, headers={"If-Match": f'"{area["version"]}"'})
        saved = self.call("read persisted coordinates", "GET", path, "PARISH")
        require(saved.get("latitude") == 10.77 and saved.get("longitude") == 106.67,
                "Coordinate update did not persist")
        self.call("volunteer cannot manage work area", "PATCH", path, "VOLUNTEER",
                  {"name": self.prefix + " denied"}, expected=(403,))
        self.call("activate isolated season", "POST", f"/api/v1/seasons/{season['id']}/activate",
                  "PARISH", version(season))
        self.wait_read("TaskExecution work area projection", path + "/tasks", "LEADER",
                       predicate=lambda data: isinstance(data, list))
        if self.args.docker_scope_checks:
            self.wait_docker_scopes(area)

    def wait_docker_scopes(self, area):
        """Administrative verification of isolated DBs; never part of a business API."""
        area_id = str(uuid.UUID(area["id"]))
        leader = str(uuid.UUID(self.users["LEADER"]["id"]))
        officer = str(uuid.UUID(self.users["MO"]["id"]))
        expected_version = int(area["version"])
        query = ("SELECT count(*) FROM work_area_scope WHERE work_area_id='" + area_id +
                 "' AND leader_user_id='" + leader + "' AND material_officer_user_id='" + officer +
                 "' AND source_version >= " + str(expected_version) + ";")
        for database in ("verify_task_db", "verify_resource_db", "verify_readiness_db"):
            started = time.monotonic()
            while True:
                result = subprocess.run(["docker", "compose", "exec", "-T", "postgres", "psql",
                    "-U", self.settings.get("POSTGRES_USER", "postgres"), "-d", database, "-At", "-v", "ON_ERROR_STOP=1"],
                    input=query, text=True, capture_output=True, timeout=self.args.timeout)
                if result.returncode == 0 and result.stdout.strip() == "1":
                    self.checks.append({"stage": "fixture", "check": database + " has latest broker-only ownership", "outcome": "passed"})
                    break
                require(time.monotonic() - started < self.args.projection_timeout,
                        "Broker projection did not reach the latest assignment version")
                time.sleep(1)

    def task_body(self, name):
        return {"name": self.prefix + " " + name, "description": "Disposable smoke task",
                "completionCriteria": "Record progress and approve completion", "priority": "MEDIUM",
                "estimatedMinutes": 30, "requiredVolunteers": 1, "deadline": self.deadline}

    def create_task(self, label):
        task = self.call("create " + label, "POST", f"/api/v1/work-areas/{self.ids['workArea']}/tasks",
                         "LEADER", self.task_body(label), expected=(201,))
        self.ids[label] = task["id"]
        state(task, "OPEN")
        return task

    def read_task(self, task_id):
        return self.call("refresh task version", "GET", f"/api/v1/tasks/{task_id}", "LEADER")

    def evidence_stage(self):
        image = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6vVsAAAAASUVORK5CYII=")
        checksum = hashlib.sha256(image).hexdigest()
        allowed_origin = base_url(self.settings.get("MINIO_PUBLIC_ENDPOINT", "http://localhost:9000"))
        for role, purpose in (("VOLUNTEER", "PROGRESS_EVIDENCE"), ("LEADER", "READINESS_EVIDENCE"), ("VOLUNTEER", "PURCHASE_RECEIPT")):
            upload = self.call("request " + purpose, "POST", "/api/v1/files/upload-requests", role,
                {"fileName": self.prefix + ".png", "contentType": "image/png", "sizeBytes": len(image),
                 "purpose": purpose, "checksum": checksum}, expected=(201,))
            url = upload["uploadUrl"]
            require(urlsplit(url).scheme + "://" + urlsplit(url).netloc == allowed_origin,
                    "Object storage returned an unexpected public origin")
            # Signed object-store requests carry only the returned storage headers, never API credentials.
            request = Request(url, data=image, headers=upload["headers"], method=upload["method"])
            with self.opener.open(request, timeout=self.args.timeout) as response:
                require(response.code in (200, 204), "Presigned upload failed")
            file = self.call("complete " + purpose, "POST", f"/api/v1/files/{upload['fileId']}/complete",
                             role, {"checksum": checksum})
            replay = self.call("idempotent complete " + purpose, "POST", f"/api/v1/files/{upload['fileId']}/complete",
                               role, {"checksum": checksum})
            require(file == replay, "Completing the same upload twice changed its identity")
            self.uploads[purpose] = file["id"]
            download = self.call("owner download " + purpose, "GET", f"/api/v1/files/{file['id']}/download-url", role)
            with self.opener.open(download["downloadUrl"], timeout=self.args.timeout) as response:
                require(hashlib.sha256(response.read()).hexdigest() == checksum, "Completed file bytes differ")
            other_role = "LEADER" if role != "LEADER" else "VOLUNTEER"
            self.call("other uploader denied " + purpose, "GET", f"/api/v1/files/{file['id']}/download-url",
                      other_role, expected=(404,), code="FILE_NOT_FOUND")
        self.evidence = [{"fileId": self.uploads["PROGRESS_EVIDENCE"], "caption": self.prefix}]

    def complete_task(self, task, label):
        path = "/api/v1/tasks/" + task["id"]
        assignment = self.call(label + " assign volunteer", "POST", path + "/assignments", "LEADER",
            {"volunteerId": self.users["VOLUNTEER"]["id"], "note": self.prefix}, expected=(201,))
        self.ids[label + "Assignment"] = assignment["id"]
        assignment_path = "/api/v1/task-assignments/" + assignment["id"]
        self.call(label + " check-in before ready denied", "POST", assignment_path + "/attendance/check-in-requests",
                  "VOLUNTEER", {"note": self.prefix}, headers={"Idempotency-Key": str(uuid.uuid4())},
                  expected=(409,))
        assignment = self.call(label + " accept assignment", "POST", assignment_path + "/accept",
                               "VOLUNTEER", version(assignment))
        state(assignment, "ACCEPTED")
        task = self.read_task(task["id"])
        ready = self.call(label + " mark ready", "POST", path + "/ready", "LEADER", version(task))
        state(ready, "NOT_STARTED")
        self.call(label + " stale version denied", "POST", path + "/ready", "LEADER", version(task),
                  expected=(412,), code="TASK_VERSION_MISMATCH")
        checkin_key = {"Idempotency-Key": str(uuid.uuid4())}
        attendance = self.call(label + " request check-in", "POST", assignment_path + "/attendance/check-in-requests",
                               "VOLUNTEER", {"note": self.prefix}, headers=checkin_key, expected=(201,))
        replay = self.call(label + " replay check-in", "POST", assignment_path + "/attendance/check-in-requests",
                           "VOLUNTEER", {"note": self.prefix}, headers=checkin_key, expected=(201,))
        require(replay["id"] == attendance["id"], "Idempotent replay created another attendance request")
        approved = self.call(label + " approve attendance", "POST",
            f"/api/v1/attendance-requests/{attendance['id']}/approve", "LEADER", version(attendance))
        state(approved, "APPROVED")
        task = self.read_task(task["id"])
        state(task, "IN_PROGRESS")
        sessions = self.call(label + " read attendance", "GET", "/api/v1/users/me/attendance?workAreaId=" + self.ids["workArea"], "VOLUNTEER")
        session = next((row for row in sessions if row["assignmentId"] == assignment["id"] and row.get("checkOutAt") is None), None)
        require(session is not None, "Approved attendance did not create an open session")
        self.call(label + " check out", "POST", assignment_path + "/attendance/check-out", "VOLUNTEER",
                  version(session), headers={"Idempotency-Key": str(uuid.uuid4())})
        task = self.read_task(task["id"])
        duplicate_file = str(uuid.uuid4())
        self.call(label + " duplicate evidence rejected", "POST", path + "/progress", "VOLUNTEER",
            {"percentComplete": 25, "note": self.prefix, **version(task), "evidence": [
                {"fileId": duplicate_file}, {"fileId": duplicate_file}]}, expected=(422,), code="TASK_EVIDENCE_INVALID")
        evidence = self.evidence if label == "work" else []
        progress = self.call(label + " add progress", "POST", path + "/progress", "VOLUNTEER",
                  {"percentComplete": 50, "note": self.prefix, "evidence": evidence, **version(task)}, expected=(201,))
        if evidence:
            file_id = evidence[0]["fileId"]
            self.wait_read("Leader reads attached evidence", path + f"/progress/{progress['id']}/evidence/{file_id}/download-url", "LEADER")
            self.call("attached file cannot be deleted", "DELETE", f"/api/v1/files/{file_id}", "VOLUNTEER", expected=(409,))
        task = self.read_task(task["id"])
        submitted = self.call(label + " submit", "POST", path + "/submit", "VOLUNTEER",
                              {"note": self.prefix, "evidence": [], **version(task)})
        state(submitted, "SUBMITTED")
        self.call(label + " volunteer cannot approve", "POST", path + "/approve", "VOLUNTEER",
                  version(submitted), expected=(403,))
        revision = self.call(label + " request revision", "POST", path + "/request-revision", "LEADER",
                             {"reason": "Exercise the revision path", **version(submitted)})
        state(revision, "REVISION_REQUIRED")
        submitted = self.call(label + " resubmit", "POST", path + "/resubmit", "VOLUNTEER",
                              {"note": "Revision complete", "evidence": [], **version(revision)})
        state(submitted, "SUBMITTED")
        self.wait_read(label + " read review package", path + "/review-package", "LEADER")
        completed = self.call(label + " approve completion", "POST", path + "/approve", "LEADER", version(submitted))
        state(completed, "COMPLETED")
        rows = self.call(label + " completed assignment", "GET", path + "/assignments", "LEADER")
        state(next(row for row in rows if row["id"] == assignment["id"]), "COMPLETED")
        return completed

    def task(self):
        self.complete_task(self.create_task("workTask"), "work")

    def readiness(self):
        self.wait_read("Parish aggregate enters readiness review", f"/api/v1/work-areas/{self.ids['workArea']}", "PARISH",
                       predicate=lambda row: (row or {}).get("status") == "READINESS_REVIEW")
        self.wait_read("Readiness season projection", f"/api/v1/seasons/{self.ids['season']}/readiness", "PARISH",
            predicate=lambda row: any(area.get("workAreaId") == self.ids["workArea"] and
                area.get("workAreaStatus") == "READINESS_REVIEW" for area in (row or {}).get("areas", [])))
        check = self.call("create readiness checklist", "POST",
            f"/api/v1/work-areas/{self.ids['workArea']}/readiness-checks", "LEADER",
            {"name": self.prefix, "items": [{"name": "Safety inspection", "mandatory": True}]}, expected=(201,))
        self.ids["readinessCheck"] = check["id"]
        self.ids["readinessItem"] = check["items"][0]["id"]
        path = "/api/v1/readiness-checks/" + check["id"]
        self.call("pending checklist cannot confirm", "POST", path + "/confirm", "LEADER", version(check), expected=(409,))
        item = self.call("pass readiness inspection", "PUT", path + "/items/" + self.ids["readinessItem"],
                         "LEADER", {"result": "PASSED", "note": self.prefix,
                            "evidenceFileIds": [self.uploads["READINESS_EVIDENCE"]], **version(check)})
        self.wait_read("Readiness evidence attachment finalized",
            path + "/items/" + self.ids["readinessItem"] + "/evidence/" + self.uploads["READINESS_EVIDENCE"] + "/download-url", "LEADER")
        ready = self.call("confirm readiness", "POST", path + "/confirm", "LEADER", version(item))
        state(ready, "READY")

    def corrective(self):
        path = "/api/v1/readiness-checks/" + self.ids["readinessCheck"]
        check = self.call("read checklist version", "GET", path, "LEADER")
        reopened = self.call("reopen readiness", "POST", path + "/reopen", "LEADER",
                             {"reason": "Exercise corrective workflow", **version(check)})
        item_path = path + "/items/" + self.ids["readinessItem"]
        failed = self.call("fail readiness inspection", "PUT", item_path, "LEADER",
                           {"result": "FAILED", "note": "Requires correction", **version(reopened)})
        definition = self.task_body("corrective")
        definition["requestId"] = str(uuid.uuid4())
        definition["requiredVolunteerCount"] = definition.pop("requiredVolunteers")
        linked = self.call("create corrective task through Readiness", "PUT", item_path + "/corrective-task",
                           "LEADER", {"create": definition, **version(failed)})
        require(bool(linked.get("correctiveTaskId")), "Corrective link omitted task ID")
        self.ids["correctiveTask"] = linked["correctiveTaskId"]
        task = self.read_task(linked["correctiveTaskId"])
        require(task.get("taskType") == "CORRECTIVE", "Created task is not corrective")
        self.complete_task(task, "corrective")
        check = self.wait_read("corrective completion projected to Readiness", path, "LEADER",
            predicate=lambda row: any(item.get("status") == "RECHECK" for item in (row or {}).get("items", [])))
        passed = self.call("recheck corrected item", "POST", item_path + "/recheck", "LEADER",
                           {"result": "PASSED", "note": "Correction verified", **version(check)})
        self.wait_read("corrected area returns to readiness review", f"/api/v1/seasons/{self.ids['season']}/readiness", "PARISH",
            predicate=lambda row: any(area.get("workAreaId") == self.ids["workArea"] and
                area.get("workAreaStatus") == "READINESS_REVIEW" for area in (row or {}).get("areas", [])))
        ready = self.call("confirm corrected readiness", "POST", path + "/confirm", "LEADER", version(passed))
        state(ready, "READY")

    def purchase(self):
        self.wait_read("Resource work area projection", f"/api/v1/work-areas/{self.ids['workArea']}/material-requirements", "MO")
        category = self.call("create isolated material category", "POST", "/api/v1/material-categories", "MO",
            {"code": self.prefix, "name": self.prefix}, expected=(201,))
        material = self.call("create isolated material", "POST", "/api/v1/materials", "MO",
            {"categoryId": category["id"], "code": self.prefix, "name": self.prefix, "unit": "piece"}, expected=(201,))
        self.ids["material"] = material["id"]
        self.call("create isolated material requirement", "POST",
            f"/api/v1/work-areas/{self.ids['workArea']}/material-requirements", "MO",
            {"materialId": material["id"], "requiredQuantity": 1}, expected=(201,))
        purchase = self.call("create purchase request", "POST", "/api/v1/purchase-requests", "MO",
            {"workAreaId": self.ids["workArea"], "reason": self.prefix,
             "items": [{"materialId": material["id"], "requestedQuantity": 1, "estimatedCost": 1000}]}, expected=(201,))
        self.ids["purchaseRequest"] = purchase["id"]
        path = "/api/v1/purchase-requests/" + purchase["id"]
        submitted = self.call("submit purchase request", "POST", path + "/submit", "MO", version(purchase))
        state(submitted, "PENDING_APPROVAL")
        approved = self.call("leader approves purchase", "POST", path + "/approve", "LEADER", version(submitted))
        state(approved, "APPROVED")
        task = self.call("create purchase task", "POST", path + "/purchase-tasks", "LEADER",
            {key: value for key, value in self.task_body("purchase").items() if key != "requiredVolunteers"}, expected=(201,))
        self.ids["purchaseTask"] = task["id"]
        require(task.get("taskType") == "PURCHASE", "Purchase task type was not persisted")
        assignment = self.call("assign purchase buyer", "POST", f"/api/v1/tasks/{task['id']}/assignments", "LEADER",
            {"volunteerId": self.users["VOLUNTEER"]["id"]}, expected=(201,))
        self.call("buyer accepts purchase task", "POST", f"/api/v1/task-assignments/{assignment['id']}/accept",
                  "VOLUNTEER", version(assignment))
        self.wait_read("buyer assignment projected to Resource", path, "LEADER",
                       predicate=lambda row: (row or {}).get("status") == "BUYER_ASSIGNED")
        recorded = self.call("buyer records purchased items and receipt", "POST", path + "/purchases", "VOLUNTEER",
            {"supplierName": self.prefix, "purchaseDate": self.now.date().isoformat(),
             "items": [{"purchaseRequestItemId": approved["items"][0]["id"], "purchasedQuantity": 1, "unitPrice": 1000}],
             "receipts": [{"id": self.uploads["PURCHASE_RECEIPT"]}]}, expected=(201,))
        self.ids["purchase"] = recorded["id"]
        receipt_path = "/api/v1/purchases/" + recorded["id"]
        self.wait_read("MO can read finalized purchase receipt", receipt_path + "/receipts/" +
                       self.uploads["PURCHASE_RECEIPT"] + "/download-url", "MO")
        delivered = self.call("MO confirms received purchase", "POST", receipt_path + "/confirm-delivery", "MO",
            {**version(recorded), "items": [{"purchaseItemId": recorded["items"][0]["id"],
                "receivedUsableQuantity": 1, "receivedUnusableQuantity": 0}]})
        state(delivered, "CONFIRMED")
        self.wait_read("delivery automatically completes purchase task", "/api/v1/tasks/" + task["id"], "LEADER",
                       predicate=lambda row: (row or {}).get("status") == "COMPLETED")

    def support(self):
        task = self.create_task("supportTask")
        request = self.call("create staffing support", "POST", "/api/v1/support-requests", "LEADER",
            {"seasonId": self.ids["season"], "workAreaId": self.ids["workArea"], "supportType": "STAFFING",
             "description": self.prefix, "deadline": (self.now + timedelta(days=1)).date().isoformat(),
             "positions": [{"name": "Smoke volunteer", "requiredVolunteerCount": 1,
                            "skills": [], "taskId": task["id"]}], "materials": []}, expected=(201,))
        self.ids["supportRequest"] = request["id"]
        path = "/api/v1/support-requests/" + request["id"]
        published = self.call("publish staffing support", "POST", path + "/publish", "LEADER", version(request))
        state(published, "PUBLISHED")
        self.call("canonical support responses route", "GET", path + "/responses", "LEADER")
        response = self.call("volunteer offers individual support", "POST", path + "/responses", "VOLUNTEER",
            {"members": [{"positionId": published["positions"][0]["id"], "volunteerId": self.users["VOLUNTEER"]["id"]}],
             "materials": [], "note": self.prefix}, expected=(201,))
        member = response["members"][0]
        approved_member = self.call("Leader approves self-volunteered member", "POST",
            "/api/v1/support-response-members/" + member["id"] + "/approve", "LEADER", version(member))
        require(approved_member.get("assignmentId"), "Support approval did not create an assignment")
        assignments = self.call("support assignment is immediately accepted", "GET", "/api/v1/tasks/" + task["id"] + "/assignments", "LEADER")
        state(next(row for row in assignments if row["id"] == approved_member["assignmentId"]), "ACCEPTED")
        self.call("Parish support summary", "GET", f"/api/v1/seasons/{self.ids['season']}/support-summary", "PARISH")
        self.call("volunteer cannot read support summary", "GET",
                  f"/api/v1/seasons/{self.ids['season']}/support-summary", "VOLUNTEER", expected=(403,))
        self.checks.append({"stage": "support", "check": "cross-community nominations and material transfers",
            "outcome": "skipped", "reason": "Requires a second community fixture and transfer inventory"})

    def run(self, stages):
        outcomes = {}
        for stage in stages:
            self.current_stage = stage
            if any(outcomes.get(dep) != "passed" for dep in DEPENDENCIES[stage]):
                outcome, reason = "skipped", "A required stage did not pass"
            else:
                try:
                    getattr(self, "evidence_stage" if stage == "evidence" else stage)()
                    outcome, reason = "passed", None
                except StageSkipped as error:
                    outcome, reason = "skipped", str(error)
                except CheckFailed as error:
                    outcome, reason = "failed", str(error)
                except Exception as error:
                    # Never serialize exception messages/tracebacks: they can contain URLs,
                    # request bodies, passwords, bearer tokens or presigned query strings.
                    outcome, reason = "failed", "Unexpected local exception: " + type(error).__name__
            outcomes[stage] = outcome
            self.stage_results.append({"stage": stage, "outcome": outcome, **({"reason": reason} if reason else {})})
        return {"runId": self.run_id, "fixturePrefix": self.prefix, "testContext": self.args.test_context,
                "mode": "executed", "startedAt": self.now.isoformat(), "fixtureIds": self.ids,
                "stages": self.stage_results, "checks": self.checks,
                "outcome": "failed" if "failed" in outcomes.values() else "passed"}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env-file", type=Path, help="Optional dotenv file; environment overrides it")
    parser.add_argument("--stages", default="security,task,readiness,purchase,support")
    parser.add_argument("--execute", action="store_true", help="Make HTTP requests (including login)")
    parser.add_argument("--allow-mutations", action="store_true", help="Allow creation of an isolated API fixture")
    parser.add_argument("--docker-scope-checks", action="store_true", help="Verify latest assignments in the three fixed verify_* database copies")
    parser.add_argument("--test-context", help="Operator-provided isolated-stack label, required for fixture stages")
    parser.add_argument("--timeout", type=float, default=15, help="Per-request timeout in seconds")
    parser.add_argument("--projection-timeout", type=float, default=60, help="Eventual projection wait in seconds")
    parser.add_argument("--output", type=Path, help="Write JSON to this path instead of stdout")
    args = parser.parse_args(argv)
    try:
        stages = selected_stages(args.stages)
        require(args.timeout > 0 and args.projection_timeout > 0, "Timeouts must be positive")
        if not args.execute:
            report = {"mode": "plan", "outcome": "not-run", "stages": stages,
                      "networkRequests": 0, "mutationsExecuted": 0}
        else:
            if "fixture" in stages:
                require(args.allow_mutations and bool(args.test_context),
                        "Fixture stages require --allow-mutations and --test-context")
            settings = load_settings(args.env_file)
            require(bool(settings.get("SMOKE_ADMIN_EMAIL") and settings.get("SMOKE_ADMIN_PASSWORD")),
                    "Set SMOKE_ADMIN_EMAIL and SMOKE_ADMIN_PASSWORD before execution")
            report = Harness(args, settings).run(stages)
    except CheckFailed as error:
        report = {"mode": "setup", "outcome": "failed", "reason": str(error)}
    except Exception as error:
        report = {"mode": "setup", "outcome": "failed", "reason": "Local setup error: " + type(error).__name__}
    encoded = json.dumps(report, indent=2) + "\n"
    if args.output:
        args.output.write_text(encoded, encoding="utf-8")
    else:
        print(encoded, end="")
    return 1 if report["outcome"] == "failed" else 0


if __name__ == "__main__":
    raise SystemExit(main())
