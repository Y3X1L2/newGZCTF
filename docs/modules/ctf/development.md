# CTF 开发定位

先读[模块概览](README.md)。本页按问题找入口，普通 CTF 与 Penetration 分开定位。

## 报名、题目与比赛管理

| 问题 | 首先查看 | 核对内容 |
| --- | --- | --- |
| 队伍无法报名或报名后不能解题 | [GameController](../../../src/GZCTF/Controllers/GameController.cs)、[ParticipationRepository](../../../src/GZCTF/Repositories/ParticipationRepository.cs) | `JoinGame`、`GetContextInfo`；队伍成员、赛区、时间、参赛审核状态 |
| 管理端保存、审核或删除异常 | [EditController](../../../src/GZCTF/Controllers/EditController.cs) | 比赛/题目编辑入口及删除编排，不只检查前端表单 |
| 题目可见性与赛区权限不一致 | [DivisionRepository](../../../src/GZCTF/Repositories/DivisionRepository.cs)、[GameChallengeRepository](../../../src/GZCTF/Repositories/GameChallengeRepository.cs) | `ViewChallenge` / `SubmitFlags`、比赛快照与题目启用状态 |
| 阶段切换后无法提交 | [GamePhaseService](../../../src/GZCTF/Services/GamePhaseService.cs)、[GamePhaseController](../../../src/GZCTF/Controllers/GamePhaseController.cs) | 阶段时间和允许模块，与比赛总时间共同判断 |

浏览器入口在 [games](../../../src/GZCTF/ClientApp/src/vnext/features/games) 与
[admin/games](../../../src/GZCTF/ClientApp/src/vnext/features/admin/games)。
请求适配分别从 `gamePlayerApi.ts` 和
[gameAdminApi](../../../src/GZCTF/ClientApp/src/vnext/features/admin/api/gameAdminApi.ts) 查起。
题目状态与环境轮询由 `challenges/useGameInstance.ts` 管理。

## 提交已受理但未得分

普通 CTF 的 `GameController.Submit` 先验证比赛、题目、参赛关系、阶段和提交权限，
持久化 `Submission` 后再执行判定链路。受理成功不应在前端直接展示“答案正确”。

- [SubmissionRepository](../../../src/GZCTF/Repositories/SubmissionRepository.cs) 保存提交并传送判定任务。
- [FlagChecker](../../../src/GZCTF/Services/FlagChecker.cs) 处理未判定提交；重启会重新读取数据库中的 `FlagSubmitted` 记录。
- [GameInstanceRepository](../../../src/GZCTF/Repositories/GameInstanceRepository.cs) 提供队伍实例与答案校验；动态 Flag 必须属于当前实例。
- [GameRepository](../../../src/GZCTF/Repositories/GameRepository.cs) 构建榜单；提交与报名变化后的缓存失效也要一起检查。

前端 `challenges/useFlagSubmission.ts` 和 `scoreboard/ScoreboardPage.tsx`
分别负责提交交互和榜单展示。排查时记录比赛、队伍、题目、提交 ID 和时间，
禁止在日志或文档记录真实 Flag。

## 容器或 VM 没有就绪

从 `GameController.CreateContainer` / `GetVmStatus` 追到
`GameInstanceRepository` 和 [DeploymentQueueService](../../../src/GZCTF/Services/Fleet/DeploymentQueueService.cs)。
创建、延期、停止应保持统一队列事实；不能增加赛事专用的第二套调度队列。
VM 入口、节点能力与真实连接的检查见[运行模块](../runtime/README.md)。
具体镜像和附件引用问题转到[内容模块](../content/README.md)。

## 外部导入题目

[OpenChallengesController](../../../src/GZCTF/Modules/Ctf/Api/OpenChallengesController.cs)
调用 [ChallengeExternalApplicationService](../../../src/GZCTF/Modules/Ctf/Application/ChallengeExternalApplicationService.cs)，
由 `ChallengeMutationOperationHandler` 执行持久化任务。
校验 token 的 `challenges:*` scope、比赛 resource grant 和幂等键；
浏览器 `/api/...` 与 `/api/open/v1/...` 的兼容承诺不同，遵守[外部 API 标准](../../architecture/external-api-standard.md)。
AWDP 服务外部入口虽然位于 `Modules/Ctf/Api/OpenAwdpServicesController.cs`，业务仍归 AWDP。

## 渗透目标与 TeamLab 绑定

| 问题 | 实现入口 | 业务边界 |
| --- | --- | --- |
| 目标绑定、可见性或前置条件错误 | [PenetrationObjectiveService](../../../src/GZCTF/Modules/Penetration/Application/PenetrationObjectiveService.cs) | `ReplaceAsync`、`SubmitAsync`；目标属于比赛，实例属于 TeamLab |
| 本队工作区没有环境或次数不对 | [PenetrationWorkspaceService](../../../src/GZCTF/Modules/Penetration/Application/PenetrationWorkspaceService.cs) | `GetAsync` 汇总本队绑定、runtime 和目标提交，不创建拓扑 |
| 管理/选手权限异常 | [PenetrationAdminController](../../../src/GZCTF/Controllers/PenetrationAdminController.cs)、[PenetrationPlayerController](../../../src/GZCTF/Controllers/PenetrationPlayerController.cs) | 先核比赛与参赛队伍，再查服务规则 |

前端管理入口在 `admin/games/teamlab`，运行专题见[TeamLab](../teamlab/README.md)。
目标提交要核对当前队伍的运行环境、发布版本、可见目标、前置目标和尝试限制；
不能因为某个拓扑含有资产就允许任意队伍提交。

## 定向验证

按改动选择测试，不把以下定位表当作已执行结果：

| 变更 | 现有测试 |
| --- | --- |
| 报名与赛事流程 | [GameJoinTests](../../../src/GZCTF.Integration.Test/Tests/Api/GameJoinTests.cs)、[GameWorkflowTests](../../../src/GZCTF.Integration.Test/Tests/Api/GameWorkflowTests.cs) |
| 计分与榜单 | [DetailedGameScoringTests](../../../src/GZCTF.Integration.Test/Tests/Api/DetailedGameScoringTests.cs)、[ScoreboardCalculationTests](../../../src/GZCTF.Integration.Test/Tests/Api/ScoreboardCalculationTests.cs) |
| 渗透目标隔离 | [PenetrationObjectiveSecurityTests](../../../src/GZCTF.Test/UnitTests/TeamLab/PenetrationObjectiveSecurityTests.cs)、[TeamLabCompetitionSubmissionTests](../../../src/GZCTF.Test/UnitTests/TeamLab/TeamLabCompetitionSubmissionTests.cs) |
| 渗透生命周期 | [PenetrationTeamLabLifecycleTests](../../../src/GZCTF.Test/UnitTests/TeamLab/PenetrationTeamLabLifecycleTests.cs) |

示例：`dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~GameJoinTests`。
数据库与 HTTP 测试不能代替真实镜像启动、入口访问、跨节点和 VM 登录验收。
共用规则见[模块边界](../../architecture/module-boundary-map.md)和[前端组件边界](../../architecture/frontend/component-boundary.md)。
