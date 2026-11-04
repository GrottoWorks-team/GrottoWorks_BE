"""Execute export/backup acceptance only against the fixed isolated verify_notify_db."""
import argparse
import io
import json
import os
from pathlib import Path
import subprocess
import time
import uuid
import zipfile
from smoke import Harness, CheckFailed, load_settings, require

SERVICES = {"Identity", "ParishCoordination", "TaskExecution", "Resource", "Readiness", "Reporting", "NotificationAudit"}


def job_state(job_id, settings):
    identity = str(uuid.UUID(job_id))
    sql = ("SELECT json_build_object('status',status,'fileId',artifact_file_id,'errorCode',error_code,"
           "'manifest',manifest_json) FROM system_job WHERE id='" + identity + "';")
    result = subprocess.run(["docker", "compose", "exec", "-T", "postgres", "psql", "-U",
        settings.get("POSTGRES_USER", "postgres"), "-d", "verify_notify_db", "-At", "-v", "ON_ERROR_STOP=1"],
        input=sql, text=True, capture_output=True, timeout=15)
    require(result.returncode == 0 and result.stdout.strip(), "Cannot read the isolated verification job")
    return json.loads(result.stdout)


def create_job(client, path, key, body):
    started = time.monotonic()
    while True:
        status, payload = client.transport("POST", path, "ADMIN", body, {"Idempotency-Key": key})
        if status != 429:
            return status, payload.get("data", payload)
        require(time.monotonic() - started < 75, "System-job rate limit did not recover")
        time.sleep(5)  # A throttled request never reached the command handler; the key remains stable.


def wait_job(job_id, settings):
    started = time.monotonic()
    while True:
        row = job_state(job_id, settings)
        if row["status"] == "Completed":
            require(row["fileId"], "Completed job omitted its artifact")
            return row
        require(row["status"] != "Failed", "System job failed: " + str(row.get("errorCode")))
        require(time.monotonic() - started < 240, "System job did not complete within the verification window")
        time.sleep(2)


def download(client, file_id):
    link = client.call("download system artifact", "GET", f"/api/v1/files/{file_id}/download-url", "ADMIN")
    with client.opener.open(link["downloadUrl"], timeout=30) as response:
        data = response.read(64 * 1024 * 1024 + 1)
    require(len(data) <= 64 * 1024 * 1024, "Verification artifact exceeds its inspection limit")
    return data


def run(args):
    settings = load_settings(args.env_file)
    args.timeout = 20
    args.projection_timeout = 120
    client = Harness(args, settings)
    client.current_stage = "system-jobs"
    client.admin()
    export_key = str(uuid.uuid4())
    code, export = create_job(client, "/api/v1/system-data-exports", export_key, {})
    require(code == 202, "Export request was not accepted")
    export_result = wait_job(export["jobId"], settings)
    export_data = download(client, export_result["fileId"])
    with zipfile.ZipFile(io.BytesIO(export_data)) as archive:
        require(archive.testzip() is None, "Export ZIP integrity failed")
        manifest = json.loads(archive.read("manifest.json"))
        require(manifest["complete"] and {row["service"] for row in manifest["sources"]} == SERVICES,
                "Export did not capture all seven services")
        for service in SERVICES:
            lines = [json.loads(line) for line in archive.read(service + ".jsonl").splitlines()]
            require(lines[-1].get("complete") is True, "Export source footer is incomplete")
            for row in lines:
                if row.get("type") == "row":
                    require(not ({"password_hash", "refresh_token", "token_hash", "secret_key"} & set(row["data"])),
                            "Export contains an authentication field")
    code, replay = create_job(client, "/api/v1/system-data-exports", export_key, {})
    require(code == 202 and replay["jobId"] == export["jobId"], "Export replay created a duplicate job")
    code, _ = create_job(client, "/api/v1/system-data-exports", export_key, {"services": ["Identity"]})
    require(code == 409, "Conflicting idempotent payload was accepted")
    code, backup = create_job(client, "/api/v1/backups", str(uuid.uuid4()), {})
    require(code == 202, "Backup request was not accepted")
    result = wait_job(backup["jobId"], settings)
    status = client.call("public backup status", "GET", "/api/v1/backups/" + backup["jobId"], "ADMIN")
    require(status["status"] == "COMPLETED", "Backup status did not report completion")
    args.artifact_dir.mkdir(parents=True, exist_ok=True)
    encrypted = args.artifact_dir / (backup["jobId"] + ".zip.enc")
    plain = args.artifact_dir / (backup["jobId"] + ".zip")
    encrypted.write_bytes(download(client, result["fileId"]))
    environment = os.environ.copy()
    environment["GROTTOWORKS_BACKUP_KEY"] = settings["GROTTOWORKS_BACKUP_KEY"]
    tool = Path(__file__).parent / "bin/Debug/net10.0/Verification.dll"
    decrypted = subprocess.run(["dotnet", str(tool), "decrypt", str(encrypted), str(plain), settings["BACKUP_KEY_ID"]],
        env=environment, capture_output=True, text=True, timeout=60)
    require(decrypted.returncode == 0, "Backup authentication or decryption failed")
    with zipfile.ZipFile(plain) as archive:
        require(archive.testzip() is None, "Decrypted backup ZIP integrity failed")
        manifest = json.loads(archive.read("manifest.json"))
        require(manifest["complete"], "Backup manifest is incomplete")
        dumps = {row["service"] for row in manifest["sources"] if "service" in row}
        require(dumps == SERVICES, "Backup is missing a database")
        target = args.artifact_dir / backup["jobId"]
        target.mkdir()
        # Fixed database/object names only. No untrusted archive path is extracted directly.
        for service in SERVICES:
            (target / (service + ".dump")).write_bytes(archive.read("databases/" + service + ".dump"))
        (target / "manifest.json").write_text(json.dumps(manifest, indent=2))
    return {"outcome": "passed", "exportJobId": export["jobId"], "backupJobId": backup["jobId"],
            "services": sorted(SERVICES), "backupDirectory": str(target), "checks": client.checks}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env-file", type=Path, required=True)
    parser.add_argument("--artifact-dir", type=Path, required=True)
    parser.add_argument("--test-context", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = run(args)
    except CheckFailed as error:
        report = {"outcome": "failed", "reason": str(error)}
    except Exception as error:
        report = {"outcome": "failed", "reason": type(error).__name__}
    args.output.write_text(json.dumps(report, indent=2))
    print(json.dumps({key: value for key, value in report.items() if key != "checks"}, indent=2))
    raise SystemExit(0 if report["outcome"] == "passed" else 1)
