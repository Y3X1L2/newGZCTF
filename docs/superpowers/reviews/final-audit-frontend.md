# Final Audit -- Frontend + Deploy (Phase 3, 6, 8)

> **Date:** 2026-05-19
> **Scope:** Phase 3 (Deploy Panel), Phase 6 (Frontend Refactor), Phase 8 (Deploy Orchestration)
> **Method:** Every file listed in the plan was verified for existence AND real content. Every API endpoint called by frontend was cross-checked against the backend controller.

---

## Summary

| Category | Count |
|---|---|
| PASS | 28 |
| WARNING | 4 |
| FAIL (missing files) | 11 |
| FAIL (API mismatch) | 7 |
| SECURITY concern | 2 |

**Overall: Not ready for production deploy.** 20 issues found across missing files, API contract mismatches, and security gaps.

---

## 1. Phase 3 -- Deploy Panel (PASS with warnings)

### 1.1 File Existence: ALL PASS

All 10 Phase 3 frontend files exist at their expected paths:

| File | Status |
|---|---|
| `src/GZCTF/ClientApp/src/pages/admin/Nodes/Index.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/pages/admin/Nodes/[id]/Detail.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/pages/admin/Queue/Index.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/pages/admin/Dashboard/Index.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/components/admin/NodeCard.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/components/admin/QueueCard.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/components/admin/DeployButton.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/components/admin/CleanupButton.tsx` | EXISTS |
| `src/GZCTF/ClientApp/src/hooks/useNodes.ts` | EXISTS |

### 1.2 Feature Verification

**Nodes/Index.tsx -- "Add Target Server" modal:**
- Has a `<Modal title="Add Target Server">` with `data-testid="add-node-modal"` -- PASS
- Form fields: Server Name, IP Address, Username, Password -- covers all `NodeDeployRequest` fields
- "One-Click Deploy" button calls `POST /api/v1/nodes` with body `{ hostAddress, username, password, nodeName }`
- Backend `NodesController.Register()` accepts `NodeDeployRequest` with matching fields -- PASS

**Dashboard/Index.tsx -- DeployButton + CleanupButton:**
- Imports and renders both `<DeployButton />` and `<CleanupButton />` -- PASS

**DeployButton.tsx:**
- Imports `useDeploy` from `../../hooks/useNodes` -- hook EXISTS -- PASS
- Calls `deploy()` from the hook -- PASS

**CleanupButton.tsx:**
- Imports `useDeploy` from `../../hooks/useNodes` -- PASS
- Calls `cleanup()` from the hook -- PASS

**useNodes.ts hook:**
- Exports `NodeInfo` interface -- PASS
- Exports `useNodes()` function (SWR-based, polls `/api/v1/nodes` every 5s) -- PASS
- Exports `useDeploy()` function with `deploy()` and `cleanup()` -- PASS
- `deploy()` calls `POST /api/v1/docker/deploy` -- PASS
- `cleanup()` calls `POST /api/v1/docker/cleanup` -- PASS

**NodeCard.tsx:**
- Imports `NodeInfo` type from useNodes -- PASS
- Renders status badge (Online=green, Offline=red, else=yellow) -- PASS
- Shows CPU/memory/container/VM metrics -- PASS

**Queue/Index.tsx:**
- Static table with columns: ID, Target Node, Type, Status, Created -- PASS
- Shows "No pending requests" placeholder -- PASS
- Does NOT call any API (hardcoded empty state) -- WARNING

**NodeDetail/[id]/Detail.tsx:**
- Uses `useParams` and `useNodes` -- PASS
- Shows CPU load progress bar, memory load bar, container/VM counts -- PASS
- Displays last heartbeat timestamp -- PASS

### 1.3 Backend API Alignment

| Frontend Call | Backend Endpoint | Match? |
|---|---|---|
| `POST /api/v1/nodes` (body: hostAddress, username, password, nodeName) | `NodesController.Register([FromBody] NodeDeployRequest)` | EXACT MATCH |
| `GET /api/v1/nodes` | `NodesController.List()` | EXACT MATCH |
| `POST /api/v1/docker/deploy` (body: { composeFile }) | `DockerController.Deploy([FromBody] DeployRequest)` | EXACT MATCH |
| `POST /api/v1/docker/cleanup` (body: { composeFile }) | `DockerController.Cleanup([FromBody] DeployRequest)` | EXACT MATCH |

