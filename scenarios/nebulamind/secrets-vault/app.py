#!/usr/bin/env python3
"""NebulaMind AI Corp - Secrets Vault (HashiCorp Vault mock).

Data-zone service providing Vault-style secret management API.
Connected to the data-plane network only (not exposed to Public).

Routes (Vault-style API):
  GET  /                              - Vault UI landing page
  GET  /healthz                       - Simple health check (Docker healthcheck)
  GET  /v1/sys/health                 - Vault-style health check
  GET  /v1/sys/mounts                 - List secret engine mounts
  GET  /v1/sys/policies/acl           - List ACL policies (requires token)
  GET  /v1/sys/policies/acl/<name>    - Read ACL policy (requires token)
  GET  /v1/secret/data/<path>         - Read secret (requires token with permission)
  GET  /v1/secret/metadata/<path>     - Read secret metadata (requires token)
  POST /v1/secret/data/<path>         - Write secret (requires bootstrap token)
  POST /v1/auth/token/create          - Create a new token (requires bootstrap token)
  GET  /v1/auth/token/lookup          - Lookup token details (requires token)
  GET  /v1/auth/token/lookup-self     - Lookup current token (requires token)

Vulnerability (by design, for CTF):
  G1 (Vault Bootstrap Token Abuse): The CI runner's vault-credentials.json contains
      a bootstrap token (s.bootstrap-nebulamind-2026) with excessive privileges.
      Players obtain this token via the E3 command injection chain, then use it to
      read secret/data/nebulamind/model-registry, which contains the G1 flag and
      model_registry_admin_token (for the G2 chain).

Authentication:
  Tokens are passed via the X-Vault-Token header or Authorization: Bearer header.
  Different tokens have different policies:
    - s.bootstrap-nebulamind-2026  : bootstrap policy (full access to all secrets)
    - s.ci-reader-2026-readonly    : ci-reader policy (only ci-config)
    - s.model-admin-2026-registry  : model-admin policy (only model-registry)
  No token or invalid token -> 403 permission denied.
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

from flask import Flask, Response, jsonify, render_template, request

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "8200"))
HOST = os.environ.get("HOST", "0.0.0.0")
APP_DIR = os.path.dirname(os.path.abspath(__file__))
SEED_FILE = os.environ.get(
    "VAULT_SEED_FILE", "/opt/nebulamind/seed-processed/secrets.json"
)
SEED_FALLBACK = os.path.join(APP_DIR, "seed", "secrets.json")
POLICIES_DIR = os.path.join(APP_DIR, "policies")

# ---------------------------------------------------------------------------
# Flag injection
# ---------------------------------------------------------------------------

FLAG_G1 = get_flag("FLAG_VAULT_POLICY_BYPASS", "flag{g1_vault_policy_bypass_placeholder}")

# ---------------------------------------------------------------------------
# Token definitions
# ---------------------------------------------------------------------------

# Bootstrap token matches ci-runner's vault-credentials.json.
# Players obtain this token via E3 command injection on ci-runner.
BOOTSTRAP_TOKEN = "s.bootstrap-nebulamind-2026"
CI_READER_TOKEN = "s.ci-reader-2026-readonly"
MODEL_ADMIN_TOKEN = "s.model-admin-2026-registry"

TOKENS: dict[str, dict] = {
    BOOTSTRAP_TOKEN: {
        "policies": ["bootstrap"],
        "display_name": "ci-runner-bootstrap",
        "entity_id": "00000000-0000-0000-0000-000000000001",
        "expire_time": None,
        "creation_time": int(time.time()) - 86400,
        "creation_ttl": 0,
        "num_uses": 0,
        "orphan": True,
        "meta": {"role": "ci-runner", "service": "nebulamind-ci"},
    },
    CI_READER_TOKEN: {
        "policies": ["ci-reader"],
        "display_name": "ci-reader-readonly",
        "entity_id": "00000000-0000-0000-0000-000000000002",
        "expire_time": None,
        "creation_time": int(time.time()) - 3600,
        "creation_ttl": 86400,
        "num_uses": 0,
        "orphan": False,
        "meta": {"role": "ci-reader", "service": "nebulamind-ci"},
    },
    MODEL_ADMIN_TOKEN: {
        "policies": ["model-admin"],
        "display_name": "model-registry-admin",
        "entity_id": "00000000-0000-0000-0000-000000000003",
        "expire_time": None,
        "creation_time": int(time.time()) - 7200,
        "creation_ttl": 86400,
        "num_uses": 0,
        "orphan": False,
        "meta": {"role": "model-admin", "service": "model-registry"},
    },
}

# ---------------------------------------------------------------------------
# Policy path permissions
# ---------------------------------------------------------------------------

# Maps policy name -> set of allowed secret paths (without "secret/data/" prefix)
POLICY_PATHS: dict[str, set[str]] = {
    "bootstrap": {"*"},  # all paths
    "ci-reader": {"nebulamind/ci-config"},
    "model-admin": {"nebulamind/model-registry"},
}


def policy_allows(policy_name: str, secret_path: str) -> bool:
    """Check if a policy allows access to the given secret path."""
    allowed = POLICY_PATHS.get(policy_name, set())
    if "*" in allowed:
        return True
    return secret_path in allowed


# ---------------------------------------------------------------------------
# Load secrets from seed file
# ---------------------------------------------------------------------------

def load_secrets() -> dict[str, dict]:
    """Load secrets from the processed seed file, with flag injected.

    The entrypoint.sh replaces __NM_FLAG_G1__ in the seed file via sed.
    As a fallback (e.g., when running outside the container), this function
    also replaces the placeholder in memory using get_flag().
    """
    seed_path = SEED_FILE if os.path.exists(SEED_FILE) else SEED_FALLBACK
    try:
        with open(seed_path, "r", encoding="utf-8") as f:
            raw = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        raw = {"_secrets": {}}

    secrets = raw.get("_secrets", {})
    # In-memory flag replacement (fallback if sed didn't run)
    model_reg = secrets.get("nebulamind/model-registry", {})
    data = model_reg.get("data", {})
    if isinstance(data.get("flag"), str) and "__NM_FLAG_G1__" in data["flag"]:
        data["flag"] = FLAG_G1
        model_reg["data"] = data
        secrets["nebulamind/model-registry"] = model_reg

    return secrets


SECRETS = load_secrets()

# ---------------------------------------------------------------------------
# Load policies from JSON files
# ---------------------------------------------------------------------------


def load_policies() -> dict[str, dict]:
    """Load policy definitions from policies/*.json."""
    policies = {}
    if os.path.isdir(POLICIES_DIR):
        for fname in os.listdir(POLICIES_DIR):
            if not fname.endswith(".json"):
                continue
            fpath = os.path.join(POLICIES_DIR, fname)
            try:
                with open(fpath, "r", encoding="utf-8") as f:
                    pol = json.load(f)
                name = pol.get("name", fname.replace(".json", ""))
                policies[name] = pol
            except (json.JSONDecodeError, OSError):
                continue
    return policies


POLICIES = load_policies()

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


def vault_response(data: dict, lease_duration: int = 0, renewable: bool = False,
                   warnings=None) -> tuple[Response, int]:
    """Build a Vault-style API response."""
    body = {
        "request_id": gen_request_id(),
        "lease_id": "",
        "renewable": renewable,
        "lease_duration": lease_duration,
        "data": data,
        "wrap_info": None,
        "warnings": warnings,
        "auth": None,
    }
    return jsonify(body), 200


def vault_error(errors: list[str], status: int = 400) -> tuple[Response, int]:
    """Build a Vault-style error response."""
    body = {
        "request_id": gen_request_id(),
        "lease_id": "",
        "renewable": False,
        "lease_duration": 0,
        "data": None,
        "wrap_info": None,
        "warnings": None,
        "errors": errors,
        "auth": None,
    }
    return jsonify(body), status


def extract_token() -> str | None:
    """Extract the Vault token from request headers.

    Supports both X-Vault-Token header and Authorization: Bearer header.
    """
    token = request.headers.get("X-Vault-Token")
    if token:
        return token.strip()
    auth = request.headers.get("Authorization", "")
    if auth.lower().startswith("bearer "):
        return auth[7:].strip()
    return None


def lookup_token(token: str) -> dict | None:
    """Look up a token and return its metadata, or None if invalid."""
    return TOKENS.get(token)


def token_has_access(token: str, secret_path: str) -> bool:
    """Check if a token's policies allow access to the given secret path."""
    info = lookup_token(token)
    if not info:
        return False
    for policy_name in info.get("policies", []):
        if policy_allows(policy_name, secret_path):
            return True
    return False


def gen_token_id(prefix: str) -> str:
    """Generate a Vault-style token ID (s. prefix)."""
    return f"s.{prefix}-{uuid.uuid4().hex[:20]}"


# ---------------------------------------------------------------------------
# Health check routes
# ---------------------------------------------------------------------------


@app.route("/healthz")
def healthz():
    """Simple health check endpoint for Docker healthcheck."""
    return jsonify({"status": "ok", "service": "secrets-vault"}), 200


@app.route("/v1/sys/health")
def sys_health():
    """Vault-style health check endpoint."""
    return jsonify({
        "initialized": True,
        "sealed": False,
        "standby": False,
        "performance_standby": False,
        "replication_performance_mode": "disabled",
        "replication_dr_mode": "disabled",
        "server_time_utc": int(time.time()),
        "version": "1.18.2",
        "cluster_name": "nebulamind-vault",
        "cluster_id": "nm-vault-2026-0001",
    }), 200


# ---------------------------------------------------------------------------
# UI route
# ---------------------------------------------------------------------------


@app.route("/")
def index():
    """Vault UI landing page."""
    return render_template("index.html")


# ---------------------------------------------------------------------------
# System routes
# ---------------------------------------------------------------------------


@app.route("/v1/sys/mounts")
def sys_mounts():
    """List secret engine mounts (Vault-style)."""
    token = extract_token()
    if not token or not lookup_token(token):
        return vault_error(["permission denied"], 403)

    mounts = {
        "secret/": {
            "type": "kv",
            "description": "Key-Value secret storage (version 2)",
            "accessor": "kv_20260101",
            "config": {
                "default_lease_ttl": 0,
                "max_lease_ttl": 0,
                "force_no_cache": False,
            },
            "options": {
                "version": "2",
            },
            "local": False,
            "seal_wrap": False,
            "external_entropy_access": False,
        },
        "auth/": {
            "type": "token",
            "description": "Token-based authentication",
            "accessor": "auth_token_20260101",
            "config": {},
            "options": {},
            "local": False,
            "seal_wrap": False,
        },
        "sys/": {
            "type": "system",
            "description": "System endpoints",
            "accessor": "sys_20260101",
            "config": {},
            "options": {},
            "local": False,
            "seal_wrap": False,
        },
    }
    return vault_response(mounts)


@app.route("/v1/sys/policies/acl")
def sys_policies_list():
    """List ACL policies (Vault-style)."""
    token = extract_token()
    if not token or not lookup_token(token):
        return vault_error(["permission denied"], 403)

    policy_names = sorted(POLICIES.keys())
    return vault_response({"keys": policy_names})


@app.route("/v1/sys/policies/acl/<name>")
def sys_policy_read(name: str):
    """Read a specific ACL policy (Vault-style)."""
    token = extract_token()
    if not token or not lookup_token(token):
        return vault_error(["permission denied"], 403)

    policy = POLICIES.get(name)
    if not policy:
        return vault_error([f"no policy named: {name}"], 404)

    return vault_response({
        "name": policy.get("name", name),
        "rules": json.dumps(policy.get("rules", {}), ensure_ascii=False),
        "description": policy.get("description", ""),
    })


# ---------------------------------------------------------------------------
# Secret read/write routes (KV v2 style)
# ---------------------------------------------------------------------------


@app.route("/v1/secret/data/<path:path>")
def secret_read(path: str):
    """Read a secret at the given path (Vault KV v2 style).

    Response format:
      {
        "data": {
          "data": { ...secret fields... },
          "metadata": { "created_time": ..., "version": ... }
        }
      }
    """
    token = extract_token()
    if not token:
        return vault_error(["missing client token"], 403)

    token_info = lookup_token(token)
    if not token_info:
        return vault_error(["permission denied"], 403)

    secret_key = f"nebulamind/{path}" if not path.startswith("nebulamind/") else path

    if not token_has_access(token, secret_key):
        return vault_error(["permission denied"], 403)

    secret = SECRETS.get(secret_key)
    if not secret:
        return vault_error([f"no value found at secret/data/{path}"], 404)

    return vault_response({
        "data": secret.get("data", {}),
        "metadata": secret.get("metadata", {
            "created_time": now_iso(),
            "deletion_time": "",
            "destroyed": False,
            "version": 1,
        }),
    })


@app.route("/v1/secret/data/<path:path>", methods=["POST", "PUT"])
def secret_write(path: str):
    """Write a secret at the given path (Vault KV v2 style).

    Only the bootstrap token can write secrets.
    """
    token = extract_token()
    if not token:
        return vault_error(["missing client token"], 403)

    token_info = lookup_token(token)
    if not token_info:
        return vault_error(["permission denied"], 403)

    secret_key = f"nebulamind/{path}" if not path.startswith("nebulamind/") else path

    # Only bootstrap policy allows writes
    if "bootstrap" not in token_info.get("policies", []):
        return vault_error(["permission denied"], 403)

    body = request.get_json(silent=True) or {}
    new_data = body.get("data", body)

    existing = SECRETS.get(secret_key, {})
    version = existing.get("metadata", {}).get("version", 0) + 1

    SECRETS[secret_key] = {
        "data": new_data,
        "metadata": {
            "created_time": now_iso(),
            "deletion_time": "",
            "destroyed": False,
            "version": version,
        },
    }

    return vault_response({
        "data": {
            "created_time": now_iso(),
            "deletion_time": "",
            "destroyed": False,
            "version": version,
        }
    })


@app.route("/v1/secret/metadata/<path:path>")
def secret_metadata(path: str):
    """Read secret metadata (Vault KV v2 style)."""
    token = extract_token()
    if not token:
        return vault_error(["missing client token"], 403)

    token_info = lookup_token(token)
    if not token_info:
        return vault_error(["permission denied"], 403)

    secret_key = f"nebulamind/{path}" if not path.startswith("nebulamind/") else path

    if not token_has_access(token, secret_key):
        return vault_error(["permission denied"], 403)

    secret = SECRETS.get(secret_key)
    if not secret:
        return vault_error([f"no value found at secret/metadata/{path}"], 404)

    return vault_response({
        "data": secret.get("metadata", {
            "created_time": now_iso(),
            "deletion_time": "",
            "destroyed": False,
            "version": 1,
        })
    })


# ---------------------------------------------------------------------------
# Token auth routes
# ---------------------------------------------------------------------------


@app.route("/v1/auth/token/create", methods=["POST"])
def token_create():
    """Create a new token (Vault-style).

    Only the bootstrap token can create new tokens.
    The new token inherits the policies specified in the request body.
    """
    token = extract_token()
    if not token:
        return vault_error(["missing client token"], 403)

    token_info = lookup_token(token)
    if not token_info:
        return vault_error(["permission denied"], 403)

    # Only bootstrap can create tokens
    if "bootstrap" not in token_info.get("policies", []):
        return vault_error(["permission denied"], 403)

    body = request.get_json(silent=True) or {}
    requested_policies = body.get("policies", [])
    display_name = body.get("display_name", "dynamic-token")
    ttl = body.get("ttl", 3600)

    # Validate requested policies
    valid_policies = []
    for p in requested_policies:
        if p in POLICIES:
            valid_policies.append(p)

    if not valid_policies:
        valid_policies = ["ci-reader"]  # default to least privilege

    new_token = gen_token_id("dyn")
    TOKENS[new_token] = {
        "policies": valid_policies,
        "display_name": display_name,
        "entity_id": str(uuid.uuid4()),
        "expire_time": None,
        "creation_time": int(time.time()),
        "creation_ttl": ttl,
        "num_uses": 0,
        "orphan": body.get("no_parent", False),
        "meta": body.get("meta", {"role": "dynamic", "created_by": "bootstrap"}),
    }

    return vault_response({
        "auth": {
            "client_token": new_token,
            "accessor": f"tok_{uuid.uuid4().hex[:16]}",
            "policies": valid_policies,
            "token_policies": valid_policies,
            "identity_policies": [],
            "metadata": body.get("meta", {"role": "dynamic"}),
            "lease_duration": ttl,
            "renewable": True,
            "entity_id": TOKENS[new_token]["entity_id"],
            "token_type": "service",
            "orphan": body.get("no_parent", False),
        }
    })


@app.route("/v1/auth/token/lookup")
def token_lookup():
    """Lookup a token by its value (Vault-style).

    The token to lookup is passed via the 'token' query parameter.
    """
    token = extract_token()
    if not token:
        return vault_error(["missing client token"], 403)

    caller_info = lookup_token(token)
    if not caller_info:
        return vault_error(["permission denied"], 403)

    target_token = request.args.get("token", "").strip()
    if not target_token:
        return vault_error(["missing 'token' query parameter"], 400)

    # Only bootstrap can lookup arbitrary tokens
    if "bootstrap" not in caller_info.get("policies", []):
        return vault_error(["permission denied"], 403)

    target_info = lookup_token(target_token)
    if not target_info:
        return vault_error(["bad token"], 403)

    return vault_response({
        "data": {
            "accessor": f"tok_{uuid.uuid4().hex[:16]}",
            "creation_time": target_info["creation_time"],
            "creation_ttl": target_info.get("creation_ttl", 3600),
            "display_name": target_info.get("display_name", ""),
            "entity_id": target_info.get("entity_id", ""),
            "expire_time": target_info.get("expire_time"),
            "id": target_token,
            "meta": target_info.get("meta", {}),
            "num_uses": target_info.get("num_uses", 0),
            "orphan": target_info.get("orphan", False),
            "policies": target_info.get("policies", []),
            "token_policies": target_info.get("policies", []),
            "ttl": target_info.get("creation_ttl", 3600),
            "type": "service",
        }
    })


@app.route("/v1/auth/token/lookup-self")
def token_lookup_self():
    """Lookup the current token (Vault-style)."""
    token = extract_token()
    if not token:
        return vault_error(["missing client token"], 403)

    token_info = lookup_token(token)
    if not token_info:
        return vault_error(["permission denied"], 403)

    return vault_response({
        "data": {
            "accessor": f"tok_{uuid.uuid4().hex[:16]}",
            "creation_time": token_info["creation_time"],
            "creation_ttl": token_info.get("creation_ttl", 3600),
            "display_name": token_info.get("display_name", ""),
            "entity_id": token_info.get("entity_id", ""),
            "expire_time": token_info.get("expire_time"),
            "id": token,
            "meta": token_info.get("meta", {}),
            "num_uses": token_info.get("num_uses", 0),
            "orphan": token_info.get("orphan", False),
            "policies": token_info.get("policies", []),
            "token_policies": token_info.get("policies", []),
            "ttl": token_info.get("creation_ttl", 3600),
            "type": "service",
        }
    })


# ---------------------------------------------------------------------------
# Error handlers
# ---------------------------------------------------------------------------


@app.errorhandler(404)
def not_found(e):
    return vault_error([f"no handler for route {request.path}"], 404)


@app.errorhandler(405)
def method_not_allowed(e):
    return vault_error([f"method {request.method} not allowed for {request.path}"], 405)


@app.errorhandler(500)
def internal_error(e):
    return vault_error(["internal server error"], 500)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    print(f"[secrets-vault] G1 flag loaded (FLAG_VAULT_POLICY_BYPASS)")
    print(f"[secrets-vault] loaded {len(SECRETS)} secrets from seed file")
    print(f"[secrets-vault] loaded {len(POLICIES)} policies")
    print(f"[secrets-vault] bootstrap token: {BOOTSTRAP_TOKEN}")
    print(f"[secrets-vault] ci-reader token: {CI_READER_TOKEN}")
    print(f"[secrets-vault] model-admin token: {MODEL_ADMIN_TOKEN}")
    print(f"[secrets-vault] G1 flag in: secret/data/nebulamind/model-registry")
    print(f"[secrets-vault] starting Flask Vault mock on {HOST}:{PORT}")
    app.run(host=HOST, port=PORT, debug=False, threaded=True)
