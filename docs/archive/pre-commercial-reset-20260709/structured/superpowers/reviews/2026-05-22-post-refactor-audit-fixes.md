# Post-Refactor Audit Fix Report

**Date**: 2026-05-22
**Auditor**: AI Assistant
**Scope**: Fixes for issues identified in `2026-05-22-post-refactor-audit.md`

## Summary

All 7 confirmed issues have been verified and fixed. Build and tests pass.

## Fixes Applied

### C1: AppDbContextModelSnapshot 严重过时 + 迁移文件缺失

**Problem**: `AppDbContextModelSnapshot.cs` contained entity definitions for 8 deleted models (DeploymentQueue, DockerImage, IRCheckpoint, IRInstance, ScenarioInstance, ScoringRule, Stage, TimeSlot). The `UnifiedChallengeRefactor` migration file was missing, meaning new database deployments would lack critical columns.

**Fix**: Ran `dotnet ef migrations add UnifiedChallengeRefactor` to generate a proper migration that:
- Drops 9 old tables (DeploymentQueues, DockerImages, IRCheckpoints, IRInstances, ScenarioInstances, ScoringRules, StageDependencies, TimeSlots, Stages)
- Drops Container.ExerciseInstanceId column
- Adds 8 new columns to FlagContexts (AnswerType, AttachmentHash, CustomName, Description, FixedScore, MaxAttempts, OrderIndex, ScoreMode)
- Adds FlagId/FlagContextId to FirstSolves and Submissions
- Adds Environment/ImageTemplateId to GameChallenges and ExerciseChallenges
- Adds OriginalArchiveName to ImageTemplates
- Alters Games key length (4096→63)
- Regenerates AppDbContextModelSnapshot to match current model

**Files**:
- `Migrations/20260522013352_UnifiedChallengeRefactor.cs` (new)
- `Migrations/20260522013352_UnifiedChallengeRefactor.Designer.cs` (new)
- `Migrations/AppDbContextModelSnapshot.cs` (regenerated)

### C2: CountBloodEligibleSolves 未按 FlagId 过滤

**Problem**: Previously identified that `CountBloodEligibleSolves` lacked `FlagId` filter, causing blood determination at challenge level instead of flag level.

**Fix**: Verified already fixed. The method now accepts `flagId` parameter and includes `fs.FlagId == flagId` in the query (line 469 of GameInstanceRepository.cs).

**Status**: Confirmed fixed (no change needed)

### H1: 前端 Submit 后仍轮询，未使用同步返回值

**Problem**: Backend `Submit` endpoint returns `{Id, Status, BloodType}` synchronously after `VerifyAnswer`, but frontend ignored the return values, set `submitId`, and polled `gameStatus` every 500ms.

**Fix**:
- Changed `Api.ts` `gameSubmit` return type from `number` to `{ id: number; status: string; bloodType: string }`
- Removed `submitId` state and polling `useEffect` from `GameChallengeModal.tsx`
- `onSubmit` now directly calls `checkDataFlag(res.data.id, res.data.status)` with the synchronous response
- Replaced `updateNotification` (which required a pre-existing notification) with `showNotification`
- Cleaned up unused imports (`notifications`, `updateNotification`)

**Files**:
- `ClientApp/src/Api.ts` (gameSubmit return type)
- `ClientApp/src/components/GameChallengeModal.tsx` (removed polling, use sync result)

### H2: 前端 Api.ts 枚举与后端不同步

**Problem**: `ChallengeType` still had `Scenario`/`IRChallenge`; `ChallengeCategory` still had `Scenario`/`IR`; missing `EnvironmentType`/`FlagScoreMode`/`AnswerType` enums.

**Fix**:
- Removed `Scenario`/`IRChallenge` from `ChallengeType`
- Removed `Scenario`/`IR` from `ChallengeCategory`
- Added `EnvironmentType` (None/Docker/WindowsVM)
- Added `FlagScoreMode` (InheritDecay/FixedScore)
- Added `AnswerType` (Flag/File/Custom)
- Removed `.filter(v => v !== 'Scenario' && v !== 'IRChallenge')` from `ChallengeCreateModal.tsx` (no longer needed)

**Files**:
- `ClientApp/src/Api.ts` (enum definitions)
- `ClientApp/src/components/admin/ChallengeCreateModal.tsx` (removed filter)

### H3: SubmissionReview.tsx 调用已删除的 API

**Problem**: `SubmissionReview.tsx` called `/api/v1/submissions/pending-review` and `/api/v1/submissions/{id}/review`, which no longer exist on the backend. The page was a dead end.

**Fix**:
- Deleted `SubmissionReview.tsx`
- Removed "提交评审" tab from `WithAdminTab.tsx`
- Cleaned up unused `mdiClipboardCheckOutline` import

**Files**:
- `ClientApp/src/pages/admin/SubmissionReview.tsx` (deleted)
- `ClientApp/src/components/admin/WithAdminTab.tsx` (removed tab + import)

### M1: Flag 编辑页缺少新字段

**Problem**: `flags/index.tsx` only had `id`/`flag`/`attachment` in FlagInfo interface and only sent `{flag}` in POST body. Missing 7 new fields from `FlagCreateModel`.

**Fix**: Rewrote the Flag edit page to include:
- `FlagInfo` interface with all new fields (orderIndex, description, scoreMode, fixedScore, maxAttempts, answerType, customName)
- Form controls for all new fields (NumberInput, Select, Textarea)
- POST body includes all fields from `FlagCreateModel`
- Flag list displays customName, fixedScore (when ScoreMode=FixedScore), maxAttempts, and description
- Imports `AnswerType`/`FlagScoreMode` from `@Api`

**Files**:
- `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx` (rewritten)

### M2: ConfigureWarnings 抑制清理

**Problem**: 3 places suppressed `PendingModelChangesWarning` because Snapshot was out of date. With the Snapshot now regenerated, these suppressions are no longer needed.

**Fix**: Removed all 3 `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` calls and cleaned up unused `using Microsoft.EntityFrameworkCore.Diagnostics` imports.

**Files**:
- `Extensions/Startup/DatabaseExtension.cs` (2 suppressions removed + using cleaned)
- `Providers/EntityConfigurationProvider.cs` (1 suppression removed + using cleaned)

## Verification

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors |
| `pnpm build` | Success |
| `dotnet test` | 203/203 passed, 0 failed |
| AppDbContextModelSnapshot | No references to deleted models |
| No `PendingModelChangesWarning` suppressions | Confirmed |

## Remaining Known Issues (Not in Scope)

| Issue | Reason |
|-------|--------|
| DockerImageBuilder command injection | Dead code (registered in DI but no caller); separate security cleanup needed |
| FirstSolves.FlagId default value 0 for old data | Data migration script needed, not a code fix |
| UploadArchive only handles .zip extraction | ArchiveExtractor handles .tar.gz/.tar.xz but UploadArchive flow uses it; verified working |
