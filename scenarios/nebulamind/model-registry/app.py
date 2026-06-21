#!/usr/bin/env python3
"""NebulaMind AI Corp - Model Registry & Inference Configuration Service.

Data-zone service providing a HuggingFace-style model registry API.
Connected to the data-plane network only (not exposed to Public).

Routes:
  GET  /healthz                                          - Health check
  GET  /                                                 - API info / registry home
  GET  /api/v1/models                                    - List all models (requires auth)
  GET  /api/v1/models/<name>                             - Model detail
  GET  /api/v1/models/<name>/versions                    - List model versions
  GET  /api/v1/models/<name>/versions/<version>          - Version detail (with manifest)
  GET  /api/v1/models/<name>/versions/<version>/manifest - Get manifest only
  POST /api/v1/auth/login                                - Login (returns token + role)

Vulnerabilities (by design, for CTF):
  G2 (Model Registry Privilege Escalation / Unauthorized Download):
      The model registry admin token (nm-model-admin-token-2026) is obtained by
      players from the G1 chain (secrets-vault secret/data/nebulamind/model-registry).
      With this admin token, players can access the private model
      recommendation-v4-private and download its manifest, which contains the G2 flag.
      A normal viewer token (viewer/viewer123) can only access public models and
      receives 403 when trying to access private models.

  G3 (Final: Model Supply Chain Audit) - partial chain in this service:
      The recommendation-v4-private manifest's compliance field contains clues that
      point to the next stage of the chain:
        - training_log_path: s3://public-model-artifacts/training-logs/recommendation-v4-private-train.log
        - audit_id: audit-2026-006
        - audit_record_location: postgresql://<platform-db-host>:5432/nebulamind/regulated_model_training_records?id=6
      Players follow this chain: manifest -> object-store training log -> customer-db
      regulated_model_training_records table (id=6) compliance_audit field, which
      contains the G3 final flag. The G3 flag itself is NOT in this service.

Authentication:
  Tokens are passed via the Authorization: Bearer header or X-Registry-Token header.
  Two authentication modes:
    1. Static admin token: nm-model-admin-token-2026 (from Vault, full registry access)
    2. Login-based token: POST /api/v1/auth/login with username/password
       - viewer/viewer123 -> viewer role (public models only)
       - model-admin/nm-admin-2026-registry -> admin role (all models)
  The admin token from Vault (nm-model-admin-token-2026) is the intended G2 entry
  point; the model-admin login is a secondary path for operational use.
"""

from __future__ import annotations

import json
import os
import sys
import time
import uuid
from datetime import datetime, timezone

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

from flask import Flask, Response, abort, jsonify, render_template, request

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "8080"))
HOST = os.environ.get("HOST", "0.0.0.0")
REGISTRY_URL = os.environ["NM_MODEL_REGISTRY_URL"].rstrip("/")
APP_DIR = os.path.dirname(os.path.abspath(__file__))
SEED_FILE = os.environ.get(
    "MODEL_REGISTRY_SEED_FILE", "/opt/nebulamind/seed-processed/models.json"
)
SEED_FALLBACK = os.path.join(APP_DIR, "seed", "models.json")

SERVICE_NAME = "NebulaMind Model Registry"
SERVICE_VERSION = "2026.06.3"

# ---------------------------------------------------------------------------
# Flag injection
# ---------------------------------------------------------------------------

# G2: Model Registry Admin Token Abuse - flag injected into recommendation-v4-private manifest
FLAG_G2 = get_flag("FLAG_MODEL_REGISTRY_ADMIN", "flag{g2_model_registry_admin_placeholder}")

# ---------------------------------------------------------------------------
# Token definitions
# ---------------------------------------------------------------------------

# Admin token from Vault (secret/data/nebulamind/model-registry).
# Players obtain this token via the G1 chain (secrets-vault).
# This is the intended G2 entry point.
ADMIN_TOKEN = "nm-model-admin-token-2026"

# Login-based credentials (secondary operational path)
USERS: dict[str, dict] = {
    "viewer": {
        "password": "viewer123",
        "role": "viewer",
        "scope": "public-read",
        "description": "Read-only viewer, public models only",
    },
    "model-admin": {
        "password": "nm-admin-2026-registry",
        "role": "admin",
        "scope": "admin:registry,read:models,write:models,read:manifests",
        "description": "Model registry admin (password rotated via Vault)",
    },
}

