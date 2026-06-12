# YINYU CTF平台 API Endpoint Analysis
Generated: 2026-05-19 | Scope: All 17 ASP.NET Core controllers

## 1. Complete API Endpoint Table

### 1.1 AccountController
Route: api/[controller]/[action] => api/Account/{action}

| Method | Route | Action | Auth | Request Model | Response Model |
| --- | --- | --- | --- | --- | --- |
| POST | /api/Account/Register | Register | RateLimit(Register) | RegisterModel | RequestResponse<RegisterStatus> |
| POST | /api/Account/Recovery | Recovery | RateLimit(Register) | RecoveryModel | RequestResponse |
| POST | /api/Account/PasswordReset | PasswordReset | RateLimit(Register) | PasswordResetModel | 200 OK |
| POST | /api/Account/Verify | Verify | -- | AccountVerifyModel | 200 OK |
| POST | /api/Account/LogIn | LogIn | -- | LoginModel | 200 OK |
| POST | /api/Account/LogOut | LogOut | [RequireUser] | -- | 200 OK |
| PUT | /api/Account/Update | Update | [RequireUser] | ProfileUpdateModel | 200 OK |
| PUT | /api/Account/ChangePassword | ChangePassword | [RequireUser] | PasswordChangeModel | 200 OK |
| PUT | /api/Account/ChangeEmail | ChangeEmail | [RequireUser] | MailChangeModel | RequestResponse<bool> |
| POST | /api/Account/MailChangeConfirm | MailChangeConfirm | [RequireUser] | AccountVerifyModel | 200 OK |
| GET | /api/Account/Profile | Profile | [RequireUser] | -- | ProfileUserInfoModel |
| PUT | /api/Account/Avatar | Avatar | [RequireUser] | IFormFile | string (URL) |

### 1.2 AdminController
Route: api/[controller] => api/Admin | Class: [RequireAdmin]

| Method | Route | Action | Request Model | Response Model |
| --- | --- | --- | --- | --- |
| GET | /api/Admin/Config | GetConfigs | -- | ConfigEditModel |
| PUT | /api/Admin/Config | UpdateConfigs | ConfigEditModel | 200 OK |
| POST | /api/Admin/Config/Logo | UpdateLogo | IFormFile | 200 OK |
| DELETE | /api/Admin/Config/Logo | ResetLogo | -- | 200 OK |
| GET | /api/Admin/Users | Users | count,skip | ArrayResponse<UserInfoModel> |
| POST | /api/Admin/Users | AddUsers | UserCreateModel[] | 200 OK |
| POST | /api/Admin/Users/Search | SearchUsers | hint | ArrayResponse<UserInfoModel> |
| GET | /api/Admin/Users/{userid:guid} | UserInfo | -- | ProfileUserInfoModel |
| PUT | /api/Admin/Users/{userid} | UpdateUserInfo | AdminUserInfoModel | 200 OK |
| DELETE | /api/Admin/Users/{userid:guid}/Password | ResetPassword | -- | string (new pwd) |
| DELETE | /api/Admin/Users/{userid:guid} | DeleteUser | -- | 200 OK |
| GET | /api/Admin/Teams | Teams | count,skip | ArrayResponse<TeamInfoModel> |
| POST | /api/Admin/Teams/Search | SearchTeams | hint | ArrayResponse<TeamInfoModel> |
| PUT | /api/Admin/Teams/{id:int} | UpdateTeam | AdminTeamModel | 200 OK |
| DELETE | /api/Admin/Teams/{id:int} | DeleteTeam | -- | 200 OK |
| GET | /api/Admin/Logs | Logs | level,count,skip | LogMessageModel[] (raw) |
| PUT | /api/Admin/Participation/{id:int} | Participation | ParticipationEditModel | 200 OK |
| GET | /api/Admin/Writeups/{id:int} | Writeups | -- | WriteupInfoModel |
| GET | /api/Admin/Writeups/{id:int}/All | DownloadAllWriteups | -- | File (tar) |
| GET | /api/Admin/Instances | Instances | -- | ArrayResponse<ContainerInstanceModel> |
| DELETE | /api/Admin/Instances/{id:guid} | DestroyInstance | -- | 200 OK |
| GET | /api/Admin/Files | Files | count,skip | ArrayResponse<LocalFile> |

