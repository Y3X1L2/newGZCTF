# Runtime 开发地图

先读 [运行底座概览](README.md)。Runtime 当前跨 Modules/Runtime、Services/Fleet、Services/Vm 和 Agent 四处；本页给出代码路线，详细行为按 [调度恢复](reference/scheduling-recovery.md) 与 [Agent/VM](reference/agent-vm.md) 阅读。

## 沿一次请求定位

| 阶段 | 真实源码入口 |
| --- | --- |
| 统一请求与票据 | [队列请求](../../../src/GZCTF/Services/Fleet/DeploymentQueueModels.cs) 与 [票据实体](../../../src/GZCTF/Models/Data/DeploymentQueueTicket.cs)；Fleet/DeploymentQueueService.cs 处理提交 |
| 受理限制与公平选择 | [准入策略](../../../src/GZCTF/Modules/Runtime/Application/RuntimeAdmissionPolicy.cs) 与同目录选择器、调度配置管理排队限制和公平性 |
| 节点适配与容量 | [调度服务](../../../src/GZCTF/Modules/Runtime/Application/RuntimeSchedulingService.cs) 结合节点资格、容量快照和并发限制；Fleet/ 管理容量预留 |
| 选中节点镜像准备 | Fleet/ 中的镜像分发服务、协调器和 worker；Content 提供模板目录 |
| 执行与任务生命周期 | [执行服务](../../../src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs) 推进票据，Fleet/ 适配容器与 VM 操作 |
| 节点信号与读投影 | [信号服务](../../../src/GZCTF/Modules/Runtime/Application/RuntimeSignalService.cs) 接收节点回报；Api/ 为机器入口，Contracts/ 为模型，Fleet/ 为玩家状态投影 |
| 恢复与事实对账 | [事实对账服务](../../../src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs) 及预览 partial；Infrastructure/RuntimeRecoveryWorker.cs 驱动恢复 |
| 后台循环与唤醒 | Infrastructure/ 中的调度、执行 worker 与 Redis/轮询唤醒实现 |

Redis 观测缓存与 PostgreSQL 回退实现管理观测读路径，数据库票据保存任务事实。票据实体文件定义任务状态、操作种类和执行阶段，各状态按自己的枚举解析。

## Fleet、VM 与独立 Agent

- [节点客户端](../../../src/GZCTF/Services/Fleet/AgentClient.cs) 是主站机器通讯边界。节点与端口相关实现位于 Services/Fleet/NodeDeployService、PortAllocationService、PortLeaseRefreshService、NginxSyncService 等。
- VM 访问、就绪与制品存储位于 Services/Fleet/；provider 接口及旧适配在 [Services/Vm/IVirtualMachineProvider](../../../src/GZCTF/Services/Vm/IVirtualMachineProvider.cs)。当前 VM 实现集中在上述服务目录。
- [Agent VM 接口](../../../src/GZCTF.Agent/Controllers/VmController.cs) 调用 Services/Vm/ 中的 libvirt provider、域定义、来宾初始化、就绪检查和网络服务。
- Agent Docker 在 [Docker 执行服务](../../../src/GZCTF.Agent/Services/DockerService.cs)，TeamLab 网络在 Services/TeamLab/；所有调用经机器接口鉴权与能力检查。
- 前端节点页面在 `ClientApp/src/vnext/features/admin/nodes/`，玩家实例展示在 `features/challenge-runtime/`；API adapter 在对应 feature 目录。

节点心跳的完整 manifest/hash 保留实测可用磁盘，容量变化仍更新；日志中的 `node.capability.changed` 只表示稳定能力变化，不再随磁盘波动或 Fabric 健康变化产生。能力比较和协议 hash 的职责见 [能力协商](../../architecture/agent-capability-protocol.md)；修改时同时验证容量刷新与事件触发，不能为降噪停掉真实容量更新。

## 管理监控读模型

负载图表可用滚轮缩放时间轴，以鼠标位置为中心；`+`、`-` 键也可缩放，`0` 键恢复。切换时间范围或节点会复位，定时刷新保留缩放窗口。数值通过图上提示读取，不再单独提供“查看数据”表格。

运行概览与节点详情通过现有节点 API 展示 CPU / 内存心跳实测值和 Docker / VM 分配、预留槽位。CPU / 内存均值只纳入最近 60 秒有有效心跳的在线或繁忙节点，未上报、离线和读取失败显示无可用读数，不能记作零负载。槽位统计是全部节点的配置容量，不能当作实时物理资源使用率。

负载趋势读取 `GET /api/v1/nodes/metrics/history?range=24h`，可用 `nodeId` 限定节点，权限为管理员。`range` 支持 `1h`、`24h`、`7d`，分别返回 1、5、30 分钟间隔的 60、288、336 个桶。响应为 `fromUtc`、`toUtc`、`intervalSeconds`、`points`；时间沿现有 API 约定使用 Unix 毫秒，最后一个桶可以尚未结束。每点包含 `timestamp`、可空 `cpuLoad` / `memoryLoad`、`nodeCount`、`sampleCount`。

