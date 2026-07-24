# Phase 9 TeamLab 组网全链路独立代码审查说明

## 1. 审查任务

本次任务是对 TeamLab 组网底座执行一次独立、严格、面向生产交付的全链路 code review。

审查目标不是确认代码能够通过某几个测试，也不是检查单个类的代码风格，而是判断当前实现能否作为商业化网络安全综合演练平台的长期基础设施。审查必须覆盖控制面、数据面、调度、Docker、Linux VM、Windows VM、镜像、恢复、观测、抓包和销毁之间的完整因果链。

质量标准：

- 生产可靠性和稳定性：并发、超时、断线、重启、部分失败、重复请求、旧任务延迟执行时均不能破坏当前环境。
- 适配性和复用性：不能依赖某个测试模板、固定发行版、固定网卡顺序、固定服务器数量或特定地址段才能工作。
- 架构一致性：公开 API、应用服务、统一 Runtime 控制面、Agent 和数据面职责清晰，不允许形成第二套事实、第二套队列或旁路生命周期。
- 代码质量：逻辑简洁、边界明确、状态可恢复、错误可观测，不以重试、延长等待、固定 sleep 或兼容分支掩盖设计问题。
- 商业底座水准：所有自动行为都有持久化状态、版本、所有者、错误分类和清理路径；不能依赖人工登录节点修复常态故障。

审查阶段默认只读。先交付 findings 和结论，不直接重构或修复；只有明确确认问题后再进入修改阶段。

## 2. 代码事实与工作树要求

当前分支为 `codex/phase-09-teamlab-networking`。Phase 9 存在大量未提交的新文件和修改文件，因此：

1. **当前工作树是唯一代码事实来源，不能只审查 `HEAD`、提交记录或已有文档。**
2. 不得 reset、checkout、clean、覆盖或删除当前修改。
3. 文档只能用于理解目标；代码、迁移、运行时状态机和 Agent 实现优先于文档描述。
4. 发现文档与代码不一致时，将其作为正式 finding 记录。
5. 使用 CodeGraph 追踪符号、调用链和影响范围；只在查找字面量、命令和配置时使用 `rg`。

审查前至少阅读：

- `docs/commercialization/phase-09-teamlab-networking-commercialization.md`
- `docs/commercialization/phase-09-runtime-readiness-and-acceptance-stabilization.md`
- `docs/commercialization/phase-09-vm-control-plane-stability-design.md`
- `docs/commercialization/phase-09-vm-control-plane-stability-implementation.md`
- `docs/commercialization/phase-06-runtime-scheduling-concurrency.md`
- `docs/commercialization/agent-capability-protocol.md`
- `docs/commercialization/teamlab-api-foundation-contract.md`
- `docs/commercialization/open-api-v1-guide.md`

## 3. 模块介绍

### 3.1 总体分层

```text
Open API / Admin API
        |
Topology save -> validate -> immutable release
        |
Runtime operation -> PostgreSQL deployment ticket
        |
Scheduling -> physical placement -> atomic reservation
        |
TeamLab orchestration -> shard deployment DAG
        |
Agent executor -> Docker/KVM/network/Fabric/capture
        |
bridge + router namespace + dnsmasq + firewall + L3 Fabric
        |
Docker / Managed VM / Opaque VM / Scenario artifact
        |
runtime signals + flow metadata + PCAP segments
        |
PostgreSQL facts + Redis wake-up/buffer + object storage
```

PostgreSQL 是持久化事实源。Redis 只能用于唤醒、低延迟 live state 和高频流量缓冲，不能成为不可恢复的唯一事实。

### 3.2 拓扑与发布

Topology v2 描述：

- 混合 RFC1918 地址池和每 runtime 子网前缀；
- Docker/VM 资产及多网卡；
- 显式交换机和路由器基础设施节点；
- 有方向的网络连接；
- 依赖 DAG、健康检查、Bootstrap Profile、观测策略；
- 发布时场景制品映射。

主要代码：