# Issued login tokens (token -> user info). Populated on /api/v1/auth/login.
ISSUED_TOKENS: dict[str, dict] = {}

# ---------------------------------------------------------------------------
# Load model data from seed file
# ---------------------------------------------------------------------------


def load_models() -> list[dict]:
    """Load models from the processed seed file, with G2 flag injected.

    The entrypoint.sh replaces __NM_FLAG_G2__ in the seed file via sed.
    As a fallback (e.g., when running outside the container), this function
    also replaces the placeholder in memory using get_flag().
    """
    seed_path = SEED_FILE if os.path.exists(SEED_FILE) else SEED_FALLBACK
    try:
        with open(seed_path, "r", encoding="utf-8") as f:
            raw = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        raw = {"models": []}

    models = raw.get("models", [])
    # In-memory flag replacement (fallback if sed didn't run)
    for model in models:
        for ver in model.get("versions", []):
            manifest = ver.get("manifest", {})
            if isinstance(manifest.get("flag"), str) and "__NM_FLAG_G2__" in manifest["flag"]:
                manifest["flag"] = FLAG_G2
    return models


def load_registry_info() -> dict:
    """Load registry info from seed file."""
    seed_path = SEED_FILE if os.path.exists(SEED_FILE) else SEED_FALLBACK
    try:
        with open(seed_path, "r", encoding="utf-8") as f:
            raw = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        raw = {}
    return raw.get("_registry_info", {
        "name": SERVICE_NAME,
        "version": SERVICE_VERSION,
        "description": "NebulaMind AI Corp internal model registry",
        "owner": "ml-platform@nebulamind.ai",
    })


MODELS = load_models()
REGISTRY_INFO = load_registry_info()


# ---------------------------------------------------------------------------
# Flask app
# ---------------------------------------------------------------------------

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = 4 * 1024 * 1024  # 4MB

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def gen_request_id() -> str:
    return uuid.uuid4().hex


def format_number(value) -> str:
    """Jinja2 filter: format a number with thousands separators."""
    try:
        return f"{int(value):,}"
    except (TypeError, ValueError):
        return str(value)


# Register Jinja2 filters
app.jinja_env.filters["format_number"] = format_number


def api_response(data: dict, status: int = 200) -> tuple[Response, int]:
    """Build a standard ML-platform-style API response."""
    body = {
        "request_id": gen_request_id(),
        "timestamp": now_iso(),
        "service": "model-registry",
        "version": SERVICE_VERSION,
        "data": data,
    }
    return jsonify(body), status


def api_error(errors, status: int = 400, message: str = "") -> tuple[Response, int]:
    """Build a standard API error response."""
    if isinstance(errors, str):
        errors = [errors]
    body = {
        "request_id": gen_request_id(),
        "timestamp": now_iso(),
        "service": "model-registry",
        "version": SERVICE_VERSION,
        "error": {
            "code": status,
            "message": message or errors[0],
            "details": errors,
        },
        "data": None,
    }
    return jsonify(body), status


def extract_token() -> str | None:
    """Extract the auth token from request headers.

    Supports both Authorization: Bearer header and X-Registry-Token header.
    """
    auth = request.headers.get("Authorization", "")
    if auth.lower().startswith("bearer "):
        return auth[7:].strip()
    token = request.headers.get("X-Registry-Token")
    if token:
        return token.strip()
    return None


def lookup_token(token: str) -> dict | None:
    """Look up a token and return its metadata, or None if invalid.

    Recognizes:
      - The static admin token (nm-model-admin-token-2026) from Vault
      - Issued login tokens from /api/v1/auth/login
    """
    if token == ADMIN_TOKEN:
        return {
            "role": "admin",
            "scope": "admin:registry,read:models,write:models,read:manifests",
            "username": "model-registry-bot",
            "source": "vault-static-token",
            "issued_at": int(time.time()) - 86400,
            "expires_at": None,
        }
    info = ISSUED_TOKENS.get(token)
    if info:
        return dict(info)
    return None


