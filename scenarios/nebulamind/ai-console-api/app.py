#!/usr/bin/env python3
"""NebulaMind AI Corp - Internal Operations Console API.

Business-zone service providing the internal operations console API.
Connected to biz-core and data-plane networks.

Routes:
  GET  /                                          - API info
  GET  /healthz                                   - Health check
  GET  /api/v1/console/session/bootstrap          - Session bootstrap (tenant info + features)
  GET  /api/v1/knowledge-bases?tenantId=xxx       - Knowledge base list (C1 IDOR)
  GET  /api/v1/knowledge-bases/<id>               - Knowledge base detail
  POST /api/v1/auth/login                         - Login (returns JWT, default role=viewer)
  GET  /api/v1/admin/audit/export                 - Audit log export (C2, requires operator JWT)
  POST /graphql                                   - GraphQL endpoint (C3)
  GET  /graphql                                   - GraphQL IDE (GraphiQL-style)

Vulnerabilities (by design, for CTF):
  C1 (IDOR): /api/v1/knowledge-bases accepts tenantId param without checking
      whether the caller is authorized for that tenant. Players enumerate other
      tenants' knowledge bases. The C1 flag is in the description of a deprecated
      knowledge base (id=17, tenant_001).
  C2 (JWT weak secret): The dev/staging environment uses the weak JWT secret
      "nebulamind-dev-secret-2026" (candidate leaked in document-worker's
      service-account.json). Players forge a role=operator JWT to access
      /api/v1/admin/audit/export. The audit log contains the C2 flag and the
      platform-injected internal Git URL.
  C3 (GraphQL): /graphql exposes introspection and the integrationSecrets field.
      Querying integrationSecrets(masked:false) requires an operator JWT and
      returns object store bucket, git service URL, low-priv access key, and the
      C3 flag.
"""

from __future__ import annotations

import os
import re
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

import jwt
from flask import Flask, Response, abort, jsonify, render_template, request

import seed_data

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "8080"))
HOST = os.environ.get("HOST", "0.0.0.0")

APP_DIR = os.path.dirname(os.path.abspath(__file__))

GIT_SERVICE_URL = os.environ["NM_GIT_SERVICE_URL"].rstrip("/")
OBJECT_STORE_URL = os.environ["NM_OBJECT_STORE_URL"].rstrip("/")

# C2: JWT weak secret (dev/staging environment).
# The candidate "nebulamind-dev-secret-2026" is leaked in document-worker's
# service-account.json (jwt_secret_candidate field).
JWT_SECRET = os.environ.get("JWT_SECRET", "nebulamind-dev-secret-2026")
JWT_ALGORITHM = "HS256"
JWT_TTL = int(os.environ.get("JWT_TTL", "3600"))

# ---------------------------------------------------------------------------
# Flag injection
# ---------------------------------------------------------------------------

FLAG_C1 = get_flag("FLAG_API_TENANT_IDOR", "flag{c1_tenant_idor_placeholder}")
FLAG_C2 = get_flag("FLAG_API_JWT_ROLE", "flag{c2_jwt_role_placeholder}")
FLAG_C3 = get_flag("FLAG_API_GRAPHQL_AUDIT", "flag{c3_graphql_audit_placeholder}")


def build_knowledge_bases() -> list[dict]:
    """Build knowledge base list with C1 flag injected into id=17 description."""
    bases = []
    for kb in seed_data.KNOWLEDGE_BASES:
        record = dict(kb)
        if record["id"] == 17:
            record["description"] = record["description"].replace(
                "__NM_FLAG_C1__", FLAG_C1
            )
        bases.append(record)
    return bases


def build_audit_logs() -> list[dict]:
    """Build audit log list with C2 flag injected into git.sync event metadata."""
    logs = []
    for entry in seed_data.AUDIT_LOGS:
        record = dict(entry)
        metadata = dict(record.get("metadata", {}))
        for key, val in metadata.items():
            if isinstance(val, str) and "__NM_FLAG_C2__" in val:
                val = val.replace("__NM_FLAG_C2__", FLAG_C2)
            if isinstance(val, str):
                metadata[key] = (
                    val.replace("__NM_GIT_SERVICE_URL__", GIT_SERVICE_URL)
                    .replace("__NM_OBJECT_STORE_URL__", OBJECT_STORE_URL)
                )
        resource = record.get("resource")
        if isinstance(resource, str):
            record["resource"] = (
                resource.replace("__NM_GIT_SERVICE_URL__", GIT_SERVICE_URL)
                .replace("__NM_OBJECT_STORE_URL__", OBJECT_STORE_URL)
            )
        record["metadata"] = metadata
        logs.append(record)
    return logs


