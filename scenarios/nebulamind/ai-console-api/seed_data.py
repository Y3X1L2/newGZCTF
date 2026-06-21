#!/usr/bin/env python3
"""Clean seed data for the NebulaMind internal console API.

The image is designed for the platform multi-segment network model. It must not
assume Docker Compose service names or fixed IP addresses. Runtime links are
rendered from NM_* environment variables by app.py during startup.
"""

from __future__ import annotations


def _kb(
    item_id: int,
    tenant_id: str,
    name: str,
    description: str,
    dataset: str,
    updated_at: str,
    status: str = "active",
) -> dict:
    return {
        "id": item_id,
        "tenant_id": tenant_id,
        "name": name,
        "description": description,
        "dataset": dataset,
        "updated_at": updated_at,
        "status": status,
    }


KNOWLEDGE_BASES = [
    _kb(
        1,
        "tenant_001",
        "Product architecture handbook",
        "Architecture notes for NebulaMind Assist, including API gateway, retrieval pipeline, and rollout controls.",
        "product-docs-v3",
        "2026-06-18T09:12:00Z",
    ),
    _kb(
        2,
        "tenant_001",
        "Customer success playbook",
        "Support team playbook covering enterprise onboarding, ticket routing, and escalation workflows.",
        "customer-cases-2026",
        "2026-06-17T14:30:00Z",
    ),
    _kb(
        3,
        "tenant_001",
        "Operations runbook",
        "Runbook for Redis-backed worker queues, model API readiness checks, and incident response.",
        "ops-handbook-v2",
        "2026-06-15T08:00:00Z",
    ),
    _kb(
        4,
        "tenant_001",
        "Compliance controls",
        "Internal controls for retention, customer isolation, and regulated model approval.",
        "compliance-2026",
        "2026-06-14T16:45:00Z",
    ),
    _kb(
        5,
        "tenant_001",
        "Pretraining corpus catalog",
        "Catalog of pretraining corpus sources, normalization rules, and sensitive-source exclusions.",
        "pretrain-corpus-v5",
        "2026-06-12T11:20:00Z",
    ),
    _kb(
        6,
        "tenant_001",
        "Embedding index design",
        "Vector index maintenance notes for product search and retrieval augmented generation.",
        "embeddings-product-v3",
        "2026-06-10T19:00:00Z",
    ),
    _kb(
        7,
        "tenant_001",
        "Gateway route map",
        "Route metadata for the public portal and internal console bootstrap flow.",
        "gateway-routes-v2",
        "2026-06-08T10:15:00Z",
    ),
    _kb(
        8,
        "tenant_001",
        "Feedback triage notes",
        "Aggregated customer feedback and defect triage notes for the June enterprise release.",
        "feedback-tickets-2026",
        "2026-06-06T13:40:00Z",
    ),
    _kb(
        9,
        "tenant_001",
        "Finance vertical guide",
        "Deployment checklist for finance industry tenants using retrieval plug-ins and audit exports.",
        "finance-vertical-v1",
        "2026-06-04T09:30:00Z",
    ),
    _kb(
        10,
        "tenant_001",
        "Healthcare vertical guide",
        "Healthcare deployment notes, redaction rules, and model evaluation considerations.",
        "medical-kb-v2",
        "2026-06-02T15:50:00Z",
    ),
    _kb(
        11,
        "tenant_001",
        "Education vertical guide",
        "Course-content classification examples and public model evaluation notes.",
        "education-courses-v1",
        "2026-05-30T08:25:00Z",
    ),
    _kb(
        12,
        "tenant_001",
        "Legal document templates",
        "Contract parsing and clause extraction examples for legal-sector tenants.",
        "legal-contracts-v3",
        "2026-05-28T17:10:00Z",
    ),
    _kb(
        13,
        "tenant_001",
        "Translation memory",
        "Translation memory snippets and terminology mappings for multilingual support.",
        "translation-memory-v4",
        "2026-05-25T12:00:00Z",
    ),
    _kb(
        14,
        "tenant_001",
        "Code index notes",
        "Internal code indexing notes for repository ingestion and semantic search.",
        "code-index-v2",
        "2026-05-22T09:45:00Z",
    ),
    _kb(
        15,
        "tenant_001",
        "Monitoring alert catalog",
        "Prometheus and Grafana alert catalog for application and worker health.",
        "monitoring-alerts-v1",
        "2026-05-20T14:30:00Z",
    ),
    _kb(
        16,
        "tenant_001",
        "Analytics report catalog",
        "Recurring BI reports for tenant usage, completion rate, and model latency trends.",
        "analytics-reports-2026",
        "2026-05-18T16:00:00Z",
    ),
    _kb(
        17,
        "tenant_001",
        "[deprecated] legacy tenant bootstrap v0.9-alpha",
        (
            "Deprecated staging bootstrap notes for the 2025Q4 single-tenant console. "
            "The record was meant to be removed before production but remains exposed "
            "through tenantId enumeration. Internal validation marker: __NM_FLAG_C1__."
        ),
        "legacy-test-kb-v0.9-alpha",
        "2025-12-03T23:59:00Z",
        "deprecated",
    ),
    _kb(
        18,
        "tenant_001",
        "Partner integration SDK",
        "SDK request signing examples and partner onboarding compatibility notes.",
        "partner-integration-v2",
        "2026-05-15T10:20:00Z",
    ),
    _kb(
        19,
        "tenant_001",
        "Agent tool policy",
        "Function-calling tool policy, allowed action scopes, and audit event mapping.",
        "agent-tools-v1",
        "2026-05-12T13:30:00Z",
    ),
    _kb(
        20,
        "tenant_001",
        "Training materials",
        "Internal training material index for support, operations, and model platform engineers.",
        "training-materials-v3",
        "2026-05-10T09:00:00Z",
    ),
    _kb(21, "tenant_002", "Xinghe support FAQ", "Support FAQ for Xinghe Financial Cloud.", "xinghe-cs-kb-v2", "2026-06-17T11:00:00Z"),
    _kb(22, "tenant_002", "Xinghe fraud rules", "Fraud risk rule descriptions and approval workflow.", "xinghe-risk-rules-v1", "2026-06-16T15:30:00Z"),
    _kb(23, "tenant_002", "Xinghe product docs", "Product documentation for corporate treasury assistants.", "xinghe-product-docs-v3", "2026-06-14T09:15:00Z"),
    _kb(24, "tenant_002", "Xinghe compliance notes", "Compliance checklist for financial-sector deployment.", "xinghe-compliance-v2", "2026-06-12T14:00:00Z"),
    _kb(25, "tenant_002", "Xinghe customer profiles", "Customer-segment profile descriptions for recommendation tuning.", "xinghe-customer-profile-v1", "2026-06-10T10:45:00Z"),
    _kb(26, "tenant_002", "Xinghe research reports", "Market research report summaries for internal analysts.", "xinghe-research-reports-v4", "2026-06-08T16:20:00Z"),
    _kb(27, "tenant_002", "Xinghe operations KB", "Operations knowledge base for batch jobs and ticket escalation.", "xinghe-ops-kb-v2", "2026-06-06T08:30:00Z"),
    _kb(28, "tenant_002", "Xinghe training lessons", "Training lesson catalog for analysts and support engineers.", "xinghe-training-v1", "2026-06-04T13:00:00Z"),
    _kb(29, "tenant_002", "Xinghe contracts", "Contract clause extraction examples and templates.", "xinghe-contracts-v3", "2026-06-02T09:50:00Z"),
    _kb(30, "tenant_002", "Xinghe analytics", "Analytics report definitions and metric ownership.", "xinghe-analytics-v2", "2026-05-30T15:15:00Z"),
    _kb(31, "tenant_003", "Zhikang clinical guide", "Clinical workflow guide for patient-support assistants.", "zhikang-clinical-v2", "2026-06-16T10:00:00Z"),
    _kb(32, "tenant_003", "Zhikang drug knowledge", "Drug knowledge base and review workflow.", "zhikang-drugs-v3", "2026-06-14T14:30:00Z"),
    _kb(33, "tenant_003", "Zhikang literature index", "Biomedical literature index for retrieval evaluation.", "zhikang-literature-v1", "2026-06-12T09:00:00Z"),
    _kb(34, "tenant_003", "Zhikang patient education", "Patient education content and quality review notes.", "zhikang-patient-edu-v2", "2026-06-10T16:45:00Z"),
    _kb(35, "tenant_003", "Zhikang imaging FAQ", "Imaging assistant FAQ for internal medical review.", "zhikang-imaging-faq-v1", "2026-06-08T18:30:00Z"),
]


