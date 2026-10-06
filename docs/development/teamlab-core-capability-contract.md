# TeamLab first-round runtime capability contract

Status: implementation contract for the first-round TeamLab workflow, 2026-10-06.
This document specifies additive fields on the existing authenticated runtime detail
response. It does not define a second runtime queue or a new probing operation.

## Runtime detail

`GET /api/admin/teamlab/runtimes/{runtimeId}` retains existing fields and adds:

| Field | Type | Meaning |
| --- | --- | --- |
| `topologyId` | UUID or null | Public scene ID through the release association, for navigation. |
| `topologyName` | string or null | Name frozen in the executed release definition, never the editable scene's current name. Null when the immutable source cannot be read. |
| `releaseVersion` | integer or null | Existing release version. |
| `generation` / `planRevision` | integer | Existing current runtime identity; all asset facts below are scoped to these values. |

The global runtime search already returns `topologyId`, `scenarioName`,
`releaseVersion`, `createdById`, `createdAt`, and `updatedAt`. Its `scenarioName`
is the current editable scene name; the UI must not label it as the frozen release
name. There is no reliable expiration or source classification in this response.

Each `assets[]` item retains `kind`, `networkKeys`, and `primaryIp` for old clients
and adds:

| Field | Type | Meaning |
| --- | --- | --- |
| `operatingSystem` | `windows` / `linux` / `unknown` | OS metadata, never inferred from a missing enum value or a machine name. Docker remains identified by `kind`. |
| `operatingSystemSource` | `execution-plan` / `template-current` / `unknown` | Explicit plan value takes priority; otherwise matching current template metadata is identified as current metadata, not a frozen or guest-observed fact. |
| `sourceTemplateId` | integer or null | Template ID already bound to this generation's runtime asset. Settings navigation only, no credential values. |
| `interfaces` | array | Per-interface allocation from this generation's runtime asset and executed asset definition. |
| `capabilities` | array | Applicable tools with `kind`, `status`, `reason`, and `settingsTemplateId`. |

An `interfaces[]` entry has `key`, `networkKey`, `primary`, `assigned`, and
`observed`. `assigned` is null if the current runtime asset has no valid frozen
allocation; otherwise it contains `ipAddress`, `prefixLength`, `dnsServers`,
`gatewayIp` (nullable), and `staticRoutes` (`destinationCidr`, `nextHop`,
`metric`). `observed` is null until a trustworthy, generation-associated guest
readback is persisted. A later observed object must carry its own values and
`observedAt`. The current API does not probe machines on read. The allocated
address and network gateway never assert guest reachability. No interface uses
`primaryIp` as a substitute for an absent per-interface address.

`capabilities[].kind` is one of `console`, `rdp`, `ssh`, `sftp`, `terminal`,
`files`. `status` is one of `unsupported`, `unconfigured`,
`configured-unverified`, `currently-unavailable`. `reason` is a stable explanatory
string. `settingsTemplateId` is populated only when the existing image-template
remote-access settings are the relevant configuration scope. A complete SSH/RDP
template configuration gives `configured-unverified`, never a claim of service
reachability. Current asset/runtime state may give `currently-unavailable`.
Windows VM supports console and optional RDP; SSH/SFTP/files are unsupported.
Linux VM supports console and optional SSH/SFTP; Docker supports terminal and
files. The existing authorization checks remain authoritative for invoking tools.

## Boundaries

- Runtime data comes from the current generation's asset records and the
  executed plan, including current plan revision after runtime changes. Neither
  scene drafts nor a newer release are a source for a running asset.
- Old execution plans remain readable. The serializer omits the default Linux
  enum in older plan JSON, so an absent OS field is not enough to call an asset
  Linux. The current template OS may be shown only if its ID and image digest
  match the executed asset, with `operatingSystemSource=template-current`.
  Otherwise OS is unknown. No OS value is a guest observation.
- Template configuration is read without returning usernames or secrets.
  Changing it can affect new connections to an existing runtime.
- TeamLab reads OS, image digest, and remote configuration presence through a
  Content-owned batch query contract. It does not directly read Content tables
  for the new runtime projection.
- Windows file requests return the existing `files.unsupported` contract error
  before any SFTP dispatch. No Windows SFTP implementation is implied.
- This GET creates no remote sessions, VPN grants, capture jobs, or deep probes.
  Live infrastructure acceptance remains separate from local tests.