def build_integration_secrets() -> list[dict]:
    """Build integration secrets with C3 flag injected."""
    secrets = []
    for sec in seed_data.INTEGRATION_SECRETS:
        record = dict(sec)
        if "__NM_FLAG_C3__" in record.get("flag", ""):
            record["flag"] = FLAG_C3
        for key, val in list(record.items()):
            if isinstance(val, str):
                record[key] = val.replace("__NM_GIT_SERVICE_URL__", GIT_SERVICE_URL).replace(
                    "__NM_OBJECT_STORE_URL__", OBJECT_STORE_URL
                )
        secrets.append(record)
    return secrets


# In-memory data stores (built at startup with flags injected)
KNOWLEDGE_BASES = build_knowledge_bases()
AUDIT_LOGS = build_audit_logs()
INTEGRATION_SECRETS = build_integration_secrets()
ACCOUNTS = seed_data.ACCOUNTS
TENANTS = seed_data.TENANTS

# Index for quick lookup
KB_BY_ID = {kb["id"]: kb for kb in KNOWLEDGE_BASES}
ACCOUNTS_BY_USERNAME = {a["username"]: a for a in ACCOUNTS}

# ---------------------------------------------------------------------------
# Flask app
# ---------------------------------------------------------------------------

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = 10 * 1024 * 1024  # 10MB


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def generate_trace_id() -> str:
    ts = int(time.time() * 1000)
    rand = uuid.uuid4().hex[:8]
    return f"nm-{ts}-{rand}"


def create_jwt(subject: str, role: str, tenant: str) -> str:
    """Create a JWT token with the given subject, role, and tenant."""
    now = int(time.time())
    payload = {
        "sub": subject,
        "role": role,
        "tenant": tenant,
        "iat": now,
        "exp": now + JWT_TTL,
        "iss": "nebulamind-console-api",
        "aud": "nebulamind-internal",
    }
    return jwt.encode(payload, JWT_SECRET, algorithm=JWT_ALGORITHM)


def verify_jwt(token: str) -> dict | None:
    """Verify a JWT token and return the payload, or None if invalid."""
    try:
        payload = jwt.decode(
            token,
            JWT_SECRET,
            algorithms=[JWT_ALGORITHM],
            options={"verify_aud": False},
        )
        return payload
    except jwt.PyJWTError:
        return None


def extract_bearer_token() -> str | None:
    """Extract Bearer token from Authorization header."""
    auth = request.headers.get("Authorization", "")
    if auth.startswith("Bearer "):
        return auth[7:].strip()
    return None


def get_auth_payload() -> dict | None:
    """Get and verify the JWT payload from the request, or None."""
    token = extract_bearer_token()
    if not token:
        return None
    return verify_jwt(token)


def require_operator() -> dict | None:
    """Verify the request has a valid operator JWT. Returns payload or None.

    Used by C2 (audit export) and C3 (integrationSecrets query).
    """
    payload = get_auth_payload()
    if payload is None:
        return None
    if payload.get("role") != "operator":
        return None
    return payload


def mask_access_key(key: str) -> str:
    """Mask an access key, showing first 4 and last 4 characters."""
    if not key or len(key) <= 8:
        return "****"
    return key[:4] + "*" * (len(key) - 8) + key[-4:]


def mask_secret(secret: dict) -> dict:
    """Return a masked copy of an integration secret."""
    masked = dict(secret)
    masked["lowPrivAccessKey"] = mask_access_key(secret.get("lowPrivAccessKey", ""))
    masked["lowPrivSecretKey"] = "****"
    masked["flag"] = "****"
    return masked


# ---------------------------------------------------------------------------
# GraphQL schema (for introspection)
# ---------------------------------------------------------------------------