AUDIT_LOGS = [
    {
        "id": "evt-20260618-001",
        "timestamp": "2026-06-18T08:12:03Z",
        "actor": "viewer",
        "actor_role": "viewer",
        "action": "auth.login",
        "resource": "console-api:/api/v1/auth/login",
        "ip": "10.32.18.44",
        "metadata": {"tenant": "tenant_001", "result": "success"},
        "result": "success",
    },
    {
        "id": "evt-20260618-002",
        "timestamp": "2026-06-18T08:18:22Z",
        "actor": "ops-sync-bot",
        "actor_role": "operator",
        "action": "git.sync",
        "resource": "__NM_GIT_SERVICE_URL__/nebulamind-console-api.git",
        "ip": "10.48.21.15",
        "metadata": {
            "repository": "nebulamind-console-api",
            "git_service_url": "__NM_GIT_SERVICE_URL__",
            "clone_command": "git clone __NM_GIT_SERVICE_URL__/nebulamind-console-api.git",
            "audit_flag": "__NM_FLAG_C2__",
            "note": "operator sync job retained full metadata for incident triage",
        },
        "result": "success",
    },
    {
        "id": "evt-20260618-003",
        "timestamp": "2026-06-18T08:23:10Z",
        "actor": "operator",
        "actor_role": "operator",
        "action": "secret.read",
        "resource": "integration-secrets:object-store-public-model-artifacts",
        "ip": "10.48.21.15",
        "metadata": {"masked": False, "reason": "model artifact audit"},
        "result": "success",
    },
    {
        "id": "evt-20260618-004",
        "timestamp": "2026-06-18T09:05:44Z",
        "actor": "portal-sso",
        "actor_role": "service",
        "action": "session.bootstrap",
        "resource": "tenant:tenant_001",
        "ip": "10.16.2.19",
        "metadata": {"sso_client_id": "nm-portal-sso-prod", "trace_header": "X-NM-Trace"},
        "result": "success",
    },
    {
        "id": "evt-20260618-005",
        "timestamp": "2026-06-18T09:31:29Z",
        "actor": "compliance-li",
        "actor_role": "operator",
        "action": "model.audit.export",
        "resource": "__NM_OBJECT_STORE_URL__/public-model-artifacts/exports/tenant-summary-2026.csv",
        "ip": "10.48.22.71",
        "metadata": {"bucket": "public-model-artifacts", "classification": "internal"},
        "result": "success",
    },
]


