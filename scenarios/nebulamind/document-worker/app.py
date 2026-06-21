#!/usr/bin/env python3
"""NebulaMind AI Corp - Document Parsing Worker.

Business-zone service for parsing remote documents and running conversion profiles.
Connected to dmz-service and biz-core networks.

Routes:
  GET  /                     - Worker info (service name, version, status, queue name)
  GET  /healthz              - Health check
  POST /api/parse            - Parse remote document (B3 SSRF + D3 command injection entry)
  GET  /api/tasks/<taskId>   - Query parse task status
  GET  /api/queue/stats      - Queue statistics (requires worker token from B2)

Vulnerabilities (by design, for CTF):
  B3: /api/parse fetches remote URLs via urllib.request.urlopen(url).
      When the URL targets an internal service (ai-console-api), the response
      includes trace metadata with service discovery info and a trace token.
      The B3 flag is embedded in metadata.internal_trace.
      Players discover the ai-console-api target via B2 (path traversal reading
      support-upload's worker.yml which lists internal endpoints).
  D3: /api/parse accepts a {profile} parameter used to build a conversion command:
      `convert --profile {profile}`. The profile filter only blocks & ; | but
      NOT $() backticks or newlines, allowing command injection.
      Players inject $(cat /opt/nebulamind/worker.flag) to read the D3 flag,
      which appears in the conversion log output.
      The injection is sandboxed: subprocess.run(shell=True) runs inside the
      container as non-root, no Docker socket, no privileges.
"""

from __future__ import annotations

import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from datetime import datetime

# Load shared flag helper
sys.path.insert(0, "/_shared/scripts")
try:
    from flag import get_flag  # type: ignore
except Exception:  # pragma: no cover - fallback if shared module unavailable
    def get_flag(name: str, default: str = "flag{not_configured}") -> str:
        key = f"GZCTF_FLAG_{name}" if name else ""
        if key and key in os.environ:
            return os.environ[key]
        return os.environ.get("GZCTF_FLAG", default)

from flask import Flask, Response, abort, jsonify, request, send_file

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "8080"))
HOST = os.environ.get("HOST", "0.0.0.0")

APP_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_DIR = os.environ.get("CONFIG_DIR", "/app/config")
UPLOAD_DIR = os.environ.get("UPLOAD_DIR", "/app/uploads")
WORKER_FLAG_FILE = "/opt/nebulamind/worker.flag"
SERVICE_ACCOUNT_FILE = "/opt/nebulamind/service-account.json"

# Worker token for queue stats (same token referenced in support-upload worker.yml)
# Players obtain this token via B2 (path traversal reading support-upload's worker.yml)
WORKER_TOKEN = os.environ.get(
    "WORKER_TOKEN", "nm_worker_token_8f3a9b2e5c1d4a6f"
)