GRAPHQL_SCHEMA = {
    "queryType": {"name": "Query"},
    "mutationType": None,
    "subscriptionType": None,
    "types": [
        {
            "name": "Query",
            "kind": "OBJECT",
            "description": "Root query type for the NebulaMind internal operations console.",
            "fields": [
                {
                    "name": "integrationSecrets",
                    "description": "List integration secrets (requires operator role). "
                                   "Use masked:false to reveal full values.",
                    "args": [
                        {
                            "name": "masked",
                            "type": {"kind": "SCALAR", "name": "Boolean"},
                            "defaultValue": "true",
                            "description": "Whether to mask sensitive values (default: true).",
                        },
                    ],
                    "type": {
                        "kind": "LIST",
                        "ofType": {"kind": "OBJECT", "name": "IntegrationSecret"},
                    },
                },
                {
                    "name": "knowledgeBases",
                    "description": "List knowledge bases for a tenant.",
                    "args": [
                        {
                            "name": "tenantId",
                            "type": {"kind": "SCALAR", "name": "String"},
                            "defaultValue": None,
                        },
                    ],
                    "type": {
                        "kind": "LIST",
                        "ofType": {"kind": "OBJECT", "name": "KnowledgeBase"},
                    },
                },
                {
                    "name": "auditEvents",
                    "description": "List audit events (requires operator role).",
                    "args": [],
                    "type": {
                        "kind": "LIST",
                        "ofType": {"kind": "OBJECT", "name": "AuditEvent"},
                    },
                },
            ],
        },
        {
            "name": "IntegrationSecret",
            "kind": "OBJECT",
            "description": "Integration secret for external services (object store, git, etc.).",
            "fields": [
                {"name": "name", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "objectStoreBucket", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "gitServiceUrl", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "lowPrivAccessKey", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "lowPrivSecretKey", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "flag", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "region", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "endpoint", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "description", "type": {"kind": "SCALAR", "name": "String"}},
            ],
        },
        {
            "name": "KnowledgeBase",
            "kind": "OBJECT",
            "description": "A knowledge base owned by a tenant.",
            "fields": [
                {"name": "id", "type": {"kind": "SCALAR", "name": "Int"}},
                {"name": "tenantId", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "name", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "description", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "dataset", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "updatedAt", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "status", "type": {"kind": "SCALAR", "name": "String"}},
            ],
        },
        {
            "name": "AuditEvent",
            "kind": "OBJECT",
            "description": "An audit event in the console.",
            "fields": [
                {"name": "id", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "timestamp", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "actor", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "actorRole", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "action", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "resource", "type": {"kind": "SCALAR", "name": "String"}},
                {"name": "result", "type": {"kind": "SCALAR", "name": "String"}},
            ],
        },
        {"name": "String", "kind": "SCALAR"},
        {"name": "Int", "kind": "SCALAR"},
        {"name": "Boolean", "kind": "SCALAR"},
        {"name": "Float", "kind": "SCALAR"},
        {"name": "ID", "kind": "SCALAR"},
    ],
    "directives": [
        {
            "name": "include",
            "description": "Directs the executor to include this field or fragment only when the `if` argument is true.",
            "locations": ["FIELD", "FRAGMENT_SPREAD", "INLINE_FRAGMENT"],
            "args": [
                {"name": "if", "type": {"kind": "SCALAR", "name": "Boolean"}, "defaultValue": None},
            ],
        },
        {
            "name": "skip",
            "description": "Directs the executor to skip this field or fragment when the `if` argument is true.",
            "locations": ["FIELD", "FRAGMENT_SPREAD", "INLINE_FRAGMENT"],
            "args": [
                {"name": "if", "type": {"kind": "SCALAR", "name": "Boolean"}, "defaultValue": None},
            ],
        },
    ],
}


# ---------------------------------------------------------------------------
# Minimal GraphQL parser/executor
# ---------------------------------------------------------------------------

def _extract_top_field(query: str) -> str | None:
    """Extract the first top-level field name from a GraphQL query.

    Handles queries like:
      { integrationSecrets { ... } }
      { integrationSecrets(masked: false) { ... } }
      query OpName { integrationSecrets { ... } }
    """
    # Remove leading "query" / "mutation" keyword
    cleaned = re.sub(r"^(query|mutation)\s+\w*\s*", "", query.strip())
    # Find the first field name after the opening brace
    match = re.search(r"[{]\s*(\w+)", cleaned)
    if match:
        return match.group(1)
    return None