### 1.3 ApiTokenController
Route: api/tokens | Class: [RequireAdmin]

| Method | Route | Action | Request Model | Response Model |
| --- | --- | --- | --- | --- |
| POST | /api/tokens | GenerateToken | ApiTokenCreateModel | ApiTokenResponse |
| GET | /api/tokens | ListTokens | -- | List<ApiToken> |
| POST | /api/tokens/{id:guid}/restore | RestoreToken | -- | 200 OK |
| DELETE | /api/tokens/{id:guid} | RevokeToken | ?delete | 200 OK |

### 1.4 AssetsController
Route: [controller] for GET, api/[controller] for POST/DELETE

| Method | Route | Action | Auth | Request Model | Response Model |
| --- | --- | --- | --- | --- | --- |
| GET | /Assets/{hash:length(64)}/{filename} | GetFile | None | -- | File stream |
| POST | /api/Assets | Upload | [RequireAdmin] | List<IFormFile> | List<LocalFile> |
| DELETE | /api/Assets/{hash:length(64)} | Delete | [RequireAdmin] | -- | 200 OK |

### 1.5 EditController
Route: api/[controller] => api/Edit | Class: [RequireAdmin]

| Method | Route | Action | Request Model | Response Model |
| --- | --- | --- | --- | --- |
| POST | /api/Edit/Posts | AddPost | PostEditModel | string (ID) |
| PUT | /api/Edit/Posts/{id} | UpdatePost | PostEditModel | PostDetailModel |
| DELETE | /api/Edit/Posts/{id} | DeletePost | -- | 200 OK |
| POST | /api/Edit/Games | AddGame | GameInfoModel | GameInfoModel |
| GET | /api/Edit/Games | GetGames | count,skip | ArrayResponse<GameInfoModel> |
| GET | /api/Edit/Games/{id:int} | GetGame | -- | GameInfoModel |
| GET | /api/Edit/Games/{id:int}/HashSalt | GetHashSalt | -- | string |
| PUT | /api/Edit/Games/{id:int} | UpdateGame | GameInfoModel | GameInfoModel |
| DELETE | /api/Edit/Games/{id:int} | DeleteGame | -- | 200 OK |
| DELETE | /api/Edit/Games/{id:int}/WriteUps | DeleteGameWriteUps | -- | 200 OK |
| PUT | /api/Edit/Games/{id:int}/Poster | UpdateGamePoster | IFormFile | string (URL) |
| POST | /api/Edit/Games/{id:int}/Notices | AddGameNotice | GameNoticeModel | GameNotice |
| GET | /api/Edit/Games/{id:int}/Notices | GetGameNotices | -- | GameNotice[] |
| PUT | /api/Edit/Games/{id:int}/Notices/{noticeId:int} | UpdateGameNotice | GameNoticeModel | GameNotice |
| DELETE | /api/Edit/Games/{id:int}/Notices/{noticeId:int} | DeleteGameNotice | -- | 200 OK |
| POST | /api/Edit/Games/{id:int}/Divisions | CreateDivision | DivisionCreateModel | Division |
| GET | /api/Edit/Games/{id:int}/Divisions | GetDivisions | -- | Division[] |
| PUT | /api/Edit/Games/{id:int}/Divisions/{divisionId:int} | UpdateDivision | DivisionEditModel | Division |
| DELETE | /api/Edit/Games/{id:int}/Divisions/{divisionId:int} | DeleteDivision | -- | 200 OK |
| POST | /api/Edit/Games/{id:int}/Challenges | AddGameChallenge | ChallengeInfoModel | ChallengeEditDetailModel |
| GET | /api/Edit/Games/{id:int}/Challenges | GetGameChallenges | -- | ChallengeInfoModel[] |
| GET | /api/Edit/Games/{id:int}/Challenges/{cId:int} | GetGameChallenge | -- | ChallengeEditDetailModel |
| PUT | /api/Edit/Games/{id:int}/Challenges/{cId:int} | UpdateGameChallenge | ChallengeUpdateModel | ChallengeEditDetailModel |
| POST | /api/Edit/Games/{id:int}/Challenges/{cId:int}/Container | CreateTestContainer | -- | ContainerInfoModel |
| DELETE | /api/Edit/Games/{id:int}/Challenges/{cId:int}/Container | DestroyTestContainer | -- | 200 OK |
| DELETE | /api/Edit/Games/{id:int}/Challenges/{cId:int} | RemoveGameChallenge | -- | 200 OK |
| POST | /api/Edit/Games/{id:int}/Challenges/{cId:int}/Attachment | UpdateAttachment | AttachmentCreateModel | 200 OK |
| POST | /api/Edit/Games/{id:int}/Challenges/{cId:int}/Flags | AddFlags | FlagCreateModel[] | 200 OK |
| DELETE | /api/Edit/Games/{id:int}/Challenges/{cId:int}/Flags/{fId:int} | RemoveFlag | -- | TaskStatus |
| POST | /api/Edit/Games/{id:int}/Export | ExportGame | -- | File (zip) |
| POST | /api/Edit/Games/Import | ImportGame | IFormFile | int (gameId) |
| POST | /api/Edit/Games/{id:int}/Scoreboard/Flush | FlushScoreboardCache | -- | 200 OK |