[NodeMetricHistoryQuery](../../../src/GZCTF/Modules/Runtime/Application/NodeMetricHistoryQuery.cs) 复用后台 `NodeMetricPersistenceWorker` 持续写入的 `WorkerNodeMetricSamples`，不依赖页面是否打开，也不按节点当前在线状态删除旧读数。PostgreSQL 按所选范围聚合，最多返回 336 行；每个有效节点分钟均值等权，心跳次数不增加其权重。CPU 和内存分别排除无效值，缺测桶返回 null，图表保留断点。历史保留由 `DataRetention.WorkerNodeMetricDays` 控制，默认 180 天；没有新增采集循环、数据库表或迁移。验证见 `NodeMetricHistoryTests`，覆盖 PostgreSQL 聚合、空桶、异常读数、节点过滤、重开读取、权限和时间范围。

节点 TeamLab 页优先显示网络、Docker 场景和 VM 场景的独立可调度判定、地址及握手。原始 Agent feature 标识默认折叠到技术详情；检测按钮以异步请求期间的忙碌状态防止重复操作。兼容性判定不能代替真实场景运行验收。

节点资源列表和详情只为活动资源提供入口；历史资源不展示失效远程链接。入口按钮不把查询参数或远程凭据作为可见标签；裸 `host:port` 规范化为 HTTP 绝对地址，拒绝非 HTTP(S)、相对路径和内嵌用户名密码地址。状态与运行实例页共用中文说明。

## 修改边界

运行请求携带规范化资源需求与身份，计分、课程和 Flag 判定由业务模块负责。Docker/KVM 独立适配；容量判断同时看预留与 inventory，在线状态与资源余量分别检查。取消、恢复、销毁按资源归属和 generation 处理，数据库保留待处理任务，Redis 唤醒可由轮询补充。

个人练习、培训、VM 与管理员测试容器的创建票据在实际执行前重新读取所属账号的 `DeletedAtUtc`。排队后账号已删除时，执行返回不可重试的授权失败，由统一队列写入 Failed 并释放容量预留；Identity 不直接修改票据。该守卫只阻止新的个人实例创建，已有实例的停止、销毁和正常生命周期继续执行，战队与 TeamLab runtime 使用各自的归属规则。回归见 `RuntimeControlPlaneTests.DeletedPersonalOwner_FailsScheduledCreateAndReleasesReservation`。

入口公布使用自身 ACK/就绪语义；VM 按访问协议分别确认配置地址、QGA 初始化、RDP 服务、VNC 图形监听和真实用户登录。服务端错误必须脱敏。修复运行状态时依据数据库身份、目标意图和节点 inventory；日志用于追踪原因。

## 验证选择

| 改动 | 现有测试定位 |
| --- | --- |
| 调度、公平、容量 | `src/GZCTF.Test/UnitTests/Runtime/RuntimeControlPlaneTests.cs`、`NodeDispatchBudgetTests.cs`、`UnifiedCapacityAccountingTests.cs`、`TeamLabPlacementCapacityTests.cs`、`WorkloadResourceVectorTests.cs` |
| 观测与恢复 | `UnitTests/Runtime/RuntimeFactReconciliationTests.cs`、`PlayerRuntimeStatusProjectionTests.cs`、`AgentRuntimeSignalJournalTests.cs`、`UnitTests/Cache/RedisRuntimeInfrastructureTests.cs` |
| 下载和缓存容量 | `UnitTests/Runtime/AgentImageStorageBudgetTests.cs`、`AgentImageDownloadWriterTests.cs`、`ImageTransferSingleFlightLifecycleTests.cs` |
| Docker/VM 就绪 | `UnitTests/Fleet/InstanceReadinessContractTests.cs`、`UnitTests/Vm/KvmProviderTests.cs`、`RdpPortReadinessTests.cs`、`VmConsoleExposureTests.cs`、`VmImageBackingChainTests.cs` |
| 持久化与网关 | `src/GZCTF.Integration.Test/Tests/Database/RuntimeSchedulingMigrationTests.cs`、`UnifiedRuntimeResourceMigrationTests.cs`；`Tests/Runtime/GuestGatewayTests.cs` |

共享调度、迁移和认证变更扩大验证，至少按 AGENTS 执行 Release build 和受影响测试；迁移须 PostgreSQL 验证。mock 与 Testcontainers 验证自动化范围，真实 Docker/KVM、网关和断连恢复另做实机验收。现场操作看 [运行恢复手册](../../operations/runtime-scheduling-and-recovery.md) 与 [当前事实](../../current-state.md)，生产发布需明确授权。