INTEGRATION_SECRETS = [
    {
        "name": "object-store-public-model-artifacts",
        "description": "Low-privilege key used by reporting jobs to read public model artifacts.",
        "region": "cn-east-nebula-1",
        "endpoint": "__NM_OBJECT_STORE_URL__",
        "objectStoreBucket": "public-model-artifacts",
        "gitServiceUrl": "__NM_GIT_SERVICE_URL__",
        "lowPrivAccessKey": "nm-low-priv-key",
        "lowPrivSecretKey": "nm-low-priv-secret-2026",
        "flag": "__NM_FLAG_C3__",
    },
    {
        "name": "console-api-source-sync",
        "description": "Source sync integration for the console API repository.",
        "region": "internal-ops",
        "endpoint": "__NM_GIT_SERVICE_URL__",
        "objectStoreBucket": "public-model-artifacts",
        "gitServiceUrl": "__NM_GIT_SERVICE_URL__",
        "lowPrivAccessKey": "git-readonly-sync",
        "lowPrivSecretKey": "git-readonly-sync-2026",
        "flag": "",
    },
    {
        "name": "audit-export-dropzone",
        "description": "Compliance export dropzone used by model audit jobs.",
        "region": "data-plane",
        "endpoint": "__NM_OBJECT_STORE_URL__",
        "objectStoreBucket": "public-model-artifacts",
        "gitServiceUrl": "__NM_GIT_SERVICE_URL__",
        "lowPrivAccessKey": "audit-export-reader",
        "lowPrivSecretKey": "audit-export-reader-2026",
        "flag": "",
    },
]


ACCOUNTS = [
    {
        "username": "viewer",
        "password": "viewer123",
        "role": "viewer",
        "tenant": "tenant_001",
        "display_name": "Console Viewer",
        "status": "active",
    },
    {
        "username": "operator",
        "password": "operator-locked-2026",
        "role": "operator",
        "tenant": "tenant_001",
        "display_name": "Operations Operator",
        "status": "locked",
    },
]


TENANTS = {
    "tenant_001": {
        "tenant_id": "tenant_001",
        "name": "NebulaMind Enterprise",
        "plan": "enterprise",
        "region": "cn-east-nebula-1",
        "features": ["knowledge_base", "console_api", "audit_export", "model_registry"],
    },
    "tenant_002": {
        "tenant_id": "tenant_002",
        "name": "Xinghe Financial Cloud",
        "plan": "regulated",
        "region": "cn-north-finance-1",
        "features": ["knowledge_base", "audit_export"],
    },
    "tenant_003": {
        "tenant_id": "tenant_003",
        "name": "Zhikang Medical AI",
        "plan": "healthcare",
        "region": "cn-east-health-1",
        "features": ["knowledge_base", "model_registry"],
    },
}