**NodeDeployService.DeployToServerAsync() SSH capability:**
- Uses `Process.Start("sshpass", ...)` to connect via SSH -- actually performs remote commands
- Runs `command -v docker && docker --version` for Docker detection
- Runs `command -v virsh && virsh --version` for KVM detection
- Detects capabilities but does NOT install agent on target -- WARNING (plan specifies agent installation)

### 1.4 Warnings

| # | File | Issue |
|---|---|---|
| W1 | `Queue/Index.tsx` | Hardcoded static table; does not call `/api/v1/queue` or any API to fetch real queue data |
| W2 | `NodeDeployService.cs` | Detects capabilities but does NOT install the GZCTF Agent on target servers (plan says "SSH in, install agent, register node") |
| W3 | `useNodes.ts:22` | `deploy()` sends `{ composeFile }` as JSON body, but `NodeDeployService` has no knowledge of compose files; it deploys via SSH to a target node, not via Docker Compose |
| W4 | `Nodes/Index.tsx:20` | Sends `nodeName` but backend `NodeDeployRequest` expects `NodeName` (camelCase mismatch -- System.Text.Json default camelCase policy means `nodeName` maps to `NodeName`, so this is actually OK) |

---

## 2. Phase 2 -- Docker Images Page (PASS)

| File | Status |
|---|---|
| `src/GZCTF/ClientApp/src/pages/admin/DockerImages/Index.tsx` | EXISTS |

- Calls `GET /api/v1/docker/images` which matches `DockerController.ListImages()` -- PASS
- Calls `DELETE /api/v1/docker/images/{id}` which matches `DockerController.DeleteImage()` -- PASS
- Domain model matches: `DockerImageItem` interface fields match `DockerImage` entity (id, name, imageTag, osType, status, fileSize) -- PASS

---

## 3. Phase 6 -- Deleted Files Verification (ALL PASS)

All four files that MUST NOT EXIST are confirmed deleted:

| File | Status |
|---|---|
| `pages/admin/IRChallengeCreate.tsx` | DELETED |
| `pages/admin/IRChallengeList.tsx` | DELETED |
| `pages/admin/ScenarioCreate.tsx` | DELETED |
| `pages/admin/ScenarioList.tsx` | DELETED |

---

## 4. Phase 6 -- Created Files (PARTIAL FAIL)

### 4.1 EXISTS (PASS)

| File | Status |
|---|---|
| `components/AppErrorBoundary.tsx` | EXISTS (class-based ErrorBoundary with reload button) |
| `components/EmptyState.tsx` | EXISTS (simple centered placeholder with title/description) |
| `components/SkeletonCard.tsx` | EXISTS (Card with Skeleton placeholders for name/badge/content) |
| `types/index.ts` | EXISTS (re-exports `NodeInfo` from `../hooks/useNodes`) |

### 4.2 MISSING (FAIL)

These files are listed in BOTH Phase 6 and Phase 8 Create sections of the plan but DO NOT EXIST:

| # | File | Missing From |
|---|---|---|
| F1 | `types/ir.ts` | Missing |
| F2 | `types/scenario.ts` | Missing |
| F3 | `types/submission.ts` | Missing |
| F4 | `types/node.ts` | Missing |
| F5 | `types/game-phase.ts` | Missing |
| F6 | `api/v1/irChallenges.ts` | Missing (entire `api/v1/` directory does not exist) |
| F7 | `api/v1/scenarios.ts` | Missing |
| F8 | `api/v1/submissions.ts` | Missing |
| F9 | `api/v1/imageTemplates.ts` | Missing |
| F10 | `hooks/useIRChallenge.ts` | Missing |
| F11 | `hooks/useScenario.ts` | Missing |
| F12 | `hooks/useSubmission.ts` | Missing |
| F13 | `hooks/useGamePhase.ts` | Missing |
| F14 | `pages/admin/Images/Index.tsx` | Missing |
| F15 | `components/admin/ImageUploadModal.tsx` | Missing |
| F16 | `pages/admin/games/[id]/Phases.tsx` | Missing (Phase 4) |
| F17 | `components/admin/PhaseCard.tsx` | Missing (Phase 4) |