### 1.6 ErrorController
Route: /error

| Method | Route | Action | Auth | Response Model |
| --- | --- | --- | --- | --- |
| ANY | /error/500 | InternalServerError | None | RequestResponse |

### 1.7 ExerciseController (EMPTY STUB)
Route: api/Exercise | Class: [RequireUser]
NO endpoints implemented -- TODO: exercise mode support

### 1.8 GameController
Route: api/[controller] => api/Game

| Method | Route | Action | Auth | Request Model | Response Model |
| --- | --- | --- | --- | --- | --- |
| GET | /api/Game/Recent | RecentGames | None | ?limit | BasicGameInfoModel[] |
| GET | /api/Game | Games | RateLimit(Query) | count,skip | ArrayResponse<BasicGameInfoModel> |
| GET | /api/Game/{id:int} | Game | None | -- | DetailedGameInfoModel |
| GET | /api/Game/{id:int}/Check | GetGameJoinCheckInfo | [RequireUser] | -- | GameJoinCheckInfoModel |
| POST | /api/Game/{id:int} | JoinGame | [RequireUser] | GameJoinModel | 200 OK |
| DELETE | /api/Game/{id:int} | LeaveGame | [RequireUser] | -- | 200 OK |
| GET | /api/Game/{id:int}/Scoreboard | Scoreboard | None | -- | ScoreboardModel |
| GET | /api/Game/{id:int}/Notices | Notices | None | count,skip | GameNotice[] |
| GET | /api/Game/{id:int}/Events | Events | [RequireMonitor] | hideContainer,count,skip | GameEvent[] |
| GET | /api/Game/{id:int}/Submissions | Submissions | [RequireMonitor] | type,count,skip | Submission[] |
| GET | /api/Game/{id:int}/CheatInfo | CheatInfo | [RequireMonitor] | -- | CheatInfoModel[] |
| GET | /api/Game/Games/{id:int}/Captures | GetChallengesWithTrafficCapturing | [RequireMonitor] | -- | ChallengeTrafficModel[] |
| GET | /api/Game/Captures/{challengeId:int} | GetChallengeTraffic | [RequireMonitor] | -- | TeamTrafficModel[] |
| GET | /api/Game/Captures/{challengeId:int}/{partId:int} | GetTeamTraffic | [RequireMonitor] | -- | FileRecord[] |
| GET | /api/Game/Captures/{challengeId:int}/{partId:int}/All | GetAllTeamTraffic | [RequireMonitor] | -- | File (tar) |
| DELETE | /api/Game/Captures/{challengeId:int}/{partId:int}/All | DeleteAllTeamTraffic | [RequireMonitor] | -- | 200 OK |
| GET | /api/Game/Captures/{challengeId:int}/{partId:int}/{filename} | GetTeamTraffic (single) | [RequireMonitor] | -- | File |
| DELETE | /api/Game/Captures/{challengeId:int}/{partId:int}/{filename} | DeleteTeamTraffic | [RequireMonitor] | -- | 200 OK |
| GET | /api/Game/{id:int}/Details | ChallengesWithTeamInfo | [RequireUser] | -- | GameDetailModel |
| GET | /api/Game/{id:int}/Participations | Participations | [RequireAdmin] | -- | ParticipationInfoModel[] |
| GET | /api/Game/{id:int}/ScoreboardSheet | ScoreboardSheet | [RequireMonitor] | -- | File (xlsx) |
| GET | /api/Game/{id:int}/SubmissionSheet | SubmissionSheet | [RequireMonitor] | -- | File (xlsx) |
| GET | /api/Game/{id:int}/Challenges/{challengeId:int} | GetChallenge | [RequireUser] | -- | ChallengeDetailModel |
| POST | /api/Game/{id:int}/Challenges/{challengeId:int} | Submit | [RequireUser],RateLimit(Submit) | FlagSubmitModel | int (ID) |
| GET | /api/Game/{id:int}/Challenges/{challengeId:int}/Status/{submitId:int} | Status | [RequireUser] | -- | AnswerResult |
| GET | /api/Game/{id:int}/Writeup | GetWriteup | [RequireUser] | -- | BasicWriteupInfoModel |
| POST | /api/Game/{id:int}/Writeup | SubmitWriteup | [RequireUser] | IFormFile | 200 OK |
| POST | /api/Game/{id:int}/Container/{challengeId:int} | CreateContainer | [RequireUser],RateLimit(Container) | -- | ContainerInfoModel |
| POST | /api/Game/{id:int}/Container/{challengeId:int}/Extend | ExtendContainerLifetime | [RequireUser],RateLimit(Container) | -- | ContainerInfoModel |
| DELETE | /api/Game/{id:int}/Container/{challengeId:int} | DeleteContainer | [RequireUser],RateLimit(Container) | -- | 200 OK |