def _extract_bool_arg(query: str, field_name: str, arg_name: str) -> bool | None:
    """Extract a boolean argument value from a GraphQL field.

    Looks for patterns like: fieldName(argName: false) or fieldName(argName: true).
    Returns None if the argument is not present.
    """
    pattern = rf"{re.escape(field_name)}\s*\([^)]*{re.escape(arg_name)}\s*:\s*(true|false)"
    match = re.search(pattern, query, re.IGNORECASE)
    if match:
        return match.group(1).lower() == "true"
    return None


def _is_introspection_query(query: str) -> bool:
    """Check if the query is an introspection query."""
    return "__schema" in query or "__type" in query


def _build_introspection_response(query: str) -> dict:
    """Build an introspection response.

    Handles __schema and __type queries.
    """
    if "__type" in query and "name:" in query:
        # __type(name: "TypeName") query
        match = re.search(r'__type\s*\(\s*name\s*:\s*"([^"]+)"\s*\)', query)
        if match:
            type_name = match.group(1)
            for t in GRAPHQL_SCHEMA["types"]:
                if t["name"] == type_name:
                    return {"__type": t}
            return {"__type": None}
    # __schema query - return full schema
    return {"__schema": GRAPHQL_SCHEMA}


def execute_graphql(query: str, variables: dict | None, operation_name: str | None) -> tuple[dict, int]:
    """Execute a GraphQL query and return (response_dict, status_code).

    Supports:
      - Introspection (__schema, __type)
      - integrationSecrets(masked: Boolean)
      - knowledgeBases(tenantId: String)
      - auditEvents
    """
    if not query or not query.strip():
        return {"errors": [{"message": "query is required"}]}, 400

    # Handle introspection
    if _is_introspection_query(query):
        return {"data": _build_introspection_response(query)}, 200

    top_field = _extract_top_field(query)
    if top_field is None:
        return {"errors": [{"message": "could not parse query"}]}, 400

    # --- integrationSecrets (C3) ---
    if top_field == "integrationSecrets":
        payload = require_operator()
        if payload is None:
            auth = extract_bearer_token()
            if auth is None:
                return {
                    "errors": [{"message": "authentication required: provide a Bearer JWT token"}],
                }, 401
            return {
                "errors": [{"message": "forbidden: operator role required to query integrationSecrets"}],
            }, 403

        masked = _extract_bool_arg(query, "integrationSecrets", "masked")
        if masked is None:
            masked = True  # default: masked

        if masked:
            result = [mask_secret(s) for s in INTEGRATION_SECRETS]
        else:
            result = [
                {
                    "name": s["name"],
                    "objectStoreBucket": s["objectStoreBucket"],
                    "gitServiceUrl": s["gitServiceUrl"],
                    "lowPrivAccessKey": s["lowPrivAccessKey"],
                    "lowPrivSecretKey": s["lowPrivSecretKey"],
                    "flag": s["flag"],
                }
                for s in INTEGRATION_SECRETS
            ]
        return {"data": {"integrationSecrets": result}}, 200

    # --- knowledgeBases ---
    if top_field == "knowledgeBases":
        # Extract tenantId argument
        match = re.search(r'knowledgeBases\s*\(\s*tenantId\s*:\s*"([^"]+)"\s*\)', query)
        tenant_id = match.group(1) if match else "tenant_001"
        result = [
            {
                "id": kb["id"],
                "tenantId": kb["tenant_id"],
                "name": kb["name"],
                "description": kb["description"],
                "dataset": kb["dataset"],
                "updatedAt": kb["updated_at"],
                "status": kb["status"],
            }
            for kb in KNOWLEDGE_BASES
            if kb["tenant_id"] == tenant_id
        ]
        return {"data": {"knowledgeBases": result}}, 200

    # --- auditEvents ---
    if top_field == "auditEvents":
        payload = require_operator()
        if payload is None:
            auth = extract_bearer_token()
            if auth is None:
                return {
                    "errors": [{"message": "authentication required: provide a Bearer JWT token"}],
                }, 401
            return {
                "errors": [{"message": "forbidden: operator role required to query auditEvents"}],
            }, 403
        result = [
            {
                "id": e["id"],
                "timestamp": e["timestamp"],
                "actor": e["actor"],
                "actorRole": e["actor_role"],
                "action": e["action"],
                "resource": e["resource"],
                "result": e["result"],
            }
            for e in AUDIT_LOGS
        ]
        return {"data": {"auditEvents": result}}, 200

    return {
        "errors": [{"message": f"cannot query field '{top_field}' on type 'Query'"}],
    }, 400


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.route("/")
def index():
    """API info endpoint."""
    return jsonify({
        "service": "ai-console-api",
        "version": "2026.06.3",
        "zone": "business",
        "status": "running",
        "description": "NebulaMind AI Corp - Internal Operations Console API",
        "documentation": "/docs",
        "graphql": "/graphql",
        "endpoints": [
            "GET  /healthz",
            "GET  /",
            "GET  /api/v1/console/session/bootstrap",
            "GET  /api/v1/knowledge-bases?tenantId=xxx",
            "GET  /api/v1/knowledge-bases/<id>",
            "POST /api/v1/auth/login",
            "GET  /api/v1/admin/audit/export",
            "POST /graphql",
            "GET  /graphql",
        ],
        "timestamp": now_iso(),
    })