def _service_host(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise RuntimeError(f"required environment variable {name} is not set")
    return value.lower()


AI_CONSOLE_API_HOST = _service_host("NM_AI_CONSOLE_API_HOST")
CACHE_BROKER_HOST = _service_host("NM_CACHE_BROKER_HOST")
DOCUMENT_WORKER_HOST = os.environ.get("NM_DOCUMENT_WORKER_HOST", "").strip().lower()


# Internal platform-injected hosts known to document-worker (for SSRF trace metadata).
# When SSRF targets these addresses, rich service discovery metadata is attached.
INTERNAL_SERVICES = {
    AI_CONSOLE_API_HOST: {
        "service_name": "ai-console-api",
        "version": "2026.06.3",
        "zone": "business",
        "endpoints": [
            "/healthz",
            "/api/v1/console/session/bootstrap",
            "/internal/metadata",
        ],
        "tenant": "tenant_001",
        "sso_client_id": "nm-portal-sso-prod",
        "trace_header": "X-NM-Trace",
    },
    CACHE_BROKER_HOST: {
        "service_name": "cache-broker",
        "version": "redis-7.2",
        "zone": "business",
        "endpoints": [":6379"],
    }
}

if DOCUMENT_WORKER_HOST:
    INTERNAL_SERVICES[DOCUMENT_WORKER_HOST] = {
        "service_name": "document-worker",
        "version": "2026.06.1",
        "zone": "business",
        "endpoints": ["/healthz", "/api/parse", "/api/tasks", "/api/queue/stats"],
    }

# Task ID validation
TASK_ID_RE = re.compile(r"^[A-Za-z0-9_-]+$")

# In-memory task store (simulates queue-backed task tracking)
TASKS: dict[str, dict] = {}

# ---------------------------------------------------------------------------
# Flask app
# ---------------------------------------------------------------------------

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = 50 * 1024 * 1024  # 50MB


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def now_str() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def now_iso() -> str:
    return datetime.now().strftime("%Y-%m-%dT%H:%M:%SZ")


def generate_task_id() -> str:
    """Generate realistic task ID: DOC-YYYYMMDD-XXXX."""
    now = datetime.now()
    rand = uuid.uuid4().hex[:4].upper()
    return f"DOC-{now.strftime('%Y%m%d')}-{rand}"


def generate_trace_token() -> str:
    """Generate realistic trace token: nm-<timestamp>-<random>."""
    ts = int(time.time() * 1000)
    rand = uuid.uuid4().hex[:8]
    return f"nm-{ts}-{rand}"


def read_b3_flag() -> str:
    """Read B3 flag via shared flag helper."""
    return get_flag("FLAG_WORKER_SSRF_METADATA", "flag{b3_ssrf_metadata_placeholder}")


def read_d3_flag_from_file() -> str:
    """Read D3 flag from injected file (used for verification only, not exposed)."""
    try:
        with open(WORKER_FLAG_FILE, "r", encoding="utf-8") as f:
            return f.read().strip()
    except (IOError, OSError):
        return ""


def extract_host(url: str) -> str:
    """Extract hostname from URL."""
    try:
        parsed = urllib.parse.urlparse(url)
        return parsed.hostname or ""
    except Exception:
        return ""


def is_internal_service(host: str) -> bool:
    """Check if host matches a known internal service."""
    if not host:
        return False
    host_lower = host.lower()
    return host_lower in INTERNAL_SERVICES


def fetch_url(url: str, timeout: int = 8) -> dict:
    """Fetch a remote URL and return content + status.

    This is the B3 SSRF sink: document-worker fetches arbitrary URLs.
    Only http/https schemes are allowed (realistic for a document parser).
    """
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme not in ("http", "https"):
        return {
            "ok": False,
            "status_code": 0,
            "content": "",
            "error": f"unsupported scheme: {parsed.scheme}",
        }

    req = urllib.request.Request(
        url,
        headers={
            "User-Agent": "NebulaMindDocWorker/2026.06 (+parse)",
            "Accept": "*/*",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = resp.read(10 * 1024 * 1024)  # cap at 10MB
            try:
                content = body.decode("utf-8", errors="replace")
            except Exception:
                content = repr(body[:4096])
            return {
                "ok": True,
                "status_code": resp.status,
                "content": content,
                "content_type": resp.headers.get("Content-Type", ""),
                "error": None,
            }
    except urllib.error.HTTPError as e:
        # HTTP error (e.g., 404, 500) - still return response info
        try:
            body = e.read(4096)
            content = body.decode("utf-8", errors="replace")
        except Exception:
            content = ""
        return {
            "ok": True,
            "status_code": e.code,
            "content": content,
            "content_type": e.headers.get("Content-Type", "") if e.headers else "",
            "error": None,
        }
    except (urllib.error.URLError, socket.timeout, ConnectionError, OSError) as e:
        reason = getattr(e, "reason", str(e))
        return {
            "ok": False,
            "status_code": 0,
            "content": "",
            "error": f"fetch failed: {reason}",
        }


def build_trace_metadata(url: str, fetch_result: dict) -> dict:
    """Build trace metadata for a parse response.

    B3 vulnerability: when the URL targets an internal service (ai-console-api),
    the metadata includes rich service discovery info and the B3 flag in
    internal_trace. This simulates a service that attaches debug trace info
    when fetching from internal services.
    """
    host = extract_host(url)
    trace_token = generate_trace_token()

    metadata = {
        "trace_token": trace_token,
        "trace_id": f"doc-{uuid.uuid4().hex[:12]}",
        "timestamp": now_iso(),
        "worker": "document-worker",
        "worker_version": "2026.06.1",
        "fetch_duration_ms": 0,
        "source_url": url,
    }

    if is_internal_service(host):
        # Internal service: attach full service discovery metadata
        svc_info = INTERNAL_SERVICES.get(host, {})
        metadata["service_info"] = {
            "service_name": svc_info.get("service_name", host),
            "version": svc_info.get("version", "unknown"),
            "zone": svc_info.get("zone", "business"),
            "host": host,
            "endpoints": svc_info.get("endpoints", []),
            "tenant": svc_info.get("tenant", "tenant_001"),
            "sso_client_id": svc_info.get("sso_client_id", ""),
            "trace_header": svc_info.get("trace_header", "X-NM-Trace"),
            "discovered_via": "service-registry",
            "health": "reachable" if fetch_result.get("ok") else "unreachable",
        }
        # B3 FLAG: embedded in internal_trace field for internal service SSRF
        metadata["internal_trace"] = {
            "debug": True,
            "trace_token": trace_token,
            "internal_flag": read_b3_flag(),
            "note": "internal service discovery trace (debug-only, do not expose externally)",
        }

    return metadata


def sanitize_profile(profile: str) -> tuple[bool, str]:
    """D3 vulnerability: profile filter only blocks & ; | but NOT $() backticks newlines.

    Returns (is_valid, reason).
    """
    if not profile:
        return True, ""

    # Only block obvious command chaining operators.
    # VULNERABILITY: $() backticks and newlines are NOT filtered,
    # allowing command substitution and injection.
    for ch in ["&", ";", "|"]:
        if ch in profile:
            return False, f"profile contains forbidden character: {ch!r}"

    return True, ""


def run_convert(profile: str) -> dict:
    """D3 vulnerability: run conversion command with injectable profile.

    Simulates calling `convert --profile {profile}`.
    The profile is filtered (poorly) and then passed to subprocess with shell=True.
    Players inject $(cat /opt/nebulamind/worker.flag) to read the D3 flag,
    which appears in the conversion log output.

    Safety: runs inside the container as non-root user, no Docker socket,
    no privileges. The injected command can only affect this container.
    """
    is_valid, reason = sanitize_profile(profile)
    if not is_valid:
        return {
            "command": None,
            "exit_code": -1,
            "log": f"[convert] rejected: {reason}\n",
            "error": reason,
        }

    # Build command string - VULNERABLE: profile injected directly
    cmd = f"convert --profile {profile}"

    try:
        result = subprocess.run(
            cmd,
            shell=True,
            capture_output=True,
            text=True,
            timeout=15,
            env={
                "PATH": "/usr/local/bin:/usr/bin:/bin",
                "HOME": "/tmp",
                "LANG": "C.UTF-8",
            },
        )
        log = result.stdout or ""
        if result.stderr:
            log += result.stderr
        return {
            "command": cmd,
            "exit_code": result.returncode,
            "log": log,
            "error": None,
        }
    except subprocess.TimeoutExpired:
        return {
            "command": cmd,
            "exit_code": -1,
            "log": "[convert] timeout: conversion exceeded 15s limit\n",
            "error": "timeout",
        }
    except Exception as e:
        return {
            "command": cmd,
            "exit_code": -1,
            "log": f"[convert] error: {e}\n",
            "error": str(e),
        }


def store_task(task_id: str, task_data: dict) -> None:
    """Store task in in-memory store (simulates queue-backed tracking)."""
    TASKS[task_id] = task_data


def verify_worker_token() -> bool:
    """Verify worker token from Authorization header or query param.

    The token is obtained by players via B2 (path traversal reading
    support-upload's worker.yml which contains the worker token).
    """
    # Check Authorization header: "Bearer <token>" or "Token <token>"
    auth = request.headers.get("Authorization", "")
    if auth.startswith("Bearer "):
        token = auth[7:].strip()
        if token == WORKER_TOKEN:
            return True
    if auth.startswith("Token "):
        token = auth[6:].strip()
        if token == WORKER_TOKEN:
            return True

    # Check X-Worker-Token header
    token = request.headers.get("X-Worker-Token", "")
    if token == WORKER_TOKEN:
        return True

    # Check query param
    token = request.args.get("token", "")
    if token == WORKER_TOKEN:
        return True

    return False


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.route("/")
def index():
    """Worker info endpoint."""
    return jsonify({
        "service": "document-worker",
        "version": "2026.06.1",
        "zone": "business",
        "status": "running",
        "queue": "document-workers",
        "consumer_group": "document-workers",
        "task_types": [
            "document-parse",
            "document-ocr",
            "document-embed",
            "document-summarize",
            "document-classify",
            "document-extract",
            "document-convert",
        ],
        "endpoints": [
            "GET /healthz",
            "GET /",
            "POST /api/parse",
            "GET /api/tasks/<taskId>",
            "GET /api/queue/stats",
        ],
        "timestamp": now_iso(),
    })


@app.route("/healthz")
def healthz():
    return Response("ok", status=200, mimetype="text/plain")


@app.route("/favicon.ico")
def favicon():
    return Response("", status=204, mimetype="image/x-icon")


@app.route("/_shared/<path:filename>")
def shared_files(filename):
    """Serve shared assets (nebulamind.css, etc.)."""
    shared_path = os.path.join("/_shared", filename)
    if not os.path.isfile(shared_path):
        abort(404)
    return send_file(shared_path)


@app.route("/api/parse", methods=["POST"])
def parse():
    """Parse remote document or run conversion profile.

    B3 SSRF: accepts {url: "..."} and fetches the URL via urllib.
    D3 Command Injection: accepts {profile: "..."} and runs convert command.

    Both can be provided in the same request.
    """
    data = request.get_json(silent=True) or {}
    url = (data.get("url") or "").strip()
    profile = (data.get("profile") or "").strip()
    source = data.get("source", "direct")

    if not url and not profile:
        return jsonify({
            "error": "url or profile is required",
            "hint": "provide {url} for remote document fetch, {profile} for conversion",
        }), 400

    task_id = generate_task_id()
    response: dict = {
        "taskId": task_id,
        "status": "completed",
        "createdAt": now_iso(),
        "source": source,
    }

    # B3: SSRF - fetch remote URL
    if url:
        fetch_result = fetch_url(url)
        host = extract_host(url)

        response["url"] = url
        response["fetched"] = {
            "ok": fetch_result["ok"],
            "status_code": fetch_result["status_code"],
            "content_type": fetch_result.get("content_type", ""),
        }

        if fetch_result["ok"]:
            response["content"] = fetch_result["content"]
            response["status"] = "completed"
        else:
            # Fetch failed - return generic error without internal details
            # BUT still attach trace metadata for internal service URLs
            # (the trace is captured regardless of fetch success)
            response["content"] = ""
            response["status"] = "fetch_failed"
            response["error"] = fetch_result["error"]

        # Attach trace metadata (B3 flag is here for internal service URLs)
        response["metadata"] = build_trace_metadata(url, fetch_result)

    # D3: Command Injection - run conversion profile
    if profile:
        convert_result = run_convert(profile)
        response["profile"] = profile
        response["conversion"] = {
            "command": convert_result["command"],
            "exitCode": convert_result["exit_code"],
            "log": convert_result["log"],
        }
        if convert_result.get("error"):
            response["conversion"]["error"] = convert_result["error"]

    # Store task for status query
    store_task(task_id, {
        "taskId": task_id,
        "status": response["status"],
        "createdAt": response["createdAt"],
        "url": url or None,
        "profile": profile or None,
        "completedAt": now_iso(),
        "result": response,
    })

    return jsonify(response)


@app.route("/api/tasks/<task_id>")
def get_task(task_id):
    """Query parse task status."""
    if not TASK_ID_RE.match(task_id):
        abort(404)

    task = TASKS.get(task_id)
    if not task:
        # Generate a realistic-looking historical task if not found
        return jsonify({
            "taskId": task_id,
            "status": "not_found",
            "error": "task not found or expired",
            "hint": "tasks are retained for 24h after completion",
        }), 404

    return jsonify(task)


@app.route("/api/queue/stats")
def queue_stats():
    """Queue statistics endpoint.

    Requires worker token (obtained via B2 path traversal reading
    support-upload's worker.yml).
    """
    if not verify_worker_token():
        return jsonify({
            "error": "unauthorized",
            "message": "worker token required",
            "hint": "provide token via Authorization: Bearer <token>, "
                    "X-Worker-Token header, or ?token= query param",
        }), 401

    # Return realistic queue statistics
    return jsonify({
        "queue": "document-workers",
        "consumer_group": "document-workers",
        "broker": f"{CACHE_BROKER_HOST}:6379",
        "stats": {
            "pending": 3,
            "processing": 1,
            "completed_24h": 847,
            "failed_24h": 12,
            "dead_letter": 2,
            "avg_process_time_ms": 1840,
            "throughput_per_min": 5.6,
        },
        "workers": [
            {
                "id": "doc-worker-1",
                "host": "document-worker",
                "status": "active",
                "current_task": "DOC-20260620-A3B2",
                "uptime_seconds": 86400,
                "tasks_completed": 423,
            },
            {
                "id": "doc-worker-2",
                "host": "document-worker",
                "status": "idle",
                "current_task": None,
                "uptime_seconds": 86400,
                "tasks_completed": 424,
            },
        ],
        "task_types": {
            "document-parse": {"pending": 2, "processing": 1, "completed_24h": 412},
            "document-ocr": {"pending": 1, "processing": 0, "completed_24h": 198},
            "document-convert": {"pending": 0, "processing": 0, "completed_24h": 156},
            "document-embed": {"pending": 0, "processing": 0, "completed_24h": 81},
        },
        "timestamp": now_iso(),
    })


# ---------------------------------------------------------------------------
# Error handlers
# ---------------------------------------------------------------------------

@app.errorhandler(404)
def not_found(e):
    if request.path.startswith("/api/"):
        return jsonify({"error": "not found"}), 404
    return Response(
        '{"error": "not found"}',
        status=404,
        content_type="application/json",
    )


@app.errorhandler(400)
def bad_request(e):
    if request.path.startswith("/api/"):
        desc = e.description if hasattr(e, "description") else "bad request"
        return jsonify({"error": str(desc)}), 400
    return Response('{"error": "bad request"}', status=400,
                    content_type="application/json")


@app.errorhandler(401)
def unauthorized(e):
    return jsonify({"error": "unauthorized"}), 401


@app.errorhandler(405)
def method_not_allowed(e):
    return jsonify({"error": "method not allowed"}), 405


@app.errorhandler(500)
def internal_error(e):
    return jsonify({"error": "internal server error"}), 500


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    os.makedirs(UPLOAD_DIR, exist_ok=True)
    print(
        f"[document-worker] NebulaMind Document Parsing Worker "
        f"starting on {HOST}:{PORT}",
        flush=True,
    )
    print(f"[document-worker] Config dir: {CONFIG_DIR}", flush=True)
    print(f"[document-worker] Worker flag file: {WORKER_FLAG_FILE}", flush=True)
    print(f"[document-worker] Service account: {SERVICE_ACCOUNT_FILE}", flush=True)
    app.run(host=HOST, port=PORT, threaded=True, debug=False)
