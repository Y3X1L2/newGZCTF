# AWDP 开发定位

[概览](README.md)说明选手流程，[手工验收](guides/manual-acceptance.md)保留真实攻击、修补和清理的检查步骤。

## 管理端与选手端

| 问题 | 后端入口 | 先检查 |
| --- | --- | --- |
| 服务配置、启动或停止失败 | [AwdpAdminController](../../../src/GZCTF/Controllers/AwdpAdminController.cs) | 角色、比赛类型、服务镜像/端口、阶段配置 |
| 无法读取本队状态或操作实例 | [AwdpPlayerController](../../../src/GZCTF/Controllers/AwdpPlayerController.cs) | `GetContextInfo`、比赛时间、Accepted 参赛关系、本队归属 |
| 服务、实例或操作历史缺失 | [AwdpRepository](../../../src/GZCTF/Repositories/AwdpRepository.cs) | Game / Service / Team / Round 关联与当前轮查询 |
| 外部 token 导入服务失败 | [OpenAwdpServicesController](../../../src/GZCTF/Modules/Ctf/Api/OpenAwdpServicesController.cs) | scope、比赛 resource grant、持久化 operation |

主要数据实体仍在 [Models/Data](../../../src/GZCTF/Models/Data)，
包括 `AwdpService`、`AwdpServiceInstance`、`AwdpRound`、`AwdpFlag`、
`AwdpPatchSubmission`、重置/恢复记录。
[AwdpEntityConfigurations](../../../src/GZCTF/Modules/Awdp/Infrastructure/Persistence/AwdpEntityConfigurations.cs)
维护 EF 约束；实体、迁移与读写路径须一起验证。

## 轮次与阶段

[AwdpRoundService](../../../src/GZCTF/Services/AwdpRoundService.cs) 是后台服务：

- `StartGame` 验证服务配置并启动比赛循环。
- `TryGetSchedule` 要求各服务的攻击分钟数、修补分钟数和总轮数一致且均为正数。
- `RunNextRoundStep` 根据数据库轮次推进 `AttackPhase -> PatchPhase -> Finished`。
- `GenerateFlagsAndInject` 按轮次、服务和队伍生成 Flag，并通过实例服务注入。
- 主站启动时读取未结束轮次并恢复循环；`StopGame` 结束轮次并按参数清理实例。

倒计时从已保存时间计算。浏览器刷新、页面计时器归零或 SignalR 通知
都不能独立改变业务阶段。多 Worker 故障与规模恢复的验收仍按[当前状态](../../current-state.md)限定。

## 攻击与计分

`AwdpPlayerController.SubmitFlag` 校验当前轮、攻击阶段、提交次数、
重复攻击与自身 Flag，再保存攻击记录。
[AwdpScoreService](../../../src/GZCTF/Services/AwdpScoreService.cs) 构建 AWDP 分数和榜单。
判分有误时同时比对服务规则、攻击记录、Checker 结果和补丁记录，
不要用普通 CTF 的 `FlagChecker` 解释 AWDP 分数。

## 补丁与健康状态

[AwdpPatchService](../../../src/GZCTF/Services/AwdpPatchService.cs) 中：

- `SubmitPatch` 接收并保存补丁提交。
- `ValidatePatchArchive` 检查归档路径、大小/条目限制及 `update.sh`。
- `ApplyAndVerifyPatch` 顺序执行补丁、Checker、EXP，保存三种结果，不能只看一个摘要标签。

[AwdpScriptRunner](../../../src/GZCTF/Services/AwdpScriptRunner.cs) 执行检查脚本；
[AwdpCheckerService](../../../src/GZCTF/Services/AwdpCheckerService.cs) 负责轮次检查。
[AwdpPatchStateResolver](../../../src/GZCTF/Services/AwdpPatchStateResolver.cs)
结合补丁、轮次、重置和恢复时间投影最近状态，重建后不能沿用旧“已防守”标签。
脚本、Flag、补丁内容不得进入文档或日志快照。

## 重置、恢复与队列

[AwdpInstanceService](../../../src/GZCTF/Services/AwdpInstanceService.cs)
负责按队创建实例、停止清理、重置和恢复。
`QueueResetAsync` 记录重置并提交统一队列；
`ExecuteQueuedCreateAsync` / `ExecuteQueuedResetAsync` 处理实际执行。
恢复另记 `AwdpRecoveryRecord`，核对自己的计数上限。
运行层异常继续查[Runtime](../runtime/development.md)，不能从日志文本重建业务状态。

## 前端与定向验证

管理界面在 [admin/awdp](../../../src/GZCTF/ClientApp/src/vnext/features/admin/awdp)，
选手界面在 [games/awdp](../../../src/GZCTF/ClientApp/src/vnext/features/games/awdp)。
`useAdminAwdpController` / `useAwdpWorkspaceController` 编排页面状态，
`awdpAdminApi.ts` / `awdpPlayerApi.ts` 适配接口；
[awdpDomain](../../../src/GZCTF/ClientApp/src/vnext/features/awdp/awdpDomain.ts) 处理展示规则。

[AwdpRoundServiceLifecycleTests](../../../src/GZCTF.Test/UnitTests/Services/AwdpRoundServiceLifecycleTests.cs)
验证后台服务生命周期，可定向运行：

`dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~AwdpRoundServiceLifecycleTests`

前端已有 `awdpDomain.test.ts`、`awdpServiceForm.test.ts`、`AwdpServiceTable.test.tsx`。
这些测试不能证明双队实际可互访、漏洞利用、修补和资源清理成功。
真实流程需明确授权并按指南执行；本页只定位实现，不授予测试授权。
依赖遵守[模块边界](../../architecture/module-boundary-map.md)，外部写入遵守[API 标准](../../architecture/external-api-standard.md)。