- `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyValidator.cs`
- `src/GZCTF/Modules/TeamLab/Application/Validation/`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyV2Compiler.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyV1Normalizer.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseCodec.cs`

发布版本必须不可变。旧 v1 只允许在读取边界通过一个 normalizer 转成当前执行模型，不允许保留两套运行实现。

### 3.3 Runtime、调度和分片

一个队伍环境对应一个 `TeamLabRuntime`，可拆成多个物理 shard。一个逻辑网段是最小放置单元，同一网段默认不能跨 WorkerNode；跨节点使用 L3 Fabric，不扩展大二层。

主要代码：

- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationApplicationService.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs`
- `src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs`
- `src/GZCTF/Modules/Runtime/Application/RuntimeSchedulingService.cs`
- `src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs`
- `src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs`
- `src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs`
- `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeSchedulingWorker.cs`
- `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeExecutionWorker.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`

Docker 和 KVM 能力必须独立判断。缺 KVM 的节点仍可承担 Docker shard；VM 只能放到满足 KVM、镜像和所请求 VM 能力的节点。

### 3.4 节点数据面

每个 shard 由 Agent 在本机应用期望状态：

- network bridge；
- router namespace；
- dnsmasq DHCP/DNS；
- nftables/iptables 隔离和允许矩阵；
- Docker network namespace 接入；
- VM TAP 接入；
- Worker 到中心 Hub 的 L3 Fabric 路由；
- 观测点和抓包进程。

主要代码：

- `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabRouterService.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabFirewallService.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricRouteStore.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabRuntimeGenerationStore.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs`

玩家只获得一个 WireGuard 配置，并且默认只允许直达入口网段。公网服务器只做 UDP 入口映射，不参与内部东西向路由。

### 3.5 Docker 生命周期

Docker 资产创建时先处于网络启动门控状态，待接口、路由、DNS 和连通性事实全部成立后再释放原始 Entrypoint/Cmd。不能因为镜像使用默认命令而绕过门控。

主要代码：

- `src/GZCTF.Agent/Services/DockerService.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs`
- `src/GZCTF/Services/Fleet/ImageDistributionService.cs`
- `src/GZCTF/Services/DockerImageRegistryService.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs`

### 3.6 VM 和镜像契约

当前已确认的目标架构不是平台在线运行 Packer。正确链路为：

```text
外部 CI/Image Factory 产出 qcow2
-> API 导入
-> 流式 SHA-256 校验
-> 内部 OCI Registry 不可变制品
-> Opaque 模板
-> 平台受控认证
-> Managed 模板
-> 能力过滤后的节点分发
-> runtime overlay + config drive + domain start
```

运行模式：

- `Managed`：通过平台受控认证，支持声明的 cloud-init/Cloudbase-init、Guest Supervisor、QGA、网络和观测能力。
- `Opaque`：不修改第三方镜像，只承诺宿主机网络、DHCP 或管理员声明的静态地址、端口健康和宿主机流量观测。
- `Scenario`：从已认证 Managed 模板发布时预制的不可变场景制品。

外部证据不能把 Opaque 提升为 Managed。Scenario baking 不能以未经认证的模板为输入。

主要代码：

- `src/GZCTF/Modules/Content/Api/OpenImagesController.cs`
- `src/GZCTF/Modules/Content/Application/ImageImportApplicationService.cs`
- `src/GZCTF/Modules/Content/Infrastructure/VmQcow2ImageImportExecutor.cs`
- `src/GZCTF/Modules/Content/Application/ImageTemplateCertificationService.cs`
- `src/GZCTF/Modules/Content/Infrastructure/VmImageCertificationProbeService.cs`
- `src/GZCTF/Modules/Content/Domain/VmPreparedArtifact.cs`
- `src/GZCTF/Models/Data/ImageTemplate.cs`
- `src/GZCTF/Services/Fleet/ImageDistributionService.cs`
- `src/GZCTF.Agent/Services/KvmService.cs`
- `src/GZCTF.Agent/Services/Vm/VmDomainBuilder.cs`
- `src/GZCTF.Agent/Services/Vm/VmRuntimeReadinessCoordinator.cs`
- `src/GZCTF.Agent/Services/GuestControl/`
- `src/GZCTF.GuestSupervisor/`

### 3.7 事件驱动就绪和恢复

资产就绪不得依靠固定 sleep、重复冷启动或无限延长 timeout。Agent 先持久化操作 receipt 和信号 journal，再向主站发布带 operation/runtime/generation/native identity 的单调信号。主站提交 PostgreSQL 后才推进 DAG；Redis 只负责唤醒。

主要代码：

