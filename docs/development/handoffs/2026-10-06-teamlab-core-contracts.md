# TeamLab core runtime contract handoff

## Task and baseline

- Goal: give the first-round TeamLab runtime workspace truthful OS, interface,
  tool capability, and scene context facts while preserving existing access
  controls and execution plans.
- Branch: `codex/teamlab-core-contracts` in the isolated
  `teamlab-managed-contracts` worktree; baseline `origin/main f21f5ef2`.
- Main implementation through `ffbd77b4`; earlier contract commits are
  `44bd462f`, `6e33ee32`, `7ad2e8ba`, and `4d4eec49`.
- Status: implemented and locally verified; pending integration review and
  infrastructure acceptance. No deployment, production access, or merge to
  `main` occurred in this task.

## Implemented facts and boundaries

- Existing authenticated runtime detail adds frozen release scene name and
  scene ID. A missing release leaves those fields null while current runtime
  state remains readable.
- Per-asset OS includes its source. Explicit executed-plan OS wins; a matching
  current image template is marked `template-current`; otherwise OS is unknown.
  The default Linux enum omitted by old JSON is not treated as evidence.
- Per-interface assigned IP/prefix/DNS/gateway/routes come from the current
  generation's persisted runtime asset and current execution-plan snapshot.
  `observed` remains null because there is no reliable persisted guest readback
  tied to generation/revision. `primaryIp` is retained only for compatibility.
- Content owns the new batched read query for template identity and remote
  configuration presence. It returns no username or protected credential.
- Windows VM file requests return `files.unsupported` before SFTP dispatch.
  Unknown OS, including a template whose digest no longer matches the
  executed image, receives the same rejection.
  Runtime tool capability states do not replace the existing permission gates.
- Remote availability now filters old generation assets and accepts the same
  normalized running state used by runtime detail for legacy asset statuses.
- Internal OpenAPI was exported by the existing TestServer test, then
  `swagger-typescript-api` regenerated `src/generated/Api.ts` with the repo
  template. The feature adapter parses old responses as unknown/empty facts.

## Validation

| Gate | Result |
| --- | --- |
| `git diff --check` | Passed. |
| `dotnet build src/GZCTF.slnx -c Release` | Passed, 0 errors; existing NuGet/analyzer warnings remain. |
| `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build` | 1292/1292 passed before the final narrow status/configuration fix. |
| Targeted runtime, file, and Content query tests after final fix | 28/28 passed. |
| TestServer internal OpenAPI export | 1/1 passed; generated client diff reviewed. |
| `pnpm build` | Passed: locales, lint, TypeScript, architecture, 114 files/379 tests, Vite and bundle budget. |

Docker Desktop daemon was unavailable locally, so the full database
integration/Testcontainers suite was not run. No live VM, Docker, guest network,
RDP, SSH, SFTP, VPN, or gateway acceptance was attempted. The runtime UI and
authoring worktrees have their own verification and need integration review.

## Integration and remaining work

Merge the contracts branch into the integration worktree before the authoring
and runtime UI branches, then rerun cross-branch checks. Preserve the current
queue, authorization, and release/version boundaries. Main and frontend must
be released together for the new DTO; no Agent binary or database migration is
required by these changes. After review and explicit deployment authorization,
perform separate real infrastructure acceptance, especially a two-interface
Windows/Linux scene and a Docker asset. Do not infer reachability from an
assigned interface or configured template.