@app.route("/healthz")
def healthz():
    return Response("ok", status=200, mimetype="text/plain")


@app.route("/favicon.ico")
def favicon():
    return Response("", status=204, mimetype="image/x-icon")


@app.route("/docs")
def docs():
    """API documentation page."""
    try:
        return render_template("index.html")
    except Exception:
        return jsonify({
            "service": "ai-console-api",
            "version": "2026.06.3",
            "note": "documentation template not available",
            "endpoints": [
                "GET  /healthz",
                "GET  /api/v1/console/session/bootstrap",
                "GET  /api/v1/knowledge-bases?tenantId=xxx",
                "GET  /api/v1/knowledge-bases/<id>",
                "POST /api/v1/auth/login",
                "GET  /api/v1/admin/audit/export",
                "POST /graphql",
                "GET  /graphql",
            ],
        })


# --- Session bootstrap ---

@app.route("/api/v1/console/session/bootstrap", methods=["GET", "POST"])
def session_bootstrap():
    """Session bootstrap endpoint.

    Returns tenant info, feature flags, and session metadata.
    Called by the portal-web frontend during SSO session initialization.
    """
    # Determine tenant from header, body, or default
    tenant_id = request.headers.get("X-NM-Tenant", "tenant_001")
    if request.method == "POST":
        data = request.get_json(silent=True) or {}
        tenant_id = data.get("tenantId", tenant_id)

    tenant = TENANTS.get(tenant_id, TENANTS["tenant_001"])

    trace_id = request.headers.get("X-NM-Trace", generate_trace_id())

    return jsonify({
        "sessionToken": "",
        "tenant": tenant["tenant_id"],
        "tenantName": tenant["name"],
        "plan": tenant["plan"],
        "region": tenant["region"],
        "features": tenant["features"],
        "ssoClientId": "nm-portal-sso-prod",
        "traceId": trace_id,
        "apiService": "ai-console-api",
        "apiVersion": "2026.06.3",
        "consoleEndpoint": "/api/v1/console/session/bootstrap",
        "expiresAt": int(time.time()) + JWT_TTL,
        "permissions": ["kb:read", "auth:login"],
        "timestamp": now_iso(),
    })


# --- Knowledge bases (C1 IDOR) ---

@app.route("/api/v1/knowledge-bases")
def knowledge_bases_list():
    """List knowledge bases for a tenant.

    C1 IDOR vulnerability: the tenantId parameter is accepted without
    verifying that the caller is authorized for that tenant. Any caller
    can enumerate any tenant's knowledge bases by changing the tenantId.

    The C1 flag is in the description of knowledge base id=17 (tenant_001),
    a deprecated knowledge base.
    """
    tenant_id = request.args.get("tenantId", "tenant_001")

    # VULNERABILITY: no authorization check on tenantId.
    # A proper implementation would verify the caller's JWT tenant matches
    # the requested tenantId, or that the caller has cross-tenant access.
    # Here we just return the data for whatever tenantId is provided.

    results = [
        {
            "id": kb["id"],
            "tenantId": kb["tenant_id"],
            "name": kb["name"],
            "description": kb["description"],
            "dataset": kb["dataset"],
            "updatedAt": kb["updated_at"],
            "status": kb["status"],
        }
        for kb in KNOWLEDGE_BASES
        if kb["tenant_id"] == tenant_id
    ]

    return jsonify({
        "tenantId": tenant_id,
        "count": len(results),
        "items": results,
        "traceId": generate_trace_id(),
        "timestamp": now_iso(),
    })


