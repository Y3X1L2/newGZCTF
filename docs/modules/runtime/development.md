# Runtime 开发地图

先读 [运行底座概览](README.md)。Runtime 当前跨 Modules/Runtime、Services/Fleet、Services/Vm 和 Agent 四处；本页给出代码路线，详细行为按 [调度恢复](reference/scheduling-recovery.md) 与 [Agent/VM](reference/agent-vm.md) 阅读。

## 沿一次请求定位

| 阶段 | 真实源码入口 |
| --- | --- |
| 统一请求与票据 | [DeploymentQueueModels](../../../src/GZCTF/Services/Fleet/DeploymentQueueModels.cs)、[DeploymentQueueTicket](../../../src/GZCTF/Models/Data/DeploymentQueueTicket.cs)、Fleet/DeploymentQueueService.cs |
| 受理限制与公平选择 | [RuntimeAdmissionPolicy](../../../src/GZCTF/Modules/Runtime/Application/RuntimeAdmissionPolicy.cs)、RuntimeQueueSelector.cs、WorkloadSchedulingContracts.cs、RuntimeSchedulingOptions.cs |
| 节点适配与容量 | [RuntimeSchedulingService](../../../src/GZCTF/Modules/Runtime/Application/RuntimeSchedulingService.cs)、NodeEligibilityEvaluator.cs、NodeCapacitySnapshotService.cs、NodeDispatchLimiter.cs、TeamLabPhysicalPlacementService.cs；Fleet/FleetCapacityReservationService.cs |
| 选中节点镜像准备 | Fleet/ImageDistributionService.cs、ImageDistributionCoordinator.cs、ImageDistributionWorker.cs；Content 提供模板目录 |
| 执行与任务生命周期 | [RuntimeExecutionService](../../../src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs)、RuntimeTicketLifecycleDispatcher.cs；Fleet/DeploymentExecutionService.cs、FleetContainerManager.cs、FleetVmService.cs |
| 节点信号与读投影 | [RuntimeSignalService](../../../src/GZCTF/Modules/Runtime/Application/RuntimeSignalService.cs)、Api/InternalRuntimeSignalsController.cs、Contracts/RuntimeSignalContracts.cs；Fleet/PlayerRuntimeStatusProjection.cs |
| 恢复与事实对账 | [RuntimeFactReconciliationService](../../../src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs) 及 Preview partial；Infrastructure/RuntimeRecoveryWorker.cs |
| 后台循环与唤醒 | Infrastructure/RuntimeSchedulingWorker.cs、RuntimeExecutionWorker.cs、RedisDeploymentQueueWakeup.cs、PollingDeploymentQueueWakeup.cs、RedisRuntimeSignalWakeup.cs |

RedisNodeLiveStateStore 与 PostgresNodeLiveStateFallback 管理观测读路径；数据库票据依旧是事实源。DeploymentQueueTicketStatus、RuntimeOperationKind、DeploymentStage 在票据实体文件，不能从 ApiOperationStatus 推断数值。

## Fleet、VM 与独立 Agent

- [AgentClient](../../../src/GZCTF/Services/Fleet/AgentClient.cs) 是主站机器通讯边界。节点与端口相关实现位于 Services/Fleet/NodeDeployService、PortAllocationService、PortLeaseRefreshService、NginxSyncService 等。
- VM 访问/就绪在 Services/Fleet/FleetVmService、CompetitionVmAccessService、VmReadyService，存储在 VmImageRegistryService 与 VmArtifactStore；provider 接口及旧适配在 [Services/Vm/IVirtualMachineProvider](../../../src/GZCTF/Services/Vm/IVirtualMachineProvider.cs)。没有 Modules/Vm 目录。
- [Agent VmController](../../../src/GZCTF.Agent/Controllers/VmController.cs) 调用 Services/Vm/ 中的 LibvirtTeamLabProvider、VmDomainBuilder、VmBootstrapService、VmRuntimeReadinessCoordinator、VmGuestAgentService 和 TeamLabVmNetworkService。
- Agent Docker 在 [DockerService](../../../src/GZCTF.Agent/Services/DockerService.cs)，TeamLab 网络在 Services/TeamLab/；机器接口鉴权和能力检查不能被业务页面绕过。
- 前端节点页面在 `ClientApp/src/vnext/features/admin/nodes/`，玩家实例展示在 `features/challenge-runtime/`；API adapter 在对应 feature 目录。

## 修改边界

运行请求携带规范化资源需求与身份，Runtime 不拥有计分、课程或 Flag 判定。Docker/KVM 独立适配；容量判断同时看预留与 inventory，不能拿在线状态替代资源余量。取消、恢复、销毁按资源归属和 generation 处理，失去 Redis 唤醒不得丢失数据库任务。

入口公布使用自身 ACK/就绪语义；VM 的配置地址、QGA 初始化、RDP 服务、VNC 图形监听和真实用户登录各不等价。服务端错误必须脱敏。不要从日志文本修复数据库运行状态。

## 验证选择

| 改动 | 现有测试定位 |
| --- | --- |
| 调度、公平、容量 | `src/GZCTF.Test/UnitTests/Runtime/RuntimeControlPlaneTests.cs`、`NodeDispatchBudgetTests.cs`、`UnifiedCapacityAccountingTests.cs`、`TeamLabPlacementCapacityTests.cs`、`WorkloadResourceVectorTests.cs` |
| 观测与恢复 | `UnitTests/Runtime/RuntimeFactReconciliationTests.cs`、`PlayerRuntimeStatusProjectionTests.cs`、`AgentRuntimeSignalJournalTests.cs`、`UnitTests/Cache/RedisRuntimeInfrastructureTests.cs` |
| 下载和缓存容量 | `UnitTests/Runtime/AgentImageStorageBudgetTests.cs`、`AgentImageDownloadWriterTests.cs`、`ImageTransferSingleFlightLifecycleTests.cs` |
| Docker/VM 就绪 | `UnitTests/Fleet/InstanceReadinessContractTests.cs`、`UnitTests/Vm/KvmProviderTests.cs`、`RdpPortReadinessTests.cs`、`VmConsoleExposureTests.cs`、`VmImageBackingChainTests.cs` |
| 持久化与网关 | `src/GZCTF.Integration.Test/Tests/Database/RuntimeSchedulingMigrationTests.cs`、`UnifiedRuntimeResourceMigrationTests.cs`；`Tests/Runtime/GuestGatewayTests.cs` |

共享调度、迁移和认证变更扩大验证，至少按 AGENTS 执行 Release build 和受影响测试；迁移须 PostgreSQL 验证。mock 或 Testcontainers 不替代真实 Docker/KVM、网关和断连恢复验收。现场操作看 [运行恢复手册](../../operations/runtime-scheduling-and-recovery.md) 与 [当前事实](../../current-state.md)，生产发布需明确授权。