def token_is_admin(token_info: dict | None) -> bool:
    return bool(token_info and token_info.get("role") == "admin")


def find_model(name: str) -> dict | None:
    """Find a model by name (case-insensitive)."""
    target = name.lower()
    for model in MODELS:
        if model["name"].lower() == target:
            return model
    return None


def find_version(model: dict, version: str) -> dict | None:
    """Find a version within a model (case-insensitive)."""
    target = version.lower()
    for ver in model.get("versions", []):
        if ver["version"].lower() == target:
            return ver
    return None


def model_summary(model: dict, include_versions: bool = False) -> dict:
    """Build a public summary of a model (without manifest details)."""
    summary = {
        "name": model["name"],
        "display_name": model.get("display_name", model["name"]),
        "task_type": model.get("task_type"),
        "architecture": model.get("architecture"),
        "framework": model.get("framework"),
        "visibility": model.get("visibility"),
        "tags": model.get("tags", []),
        "description": model.get("description", ""),
        "owner": model.get("owner"),
        "created_at": model.get("created_at"),
        "updated_at": model.get("updated_at"),
        "downloads": model.get("downloads", 0),
        "likes": model.get("likes", 0),
        "version_count": len(model.get("versions", [])),
        "latest_version": model.get("versions", [{}])[-1].get("version") if model.get("versions") else None,
    }
    if include_versions:
        summary["versions"] = [
            {
                "version": v.get("version"),
                "status": v.get("status"),
                "created_at": v.get("created_at"),
                "created_by": v.get("created_by"),
            }
            for v in model.get("versions", [])
        ]
    return summary


def version_detail(model: dict, version: dict, can_access: bool) -> dict:
    """Build a version detail response.

    The manifest is included only if the caller can access the model
    (public models: any auth user; private models: admin only).
    """
    detail = {
        "model_name": model["name"],
        "version": version["version"],
        "status": version.get("status"),
        "created_at": version.get("created_at"),
        "created_by": version.get("created_by"),
    }
    if can_access:
        detail["manifest"] = version.get("manifest", {})
    else:
        detail["manifest"] = None
        detail["access_required"] = "admin"
        detail["reason"] = "Private model manifest requires admin token"
    return detail


# ---------------------------------------------------------------------------
# Health check & info routes
# ---------------------------------------------------------------------------


@app.route("/healthz")
def healthz():
    """Simple health check endpoint for Docker healthcheck."""
    return jsonify({"status": "ok", "service": "model-registry"}), 200


@app.route("/")
def index():
    """Model registry home page (HuggingFace-style)."""
    # If the client prefers JSON (API client), return API info
    accept = request.headers.get("Accept", "")
    if "application/json" in accept and "text/html" not in accept:
        return api_response({
            "service": SERVICE_NAME,
            "version": SERVICE_VERSION,
            "description": REGISTRY_INFO.get("description", ""),
            "owner": REGISTRY_INFO.get("owner", ""),
            "endpoints": [
                {"method": "GET", "path": "/healthz", "description": "Health check"},
                {"method": "GET", "path": "/", "description": "API info / registry home"},
                {"method": "GET", "path": "/api/v1/models", "description": "List all models (requires auth)"},
                {"method": "GET", "path": "/api/v1/models/<name>", "description": "Model detail"},
                {"method": "GET", "path": "/api/v1/models/<name>/versions", "description": "List model versions"},
                {"method": "GET", "path": "/api/v1/models/<name>/versions/<version>", "description": "Version detail (with manifest)"},
                {"method": "GET", "path": "/api/v1/models/<name>/versions/<version>/manifest", "description": "Get manifest only"},
                {"method": "POST", "path": "/api/v1/auth/login", "description": "Login (returns token + role)"},
            ],
            "model_count": len(MODELS),
        })

    # Render HTML page
    models_for_view = []
    for model in MODELS:
        m = model_summary(model, include_versions=True)
        models_for_view.append(m)
    return render_template(
        "index.html",
        models=models_for_view,
        registry=REGISTRY_INFO,
        registry_url=REGISTRY_URL,
    )