### 1.9 ImageTemplateController
Route: api/v1/image-templates | Class: [Authorize]

| Method | Route | Action | Auth | Response Model |
| --- | --- | --- | --- | --- |
| POST | /api/v1/image-templates | Upload | [Authorize(Roles=Admin,Author)] | ImageTemplate (201) |
| GET | /api/v1/image-templates | List | [Authorize] | {total,page,pageSize,items} (anon) |
| GET | /api/v1/image-templates/{id:int} | GetById | [Authorize] | ImageTemplate |
| DELETE | /api/v1/image-templates/{id:int} | Delete | [Authorize(Roles=Admin)] | 204 No Content |

### 1.10 InfoController
Route: api (bare, no controller name in path)

| Method | Route | Action | Auth | Response Model |
| --- | --- | --- | --- | --- |
| GET | /api/Posts/Latest | GetLatestPosts | None | PostInfoModel[] |
| GET | /api/Posts | GetPosts | RateLimit(Query) | PostInfoModel[] |
| GET | /api/Posts/{id} | GetPost | None | PostDetailModel |
| GET | /api/Config | GetClientConfig | None | ClientConfig |
| GET | /api/Captcha | GetClientCaptchaInfo | None | ClientCaptchaInfoModel |
| GET | /api/Captcha/PowChallenge | PowChallenge | RateLimit(PowChallenge) | HashPowChallenge |

### 1.11 IRChallengeController
Route: api/v1/ir-challenges | Class: class-level 400/401/403 produces