@app.route("/api/v1/knowledge-bases/<int:kb_id>")
def knowledge_base_detail(kb_id: int):
    """Get a single knowledge base by ID.

    Also vulnerable to IDOR: no check that the caller owns this knowledge base.
    """
    kb = KB_BY_ID.get(kb_id)
    if kb is None:
        return jsonify({
            "error": "not_found",
            "message": f"knowledge base {kb_id} not found",
        }), 404

    return jsonify({
        "id": kb["id"],
        "tenantId": kb["tenant_id"],
        "name": kb["name"],
        "description": kb["description"],
        "dataset": kb["dataset"],
        "updatedAt": kb["updated_at"],
        "status": kb["status"],
        "traceId": generate_trace_id(),
        "timestamp": now_iso(),
    })


# --- Authentication (C2 login) ---

@app.route("/api/v1/auth/login", methods=["POST"])
def auth_login():
    """Login endpoint.

    Accepts {username, password} and returns a JWT.
    Default account: viewer/viewer123 -> role=viewer.
    The operator account exists but is locked (hinting that operator role exists).
    Players must forge a role=operator JWT using the weak secret.
    """
    data = request.get_json(silent=True) or {}
    username = (data.get("username") or "").strip()
    password = (data.get("password") or "").strip()

    if not username or not password:
        return jsonify({
            "error": "invalid_request",
            "message": "username and password are required",
        }), 400

    account = ACCOUNTS_BY_USERNAME.get(username)
    if account is None or account["password"] != password:
        return jsonify({
            "error": "invalid_credentials",
            "message": "invalid username or password",
        }), 401

    if account["status"] == "locked":
        return jsonify({
            "error": "account_locked",
            "message": f"account '{username}' is locked. Contact your administrator.",
            "hint": "this account has elevated privileges and requires admin unlock",
        }), 403

    token = create_jwt(
        subject=username,
        role=account["role"],
        tenant=account["tenant"],
    )

    return jsonify({
        "accessToken": token,
        "tokenType": "Bearer",
        "expiresIn": JWT_TTL,
        "user": {
            "username": account["username"],
            "role": account["role"],
            "tenant": account["tenant"],
            "displayName": account["display_name"],
        },
        "traceId": generate_trace_id(),
        "timestamp": now_iso(),
    })


# --- Audit log export (C2) ---

@app.route("/api/v1/admin/audit/export")
def audit_export():
    """Export audit logs.

    C2 vulnerability: requires operator role JWT, but the JWT secret is weak
    ("nebulamind-dev-secret-2026", leaked in document-worker's service-account.json).
    Players forge a role=operator JWT to access this endpoint.

    The exported audit logs contain the C2 flag (in the git.sync event metadata)
    and the platform-injected internal Git URL.
    """
    payload = require_operator()
    if payload is None:
        token = extract_bearer_token()
        if token is None:
            return jsonify({
                "error": "unauthorized",
                "message": "authentication required: provide a Bearer JWT token",
            }), 401
        caller_payload = get_auth_payload()
        return jsonify({
            "error": "forbidden",
            "message": "operator role required to export audit logs",
            "yourRole": caller_payload.get("role") if caller_payload else "unknown",
        }), 403

    return jsonify({
        "exportId": f"audit-export-{uuid.uuid4().hex[:8]}",
        "exportedBy": payload.get("sub", "unknown"),
        "exportedAt": now_iso(),
        "totalEvents": len(AUDIT_LOGS),
        "events": AUDIT_LOGS,
        "summary": {
            "timeRange": {
                "start": AUDIT_LOGS[0]["timestamp"] if AUDIT_LOGS else None,
                "end": AUDIT_LOGS[-1]["timestamp"] if AUDIT_LOGS else None,
            },
            "internalServices": {
                "gitService": GIT_SERVICE_URL,
                "objectStore": OBJECT_STORE_URL,
            },
        },
        "traceId": generate_trace_id(),
    })


