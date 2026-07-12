# TeamLab Foundation Acceptance Runbook

## Scope

This runbook validates the Phase 3 boundary: TeamLab is an independent topology, release, runtime, access, and traffic control plane; Penetration is a gameplay adapter that owns objectives, submissions, scoring, and team bindings.

## Automated Evidence

Validated on 2026-07-12 from branch `codex/phase-3-teamlab-foundation`:

| Gate | Result |
|---|---|
| Production and test compilation | 0 warnings, 0 errors |
| EF pending model changes | None |
| Unit tests | 476 passed |
| PostgreSQL contract migration | 2 passed |
| OpenAPI TestServer snapshot | Passed |
| OpenAPI compatibility comparator | 26 breaking and 11 additive self-tests passed; contract compatible |
| Frontend locale, strict TypeScript, production build | Passed |
| Git whitespace check | Passed |

The migration test verifies both successful schema contraction and atomic rejection of an active legacy environment without a runtime binding. It runs against PostgreSQL 16.

The final quality review findings are closed: runtime generation reuse and stable ownership, active-release reset, access grant reissue, object-level runtime authorization, per-shard capacity accounting, connection-aware route isolation, incremental flow cursors, and idempotent capture completion are implemented. The complete integration suite reached 220 passing tests before two Phase 1 resource-grant fixture failures; those fixtures were corrected and both affected tests then passed.

## Pre-Deployment Gate

1. Enter maintenance mode for TeamLab topology writes, release publication, runtime create/reset/destroy, and capture creation.
2. Wait until all active TeamLab deployment tickets reach a terminal state.
3. Back up PostgreSQL and record active runtime, generation, shard, network lease, asset, access grant, UDP mapping, and node resource facts.
4. Apply migrations. Do not bypass a migration precondition failure; repair the reported binding or runtime fact and retry the complete transaction.
5. Confirm all registered Agents satisfy the deployed TeamLab protocol and required Docker/KVM/network capabilities.

## Independent Docker Flow

1. Issue an API token with TeamLab topology, runtime, traffic, and capture scopes and no Game or Team resource binding.
2. Create a two-network RFC1918 topology with an entry Docker asset, a routed internal Docker asset, health checks, and one allowed connection.
3. Validate, publish, plan, and create the runtime with a stable external reference.
4. Poll the durable operation and runtime events until ready.
5. Create and consume a one-time WireGuard access grant.
6. Verify direct access is limited to the entry network, routed access reaches the internal HTTP service, and an unconnected network is unreachable.
7. Query flow metadata, start a bounded capture, generate traffic, stop the capture, and download the PCAP.
8. Destroy the runtime and verify cleanup on every participating node.

## Independent Linux VM Flow

1. Publish a topology using a Ready Linux cloud-image template and at least two RFC1918 networks.
2. Create the runtime and verify cloud-init hostname, MAC-bound static addresses, DNS, routes, qemu guest-agent response, and service health.
3. Verify SSH and the intended HTTP service through the routed path.
4. Reset the runtime. Confirm PublicId and external reference remain stable, generation increments, old grants are revoked, and old generation facts remain queryable.
5. Destroy the runtime and verify overlay, seed ISO, bridge, namespace, route, capture, and staging files are absent.

## Penetration Adapter Flow

1. Create a Penetration game and bind an existing TeamLab topology and active release.
2. Configure objectives against topology asset keys, including one dynamic Flag and one prerequisite.
3. Start environments for two teams and verify each binding points to a distinct TeamLab runtime.
4. Submit the dynamic Flag, verify score and submission audit, then reset one team.
5. Confirm the other team's runtime, score, grant, and traffic facts are unchanged.
6. Stop both environments and verify runtime cleanup and queue/system event visibility.

## Residual Resource Check

For every destroyed runtime PublicId, inspect all participating nodes for containers, libvirt domains, qcow2 overlays, seed ISOs, bridges, router namespaces, WireGuard interfaces, routes, capture processes, PCAPs, and staging files. Any residue blocks Phase 3 production acceptance.

## Rollback Boundary

The contract migration removes legacy Penetration topology and runtime tables. Application rollback across this migration requires restoring the pre-migration database backup and the matching application release together. Rolling back application binaries alone is unsupported.
