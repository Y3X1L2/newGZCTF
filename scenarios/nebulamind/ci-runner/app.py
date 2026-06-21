#!/usr/bin/env python3
"""NebulaMind AI Corp - CI Build Runner.

Operations-zone service providing CI/CD pipeline execution.
Connected to ops-control and data-plane networks.

Routes:
  GET  /                                - CI system homepage (project list, recent builds)
  GET  /healthz                         - Health check
  GET  /api/projects                    - Project list (JSON)
  GET  /api/projects/{id}               - Project detail (JSON)
  GET  /api/projects/{id}/variables     - Project variables (E2 vulnerability, requires token)
  POST /api/projects/{id}/trigger       - Trigger build (E3 vulnerability, requires token)
  GET  /api/builds/{buildId}            - Build detail (JSON)
  GET  /api/builds/{buildId}/logs       - Build logs (JSON)
  GET  /projects/{id}                   - Project detail page (HTML)
  GET  /builds/{buildId}                - Build detail page (HTML)

Vulnerabilities (by design, for CTF):
  E2: GET /api/projects/{id}/variables returns masked variables in plaintext.
      The UI masks sensitive variables (shows ********), but the API endpoint
      erroneously returns the actual values. Players obtain a worker token
      (via B2 path traversal reading document-worker's config) or git token
      to access this endpoint and read FLAG_CI_VARIABLE_LEAK.
  E3: POST /api/projects/{id}/trigger accepts {variables} that get substituted
      into the .nebulaci.yml script lines via string replacement BEFORE shell
      execution. This allows command injection: a variable value containing
      $(cmd) or `cmd` gets executed by the shell. Players inject
      $(cat /opt/nebulamind/ci.flag) to read the E3 flag, which appears in
      the build log output.
      Safety: execution is sandboxed inside the container (non-root, no Docker
      socket, no privileges). The injection can only affect this container.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import time
import uuid
from datetime import datetime
from typing import Optional

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

from flask import (
    Flask,
    Response,
    abort,
    jsonify,
    render_template,
    request,
    send_file,
)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "8080"))
HOST = os.environ.get("HOST", "0.0.0.0")

APP_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_DIR = os.environ.get("CONFIG_DIR", "/app/config")
CI_FLAG_FILE = "/opt/nebulamind/ci.flag"
VAULT_CREDS_FILE = "/opt/nebulamind/vault-credentials.json"

GIT_SERVICE_URL = os.environ["NM_GIT_SERVICE_URL"].rstrip("/")
CUSTOMER_DB_HOST = os.environ["NM_CUSTOMER_DB_HOST"]
CACHE_BROKER_HOST = os.environ["NM_CACHE_BROKER_HOST"]

# Worker token for CI API auth (same token as document-worker's config).
# Players obtain this via B2 (path traversal reading document-worker's worker.yml).
WORKER_TOKEN = os.environ.get(
    "WORKER_TOKEN", "nm_worker_token_8f3a9b2e5c1d4a6f"
)
# CI token (weak auth - accepts any token starting with nm_ci_ or glpat-)
CI_TOKEN_PREFIX = os.environ.get("CI_TOKEN_PREFIX", "nm_ci_")

READONLY_DATABASE_URL = (
    f"postgresql://readonly:readonly_password_2026@{CUSTOMER_DB_HOST}:5432/nebulamind"
)
REDIS_URL = f"redis://{CACHE_BROKER_HOST}:6379/2"

# Build ID validation
BUILD_ID_RE = re.compile(r"^[0-9]+$")
PROJECT_ID_RE = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9._-]*$")

# In-memory stores
BUILDS: dict[int, dict] = {}
BUILD_LOGS: dict[int, str] = {}
NEXT_BUILD_ID = 43

# ---------------------------------------------------------------------------
# Mock build commands - these return simulated output instead of executing.
# Real commands (echo, cat, ls, etc.) execute normally via subprocess.
# ---------------------------------------------------------------------------
MOCK_BUILD_COMMANDS = {
    "flake8": "[mock] flake8: 0 issues found (simulated)",
    "pytest": "[mock] pytest: 15 passed, 0 failed in 2.34s (simulated)",
    "pip install": "[mock] pip: packages installed (simulated)",
    "pip3 install": "[mock] pip: packages installed (simulated)",
    "docker build": "[mock] docker: image built (simulated)",
    "docker push": "[mock] docker: image pushed (simulated)",
    "python -m compileall": "[mock] compileall: all files compiled (simulated)",
    "python3 -m compileall": "[mock] compileall: all files compiled (simulated)",
    "npm install": "[mock] npm: packages installed (simulated)",
    "npm run build": "[mock] npm: build complete (simulated)",
    "ansible-lint": "[mock] ansible-lint: 0 issues found (simulated)",
    "ansible-playbook": "[mock] ansible: playbook executed (simulated)",
}

# ---------------------------------------------------------------------------
# Project data
# ---------------------------------------------------------------------------

def get_projects() -> dict[str, dict]:
    """Get project data with variables. E2 flag is read from environment."""
    e2_flag = get_flag(
        "FLAG_CI_VARIABLE_LEAK", "flag{e2_ci_variable_leak_placeholder}"
    )

    return {
        "nebulamind-console-api": {
            "id": "nebulamind-console-api",
            "name": "NebulaMind Console API",
            "description": "Internal admin console backend service",
            "default_branch": "main",
            "language": "Python",
            "repo_url": f"{GIT_SERVICE_URL}/nebulamind/console-api",
            "variables": [
                {
                    "key": "DATABASE_URL",
                    "value": READONLY_DATABASE_URL,
                    "masked": True,
                    "protected": True,
                },
                {
                    "key": "OBJECT_STORE_ADMIN_KEY",
                    "value": "AKIA-NEBULA-ADMIN-2026",
                    "masked": True,
                    "protected": True,
                },
                {
                    "key": "VAULT_BOOTSTRAP_TOKEN",
                    "value": "s.bootstrap-nebulamind-2026",
                    "masked": True,
                    "protected": True,
                },
                {
                    "key": "FLAG_CI_VARIABLE_LEAK",
                    "value": e2_flag,
                    "masked": True,
                    "protected": True,
                },
                {
                    "key": "JWT_SECRET",
                    "value": "nebulamind-dev-secret-2026",
                    "masked": False,
                    "protected": False,
                },
                {
                    "key": "GIT_SERVICE_URL",
                    "value": GIT_SERVICE_URL,
                    "masked": False,
                    "protected": False,
                },
            ],
        },
        "nebulamind-doc-worker": {
            "id": "nebulamind-doc-worker",
            "name": "NebulaMind Document Worker",
            "description": "Document parsing and conversion worker",
            "default_branch": "main",
            "language": "Python",
            "repo_url": f"{GIT_SERVICE_URL}/nebulamind/doc-worker",
            "variables": [
                {
                    "key": "DATABASE_URL",
                    "value": READONLY_DATABASE_URL,
                    "masked": True,
                    "protected": True,
                },
                {
                    "key": "REDIS_URL",
                    "value": REDIS_URL,
                    "masked": False,
                    "protected": False,
                },
                {
                    "key": "WORKER_TOKEN",
                    "value": "nm_worker_token_8f3a9b2e5c1d4a6f",
                    "masked": True,
                    "protected": True,
                },
            ],
        },
        "nebulamind-infra-playbooks": {
            "id": "nebulamind-infra-playbooks",
            "name": "NebulaMind Infrastructure Playbooks",
            "description": "Ansible deployment and configuration",
            "default_branch": "main",
            "language": "YAML",
            "repo_url": f"{GIT_SERVICE_URL}/nebulamind/infra-playbooks",
            "variables": [
                {
                    "key": "ANSIBLE_VAULT_PASSWORD",
                    "value": "nm_vault_dev_2026",
                    "masked": True,
                    "protected": True,
                },
            ],
        },
    }


# ---------------------------------------------------------------------------
# .nebulaci.yml configs (parsed in Python to avoid YAML dependency)
# ---------------------------------------------------------------------------

NEBULACI_CONFIGS: dict[str, dict] = {
    "nebulamind-console-api": {
        "image": "python:3.11-alpine",
        "variables": {
            "NM_PROJECT_NAME": "nebulamind-console-api",
            "NM_VERSION": "2026.06.1",
            "NM_BUILD_ARGS": "--no-cache",
            "NM_TEST_ARGS": "-v --cov=src/",
        },
        "stages": ["lint", "build", "test", "deploy"],
        "jobs": {
            "lint": {
                "stage": "lint",
                "script": [
                    'echo "Running flake8 on ${NM_PROJECT_NAME}..."',
                    "flake8 src/ --max-line-length=120",
                ],
            },
            "build": {
                "stage": "build",
                "script": [
                    'echo "Building ${NM_PROJECT_NAME} version ${NM_VERSION}..."',
                    'echo "Build args: ${NM_BUILD_ARGS}"',
                    "pip install -r requirements.txt",
                    "python -m compileall src/",
                ],
            },
            "test": {
                "stage": "test",
                "script": [
                    'echo "Running tests with args ${NM_TEST_ARGS}..."',
                    "pytest ${NM_TEST_ARGS}",
                ],
            },
            "deploy": {
                "stage": "deploy",
                "script": [
                    'echo "Deploying ${NM_PROJECT_NAME} to staging..."',
                    'echo "Image tag: ${NM_PROJECT_NAME}:${NM_VERSION}"',
                ],
                "only": ["main"],
            },
        },
    },
    "nebulamind-doc-worker": {
        "image": "python:3.11-alpine",
        "variables": {
            "NM_PROJECT_NAME": "nebulamind-doc-worker",
            "NM_VERSION": "2026.06.1",
            "NM_BUILD_ARGS": "--no-cache",
        },
        "stages": ["lint", "build", "test"],
        "jobs": {
            "lint": {
                "stage": "lint",
                "script": [
                    'echo "Running flake8 on ${NM_PROJECT_NAME}..."',
                    "flake8 src/ --max-line-length=120",
                ],
            },
            "build": {
                "stage": "build",
                "script": [
                    'echo "Building ${NM_PROJECT_NAME} version ${NM_VERSION}..."',
                    'echo "Build args: ${NM_BUILD_ARGS}"',
                    "pip install -r requirements.txt",
                ],
            },
            "test": {
                "stage": "test",
                "script": [
                    'echo "Running tests..."',
                    "pytest -v",
                ],
            },
        },
    },
    "nebulamind-infra-playbooks": {
        "image": "alpine:latest",
        "variables": {
            "NM_PROJECT_NAME": "nebulamind-infra-playbooks",
        },
        "stages": ["validate", "deploy"],
        "jobs": {
            "validate": {
                "stage": "validate",
                "script": [
                    'echo "Validating ansible playbooks..."',
                    "ansible-lint playbooks/",
                ],
            },
            "deploy": {
                "stage": "deploy",
                "script": [
                    'echo "Deploying infrastructure..."',
                    "ansible-playbook -i inventory/ playbooks/deploy.yml",
                ],
                "only": ["main"],
            },
        },
    },
}


# ---------------------------------------------------------------------------
# Historical builds (realistic data)
# ---------------------------------------------------------------------------

def get_initial_builds() -> list[dict]:
    """Generate initial historical builds for realism."""
    return [
        {
            "id": 42,
            "projectId": "nebulamind-console-api",
            "ref": "main",
            "status": "success",
            "createdAt": "2026-06-19 14:23:11",
            "completedAt": "2026-06-19 14:23:20",
            "duration": "8.7s",
            "triggeredBy": "scheduler",
            "message": "feat: add tenant management endpoints",
            "variables": {},
        },
        {
            "id": 41,
            "projectId": "nebulamind-console-api",
            "ref": "main",
            "status": "success",
            "createdAt": "2026-06-19 10:15:33",
            "completedAt": "2026-06-19 10:15:41",
            "duration": "7.2s",
            "triggeredBy": "devops@nebulamind.ai",
            "message": "refactor: extract JWT auth middleware",
            "variables": {},
        },
        {
            "id": 40,
            "projectId": "nebulamind-console-api",
            "ref": "feature/graphql",
            "status": "failed",
            "createdAt": "2026-06-18 16:45:22",
            "completedAt": "2026-06-18 16:45:28",
            "duration": "5.1s",
            "triggeredBy": "devops@nebulamind.ai",
            "message": "feat: add GraphQL endpoint",
            "variables": {},
        },
        {
            "id": 28,
            "projectId": "nebulamind-doc-worker",
            "ref": "main",
            "status": "success",
            "createdAt": "2026-06-19 09:30:00",
            "completedAt": "2026-06-19 09:30:07",
            "duration": "6.3s",
            "triggeredBy": "scheduler",
            "message": "chore: update dependencies",
            "variables": {},
        },
        {
            "id": 27,
            "projectId": "nebulamind-doc-worker",
            "ref": "main",
            "status": "success",
            "createdAt": "2026-06-18 14:20:15",
            "completedAt": "2026-06-18 14:20:21",
            "duration": "5.8s",
            "triggeredBy": "devops@nebulamind.ai",
            "message": "feat: add OCR profile support",
            "variables": {},
        },
        {
            "id": 15,
            "projectId": "nebulamind-infra-playbooks",
            "ref": "main",
            "status": "success",
            "createdAt": "2026-06-17 11:00:00",
            "completedAt": "2026-06-17 11:00:05",
            "duration": "4.2s",
            "triggeredBy": "scheduler",
            "message": "chore: rotate deployment keys",
            "variables": {},
        },
    ]


def generate_historical_log(build: dict) -> str:
    """Generate a realistic historical build log."""
    ts = build["createdAt"]
    project = build["projectId"]
    ref = build["ref"]
    status = build["status"]
    msg = build.get("message", "")

    log = (
        f"[{ts}] [INFO] Running on ci-runner-1 (alpine, python:3.11)...\n"
        f"[{ts}] [INFO] Cloning repository {project}...\n"
        f"[{ts}] [INFO] Fetching changes...\n"
        f"[{ts}] [INFO] Checking out {ref} as ref...\n"
        f"[{ts}] [INFO] Skipping Git submodules setup\n"
        f"[{ts}] [INFO] Commit: {msg}\n"
    )

    config = NEBULACI_CONFIGS.get(project, {})
    for stage_name in config.get("stages", []):
        log += f"\n[{ts}] [INFO] === Stage: {stage_name} ===\n"
        job = config.get("jobs", {}).get(stage_name, {})
        for line in job.get("script", []):
            log += f"$ {line}\n"
            # Mock output for build commands
            stripped = line.strip()
            for cmd_prefix, mock_output in MOCK_BUILD_COMMANDS.items():
                if stripped.startswith(cmd_prefix):
                    log += mock_output + "\n"
                    break
            else:
                # echo commands - simulate output
                if stripped.startswith("echo "):
                    # Extract the echo content (simple simulation)
                    content = stripped[5:].strip().strip('"').strip("'")
                    log += content + "\n"

        if status == "success":
            log += f"[{ts}] [INFO] Stage {stage_name} completed (exit 0)\n"
        else:
            log += f"[{ts}] [ERROR] Stage {stage_name} failed (exit 1)\n"
            log += f"[{ts}] [ERROR] Build failed at stage {stage_name}\n"
            break

    if status == "success":
        log += f"\n[{ts}] [INFO] Job succeeded in {build['duration']}\n"
    else:
        log += f"\n[{ts}] [ERROR] Job failed in {build['duration']}\n"

    return log


# Initialize builds and logs
for _b in get_initial_builds():
    BUILDS[_b["id"]] = _b
    BUILD_LOGS[_b["id"]] = generate_historical_log(_b)


# ---------------------------------------------------------------------------
# Flask app
# ---------------------------------------------------------------------------

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = 10 * 1024 * 1024  # 10MB


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def now_str() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def now_iso() -> str:
    return datetime.now().strftime("%Y-%m-%dT%H:%M:%SZ")


def verify_ci_token() -> tuple[bool, str]:
    """Verify CI API token from request.

    Accepts:
      - Worker token: nm_worker_token_8f3a9b2e5c1d4a6f (from document-worker config)
      - CI token: any token starting with nm_ci_ (weak auth)
      - Git token: any token starting with glpat- (weak auth, GitLab PAT format)

    Returns (is_valid, token_type).
    """
    # Check Authorization header: "Bearer <token>"
    auth = request.headers.get("Authorization", "")
    token = ""
    if auth.startswith("Bearer "):
        token = auth[7:].strip()
    elif auth.startswith("Token "):
        token = auth[6:].strip()

    # Check X-CI-Token header
    if not token:
        token = request.headers.get("X-CI-Token", "").strip()

    # Check X-Worker-Token header (compatibility with document-worker)
    if not token:
        token = request.headers.get("X-Worker-Token", "").strip()

    # Check query param
    if not token:
        token = request.args.get("token", "").strip()

    if not token:
        return False, ""

    # Worker token (full match)
    if token == WORKER_TOKEN:
        return True, "worker"

    # CI token (prefix match - weak auth)
    if token.startswith(CI_TOKEN_PREFIX):
        return True, "ci"

    # Git token (prefix match - weak auth, GitLab PAT format)
    if token.startswith("glpat-"):
        return True, "git"

    return False, ""


def substitute_variables(line: str, variables: dict[str, str]) -> str:
    """Substitute variables in a script line via string replacement.

    E3 VULNERABILITY: This does naive string replacement of ${VAR} and $VAR
    patterns. The substituted value is then passed to subprocess with
    shell=True, which means if a variable value contains $(cmd) or `cmd`,
    the shell will execute it as command substitution.

    This simulates a CI system that does variable expansion in the config
    before passing the script to the shell - a known insecure pattern that
    enables command injection via variable values.
    """
    result = line
    # Sort by key length descending to avoid partial matches
    for key in sorted(variables.keys(), key=len, reverse=True):
        value = str(variables[key])
        # Replace ${VAR} form
        result = result.replace(f"${{{key}}}", value)
        # Replace $VAR form (word boundary)
        result = re.sub(rf"\${re.escape(key)}\b", value, result)
    return result


def execute_script_line(line: str, env: dict[str, str]) -> tuple[str, int]:
    """Execute a script line.

    For mock build commands (flake8, pytest, pip, etc.), return simulated
    output without executing. For real commands (echo, cat, ls, etc.),
    execute via subprocess with shell=True.

    The shell=True execution is the E3 vulnerability sink: if the line
    contains $(cmd) or `cmd` (from variable substitution), the shell
    evaluates it.

    Safety: runs inside the container as non-root user, no Docker socket,
    no privileges. Minimal environment. 15-second timeout.
    """
    stripped = line.strip()
    if not stripped:
        return "", 0

    # Check for mocked build commands first
    for cmd_prefix, mock_output in MOCK_BUILD_COMMANDS.items():
        if stripped.startswith(cmd_prefix):
            return mock_output, 0

    # Execute with shell=True (vulnerable to $() and backticks in
    # substituted variable values)
    try:
        result = subprocess.run(
            line,
            shell=True,
            capture_output=True,
            text=True,
            timeout=15,
            env=env,
        )
        output = (result.stdout or "") + (result.stderr or "")
        return output, result.returncode
    except subprocess.TimeoutExpired:
        return "[ERROR] command timed out (15s limit)\n", -1
    except Exception as e:
        return f"[ERROR] execution failed: {e}\n", -1


def execute_build(
    project_id: str, ref: str, user_variables: dict
) -> tuple[str, bool]:
    """Execute a CI build and return (log, success).

    E3 vulnerability flow:
    1. Load .nebulaci.yml config for the project
    2. Merge config variables with user-provided variables
    3. For each stage/job, substitute variables in script lines (insecure)
    4. Execute each script line with shell=True
    5. If a variable value contains $(cmd), the shell executes it

    The build environment contains:
    - /opt/nebulamind/ci.flag (E3 flag)
    - /opt/nebulamind/vault-credentials.json (Vault creds for G1)

    Players inject variables like:
      NM_BUILD_ARGS=$(cat /opt/nebulamind/ci.flag)
    The echo line "Build args: ${NM_BUILD_ARGS}" becomes:
      echo "Build args: flag{...}"
    And the flag appears in the build log.
    """
    config = NEBULACI_CONFIGS.get(project_id, {})
    if not config:
        return f"[ERROR] No .nebulaci.yml found for project {project_id}\n", False

    # Merge variables: config defaults + user-provided (user overrides)
    variables: dict[str, str] = {}
    for k, v in config.get("variables", {}).items():
        variables[k] = str(v)
    for k, v in (user_variables or {}).items():
        variables[str(k)] = str(v)

    # Prepare minimal environment for subprocess execution
    # Note: CI variables are NOT in env - they're substituted into script
    # lines via string replacement (the vulnerability)
    exec_env = {
        "PATH": "/usr/local/bin:/usr/bin:/bin",
        "HOME": "/tmp",
        "LANG": "C.UTF-8",
        "CI": "true",
        "CI_PROJECT_NAME": project_id,
        "CI_COMMIT_REF": ref,
        "CI_RUNNER": "ci-runner-1",
    }

    log_lines: list[str] = []
    start_ts = now_str()
    log_lines.append(f"[{start_ts}] [INFO] Running on ci-runner-1 (alpine, python:3.11)...")
    log_lines.append(f"[{start_ts}] [INFO] Cloning repository {project_id}...")
    log_lines.append(f"[{start_ts}] [INFO] Fetching changes...")
    log_lines.append(f"[{start_ts}] [INFO] Checking out {ref} as ref...")
    log_lines.append(f"[{start_ts}] [INFO] Skipping Git submodules setup")
    log_lines.append(
        f"[{start_ts}] [INFO] Variables: "
        + ", ".join(f"{k}={v}" for k, v in variables.items())
    )

    overall_success = True

    for stage_name in config.get("stages", []):
        job = config.get("jobs", {}).get(stage_name, {})
        script_lines = job.get("script", [])

        # Check "only" constraint
        only_refs = job.get("only", [])
        if only_refs and ref not in only_refs:
            log_lines.append(
                f"\n[{now_str()}] [INFO] === Stage: {stage_name} (skipped, ref {ref} not in only) ==="
            )
            continue

        log_lines.append(f"\n[{now_str()}] [INFO] === Stage: {stage_name} ===")
        stage_success = True

        for line in script_lines:
            # E3 VULNERABILITY: substitute variables via string replacement
            # This allows $(cmd) in variable values to be executed by the shell
            substituted = substitute_variables(line, variables)

            log_lines.append(f"$ {substituted}")

            output, exit_code = execute_script_line(substituted, exec_env)
            if output:
                log_lines.append(output.rstrip())

            if exit_code != 0:
                stage_success = False
                log_lines.append(
                    f"[{now_str()}] [ERROR] Command failed (exit {exit_code})"
                )
                break

        if stage_success:
            log_lines.append(
                f"[{now_str()}] [INFO] Stage {stage_name} completed (exit 0)"
            )
        else:
            overall_success = False
            log_lines.append(
                f"[{now_str()}] [ERROR] Stage {stage_name} failed"
            )
            log_lines.append(
                f"[{now_str()}] [ERROR] Build failed at stage {stage_name}"
            )
            break

    # If user provided a custom build script variable, execute it as an
    # extra step (another injection vector)
    if user_variables and "NM_BUILD_SCRIPT" in user_variables:
        custom_script = str(user_variables["NM_BUILD_SCRIPT"])
        log_lines.append(f"\n[{now_str()}] [INFO] === Stage: custom (NM_BUILD_SCRIPT) ===")
        substituted = substitute_variables(custom_script, variables)
        log_lines.append(f"$ {substituted}")
        output, exit_code = execute_script_line(substituted, exec_env)
        if output:
            log_lines.append(output.rstrip())
        if exit_code != 0:
            overall_success = False

    end_ts = now_str()
    if overall_success:
        log_lines.append(f"\n[{end_ts}] [INFO] Job succeeded")
    else:
        log_lines.append(f"\n[{end_ts}] [ERROR] Job failed")

    return "\n".join(log_lines), overall_success


def get_nebulaci_config_raw(project_id: str) -> str:
    """Read raw .nebulaci.yml content for display."""
    # Map project ID to config file name
    file_map = {
        "nebulamind-console-api": "nebulaci-console-api.yml",
        "nebulamind-doc-worker": "nebulaci-doc-worker.yml",
    }
    filename = file_map.get(project_id)
    if not filename:
        # Generate a minimal config for display
        config = NEBULACI_CONFIGS.get(project_id, {})
        if not config:
            return "# No .nebulaci.yml found"
        lines = [f"# NebulaMind CI Configuration - {project_id}"]
        lines.append(f"image: {config.get('image', 'alpine:latest')}")
        lines.append("")
        lines.append("variables:")
        for k, v in config.get("variables", {}).items():
            lines.append(f'  {k}: "{v}"')
        lines.append("")
        lines.append("stages:")
        for stage in config.get("stages", []):
            lines.append(f"  - {stage}")
        return "\n".join(lines)

    filepath = os.path.join(CONFIG_DIR, filename)
    try:
        with open(filepath, "r", encoding="utf-8") as f:
            return f.read()
    except (IOError, OSError):
        return f"# Could not read {filename}"


def get_project_builds(project_id: str) -> list[dict]:
    """Get builds for a specific project, sorted by ID descending."""
    return sorted(
        [b for b in BUILDS.values() if b["projectId"] == project_id],
        key=lambda b: b["id"],
        reverse=True,
    )


def get_recent_builds(limit: int = 10) -> list[dict]:
    """Get recent builds across all projects, sorted by ID descending."""
    return sorted(
        list(BUILDS.values()),
        key=lambda b: b["id"],
        reverse=True,
    )[:limit]


# ---------------------------------------------------------------------------
# Health and utility routes
# ---------------------------------------------------------------------------

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


# ---------------------------------------------------------------------------
# HTML page routes
# ---------------------------------------------------------------------------

@app.route("/")
def index():
    """CI system homepage."""
    projects_data = get_projects()
    projects_list = []
    for pid, p in projects_data.items():
        builds = get_project_builds(pid)
        projects_list.append({
            **p,
            "last_build": builds[0] if builds else None,
        })

    all_builds = get_recent_builds(10)
    success_count = sum(1 for b in all_builds if b["status"] == "success")
    failed_count = sum(1 for b in all_builds if b["status"] == "failed")

    stats = {
        "projects": len(projects_list),
        "success": success_count,
        "failed": failed_count,
        "total": len(BUILDS),
    }

    return render_template(
        "index.html",
        projects=projects_list,
        recent_builds=all_builds,
        stats=stats,
    )


@app.route("/projects/<project_id>")
def project_page(project_id):
    """Project detail page."""
    if not PROJECT_ID_RE.match(project_id):
        abort(404)

    projects_data = get_projects()
    project = projects_data.get(project_id)
    if not project:
        abort(404)

    builds = get_project_builds(project_id)
    config_raw = get_nebulaci_config_raw(project_id)

    return render_template(
        "project.html",
        project=project,
        builds=builds,
        nebulaci_config=config_raw,
    )


@app.route("/builds/<int:build_id>")
def build_page(build_id):
    """Build detail page."""
    build = BUILDS.get(build_id)
    if not build:
        abort(404)

    build_log = BUILD_LOGS.get(build_id, "No log available")

    return render_template(
        "build.html",
        build=build,
        build_log=build_log,
    )


# ---------------------------------------------------------------------------
# API routes
# ---------------------------------------------------------------------------

@app.route("/api/projects")
def api_projects():
    """Project list (JSON)."""
    projects_data = get_projects()
    result = []
    for pid, p in projects_data.items():
        builds = get_project_builds(pid)
        result.append({
            "id": p["id"],
            "name": p["name"],
            "description": p["description"],
            "default_branch": p["default_branch"],
            "language": p["language"],
            "repo_url": p["repo_url"],
            "variables_count": len(p["variables"]),
            "last_build": {
                "id": builds[0]["id"],
                "status": builds[0]["status"],
                "ref": builds[0]["ref"],
                "createdAt": builds[0]["createdAt"],
            } if builds else None,
        })
    return jsonify({"projects": result})


@app.route("/api/projects/<project_id>")
def api_project_detail(project_id):
    """Project detail (JSON)."""
    if not PROJECT_ID_RE.match(project_id):
        return jsonify({"error": "invalid project id"}), 400

    projects_data = get_projects()
    project = projects_data.get(project_id)
    if not project:
        return jsonify({"error": "project not found"}), 404

    builds = get_project_builds(project_id)
    return jsonify({
        "id": project["id"],
        "name": project["name"],
        "description": project["description"],
        "default_branch": project["default_branch"],
        "language": project["language"],
        "repo_url": project["repo_url"],
        "variables_count": len(project["variables"]),
        "stages": NEBULACI_CONFIGS.get(project_id, {}).get("stages", []),
        "recent_builds": [
            {
                "id": b["id"],
                "ref": b["ref"],
                "status": b["status"],
                "createdAt": b["createdAt"],
                "duration": b["duration"],
                "triggeredBy": b["triggeredBy"],
            }
            for b in builds[:5]
        ],
    })


@app.route("/api/projects/<project_id>/variables")
def api_project_variables(project_id):
    """E2: Project variables endpoint.

    VULNERABILITY: This endpoint returns masked variables in plaintext.
    The UI correctly masks sensitive variables (shows ********), but this
    API endpoint erroneously returns the actual values for all variables,
    including those marked as masked.

    Requires authentication: worker token, CI token, or git token.
    Players obtain the worker token via B2 (path traversal reading
    document-worker's worker.yml which contains the token).
    """
    if not PROJECT_ID_RE.match(project_id):
        return jsonify({"error": "invalid project id"}), 400

    # Auth check
    is_valid, token_type = verify_ci_token()
    if not is_valid:
        return jsonify({
            "error": "unauthorized",
            "message": "CI token required",
            "hint": "provide token via Authorization: Bearer <token>, "
                    "X-CI-Token header, or ?token= query param",
            "accepted_tokens": [
                "worker token (nm_worker_token_...)",
                "CI token (nm_ci_...)",
                "git token (glpat-...)",
            ],
        }), 401

    projects_data = get_projects()
    project = projects_data.get(project_id)
    if not project:
        return jsonify({"error": "project not found"}), 404

    # E2 VULNERABILITY: return all variables with actual values,
    # including masked ones (should be redacted but isn't)
    variables = []
    for v in project["variables"]:
        variables.append({
            "key": v["key"],
            "value": v["value"],  # BUG: masked variables should be redacted
            "masked": v["masked"],
            "protected": v["protected"],
        })

    return jsonify({
        "projectId": project["id"],
        "projectName": project["name"],
        "variables": variables,
        "note": "Variables marked as masked are redacted in the UI",
        "authenticated_as": token_type,
        "timestamp": now_iso(),
    })


@app.route("/api/projects/<project_id>/trigger", methods=["POST"])
def api_trigger_build(project_id):
    """E3: Trigger a build.

    VULNERABILITY: The {variables} passed by the user are substituted into
    the .nebulaci.yml script lines via string replacement BEFORE shell
    execution. This allows command injection: a variable value containing
    $(cmd) or `cmd` gets executed by the shell.

    Example exploit:
      POST /api/projects/nebulamind-console-api/trigger
      Authorization: Bearer nm_worker_token_8f3a9b2e5c1d4a6f
      {
        "ref": "main",
        "variables": {
          "NM_BUILD_ARGS": "$(cat /opt/nebulamind/ci.flag)"
        }
      }

    The echo line "Build args: ${NM_BUILD_ARGS}" becomes:
      echo "Build args: $(cat /opt/nebulamind/ci.flag)"
    The shell executes the command substitution and the flag appears in
    the build log.

    Requires authentication: worker token, CI token, or git token.
    """
    if not PROJECT_ID_RE.match(project_id):
        return jsonify({"error": "invalid project id"}), 400

    # Auth check
    is_valid, token_type = verify_ci_token()
    if not is_valid:
        return jsonify({
            "error": "unauthorized",
            "message": "CI token required to trigger builds",
            "hint": "provide token via Authorization: Bearer <token>",
        }), 401

    projects_data = get_projects()
    project = projects_data.get(project_id)
    if not project:
        return jsonify({"error": "project not found"}), 404

    data = request.get_json(silent=True) or {}
    ref = (data.get("ref") or "main").strip()
    user_variables = data.get("variables") or {}

    # Validate ref (basic check)
    if not ref or len(ref) > 100 or not re.match(r"^[a-zA-Z0-9][a-zA-Z0-9._/-]*$", ref):
        return jsonify({"error": "invalid ref"}), 400

    # Create build record
    global NEXT_BUILD_ID
    build_id = NEXT_BUILD_ID
    NEXT_BUILD_ID += 1

    build = {
        "id": build_id,
        "projectId": project_id,
        "ref": ref,
        "status": "running",
        "createdAt": now_str(),
        "completedAt": None,
        "duration": None,
        "triggeredBy": f"api:{token_type}",
        "message": f"Triggered via API (ref={ref})",
        "variables": user_variables,
    }
    BUILDS[build_id] = build

    # Execute build synchronously
    start_time = time.time()
    log, success = execute_build(project_id, ref, user_variables)
    elapsed = time.time() - start_time

    # Update build record
    build["status"] = "success" if success else "failed"
    build["completedAt"] = now_str()
    build["duration"] = f"{elapsed:.1f}s"
    BUILD_LOGS[build_id] = log

    return jsonify({
        "buildId": build_id,
        "projectId": project_id,
        "ref": ref,
        "status": build["status"],
        "duration": build["duration"],
        "createdAt": build["createdAt"],
        "completedAt": build["completedAt"],
        "logUrl": f"/api/builds/{build_id}/logs",
        "buildUrl": f"/builds/{build_id}",
    })


@app.route("/api/builds/<int:build_id>")
def api_build_detail(build_id):
    """Build detail (JSON)."""
    build = BUILDS.get(build_id)
    if not build:
        return jsonify({
            "error": "build not found",
            "buildId": build_id,
        }), 404

    return jsonify({
        **build,
        "logUrl": f"/api/builds/{build_id}/logs",
    })


@app.route("/api/builds/<int:build_id>/logs")
def api_build_logs(build_id):
    """Build logs (JSON)."""
    build = BUILDS.get(build_id)
    if not build:
        return jsonify({
            "error": "build not found",
            "buildId": build_id,
        }), 404

    log = BUILD_LOGS.get(build_id, "No log available")
    return jsonify({
        "buildId": build_id,
        "projectId": build["projectId"],
        "status": build["status"],
        "log": log,
    })


# ---------------------------------------------------------------------------
# Error handlers
# ---------------------------------------------------------------------------

@app.errorhandler(404)
def not_found(e):
    if request.path.startswith("/api/"):
        return jsonify({"error": "not found"}), 404
    return Response(
        '<!DOCTYPE html><html lang="zh-CN"><head><meta charset="UTF-8">'
        '<title>404 - NebulaMind CI</title>'
        '<link rel="stylesheet" href="/_shared/assets/nebulamind.css">'
        '<link rel="stylesheet" href="/static/css/ci.css">'
        '</head><body><nav class="nm-nav">'
        '<a href="/" class="nm-nav-brand">NebulaMind CI</a>'
        '</nav><div class="nm-container" style="text-align:center;padding-top:80px;">'
        '<h1 style="font-size:64px;color:var(--nm-text-soft);">404</h1>'
        '<p class="nm-text-soft nm-mt-16">页面不存在</p>'
        '<a href="/" class="nm-btn nm-btn-primary nm-mt-32">返回首页</a>'
        '</div></body></html>',
        status=404,
        content_type="text/html; charset=utf-8",
    )


@app.errorhandler(400)
def bad_request(e):
    if request.path.startswith("/api/"):
        desc = e.description if hasattr(e, "description") else "bad request"
        return jsonify({"error": str(desc)}), 400
    return Response("Bad Request", status=400)


@app.errorhandler(401)
def unauthorized(e):
    return jsonify({"error": "unauthorized"}), 401


@app.errorhandler(405)
def method_not_allowed(e):
    if request.path.startswith("/api/"):
        return jsonify({"error": "method not allowed"}), 405
    return Response("Method Not Allowed", status=405)


@app.errorhandler(500)
def internal_error(e):
    if request.path.startswith("/api/"):
        return jsonify({"error": "internal server error"}), 500
    return Response("Internal Server Error", status=500)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    os.makedirs("/opt/nebulamind", exist_ok=True)
    print(
        f"[ci-runner] NebulaMind CI Build Runner "
        f"starting on {HOST}:{PORT}",
        flush=True,
    )
    print(f"[ci-runner] Config dir: {CONFIG_DIR}", flush=True)
    print(f"[ci-runner] CI flag file: {CI_FLAG_FILE}", flush=True)
    print(f"[ci-runner] Vault creds file: {VAULT_CREDS_FILE}", flush=True)
    print(f"[ci-runner] Worker token configured: {WORKER_TOKEN[:12]}...", flush=True)
    print(f"[ci-runner] Loaded {len(BUILDS)} historical builds", flush=True)
    app.run(host=HOST, port=PORT, threaded=True, debug=False)