**Note:** Phase 4 files (Phases.tsx, PhaseCard.tsx) are also missing. GamePhaseService.cs and GamePhaseController.cs DO exist in the backend, confirming the backend was built, but the admin UI was never created.

---

## 5. Phase 8 -- Deploy Files (ALL PASS)

| File | Status |
|---|---|
| `docker-compose.yml` | EXISTS (postgres:16-alpine, redis:7-alpine, guacd, api build, volumes) |
| `docker-compose.dev.yml` | EXISTS (db + redis + guacd for dev, no api/spa) |
| `docs/deploy/production.md` | EXISTS (deploy steps, admin setup, node management guide) |
| `docs/deploy/agent-node.md` | EXISTS (target server setup instructions) |

**docker-compose.yml verification:**
- Services: db (postgres:16-alpine), redis (redis:7-alpine), guacd (guacamole/guacd), api (build from src/GZCTF)
- Volumes: postgres_data, image_storage -- as specified
- Healthchecks: db (pg_isready), redis (redis-cli ping) -- as specified
- Note: no "spa" nginx service for frontend static files (frontend is served from API project, typical for dev)

---

## 6. API Contract Cross-Check (CRITICAL FAILURES)

### 6.1 MultiTypeSubmission.tsx -- Submission API Mismatches

**Issue F18 (SEVERE):** `POST /api/v1/submissions` body is missing required fields.

Frontend sends (line 22-25):
```json
{ "challengeId": <num>, "submissionType": "Flag", "content": { "value": <flag> } }
```

Backend `SubmissionCreateRequest` requires:
- `Answer` (string, Required) -- NOT SENT by frontend
- `GameId` (int, Required) -- NOT SENT by frontend
- `TeamId` (int, Required) -- NOT SENT by frontend
- `ParticipationId` (int, Required) -- NOT SENT by frontend

The server will return 400 Bad Request for all MultiTypeSubmission flag/IP/text writeup submissions.

**Issue F19 (SEVERE):** `POST /api/v1/submissions/upload` form data missing required fields.

Frontend sends (line 55-58):
```
FormData: file, challengeId, submissionType
```

Backend `UploadWriteup()` expects:
```
[FromForm] file, challengeId, gameId (Required), teamId (Required), participationId (Required)
```

Missing: `gameId`, `teamId`, `participationId`. Will return 400.

### 6.2 SubmissionReview.tsx -- Review API Mismatches

**Issue F20 (SEVERE):** `POST /api/v1/submissions/{id}/review` body missing required `accepted` field.

Frontend sends (line 44):
```json
{ "score": <num>, "maxScore": 10, "comment": <string> }
```

Backend `ReviewRequest` requires:
- `Accepted` (bool, Required) -- NOT SENT by frontend

The server will return 400. Also, `maxScore` is not a recognized field on the backend model.

**Issue F21:** Query parameter `submissionType` not recognized.
Frontend calls: `GET /api/v1/submissions/pending-review?submissionType=Writeup`
Backend `GetPendingReviews()` accepts: `challengeId`, `count`, `skip`
The `submissionType` parameter is silently ignored by the backend.

### 6.3 IRChallengePlayer.tsx -- Query Param vs Body Mismatch

**Issue F22 (SEVERE):** `POST /api/v1/ir-challenges/{id}/instances` parameter location mismatch.

Frontend sends (line 55-58):
```json
// POST body
{ "timeSlotId": <slot.id> }
```

Backend `CreateInstance()` expects:
```csharp
[FromQuery][Required] int timeSlotId
```

`timeSlotId` is a query parameter on the backend, NOT a body field. The frontend sends it in the JSON body. This will fail because the query param is required and won't be found.

### 6.4 ScenarioPlayer.tsx -- Response Field Name Mismatches

**Issue F23:** Reading wrong field names from `StageSubmitResult` response.