| Method | Route | Action | Auth | Request Model | Response Model |
| --- | --- | --- | --- | --- | --- |
| POST | /api/v1/ir-challenges | Create | [RequirePrivilege(Role.Admin)] | IRChallengeCreateModel | IRChallengeDetailModel |
| GET | /api/v1/ir-challenges | List | None | ?gameId,count,skip | ArrayResponse<IRChallengeListItemModel> |
| GET | /api/v1/ir-challenges/{id} | Get | None | -- | IRChallengeDetailModel |
| PUT | /api/v1/ir-challenges/{id} | Update | [RequirePrivilege(Role.Admin)] | IRChallengeUpdateModel | IRChallengeDetailModel |
| DELETE | /api/v1/ir-challenges/{id} | Delete | [RequirePrivilege(Role.Admin)] | -- | 204 No Content |
| POST | /api/v1/ir-challenges/{id}/instances | CreateInstance | [RequireUser] | ?timeSlotId | IRInstanceDetailModel |
| GET | /api/v1/ir-challenges/instances/{instanceId:guid} | GetInstance | [RequireUser] | -- | IRInstanceDetailModel |
| POST | /api/v1/ir-challenges/instances/{instanceId:guid}/checkpoints/{checkpointId:int}/submit | SubmitCheckpoint | [RequireUser] | CheckpointSubmitModel | IRInstanceDetailModel |
| POST | /api/v1/ir-challenges/instances/{instanceId:guid}/reset | ResetInstance | [RequireUser] | -- | IRInstanceDetailModel |

### 1.12 LeaderboardController
Route: api/v1/scenarios

| Method | Route | Action | Auth | Response Model |
| --- | --- | --- | --- | --- |
| GET | /api/v1/scenarios/{challengeId:int}/leaderboard | GetLeaderboard | [RequireUser] | LeaderboardResponse |

### 1.13 ProxyController
Route: api/[controller] => api/Proxy

| Method | Route | Action | Auth | Response Model |
| --- | --- | --- | --- | --- |
| ANY | /api/Proxy/{id:guid} | ProxyForInstance | None | WebSocket / 204 |
| ANY | /api/Proxy/NoInst/{id:guid} | ProxyForNoInstance | None | WebSocket / 204 |

### 1.14 ScenarioController
Route: api/v1/scenarios | Class: class-level 401/403 produces

| Method | Route | Action | Auth | Request Model | Response Model |
| --- | --- | --- | --- | --- | --- |
| POST | /api/v1/scenarios | CreateScenario | [RequireAdmin] | ScenarioCreateModel | ScenarioDetailModel |
| GET | /api/v1/scenarios | ListScenarios | None | ?gameId,count,skip | ArrayResponse<ScenarioListModel> |
| GET | /api/v1/scenarios/{id:int} | GetScenario | None | -- | ScenarioDetailModel |
| PUT | /api/v1/scenarios/{id:int} | UpdateScenario | [RequireAdmin] | ScenarioUpdateModel | ScenarioDetailModel |
| DELETE | /api/v1/scenarios/{id:int} | DeleteScenario | [RequireAdmin] | -- | 200 OK |
| POST | /api/v1/scenarios/{id:int}/publish | PublishScenario | [RequireAdmin] | -- | {Id,IsEnabled} (anon) |
| POST | /api/v1/scenarios/{id:int}/instances | CreateInstance | [RequireUser] | CreateInstanceRequest | ScenarioInstanceModel |
| GET | /api/v1/scenarios/instances/{instanceId:guid} | GetInstanceStatus | [RequireUser] | -- | ScenarioInstanceModel |
| POST | /api/v1/scenarios/instances/{instanceId:guid}/stages/{stageId:int}/submit | SubmitStageFlag | [RequireUser] | FlagSubmitModel | StageSubmitResult |

### 1.15 SubmissionController
Route: api/v1/submissions | Class: class-level 401/403 produces

| Method | Route | Action | Auth | Request Model | Response Model |
| --- | --- | --- | --- | --- | --- |
| POST | /api/v1/submissions | CreateSubmission | [RequireUser] | SubmissionCreateRequest | SubmissionResponse |
| GET | /api/v1/submissions | QuerySubmissions | [RequireUser] | ?challengeId,userId,... | ArrayResponse<SubmissionResponse> |
| POST | /api/v1/submissions/upload | UploadWriteup | [RequireUser] | IFormFile | SubmissionResponse |
| GET | /api/v1/submissions/pending-review | GetPendingReviews | [RequireAdmin] | ?challengeId,count,skip | ArrayResponse<SubmissionResponse> |
| POST | /api/v1/submissions/{id:int}/review | SubmitReview | [RequireAdmin] | ReviewRequest | SubmissionResponse |