- `src/GZCTF.Agent/Services/RuntimeSignals/`
- `src/GZCTF.Agent/Services/Vm/AgentOperationReceiptStore.cs`
- `src/GZCTF/Modules/Runtime/Application/RuntimeSignalService.cs`
- `src/GZCTF/Modules/Runtime/Domain/AgentRuntimeSignal.cs`
- `src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs`
- `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeRecoveryWorker.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeRecoveryPolicy.cs`

### 3.8 流量元数据、路径和 PCAP

默认观察点位于 bridge、router namespace、Fabric 和支持的 endpoint sensor。平台聚合流元数据，并可按 runtime 启动有限时、限大小、有限保留期的多节点 PCAP。

主要代码：

- `src/GZCTF.Agent/Services/Observation/`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficPathCorrelator.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabCaptureCoordinator.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/RedisTeamLabTrafficIngestor.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/PostgresTeamLabTrafficBatchWriter.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabTrafficPersistenceWorker.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabTrafficPathWorker.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabCaptureArtifactStore.cs`

## 4. 必须追踪的端到端链路

每条链路都必须从 API/worker 入口追踪到数据库事实、Agent 命令、宿主机资源和最终状态，不允许只读入口类。

### 4.1 Topology 保存、校验和发布

检查：

- schema v1/v2 是否只有一个执行模型；
- key、CIDR、host offset、多网卡、默认路由、连接方向和依赖 DAG 是否 fail closed；
- 发布快照是否完全不可变；
- 模板、Bootstrap Profile 和 Scenario artifact 引用是否固定到 digest/version；
- 相同输入是否产生确定性 hash 和计划。

### 4.2 Runtime 创建、排队和物理放置

检查：

- API 幂等键、operation、deployment ticket 和 runtime 是否一一对应；
- 同一 subject 的 Create/Reset/Destroy 是否正确串行化；
- placement 是否同时考虑 current、reserved 和正在构建/传输的资源；
- 多节点预留是否原子提交，部分失败是否完整回滚；
- 算法是否确定性，是否减少跨节点边，同时不牺牲能力和容量约束；
- Docker-only shard 是否不会被 KVM 缺失阻断。

### 4.3 Shard 网络、路由和 Fabric

检查：

- bridge、namespace、veth/TAP、dnsmasq、route、firewall 的创建顺序和幂等所有权；
- 同 runtime 多 shard 是否共享稳定 Fabric/WireGuard 语义；
- 未声明 connection 的网络是否真正不可达；
- 有向连接是否只允许发起方向，同时允许合法返回流量；
- reset 后 IP、MAC、DNS、路由和玩家入口语义是否稳定；
- delayed old-generation cleanup 是否可能删除新 generation 的共享资源；
- 中心 Hub、Worker 和公网 UDP gateway 的职责是否存在隐式旁路。

### 4.4 Docker 创建和网络门控

检查：

- 镜像已预分发时不重复 pull；缺失时是否明确进入镜像准备阶段；
- 默认 Entrypoint/Cmd 和显式 StartCommand 是否都被启动门控覆盖；
- network finalization 是否一次性验证接口、地址、路由、DNS 和真实解析；
- Agent/main 任一侧重启是否会重复启动业务命令；
- 容器创建成功、网络失败时是否精确补偿。

### 4.5 Linux/Windows VM 创建

检查：

- overlay、config drive、domain XML/virt-install 参数和 native UUID 是否持久化后再返回；
- 主网卡才获得默认路由，多网卡使用 MAC 匹配且不依赖枚举顺序；
- Managed/Opaque/Scenario 的能力门控是否严格对应实际请求；
- Windows/Linux、VirtIO/e1000e、DHCP/Preconfigured 是否没有硬编码发行版分支；
- Opaque 不得被要求拥有 Guest Supervisor，也不得伪造 guest readiness；
- Managed 必须绑定当前 image digest、认证能力和协议版本；
- VM 失败、Agent 重启和主站重启后是否能根据 domain identity 恢复。

### 4.6 镜像导入、认证、分发和删除

检查：

- qcow2 是否流式校验而非整文件进内存；
- OCI repository/tag/digest 是否不可变且不可路径注入；
- 相同 digest 是否安全复用，相同模板多引用是否不重复传输；
- 认证 probe 是否真正受控，失败不会污染模板状态；
- external-evidence 是否绝无 Managed 提升路径；
- 分发 claim、reference count、运行中实例保护和清理是否并发安全；
- 模板删除是否先写删除意图，再清理节点和 Registry，服务重启后可恢复；
- Registry、节点缓存、prepared artifact 和数据库元数据是否不会半删除。

### 4.7 WireGuard 玩家入口

检查：

- grant 创建、下载、撤销、过期和 reset generation 轮换；
- 私钥和一次性下载 token 是否不会进入日志、事件或普通 API projection；
- AllowedIPs 是否只包含入口网段；
- 公网 UDP 映射失败是否可观测、可回滚；
- 销毁后旧配置是否立即失效；
- 不得用 Worker 自身访问公网映射的 NAT hairpin 结果代替真实外部客户端验收。

### 4.8 流量元数据、路径和抓包

检查：

- A→B、B→C、C→B、B→A 四段能否分别保留方向和观察点；
- 去重 fingerprint 是否不会把不同 hop、不同 generation 或不同方向合并；
- cursor、Redis buffer、PostgreSQL batch 和服务重启是否不会丢流量或重复计数；
- path correlation 的置信度是否与证据类型一致，不能把纯网络推断标成进程级证据；
- capture 总预算是否正确分配到多节点 segment；
- stop、超时、大小上限、上传失败、过期和 destroy 是否全部清理 Agent 进程、本地文件和对象存储；
- PCAP manifest、segment digest 和下载包是否逐字节可验证。

### 4.9 Reset、Destroy 和恢复

检查：

- reset checkpoint 是否支持主站在任意持久化边界重启后继续；
- reset 目标 generation 是否只有一个 owner；
- destroy 是否可以安全等待或取消前序 create/reset；
- 资源不存在时是否幂等完成，identity 不匹配时是否 fail closed；
- reconciliation 是否不会与活动生命周期 owner 对抗；
- stale operation、offline node、部分 shard 清理失败是否能收敛；
- 容量 reservation 只能在真实资源清理完成后释放；
- 最终残留检查覆盖 container、domain、overlay、ISO、bridge、namespace、veth、route、firewall、WireGuard、capture、lease 和 distribution claim。

## 5. 生产级不变量

下面任一不变量被破坏，至少属于 P1：

1. 同一 runtime/generation/asset 的创建只能有一个有效 owner。
2. 旧 generation 的命令、信号和清理不能修改当前 generation。
3. 所有 Agent mutation 必须幂等，并绑定稳定 operation identity。
4. 数据库状态不能在宿主机资源尚未成立时提前标记成功。
5. 宿主机资源已经成立但响应丢失时，重放不能创建副本。
6. 多节点容量必须原子预留，不允许部分预留后继续部署。
7. Docker 和 KVM 能力独立；能力缺失只能排除相关 workload 类型。
8. 未连线网络不可达；连接方向和返回流量语义必须一致。
9. 网络地址、MAC、路由和 DNS 必须由发布拓扑决定，不能依赖设备枚举顺序。
10. Managed 能力只来源于当前 digest 的受控认证。
11. Opaque 模板不被在线改造，不伪造 guest 能力。
12. Redis、日志和事件都不能替代 PostgreSQL 当前事实。
13. readiness 必须来自观察事实，不得使用固定 sleep 或自动重启蒙混。
14. reset 不改变玩家可见网络语义；旧 grant 必须失效。
15. destroy 完成意味着所有节点和存储上的运行资源都已清理。
16. 失败必须保留 correlation、阶段、节点、资产和稳定错误码。
17. 秘密不得进入 config drive 明文、日志、事件、PCAP metadata 或 API projection。
18. 命令参数必须使用结构化参数或统一 shell escaping，禁止拼接未校验输入。

## 6. 并发与故障矩阵

必须逐项检查实现，而不是仅确认存在测试名称：

| 场景 | 必须保证 |
| --- | --- |
| 两个队伍同时部署 | 公平调度，不超卖，不争用同一地址/端口/Fabric lease |
| 同 runtime 重复 Create | 返回同一 operation/resource 或稳定冲突，不创建副本 |
| Create 中提交 Reset/Destroy | subject 顺序明确，不能提前释放容量 |
| 两个 shard 一成一败 | runtime 失败，成功侧精确补偿，事实和资源一致 |
| Agent 响应丢失 | receipt/inventory 证明已完成，重放不重复创建 |
| Agent 在网络应用中重启 | desired-state 重放收敛，不留下半套 firewall/route |
| 主站在 reset checkpoint 后重启 | 从持久化 checkpoint 恢复，不重建已完成阶段 |
| 旧 cleanup 延迟到新 generation | generation fence 阻止删除当前资源 |
| Registry 或对象存储中断 | operation 保留可恢复状态，不污染 Ready 模板 |
| Redis 不可用 | 核心状态正确，允许延迟但不能丢失事实 |
| 节点离线后恢复 | 不误判资源丢失，不自动重建有状态 VM |
| capture 上传中断 | segment 可恢复或明确失败，destroy 可完全清理 |
| API/Agent 协议不兼容 | 调度前拒绝并说明缺失 feature，不使用硬编码版本阈值 |

## 7. 适配性和可复用性审查

重点寻找以下反模式：

- 对 Ubuntu、Windows Server 具体版本写 C# 分支，而不是能力/配方契约；
- 固定假设网卡名为 `eth0`、`ens3` 或固定 libvirt 顺序；
- 固定 `10.x` 地址、单一 `/24`、固定 DNS 或固定 gateway；
- 假设 runtime 只有一个 shard 或一个 WorkerNode；
- 假设 Docker 与 KVM 总在同一节点同时可用；
- 通过镜像名称、模板 ID、节点名称判断行为；
- 只有测试环境能访问的 SSH、数据库或宿主机旁路；
- 业务逻辑直接拼 shell，未复用 Agent 命令边界；
- 为单个模板增加 timeout、重启或特殊认证分支；
- 为旧实现保留长期双轨、重复 DTO、重复队列或重复状态表；
- 在捕获、流量或日志热路径产生无界内存、无界任务或高基数指标。

## 8. 代码简洁性和维护性审查

必须指出：

- 超大类承担多个所有权边界；
- 同一状态转换在多处手写；
- 主站和 Agent 对同一协议有重复但不一致的校验；
- catch-all 异常吞掉稳定错误码或 cancellation；
- `Task.Run`、fire-and-forget、未受控并发或共享 `DbContext`；
- 无上限集合、channel、spool、日志 detail 或批量查询；
- N+1、深 OFFSET、逐资产/逐节点重复查询；
- 先改状态后执行外部副作用但无补偿/恢复；
- 为测试方便暴露生产旁路接口；
- 注释、命名和实际语义不一致；
- 已删除架构仍有活动服务注册、API、迁移字段或能力广告。

发现复杂实现时，不要只建议“抽象一层”。必须先判断该复杂度是否由真实生产不变量要求；无必要的状态、重试、兼容分支应直接建议删除。

## 9. 安全审查边界

至少覆盖：

- Open API scope、资源 grant 和 runtime/topology 所有权；
- 主站到 Agent 的认证、endpoint 分区和 correlation 传播；
- Guest enrollment、mTLS、证书/boot epoch/generation 绑定；
- WireGuard 私钥、下载 token、Bootstrap secret 和 DSRM 密码；
- OCI repository、上传文件名、tar/qcow2、路径穿越和 digest；
- shell/PowerShell/virt-install/docker 参数注入；
- PCAP 下载授权、对象 key、保留期和跨队伍数据泄露；
- namespace/firewall 失效时的 fail-closed 行为；
- 日志、事件、metrics label 和错误正文的脱敏。

## 10. 当前验证事实与未完成边界

这些事实用于帮助定位风险，不能替代 code review：

- 本地 Release solution build 已通过，单元测试最近一次为 `614/614`。
- 本地 Docker Desktop 不可用，因此完整 Testcontainers 集成测试未在当前开发机执行。
- qcow2 导入、SHA-256、OCI、两节点预分发和删除清理已在 `10.0.7.118/125` 真实验证。
- 四网段、双物理节点、两个 Docker、Managed Linux 和 Opaque Windows 的混合 runtime 已两次到达 `Ready` 并成功销毁。
- 当前 Windows 仅为 Opaque 模板，不是 Managed AD 制品；不能据此宣称 AD 验收完成。
- 旧 Windows Opaque 模板到 RDP Ready 约需 6 分钟，不满足最终性能目标。
- Worker 不能作为公网 WireGuard 映射的 NAT hairpin 客户端；最终玩家入口必须由真正外部客户端验收。
- 已观察到多网段流元数据和多观察点 capture，但 A→B→C→B→A 的最终跨资产 path/PCAP 闭环仍未完成。
- 通过 SSH/数据库定位容器并注入流量不是正式 API 验收路径，相关临时脚本已撤销，不得恢复为产品或最终验收方案。

因此，审查者不能将 Phase 9 标记为已完成，也不能因现有运行证据而降低对恢复、并发、Windows/AD、WireGuard 和完整流量路径的审查强度。

## 11. 审查执行顺序

建议按以下顺序执行，前一阶段形成的事实图作为后一阶段输入：

1. 建立模块、数据库实体、后台 worker、API 和 Agent endpoint 的结构图。
2. 追踪 Topology save/validate/publish，确认唯一执行模型。
3. 追踪 Runtime create 到 ticket、调度、预留和 physical placement。
4. 追踪 shard network/Fabric 到 Agent 宿主机命令和 generation ownership。
5. 分别追踪 Docker、Managed VM、Opaque VM 和 Scenario 生命周期。
6. 追踪镜像导入、认证、分发、引用和删除。
7. 追踪 signal、reconciliation、reset 和 destroy。
8. 追踪 WireGuard grant、公网映射和撤销。
9. 追踪 flow、path、capture、上传、下载、过期和清理。
10. 检查迁移、索引、唯一约束、外键和 hot-path 查询计划。
11. 对照并发/故障矩阵进行第二遍反向审查。
12. 汇总 findings、覆盖盲区和生产准入结论。

不要一开始运行全量测试来代替阅读代码。只有在 finding 需要验证时运行最小、针对性的静态检查或测试；完整验证建议留到修复批次完成后统一执行。

## 12. Findings 输出规范

审查报告写入：

`docs/commercialization/reviews/phase-09-teamlab-networking-independent-review.md`

报告必须 findings first，按严重性排序：

- `P0`：可造成跨队伍访问、凭据/PCAP 泄露、大范围资源破坏或不可恢复数据损坏。
- `P1`：可造成生产环境错误组网、当前 generation 被旧任务破坏、资源超卖、环境无法恢复、销毁谎报成功或核心链路不具备普适性。
- `P2`：有限条件下的可靠性、观测、性能、维护性或 API 契约问题，会显著增加商业运行风险。
- `P3`：局部质量问题，不影响当前正确性，但应纳入后续治理。

每个 finding 必须包含：

1. 简短标题；
2. 严重性；
3. 精确文件和行号；
4. 所属端到端链路；
5. 触发条件；
6. 实际影响；
7. 被破坏的不变量；
8. 根因，而不是表面异常；
9. 最小且架构正确的修复方向；
10. 修复后的验证方式。

禁止以下低价值 finding：

- 只描述个人风格偏好；
- 没有可达触发路径；
- 只说“可能有竞态”但不给出两个 owner 和时序；
- 只建议增加重试、sleep、timeout 或日志；
- 只因为代码长就建议拆分类；
- 重复报告同一根因在不同文件的表现。

报告末尾必须包含：

- 审查覆盖矩阵：上述 4.1 至 4.9 每项为 `Reviewed / Partial / Not Reviewed`；
- 架构偏离清单；
- 迁移和数据一致性结论；
- 并发与恢复结论；
- 安全边界结论；
- 性能和容量风险；
- 缺失测试与真实环境验收项；
- 被检查但确认不是问题的高风险点；
- 最终生产准入结论。

## 13. 生产准入结论

只能使用以下结论之一：

- `BLOCKED`：存在 P0/P1，或关键链路未完成审查。
- `CONDITIONAL`：无 P0/P1，但存在必须在正式发布前关闭的 P2 或真实环境验收缺口。
- `APPROVED`：无未关闭 P0/P1/P2，4.1 至 4.9 全部 Reviewed，关键迁移/集成/真实环境证据完整。

当前已有验收边界决定了本轮审查在未获得外部 WireGuard、Managed Windows/AD 和完整跨资产流量证据前，最高只能给出 `CONDITIONAL`。如果代码审查发现任一 P0/P1，则必须给出 `BLOCKED`。

审查者的价值在于找出会在真实并发、故障和异构环境中破坏系统的根因，而不是证明现有实现“看起来能运行”。
