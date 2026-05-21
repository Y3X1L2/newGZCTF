# Scoring and Flag Verification Pipeline Analysis

Date: 2026-05-19
Scope: ScoringService, FlagChecker, CheckpointVerificationService, SubmissionController, and related model/repository files under src/GZCTF/.

---

## 1. How Many Separate Verification/Flag-Checking Systems Exist?

Seven distinct verification paths were identified:

### System A: FlagChecker (Background Service)
- File: Services/FlagChecker.cs (lines 1-209)
- Mechanism: Channel<Submission> consumer with 1-4 parallel workers. Reads unchecked submissions from DB on startup, processes them in a loop.
- Verification call: Delegates to GameInstanceRepository.VerifyAnswer() (line 99).
- Scope: Traditional GZCTF challenge types (DynamicContainer, StaticAttachment, DynamicAttachment, StaticContainer).
- Post-verification: Adds GameEvent, flushes scoreboard cache, checks for cheating, sends SignalR game notice.

### System B: GameInstanceRepository.VerifyAnswer()
- File: Repositories/GameInstanceRepository.cs (lines 261-389)
- Mechanism: Direct string comparison (instance.FlagContext?.Flag == submission.Answer) for dynamic challenges, or queries FlagContexts table for static challenges (lines 288-302).
- Additional logic: Blood bonus calculation (first/second/third blood), PostgreSQL advisory lock for race-condition prevention, FirstSolve record creation.
- Returns: VerifyResult(SubmissionType, AnswerResult) from Utils/Shared.cs (line 81).

### System C: SubmissionController.VerifySubmissionAsync()
- File: Controllers/SubmissionController.cs (lines 389-403)
- Mechanism: Dispatches to one of four verification modes based on ScoringRule.VerificationMode.
- Sub-paths:
  - C1: VerifyAutoExactAsync (lines 411-479) -- SHA256 hash comparison with three fallback paths.
  - C2: VerifyAutoRegexAsync (lines 484-519) -- Regex pattern matching from VerificationConfig.
  - C3: ManualReview -- Returns (FlagSubmitted, 0), queues for admin review.
  - C4: AutoScript -- Returns (FlagSubmitted, 0) with "Deferred to background service". This is a dead end.

### System D: CheckpointVerificationService (Background Service)
- File: Services/CheckpointVerificationService.cs (lines 1-351)
- Mechanism: Polls IRInstances where EnvironmentStatus == Ready every 30 seconds. Checks IRCheckpoint records with VerificationType == AutoCommand or AutoScript. Updates IRInstance.CheckpointResults JSON.
- Sub-paths:
  - D1: VerifyAutoCommandAsync (lines 191-258) -- SSH-based command execution and output matching (Contains/Exact/Regex).
  - D2: VerifyAutoScriptAsync (lines 260-284) -- Always returns false.

### System E: IRChallengeController.SubmitCheckpoint()
- File: Controllers/IRChallengeController.cs (lines 519-597)
- Mechanism: Player submits answer for ManualAnswer checkpoints. Compares against ExpectedAnswer in VerificationConfig JSON. Case-sensitive option.

### System F: ScenarioController.SubmitStageFlag()
- File: Controllers/ScenarioController.cs (lines 496-617)
- Mechanism: SHA256 hash comparison via Stage.VerifyFlag() (ScenarioEntities.cs lines 143-148). Manages stage unlock progression. Updates ScenarioInstance.StageStatuses JSON.
- Does NOT interact with ScoringRule, Submission table, or FlagChecker at all.

### System G: ExerciseInstanceRepository.VerifyAnswer()
- File: Repositories/ExerciseInstanceRepository.cs (lines 194-219)
- Mechanism: Separate from game challenges. For exercise mode only.

---

## 2. ApplyScoreDecay Logic: Duplicated in Two Places

### Location 1: Services/ScoringService.cs (lines 73-82)
Called from CalculateTotalScoreAsync (line 51) during leaderboard aggregation.

### Location 2: Controllers/SubmissionController.cs (lines 524-533)
Identical logic. Called from CreateSubmission (line 113) when a submission is first accepted.

### Analysis
- The logic is byte-for-byte identical.
- The SubmissionController version applies decay at submission creation time (computing the stored score).
- The ScoringService version applies decay again when computing the total score from stored submissions.
- Bug: ScoringService.CalculateTotalScoreAsync (line 51) uses s.Score as baseScore but s.Score was already decayed at creation time (SubmissionController line 113). So the second ApplyScoreDecay call uses a pre-decayed value as its base, compounding the reduction. Example: a submission on attempt 2 with Half decay and base 100 stores 50 at creation, but ScoringService then computes ApplyScoreDecay(50, 1, rule) = 50/2 = 25.

---

## 3. IR Challenges vs Scenario Challenges vs Traditional Challenges Scoring