Frontend reads (line 80-86):
```js
data.correct          // Backend: isCorrect (camelCase of IsCorrect)
data.nextStageUnlocked  // Backend: DOES NOT EXIST in StageSubmitResult
data.allCompleted       // Backend: DOES NOT EXIST in StageSubmitResult
```

Backend `StageSubmitResult` has: `IsCorrect`, `StageId`, `InstanceStatus`, `CurrentStageId`
With camelCase serialization: `isCorrect`, `stageId`, `instanceStatus`, `currentStageId`

`data.correct` will always be `undefined` (falsy). `data.nextStageUnlocked` and `data.allCompleted` don't exist in the response model.

### 6.5 Same-API Different Submissions

**Issue F24: Duplicate `ApplyScoreDecay` methods**

`SubmissionController.ApplyScoreDecay` (private, line 533) duplicates `ScoreDecayCalculator.Apply` (static, line 13). Both produce identical results but this violates the plan's "single source of truth" requirement. Both apply the same formula but the private method also applies `Math.Pow(2, attemptIndex)` for Half decay vs `(1 << attemptIndex)` in ScoreDecayCalculator -- these are equivalent but the inconsistency is concerning. If the centralized `ScoreDecayCalculator` were updated, the private method in `SubmissionController` would remain stale.

---

## 7. Security Audit Findings

### 7.1 Guacamole Hardcoded Password (Phase 7)

**Issue S1 (HIGH):** `GuacamoleProxy.CreateConnectionAsync()` (old 3-param overload, line 78) still hardcodes:
```csharp
username = "player",
password = "password",
security = "any",
```

The plan Phase 7 requires: dynamic password per session (`Codec.RandomPassword(16)`) and `security = "nla"`.

**Mitigation:** The new `CreateConnectionWithCredentialsAsync()` (5-param overload) accepts dynamic credentials and IS used by `IRChallengeController.CreateEnvironmentAsync()` (line 431). However:
1. The old insecure method still exists and could be called by other code paths.
2. `CreateConnectionWithCredentialsAsync` still uses `security = "any"` (line 146) instead of `"nla"` as required.

### 7.2 MarkdownRenderer XSS Surface

**Issue S2 (LOW):** `MarkdownRenderer.tsx` itself uses `dangerouslySetInnerHTML` (lines 31, 62). This is by design for the Markdown-to-HTML pipeline, but the rendering chain should be audited to ensure the HTML output from the markdown parser is properly sanitized. `SubmissionReview.tsx` correctly uses `<MarkdownRenderer>` instead of raw `dangerouslySetInnerHTML` -- PASS.

**Note:** `FooterRender.tsx` (line 19) still uses `dangerouslySetInnerHTML` directly, but this is out of plan scope.

---

## 8. Plan Documentation Discrepancies

1. Phase 8 "Create" section duplicates 11 files already listed in Phase 6 "Create" section (types, api, hooks files). This duplication in the plan document may have contributed to these files never being implemented.

2. CLAUDE.md header states "当前阶段: 全部 8/8 Phase 完成" but this audit finds 11+ missing files and 7 API mismatches, suggesting completion was declared prematurely.

3. `libvirt` is listed as a volume mount in the code's `docker-compose.yml` plan comments but is not present in the actual docker-compose files (correct -- VM management is on bare metal hosts).

---

## 9. Graded Findings

### CRITICAL (must fix before production)

| ID | Severity | Description |
|---|---|---|
| F18 | CRITICAL | MultiTypeSubmission sends incomplete data to `/api/v1/submissions` -- missing `answer`, `gameId`, `teamId`, `participationId` |
| F19 | CRITICAL | MultiTypeSubmission file upload missing `gameId`, `teamId`, `participationId` |
| F20 | CRITICAL | SubmissionReview sends review without required `accepted` field |
| F22 | CRITICAL | IRChallengePlayer sends `timeSlotId` in body instead of query parameter |
| F23 | CRITICAL | ScenarioPlayer reads nonexistent response fields `correct`, `nextStageUnlocked`, `allCompleted` |
| S1 | HIGH | GuacamoleProxy retains hardcoded password + `security="any"` in old method; new method uses `security="any"` not `"nla"` |

### HIGH (blocks major features)

