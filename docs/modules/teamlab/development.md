# TeamLab 开发地图

先读 [模块入口](README.md)，按改动选择一条业务链路。本页只导航源码、契约和测试；完整字段在 [参考导航](reference/README.md)。基线为本任务 fetch 后的 main 源码，生产证据另见 [当前状态](../../current-state.md)。

## 从任务找到实现

以下路径相对仓库根目录，链接指向真实源码入口；同目录 partial 文件一起检查。

| 链路 | 入口、编排与持久化 | 专题 |
| --- | --- | --- |
| 草稿、校验、发布 | [OpenTeamLabTopologiesController](../../../src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs) → [TeamLabTopologyApplicationService](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs)、Validation/、TeamLabReleaseService；Domain/TeamLabTopology 与 TeamLabTopologyRelease | [拓扑与发布](reference/topology-release.md)、[基座契约](reference/foundation-contract.md) |
| 镜像条件与显式预热 | [TeamLabReleaseImagePreparationService](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseImagePreparationService.cs) → Fleet/ImageDistributionService；创建路径只为选中节点准备 | [镜像准备](reference/image-preparation.md)、[Content 生命周期](../content/reference/image-lifecycle.md) |
| 单次运行与代次 | [TeamLabRuntimeOrchestrator](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs)、TeamLabRuntimePlanner、TeamLabExecutionPlanCompiler、TeamLabShardDeploymentService；Domain/Runtime/ 与执行快照 | [运行生命周期](reference/runtime-lifecycle.md)、[事实投影](reference/runtime-capabilities.md) |
| 运行更新与单资产控制 | [TeamLabRuntimeUpdateService](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeUpdateService.cs)、TeamLabAssetControlService、TeamLabAssetControlGuard；走原部署票据 | [授权及更新](guides/integration-grants-updates.md)、[资产操作](reference/asset-operations.md) |
| 批量目标管理 | [TeamLabRolloutApplicationService](../../../src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutApplicationService.cs)、TeamLabRolloutCoordinator；Domain/Rollout/ | [Rollout](reference/rollouts.md) |
| 网络与链路策略 | [TeamLabRouteApplicationService](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabRouteApplicationService.cs)、TeamLabLinkPolicyService、TeamLabFabricLinkAllocator；Agent/Services/TeamLab/ | [组网原则](reference/networking-practices.md)、[链路策略](reference/networking-policy.md) |
| 访问、会话与文件 | [TeamLabAuthorizationService](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabAuthorizationService.cs)、TeamLabRemoteAccessService、TeamLabAssetFileService；基础设施 relay/file gateway | [会话](reference/remote-sessions.md)、[文件和证据](reference/asset-operations.md) |
| 流量、路径与 PCAP | [TeamLabTrafficApplicationService](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs)、TeamLabTrafficPathCorrelator、TeamLabCaptureCoordinator；Infrastructure/TeamLabCapture* | [观测](reference/observation.md) |
| 销毁与记录删除 | [TeamLabRuntimeCleanupService](../../../src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs)、TeamLabRecordDeletionService；检查所有分片与跨模块引用 | [生命周期](reference/runtime-lifecycle.md)、[清理原则](reference/networking-practices.md) |

## 接口与前端定位

- `src/GZCTF/Modules/TeamLab/Api/`：OpenTeamLab* 为 token 入口，TeamLabAdmin*/TeamLabAsset* 为浏览器入口，InternalTeamLab* 为机器入口。
- `src/GZCTF/Modules/TeamLab/Contracts/`：拓扑、计划、runtime、文件、访问、rollout、流量等公开模型；共用执行 DTO 在 [GZCTF.TeamLab.Contracts](../../../src/GZCTF.TeamLab.Contracts/TeamLabAssetFiles.cs)。
- `src/GZCTF/Modules/TeamLab/Infrastructure/`：EF 配置、query provider、持久化 operation handler、AgentTeamLabNodeExecutor、远程 relay、抓包存储。
- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/`：场景、发布与运行详情 controller/hook；玩家入口在 `features/games/teamlab/`，比赛绑定与计分仍属业务模块。
- Agent 网络在 `src/GZCTF.Agent/Services/TeamLab/`，VM 网卡在 `Services/Vm/TeamLabVmNetworkService*`，流量和 PCAP 在 `Services/Observation/`。

## 修改时保持的边界

跨模块读模板能力用 Content query，跨模块写用 command；Controller 不直接下发 Agent 命令。发布摘要、runtime generation、planRevision 和资产原生身份共同约束执行与清理，不能从当前草稿重建旧运行事实。GET 投影不产生探测、会话或 VPN grant；分配地址和模板 metadata 不等于观测值。

外部 token 的 scope、teamlab-scope grant 和 Runtime Grant 各有作用，不继承签发者管理员身份。资产生命周期、终端、文件权限分别校验。文件仅支持 Docker 与可确认 OS 的 Linux VM，Windows/unknown 返回 files.unsupported；凭据不返回给浏览器。详细约束见 [运行事实契约](reference/runtime-capabilities.md) 和 [外部控制面](reference/external-control-plane.md)。

## 最小充分验证

| 变更 | 优先定位的现有测试 |
| --- | --- |
| schema、编译与发布 | `src/GZCTF.Test/UnitTests/TeamLab/TeamLabTopologyV2Tests.cs`、`TeamLabExecutionPlanV2Tests.cs`、`TeamLabFoundationBoundaryTests.cs` |
| 调度、清理、代次 | `TeamLabDeploymentOrchestrationTests.cs`、`TeamLabCleanupOwnershipTests.cs`、`TeamLabRuntimeUpdateDispatchTests.cs` |
| 来宾网络与路由 | `TeamLabManagedGuestNetworkTests.cs`、`TeamLabManagedVmNetworkTests.cs`、`TeamLabRouteIsolationTests.cs`、`TeamLabGuestNetworkDeadlineAndFailureTests.cs` |
| 权限、单资产、文件/会话 | `TeamLabScopeAuthorizationTests.cs`、`TeamLabAssetControlTests.cs`、`TeamLabAssetFileTests.cs`、`TeamLabRemoteAuditTests.cs`、`TeamLabVncConsoleTests.cs` |
| 数据与 HTTP | `src/GZCTF.Integration.Test/Tests/Database/TeamLab*Tests.cs`；`Tests/Api/OpenTeamLabOperationsApiTests.cs` 与 `OpenTeamLabRetirementApiTests.cs` |

定向测试通过不覆盖真实 OVN/WireGuard、来宾网络、桌面登录、物理设备与跨节点故障验收。按 [基础验收](guides/foundation-acceptance.md) 和 [执行手册](guides/execution-operations.md) 留独立证据；发布流程用 [维护窗口](../../operations/vnext-maintenance-window-rollout.md)。本次仅文档整理，不重新声称实机通过。