### Traditional Challenges
- Use GameChallenge.OriginalScore with dynamic scoring formula (GameChallenge.cs lines 42-58): originalScore * (minScoreRate + (1.0 - minScoreRate) * exp((1 - acceptedCount) / difficulty)).
- Blood bonuses: first/second/third blood with configurable multipliers.
- Scoreboard computed in ScoreboardCacheHandler -> GameRepository.GenScoreboard().
- Verification through FlagChecker -> GameInstanceRepository.VerifyAnswer().

### Scenario Challenges (New)
- Use ScoringRule weighted composition. Total score = sum(weight * best_decayed_score / 100).
- Verification through SubmissionController.VerifyAutoExactAsync with fallback to Stage.FlagHash.
- Submission records created in the Submission table.
- Leaderboard computed by LeaderboardService (lines 26-87 in LeaderboardService.cs), calling ScoringService.CalculateTotalScoreAsync.
- Crucially: ScenarioController.SubmitStageFlag does NOT use ScoringRule at all. It updates StageStatuses JSON directly but never creates a Submission record. Two parallel paths exist:
  - Path 1: SubmissionController API creates Submissions -> ScoringService computes scores.
  - Path 2: ScenarioController API updates StageStatuses JSON directly.

### IR Challenges
- Use IRCheckpoint objects with individual scores stored in CheckpointResults JSON on the IRInstance.
- Auto-verification via CheckpointVerificationService (SSH commands).
- Manual verification via IRChallengeController.SubmitCheckpoint().
- No integration with ScoringRule or ScoringService at all.
- IR checkpoint scores are stored in JSON on the instance, not in the Submission table.
- The LeaderboardService queries IRInstances but then only uses ScoringService.CalculateTotalScoreAsync, which only looks at ScoringRules and Submissions. Since IR challenges don't create Submissions through the scoring rule path, IR challenge scores will always show as 0 on the leaderboard.

---

## 4. What Happens When AutoScript Verification Is Used?

### Path 1: ScoringRule with VerificationMode.AutoScript
In SubmissionController.VerifySubmissionAsync (line 400):
VerificationMode.AutoScript => (AnswerResult.FlagSubmitted, 0) // Deferred to background service

The submission is created with status FlagSubmitted and score 0. No background service processes these. The FlagChecker only processes submissions through GameInstanceRepository.VerifyAnswer(), which compares against FlagContext.Flag or FlagContexts, not against ScoringRules.

Result: AutoScript submissions via ScoringRule are permanently stuck in FlagSubmitted status with score 0. They require manual admin review via SubmissionController.SubmitReview() to be resolved.

### Path 2: IRCheckpoint with VerificationType.AutoScript
In CheckpointVerificationService.VerifyAutoScriptAsync (lines 260-284):
"AutoScript verification requires scripts to be deployed on the host machine. Return false by default - scripts should be triggered externally or via a separate runner."
return Task.FromResult(false);

Always returns false. The method reads ScriptPath from config, logs a message, and returns false. The comment explicitly states this is a placeholder.

Result: AutoScript IR checkpoints can never be auto-completed. Polled every 30s forever, always failing.

---

## 5. Circular Dependencies Between Verification Systems

### Overlap: Submission Table Shared by Multiple Systems
The Submission entity (Models/Data/Submission.cs) is used by:
1. FlagChecker -- reads unchecked submissions, calls GameInstanceRepository.VerifyAnswer().
2. SubmissionController -- creates submissions directly via _context.Submissions.AddAsync().
3. ScoringService.CalculateTotalScoreAsync -- aggregates scores from submissions.
4. GameInstanceRepository.VerifyAnswer() -- updates submission status to Accepted/WrongAnswer.

### Identified Conflict:
If a challenge has both ScoringRules AND traditional FlagContext-based flags:
- SubmissionController.CreateSubmission() verifies via VerifyAutoExactAsync() which checks FlagContexts as a third fallback (line 445-450).
- FlagChecker also calls GameInstanceRepository.VerifyAnswer() which does the same check.
- Submissions created as Accepted by SubmissionController would be skipped by FlagChecker if it filters by status. But ManualReview submissions (FlagSubmitted) could be double-processed.

### No Bridge Between IR Checkpoints and ScoringRules
- CheckpointVerificationService updates IRInstance.CheckpointResults JSON but never creates Submission records.
- The LeaderboardService queries ScoringRules and Submissions for leaderboard calculation.
- IR checkpoint completions contribute nothing to the leaderboard score.

### ScenarioController Bypasses Submission System
- ScenarioController.SubmitStageFlag() updates StageStatuses JSON directly without creating a Submission record.
- The scenario's ScoringRules and the SubmissionController's scoring path operate completely independently.
- A player could complete stages via ScenarioController and also submit flags via SubmissionController, resulting in duplicate or confusing scoring.

---

## 6. Relationship Between IRCheckpoint.VerificationType and ScoringRule.VerificationMode

