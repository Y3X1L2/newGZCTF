# TeamLab first-round workflow implementation

Status: local development in progress, 2026-10-06. This is an engineering plan
based on the approved local page plan and the 2026-10-05 UX audit. It records
no production acceptance or deployment.

## Goal and ownership

The main path is image template, scene design, immutable release, runtime,
per-asset access, and teardown. Scenes, releases, and runtimes stay distinct.

| Task | Owner boundary | Acceptance |
| --- | --- | --- |
| Capability and API contract | TeamLab backend runtime projection, file guard, frontend feature API adapter and generated contract | Current generation/revision asset OS, template reference, assigned interfaces, honest missing observation, applicable capabilities; old response compatibility; Windows files rejected. |
| Scene and authoring workflow | Scene library, design, release and launch, global runtime list | A creator can choose existing images, edit and publish a scene, select a release, launch, and find that runtime without internal resource IDs. |
| Runtime workspace | Runtime detail routing, machine/network, access methods, operation records and legacy redirects | Direct runtime URL works with or without a scene parent; multi-interface addresses and tool states are truthful; normal reads create no access/session/probe. |

The root integration review owns cross-branch merging and checks that public
routes, authorization, lifecycle decisions, and tests still agree. Existing
backend APIs and advanced tools remain available to authorized users even if
removed from the primary page flow. No attack, vulnerability, or target-content
workflow is added.

## Contract and behavior gates

- Detail is based on the current runtime generation's asset state and accepted
  execution snapshot. Editable scene state cannot overwrite runtime facts.
- The per-interface assigned address, prefix, DNS, gateway and routes are
  configuration facts. A separate nullable observation requires persisted,
  generation-associated readback. `Ready` never implies tested connectivity.
- Image OS comes from an explicit execution snapshot value or from a current
  template whose image digest still matches, marked `template-current`.
  Older snapshots without either source report unknown, not Linux. Missing
  links produce a visible empty state with only verified navigation IDs.
- Windows offers console and optional RDP. Linux offers console, optional SSH
  and SFTP. Docker offers terminal and files. Template configuration alone is
  unverified; credential values are never returned. Invocation still uses its
  existing permission checks.
- Run detail remains addressable by runtime ID. Search/list scope and managed
  runtime ownership restrictions remain intact. New GET paths have no side
  effects and do not start a session, VPN grant, capture, or machine probe.
- No second queue, inferred log state, credential snapshot, or migration is
  planned. If a reliable observation requires persistence later, it needs a
  separate reviewed design and migration.

## Validation and handoff

Local verification covers parser compatibility, two network interfaces,
missing legacy data, generation reset, changed plan revision, file rejection,
relevant backend tests, frontend checks, and build. Docker/VM connectivity,
actual guest readback, RDP/SSH/SFTP, four-machine reset, gateway access, and
release deployment require separate authorized infrastructure acceptance.
Until then this task remains unmerged and undeployed.

## Recovery point

- Baseline: `f21f5ef2` on `origin/main` when the three isolated tasks began.
- Integration: `codex/teamlab-core-workflow` in the existing
  `teamlab-windows-network-review` worktree.
- Contracts: `codex/teamlab-core-contracts` in `teamlab-managed-contracts`.
- Authoring UI: `codex/teamlab-core-authoring` in `teamlab-managed-ui`.
- Runtime UI: `codex/teamlab-core-runtime` in `teamlab-managed-agent`.

Each task commits and reports its SHA to integration. The integration owner
reviews contracts, then authoring, then runtime. Existing workspaces and local
planning material remain in place; there is no automatic deployment or cleanup.
