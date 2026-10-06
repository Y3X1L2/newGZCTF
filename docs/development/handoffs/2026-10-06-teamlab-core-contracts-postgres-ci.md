# TeamLab core contracts: PostgreSQL and candidate evidence

Status: local PostgreSQL evidence, a3 Quality CI, and combined candidate
verified; PR-head Quality CI pending, 2026-10-06. This supplements the core
contracts handoff.

## Source identity

- Final reviewed feature source: `a3bdb34a5e69a15e9aacdac597a2c02792e7c766`
  on `codex/teamlab-core-workflow`; Draft PR #13 subsequently advanced to
  `ce17bf1ce6c98847f05bc6c3faf8fcc73fb93b88` with frontend-only changes.
- The a3 and ce17 revisions have no diff in `src/GZCTF/Modules`, the Main
  project file, Agent, shared execution contracts, or GuestSupervisor.
- Isolated test-only follow-up `8c36a78e` adds
  `TeamLabRuntimeCapabilityProjectionPostgresTests`; it changes no production
  source or migration.

## Verified local facts

- Docker CLI context `desktop-linux` resolved to the local
  `npipe:////./pipe/dockerDesktopLinuxEngine` endpoint and Docker Desktop
  server 29.5.2. No remote or production Docker context was used.
- A `postgres:16-alpine` Testcontainer ran the new query/projection regression
  successfully, 1/1. It verified Npgsql translation of blank remote-account
  fields, a subsequent complete configuration, frozen release scene name,
  execution-plan Windows OS source, RDP `configured-unverified`, and that
  runtime GET created no remote sessions.
- Earlier local gates on the contracts branch: solution Release build,
  backend unit 1292/1292, and frontend build including 379/379 tests passed.
  The integrated a3 frontend candidate passed 394/394 tests.
- TestServer internal OpenAPI export passed and generated the client with the
  repository template. No Testcontainer result is presented as guest network,
  RDP, SSH, SFTP, or real Docker/VM acceptance.

## Candidate files

- Main `linux-x64` publish was produced from the a3 source tree at
  `D:/Work/YINYU-TeamLab-Core-Workflow-20261006/main-a3bdb34a-linux-x64/publish`.
  The tree of the local merge checkout exactly matched a3. Publish used
  `FrontendBuildMode=Artifact`, `DebugType=None`, and `DebugSymbols=false`.
- Frontend manifest inside that publish identifies a3 and 223 asset files;
  all 223 listed SHA256 values matched the published files. This directory
  and its a3 archive retain the old UI and are not the final combined
  candidate for PR #13.
- The separate combined candidate pairs the unchanged a3 Main publish with
  ce17 frontend files:
  `D:/Work/YINYU-TeamLab-Core-Workflow-20261006/main-a3bdb34a-frontend-ce17bf1c-linux-x64.tar.gz`.
  SHA256 is
  `d4aaa5b65cd1e7633e765fb736e2a24e9f4ee9b3301883c4bb57d5f1fbbf81a0`
  (168,327,913 bytes). All 147 non-frontend published files matched the a3
  publish by SHA256; all 223 frontend files matched the ce17 manifest.
  `GZCTF.dll` SHA256 is
  `87c26628bd00c93123854834d997e010c92d4f565ed887e29032c8aebf3dee1d`;
  the ce17 frontend manifest SHA256 is
  `a8f43b6b7bea8ddb70501cb1bb58f706de78c351cd7469a67e5ca279f4dc4659`.
  The archive contains 388 members, with no `node_modules`, `.env`, credential
  or database-dump path. Structured provenance is in the repository-external
  `candidate-evidence.json` beside the archive.
- No Agent or shared execution-contract source changed from baseline
  `f21f5ef2`; a bundled Agent binary is a publish byproduct, not evidence
  that an Agent upgrade is needed.

## Pending evidence

Quality run `37414453314` was manually dispatched for a3 because branch
push alone does not trigger the workflow. It completed successfully: both
jobs passed, including backend unit, Testcontainers integration, migration
model, PostgreSQL query-plan, and OpenAPI compatibility steps. PR #13 head
ce17 triggered run `37414844681`; its frontend job passed and backend
integration was still running when recorded. The PR-head run is the acceptance
reference. A newer PR head that includes test-only follow-up `8c36a78e`
will trigger its own run; record its final conclusion after completion.

Nothing has been uploaded or deployed to production. Real VM/Docker guest
access, network readback, and business connectivity still need separate
authorized infrastructure acceptance.