# --- GraphQL (C3) ---

@app.route("/graphql", methods=["GET"])
def graphql_ide():
    """GraphQL IDE (GraphiQL-style simple page)."""
    try:
        return render_template("graphiql.html")
    except Exception:
        return Response(
            "<!DOCTYPE html><html><body><h1>GraphQL IDE</h1>"
            "<p>POST your GraphQL queries to /graphql</p></body></html>",
            status=200,
            content_type="text/html; charset=utf-8",
        )


@app.route("/graphql", methods=["POST"])
def graphql_endpoint():
    """GraphQL API endpoint.

    C3 vulnerability: introspection is enabled, and integrationSecrets(masked:false)
    returns the C3 flag along with internal service URLs and access keys.
    Requires an operator JWT (same weak-secret forgery as C2).
    """
    content_type = request.content_type or ""

    if "application/graphql" in content_type:
        query = request.get_data(as_text=True)
        variables = None
        operation_name = None
    else:
        data = request.get_json(silent=True) or {}
        query = data.get("query", "")
        variables = data.get("variables")
        operation_name = data.get("operationName")

    if not query:
        return jsonify({"errors": [{"message": "query is required"}]}), 400

    response, status = execute_graphql(query, variables, operation_name)
    return jsonify(response), status


# ---------------------------------------------------------------------------
# Internal metadata endpoint (referenced by document-worker SSRF)
# ---------------------------------------------------------------------------

@app.route("/internal/metadata")
def internal_metadata():
    """Internal service metadata endpoint.

    Referenced by document-worker's worker.yml. Returns service discovery info.
    This endpoint is meant for internal service-to-service communication.
    """
    return jsonify({
        "service": "ai-console-api",
        "version": "2026.06.3",
        "zone": "business",
        "networks": ["biz-core", "data-plane"],
        "endpoints": [
            "/healthz",
            "/api/v1/console/session/bootstrap",
            "/api/v1/knowledge-bases",
            "/api/v1/auth/login",
            "/api/v1/admin/audit/export",
            "/graphql",
            "/internal/metadata",
        ],
        "tenant": "tenant_001",
        "ssoClientId": "nm-portal-sso-prod",
        "traceHeader": "X-NM-Trace",
        "timestamp": now_iso(),
    })


# ---------------------------------------------------------------------------
# Error handlers
# ---------------------------------------------------------------------------

@app.errorhandler(404)
def not_found(e):
    if request.path.startswith("/api/") or request.path.startswith("/graphql"):
        return jsonify({"error": "not_found"}), 404
    return Response('{"error": "not found"}', status=404,
                    content_type="application/json")


@app.errorhandler(400)
def bad_request(e):
    desc = e.description if hasattr(e, "description") else "bad request"
    return jsonify({"error": "bad_request", "message": str(desc)}), 400


@app.errorhandler(401)
def unauthorized(e):
    return jsonify({"error": "unauthorized"}), 401


@app.errorhandler(403)
def forbidden(e):
    return jsonify({"error": "forbidden"}), 403


@app.errorhandler(405)
def method_not_allowed(e):
    return jsonify({"error": "method_not_allowed"}), 405


@app.errorhandler(500)
def internal_error(e):
    return jsonify({"error": "internal_server_error"}), 500


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    print(
        f"[ai-console-api] NebulaMind Internal Operations Console API "
        f"starting on {HOST}:{PORT}",
        flush=True,
    )
    print(f"[ai-console-api] JWT algorithm: {JWT_ALGORITHM}", flush=True)
    print(f"[ai-console-api] JWT TTL: {JWT_TTL}s", flush=True)
    print(f"[ai-console-api] Knowledge bases: {len(KNOWLEDGE_BASES)} loaded", flush=True)
    print(f"[ai-console-api] Audit logs: {len(AUDIT_LOGS)} loaded", flush=True)
    print(f"[ai-console-api] Integration secrets: {len(INTEGRATION_SECRETS)} loaded", flush=True)
    print(f"[ai-console-api] C1 flag injected: id=17 description", flush=True)
    print(f"[ai-console-api] C2 flag injected: audit git.sync metadata", flush=True)
    print(f"[ai-console-api] C3 flag injected: integration secrets flag field", flush=True)
    app.run(host=HOST, port=PORT, threaded=True, debug=False)