@app.route("/api/v1/models/<name>")
def model_page(name: str):
    """Model detail HTML page (HuggingFace-style model card)."""
    model = find_model(name)
    if not model:
        abort(404)
    return render_template(
        "model.html",
        model=model,
        registry=REGISTRY_INFO,
        registry_url=REGISTRY_URL,
    )


# ---------------------------------------------------------------------------
# Auth routes
# ---------------------------------------------------------------------------


@app.route("/api/v1/auth/login", methods=["POST"])
def auth_login():
    """Login endpoint.

    Request body: {"username": "...", "password": "..."}
    Response: {"token": "...", "role": "...", "scope": "...", "expires_in": 3600}

    The static admin token (nm-model-admin-token-2026) from Vault is NOT
    issued here; it is a pre-provisioned service token. Players obtain it
    via the G1 chain (secrets-vault).
    """
    body = request.get_json(silent=True) or {}
    username = body.get("username", "").strip()
    password = body.get("password", "")

    if not username or not password:
        return api_error(["username and password are required"], 400,
                         message="Invalid credentials")

    user = USERS.get(username)
    if not user or user["password"] != password:
        return api_error(["invalid username or password"], 401,
                         message="Authentication failed")

    # Issue a new token
    token = f"nmr.{uuid.uuid4().hex}"
    ISSUED_TOKENS[token] = {
        "role": user["role"],
        "scope": user["scope"],
        "username": username,
        "source": "login",
        "issued_at": int(time.time()),
        "expires_at": int(time.time()) + 3600,
    }

    return api_response({
        "token": token,
        "role": user["role"],
        "scope": user["scope"],
        "username": username,
        "expires_in": 3600,
        "token_type": "Bearer",
    })


# ---------------------------------------------------------------------------
# Model API routes
# ---------------------------------------------------------------------------


@app.route("/api/v1/models")
def list_models():
    """List all models (requires authentication).

    Public models are visible to any authenticated user.
    Private models are listed with visibility=private but their manifests
    are not included; accessing private model manifests requires admin token.
    """
    token = extract_token()
    if not token:
        return api_error(["authentication required"], 401,
                         message="Missing authentication token")

    token_info = lookup_token(token)
    if not token_info:
        return api_error(["invalid or expired token"], 401,
                         message="Authentication failed")

    is_admin = token_is_admin(token_info)
    result = []
    for model in MODELS:
        # Private models are listed to all authenticated users (so they know
        # the model exists), but manifests require admin access. This mirrors
        # real model registries where model metadata is visible but artifacts
        # are gated.
        summary = model_summary(model, include_versions=True)
        summary["manifest_accessible"] = is_admin or model.get("visibility") == "public"
        result.append(summary)

    return api_response({
        "models": result,
        "total": len(result),
        "caller_role": token_info.get("role"),
    })


@app.route("/api/v1/models/<name>")
def get_model(name: str):
    """Get model detail (requires authentication)."""
    token = extract_token()
    if not token:
        return api_error(["authentication required"], 401,
                         message="Missing authentication token")

    token_info = lookup_token(token)
    if not token_info:
        return api_error(["invalid or expired token"], 401,
                         message="Authentication failed")

    model = find_model(name)
    if not model:
        return api_error([f"model '{name}' not found"], 404,
                         message="Model not found")

    is_admin = token_is_admin(token_info)
    summary = model_summary(model, include_versions=True)
    summary["manifest_accessible"] = is_admin or model.get("visibility") == "public"
    return api_response({"model": summary})


@app.route("/api/v1/models/<name>/versions")
def list_versions(name: str):
    """List model versions (requires authentication)."""
    token = extract_token()
    if not token:
        return api_error(["authentication required"], 401,
                         message="Missing authentication token")

    token_info = lookup_token(token)
    if not token_info:
        return api_error(["invalid or expired token"], 401,
                         message="Authentication failed")

    model = find_model(name)
    if not model:
        return api_error([f"model '{name}' not found"], 404,
                         message="Model not found")

    versions = [
        {
            "version": v.get("version"),
            "status": v.get("status"),
            "created_at": v.get("created_at"),
            "created_by": v.get("created_by"),
        }
        for v in model.get("versions", [])
    ]
    return api_response({
        "model_name": model["name"],
        "versions": versions,
        "total": len(versions),
    })