### 1.16 TeamController
Route: api/[controller] => api/Team

| Method | Route | Action | Auth | Request Model | Response Model |
| --- | --- | --- | --- | --- | --- |
| GET | /api/Team/{id:int} | GetBasicInfo | None | -- | TeamInfoModel |
| GET | /api/Team | GetTeamsInfo | [RequireUser] | -- | TeamInfoModel[] |
| POST | /api/Team | CreateTeam | [RequireUser],RateLimit(Concurrency) | TeamUpdateModel | TeamInfoModel |
| PUT | /api/Team/{id:int} | UpdateTeam | [RequireUser] | TeamUpdateModel | TeamInfoModel |
| PUT | /api/Team/{id:int}/Transfer | Transfer | [RequireUser] | TeamTransferModel | TeamInfoModel |
| GET | /api/Team/{id:int}/Invite | InviteCode | [RequireUser] | -- | string |
| PUT | /api/Team/{id:int}/Invite | UpdateInviteToken | [RequireUser] | -- | string |
| POST | /api/Team/{id:int}/Kick/{userId:guid} | KickUser | [RequireUser] | -- | TeamInfoModel |
| POST | /api/Team/Accept | Accept | [RequireUser] | string (code) | 200 OK |
| POST | /api/Team/{id:int}/Leave | Leave | [RequireUser] | -- | 200 OK |
| PUT | /api/Team/{id:int}/Avatar | Avatar | [RequireUser] | IFormFile | string (URL) |
| DELETE | /api/Team/{id:int} | DeleteTeam | [RequireUser] | -- | 200 OK |
| POST | /api/Team/Verify | VerifySignature | None | SignatureVerifyModel | 200 OK |

### 1.17 TimeSlotController
Route: api/v1/scenarios (same base as ScenarioController)

| Method | Route | Action | Auth | Response Model |
| --- | --- | --- | --- | --- |
| GET | /api/v1/scenarios/{id:int}/timeslots | GetTimeSlots | None | TimeSlotResponse[] |
| POST | /api/v1/scenarios/{id:int}/timeslots/{slotId:int}/reserve | ReserveSlot | [RequireUser] | ReservationResult |

## 2. Route Versioning Analysis

### Five Distinct Conventions
| Convention | Pattern | Controllers |
| --- | --- | --- |
| A (Legacy) | api/[controller] | Admin, Game, Edit, Team, Proxy, Exercise |
| A2 (Legacy+Action) | api/[controller]/[action] | Account |
| B (Versioned) | api/v1/{feature-name} | IRChallenge, Scenario, Submission, ImageTemplate, TimeSlot, Leaderboard |
| C (Bare) | api/ | InfoController (no controller name) |
| D (No prefix) | [controller] | AssetsController (GET only) |
| E (Error) | /error | ErrorController |
| F (Outlier) | api/tokens | ApiTokenController |

## 3. Key Inconsistencies

### 3.1 Route Bug in GameController
The route [HttpGet("Games/{id:int}/Captures")] on a controller mounted at api/Game produces: /api/Game/Games/{id}/Captures -- the word "Games" appears twice. This is a bug; likely should be [HttpGet("{id:int}/Captures")].

### 3.2 DELETE Status Codes
| Controller | HTTP Status |
| --- | --- |
| IRChallengeController.Delete | 204 No Content |
| ScenarioController.DeleteScenario | 200 OK |
| ImageTemplateController.Delete | 204 No Content |
| EditController.DeleteGame | 200 OK |
| AdminController.DeleteUser | 200 OK |

Same semantic operation (resource deletion), three different conventions.

### 3.3 Auth Attribute Inconsistency
IRChallengeController uses [RequirePrivilege(Role.Admin)] (generic base class), while ScenarioController uses [RequireAdmin] (derived class). Functionally identical but applied inconsistently.

### 3.4 Writeup Casing
AdminController uses "Writeups" (all lowercase s), EditController uses "WriteUps" (capital U). Different casing in routes for the same concept.

### 3.5 Anonymous Response Types
ImageTemplateController.List returns {total, page, pageSize, items} -- no typed model.
ScenarioController.PublishScenario returns new {Id, IsEnabled} -- anonymous type from controller.