### IRCheckpoint.VerificationType (Utils/Enums.cs lines 358-380):
| Value | Name | Usage |
|-------|------|-------|
| 0 | AutoScript | Background SSH script execution (stub, always fails) |
| 1 | AutoCommand | Background SSH command execution (working, output matching) |
| 2 | ManualAnswer | Player submits answer, compared via config JSON (working) |
| 3 | ManualReview | Requires admin review (not implemented in IRChallengeController) |

### ScoringRule.VerificationMode (Models/Data/ScoringRule.cs lines 18-24):
| Value | Name | Usage |
|-------|------|-------|
| 0 | AutoExact | SHA256 hash comparison (ExpectedAnswerHash / Stage.FlagHash / FlagContexts) |
| 1 | AutoRegex | Regex pattern match against VerificationConfig JSON |
| 2 | AutoScript | "Deferred to background service" (no service processes it) |
| 3 | ManualReview | Admin review via SubmitReview endpoint |

### Overlapping Semantics:
- Both have AutoScript and ManualReview with similar intended meaning.
- VerificationType.AutoCommand has no equivalent in ScoringRule.
- VerificationMode.AutoExact and AutoRegex have no equivalent in IRCheckpoint.
- VerificationType.ManualAnswer has no equivalent in ScoringRule.

### Key Difference:
- IRCheckpoint.VerificationType controls how a checkpoint objective (within an IR challenge) is verified.
- ScoringRule.VerificationMode controls how a submission (Flag/Writeup/IP/Credential/Custom) is verified for scoring.
- They operate on different data models and different storage (CheckpointResults JSON vs Submission table).

---

## 7. Can a Challenge Use Both ScoringRule AND IRCheckpoint?

Yes, technically. Nothing prevents creating ScoringRules for an IRChallenge (ChallengeType.IRChallenge). But:

- IR challenges use IRCheckpoint objects with scores in CheckpointResults JSON on IRInstance.
- ScoringRules are designed for Submission records in the Submission table.
- There is no code that bridges the two systems.
- If both existed on the same challenge:
  - Submissions via SubmissionController would be verified against ScoringRules and stored in Submission table.
  - Checkpoints completed via CheckpointVerificationService or IRChallengeController would update CheckpointResults JSON.
  - The leaderboard would only see Submission records (via ScoringService), ignoring checkpoint completions.

---

## 8. TODO, FIXME, NotImplementedException in Scope

### In the directly analyzed files: None found.

### In related files (project-wide):

| File | Line | Type | Content |
|------|------|------|---------|
| Controllers/ExerciseController.cs | 16 | TODO | exercise mode support |
| Controllers/GameController.cs | 317 | FIXME | After approval, new users can be added, but cannot exit? |
| Utils/CulturedLocalizer.cs | 27 | NotImplementedException | throw |
| Repositories/RepositoryBase.cs | 42 | NotImplementedException | throw |
| Services/Container/Provider/DockerProvider.cs | 60 | TODO | After Docker.DotNet.Enhanced 3.132.0 is adapted |
| Services/Container/ContainerServiceExtension.cs | 36 | FIXME | custom IPortMapper |
| Services/Mail/MailSender.cs | 85-87 | TODO | Three TODOs about email templates |

---

## 9. Summary of Key Findings

### Data Flow Gaps
1. AutoScript is a dead end in both paths. ScoringRule.AutoScript creates pending submissions; IRCheckpoint.AutoScript always returns false. Neither has an implementation.

2. IR checkpoint scoring is invisible to the leaderboard. Checkpoint completions update JSON on IRInstance but no Submission record is created, so ScoringService/LeaderboardService cannot see them.

3. ScenarioController has an independent scoring bypass. SubmitStageFlag updates stage statuses without creating Submissions.

### Logic Issues
4. Double decay bug in ScoringService.CalculateTotalScoreAsync. Decay is applied at submission creation time (SubmissionController.cs:113) and again at total score computation time (ScoringService.cs:51). The second call uses the already-decayed s.Score as its base, compounding the reduction.

5. Duplicate ApplyScoreDecay implementation. Two identical copies exist (ScoringService.cs:73-82 and SubmissionController.cs:524-533).

6. VerifyAutoExactAsync for Flag type has three fallback paths (rule.ExpectedAnswerHash -> Stage.FlagHash -> FlagContexts). If rule.ExpectedAnswerHash is set but wrong, the method returns WrongAnswer immediately and never checks stages or FlagContexts. This means a challenge with both ExpectedAnswerHash and FlagContexts would only check the first.

### Architecture Overlaps
7. Two parallel scoring universes. Original GZCTF uses GenScoreboard() with dynamic scoring + blood bonuses. New system uses ScoringService with weighted rules. They coexist but serve different challenge types. IR and Scenario challenges straddle both systems incompletely.

8. SignalR group naming: SubmissionController broadcasts to scenario_{challengeId} (line 546). CheckpointVerificationService broadcasts to ir_{instance.ChallengeId} (line 138). IRChallengeController broadcasts to ir_{instance.ChallengeId} (line 586). ScenarioController broadcasts to scenario_{instance.ScenarioId} (line 585). These are consistent within domains but a client needs to know which type to subscribe to.