@app.route("/api/v1/models/<name>/versions/<version>")
def get_version(name: str, version: str):
    """Get model version detail with manifest (requires authentication).

    For private models, the manifest is only included if the caller has
    admin role. Otherwise, a 403 is returned (the version metadata is
    not leaked for private models to non-admin callers).
    """
    token = extract_token()
    if not token:
        return api_error(["authentication required"], 401,
                         message="Missing authentication token")

    token_info = lookup_token(token)
    if not token_info:
        return api_error(["invalid or expired token"], 401,
                         message="Authentication failed")

    model = find_model(name)
    if not model:
        return api_error([f"model '{name}' not found"], 404,
                         message="Model not found")

    ver = find_version(model, version)
    if not ver:
        return api_error([f"version '{version}' not found for model '{name}'"], 404,
                         message="Version not found")

    is_admin = token_is_admin(token_info)
    visibility = model.get("visibility", "public")

    # G2 vulnerability: private model manifest requires admin token.
    # A viewer token receives 403 when accessing a private model version detail.
    if visibility == "private" and not is_admin:
        return api_error([
            f"access denied: model '{name}' is private and requires admin token",
            "hint: obtain the model-registry admin token from Vault (secret/data/nebulamind/model-registry)",
        ], 403, message="Forbidden: private model requires admin privileges")

    detail = version_detail(model, ver, can_access=True)
    return api_response({"version": detail})


@app.route("/api/v1/models/<name>/versions/<version>/manifest")
def get_manifest(name: str, version: str):
    """Get model manifest only (requires authentication).

    For private models, admin token is required. This is the G2 target:
    GET /api/v1/models/recommendation-v4-private/versions/v4/manifest
    with the admin token returns the manifest containing the G2 flag and
    the G3 compliance chain clues.
    """
    token = extract_token()
    if not token:
        return api_error(["authentication required"], 401,
                         message="Missing authentication token")

    token_info = lookup_token(token)
    if not token_info:
        return api_error(["invalid or expired token"], 401,
                         message="Authentication failed")

    model = find_model(name)
    if not model:
        return api_error([f"model '{name}' not found"], 404,
                         message="Model not found")

    ver = find_version(model, version)
    if not ver:
        return api_error([f"version '{version}' not found for model '{name}'"], 404,
                         message="Version not found")

    is_admin = token_is_admin(token_info)
    visibility = model.get("visibility", "public")

    # G2 vulnerability: private model manifest requires admin token.
    if visibility == "private" and not is_admin:
        return api_error([
            f"access denied: manifest for private model '{name}' requires admin token",
            "hint: obtain the model-registry admin token from Vault (secret/data/nebulamind/model-registry)",
        ], 403, message="Forbidden: private model manifest requires admin privileges")

    manifest = ver.get("manifest", {})
    return api_response({
        "model_name": model["name"],
        "version": ver["version"],
        "manifest": manifest,
    })


# ---------------------------------------------------------------------------
# Error handlers
# ---------------------------------------------------------------------------


@app.errorhandler(404)
def not_found(e):
    return api_error([f"no handler for route {request.path}"], 404,
                     message="Not found")


@app.errorhandler(405)
def method_not_allowed(e):
    return api_error([f"method {request.method} not allowed for {request.path}"], 405,
                     message="Method not allowed")


@app.errorhandler(500)
def internal_error(e):
    return api_error(["internal server error"], 500,
                     message="Internal server error")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    print(f"[model-registry] G2 flag loaded (FLAG_MODEL_REGISTRY_ADMIN)")
    print(f"[model-registry] loaded {len(MODELS)} models from seed file")
    print(f"[model-registry] admin token (from Vault): {ADMIN_TOKEN}")
    print(f"[model-registry] viewer login: viewer/viewer123 (public models only)")
    print(f"[model-registry] model-admin login: model-admin/nm-admin-2026-registry (all models)")
    print(f"[model-registry] G2 flag in: recommendation-v4-private v4 manifest (flag field)")
    print(f"[model-registry] G3 clues in: recommendation-v4-private v4 manifest (compliance field)")
    print(f"[model-registry] starting Flask model registry on {HOST}:{PORT}")
    app.run(host=HOST, port=PORT, debug=False, threaded=True)