### 3.6 Response Wrapping Variance
| Pattern | Used By |
| --- | --- |
| ArrayResponse<T> | Admin (Users/Teams/Instances/Files), IRChallenge (List), Submission (Query), Edit (Games) |
| .ToResponse() extension | Scenario (ListScenarios), Admin (Search), Edit, Submission |
| Raw T[] (no wrapper) | Admin (Logs), Game (Events/Submissions), Edit (Challenges), Info (Posts) |
| Anonymous object | ImageTemplate (List), Scenario (Publish) |
| Custom class | Leaderboard (LeaderboardResponse), TimeSlot (TimeSlotResponse[]) |


### 3.7 Missing 400 on Class-Level Produces
IRChallengeController has [ProducesResponseType(400)] at class level. ScenarioController and SubmissionController do NOT -- they only declare 401 and 403.

### 3.8 No Rate Limiting on v1 Endpoints
IRChallengeController, ScenarioController, and SubmissionController have zero [EnableRateLimiting] attributes, including on flag submission endpoints.

## 4. Dead Code

1. ExerciseController -- empty stub, TODO placeholder, no endpoints registered.
2. RequireAdminOrTokenAttribute -- defined in PrivilegeAuthentication.cs, never used on any controller.

## 5. Missing Endpoints

### IRChallengeController
- DELETE /api/v1/ir-challenges/instances/{instanceId} (destroy instance)
- GET /api/v1/ir-challenges/{id}/instances (list instances per challenge)
- PATCH /api/v1/ir-challenges/instances/{instanceId}/checkpoints/{checkpointId} (ManualReview admin approval)
- Individual checkpoint CRUD (add/edit/delete checkpoints separately from challenge)

### ScenarioController
- DELETE /api/v1/scenarios/instances/{instanceId} (abandon/delete instance)
- Individual stage CRUD endpoints
- ScoringRule management endpoints
- GET /api/v1/scenarios/{id}/instances (list user instances)

### General
- Health check / readiness probe endpoint
- Bulk instance cleanup for admins
- Rate limiting on v1 flag submission endpoints (brute-force protection gap)

## 6. Rate Limiting Summary

| Policy | Type | Limits | Applied To Endpoints |
| --- | --- | --- | --- |
| Global (default) | SlidingWindow | 150/min, queue 60 | All requests |
| Concurrency | Concurrency | 1 concurrent, queue 20 | Team.CreateTeam |
| Register | SlidingWindow | 20/150s | Account.Register/Recovery/PasswordReset/ChangeEmail |
| Query | TokenBucket | 100 tokens, 10/10s | Game.Games, Info.GetPosts |
| Container | TokenBucket | 120 tokens, 30/10s | Game container operations |
| Submit | TokenBucket | 100 tokens, 50/5s | Game.Submit (only!) |
| PowChallenge | TokenBucket | 40 tokens, 5/30s | Info.PowChallenge |

## 7. Top 10 Critical Findings

1. FIVE route conventions co-existing: api/[controller], api/v1/, bare api/, api/tokens, and Assets/ (no api prefix).
2. Route bug in GameController: /api/Game/Games/{id}/Captures -- double "Games" in URL path.
3. DELETE status codes inconsistent: some return 200, some return 204 for same operation.
4. Anonymous response types: ImageTemplate.List returns {total,page,pageSize,items} instead of ArrayResponse<T>.
5. No rate limiting on v1 flag/submission endpoints -- brute-force protection gap in SubmissionController, ScenarioController, IRChallengeController.
6. ExerciseController is a completely empty stub (dead code).
7. RequireAdminOrTokenAttribute is defined but never applied to any controller (dead code).
8. Auth attribute inconsistency: [RequirePrivilege(Role.Admin)] vs the preferred [RequireAdmin] derived class.
9. Missing instance deletion endpoints: neither IR nor Scenario instances can be explicitly destroyed by users.
10. Response shape varies: ArrayResponse<T> vs .ToResponse() extension vs raw arrays vs anonymous objects -- no single standard.