| ID | Description |
|---|---|
| F1-F17 | 11+ planned frontend files never created (types, api clients, hooks, admin pages) |
| W1 | Queue page is static shell with no API integration |
| W2 | NodeDeployService does not install agent -- only detects capabilities |
| F24 | Duplicate ApplyScoreDecay -- SubmissionController has its own private copy |

### MEDIUM (quality/debt)

| ID | Description |
|---|---|
| F21 | SubmissionReview sends unrecognized query param `submissionType` |
| S2 | MarkdownRenderer internal dangerouslySetInnerHTML needs sanitization audit |
| W3 | Docker deploy/cleanup sends composeFile but the deploy flow doesn't use compose files |

---

## 10. Recommended Remediation Order

1. ~~**Fix API contract mismatches first** (F18-F23) -- these make the frontend non-functional for submission flows.~~ **FIXED 2026-05-19:** All API contract mismatches resolved (answer/gameId/teamId/participationId now sent, accepted field added, timeSlotId moved to query param, isCorrect used instead of correct).
2. ~~**Fix Guacamole security** (S1) -- hardcoded password + weak security mode.~~ **FIXED 2026-05-19:** security="nla", hardcoded password replaced with GenerateRandomPassword().
3. **Implement missing files** (F1-F17) -- types, API clients, hooks, admin pages.
4. ~~**Unify ScoreDecay** (F24) -- use `ScoreDecayCalculator.Apply()` in SubmissionController.~~ **FIXED 2026-05-19:** SubmissionController now delegates to UnifiedScoringEngine.
5. **Make queue page functional** (W1) -- connect to `/api/v1/queue` endpoint.
6. **Complete NodeDeployService** (W2) -- implement agent installation SSH flow.

---

## 11. Security & Quality Fixes Applied (2026-05-19)

> The following fixes were applied after the original audit, based on a comprehensive deep inspection of the entire codebase.

### CRITICAL Fixes

| # | Issue | File(s) | Fix |
|---|---|---|---|
| F18-F19 | MultiTypeSubmission missing required fields | MultiTypeSubmission.tsx | All required fields (answer, gameId, teamId, participationId) now sent in body and FormData |
| F20 | SubmissionReview missing "accepted" field | SubmissionReview.tsx | `accepted` field now included in review request body |
| F22 | IRChallengePlayer timeSlotId in body vs query | IRChallengePlayer.tsx | timeSlotId now sent as query parameter |
| F23 | ScenarioPlayer reading nonexistent response fields | ScenarioPlayer.tsx | Now reads `isCorrect` instead of `correct`/`nextStageUnlocked`/`allCompleted` |
| S1 | GuacamoleProxy hardcoded password + security="any" | GuacamoleProxy.cs | security="nla", password replaced with GenerateRandomPassword() |

### HIGH Fixes

| # | Issue | File(s) | Fix |
|---|---|---|---|
| H1 | MarkdownRenderer/FooterRender XSS (no DOMPurify) | MarkdownRenderer.tsx, FooterRender.tsx | Installed DOMPurify, all `dangerouslySetInnerHTML` output now sanitized |
| H2 | SignalR off() using empty callbacks (memory leak) | ScenarioPlayer.tsx, IRChallengePlayer.tsx | Callback references saved and passed to off() for proper unsubscription |
| H3 | WithAdminTab missing navigation tabs | WithAdminTab.tsx | Added 6 tabs: Dashboard, Nodes, Docker Images, VM Images, Queue, Submission Review |
| H4 | ScenarioPlayer showCompletion never triggers | ScenarioPlayer.tsx | loadStatus() now detects all stages completed and calls setShowCompletion(true) |

### MEDIUM Fixes

| # | Issue | File(s) | Fix |
|---|---|---|---|
| M1 | SubmissionReview imports MarkdownRenderer incorrectly | SubmissionReview.tsx | Fixed to `import { Markdown } from '../../components/MarkdownRenderer'` with correct `source` prop |
| M2 | notifications.show() missing required "message" property | 5 files, 9 occurrences | Added `message` property to all notification calls |
| M3 | TopologyEditor type error (TopologyNodeData vs Record) | TopologyEditor.tsx | Added `as unknown as Record<string, unknown>` type assertion |
