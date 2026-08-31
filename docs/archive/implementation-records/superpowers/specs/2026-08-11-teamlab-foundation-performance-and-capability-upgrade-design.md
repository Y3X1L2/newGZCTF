# TeamLab 商业组网底座性能与能力升级设计

日期：2026-08-11

状态：待负责人书面复核后进入实施计划

依据：

- `docs/platform-commercialization-master-plan.md`
- `docs/commercialization/module-boundary-map.md`
- `docs/commercialization/external-api-standard.md`
- `docs/commercialization/teamlab-networking-market-research-20260811.md`
- `docs/development/current-state.md`

## 1. 目标

本设计将 TeamLab 从已经能够运行混合组网场景的功能模块，升级为可以被比赛、培训和外部系统长期复用的商业组网底座。升级分为两条可并行开发的工作流：

1. 高性能底座：重构节点网络、VM 生命周期、制品分发和批量执行主路径，使大规模环境的启动、暂停、恢复、销毁和故障收敛具备稳定上限。
2. 功能闭环与升级：补齐现有能力的真实验收和资源生命周期，重点建设设备包、链路策略、虚实连接、资源池、批量运维和完整外部控制面。

两条工作流共同遵守以下原则：

- 现有 topology、release、plan、runtime、rollout、operation、统一部署队列、容量账本、权限、审计和事件模型继续作为唯一业务事实，不建立平行实现。
- 已经实现的功能只补缺失验收或修复真实缺口，不因技术改造重新实现同一产品流程。
- 服务端不制作或改造模板。外部制品流水线负责系统安装、驱动、软件、泛化、质量认证和不可变制品发布。
- 平台只登记制品、校验摘要/签名、记录能力、分发、调度、运行和回收。
- 正确性来自事务、唯一身份、运行代次、期望状态和真实 inventory，不来自增加等待、重复探测或无界重试。
- 技术路线按成熟架构直接确定，性能数据用于验收和容量规划，不作为拖延结构性改造的理由。

## 2. 当前基线与升级重点

### 2.1 已有主体能力

以下能力已有代码和部分现场证据，本轮不重建：

| 能力面 | 当前主体能力 | 本轮处理方式 |
| --- | --- | --- |
| 场景设计 | Docker、Linux VM、Windows VM、交换机、路由器、网段、接口、依赖和布局。 | 只补真实交互、复杂拓扑和契约一致性验收。 |
| 版本管理 | 草稿校验、不可变发布版本、计划和发布版本复用。 | 保持身份与版本语义，补归档和依赖回收闭环。 |
| 运行控制 | 创建、试运行、重置、暂停、恢复、销毁、运行代次和多节点分片。 | 补批量比赛控制、失败恢复和资源回收。 |
| 统一运行底座 | DeploymentQueueTicket、容量预留、节点能力、镜像分发和 Agent 执行。 | 保留唯一队列和容量账本，替换低效执行主路径。 |
| 外部控制面 | API token、scope、resource grant、operation、幂等、rollout、事件游标和 webhook 基础。 | 补完整外部端到端验收及新增资源 API。 |
| 运维访问 | Docker 终端、Linux SSH、Windows RDP、权限与会话审计基础。 | 补模板账号配置、会话回收、失败提示和规模验收。 |
| 观测 | 运行事件、日志、流量元数据、按需 PCAP 和部分筛选。 | 补统一关联、完整筛选、链路策略和协议事件。 |

### 2.2 已确认必须修复的缺口

1. 当前节点网络通过大量 shell、Linux bridge、router namespace 和每网段 dnsmasq 执行，大规模运行会产生进程和命令风暴。
2. VM 创建与生命周期依赖 `qemu-img`、`virt-install`、`virsh` 子进程及文本解析，事件链不够直接。
3. 当前发布准备引用会让节点模板缓存继续保留；试运行销毁后即使没有其他使用者，缓存仍可能被发布引用和固定保留窗口阻止回收。
4. 试运行、比赛准备、正式比赛和发布版本对制品缓存的所有权没有完全按真实使用目的区分。
5. 大型比赛的赛前准备、批量启动、暂停、恢复、停止、销毁和比赛结束清理仍需形成统一操作面。
6. 设备包、链路故障、现场连接器和稀缺物理资源尚未形成稳定产品对象。
7. 部分第 1 至第 3 层能力存在代码但缺少 API-token-only、多队并发、中断恢复、真实 SSH/RDP、流量路径和销毁残留签收。

## 3. 两位协作者的并行边界

### 3.1 工作流 A：高性能执行面

工作流 A 负责：

- OVN/OVS 网络 Provider、节点数据面和迁移切换。
- 原生 libvirt VM Provider、生命周期事件和 inventory。
- Docker/libvirt/OVN 节点批量执行与有界并发。
- ImageDistribution 内部引用实现、节点缓存物理回收和制品预热效率。
- Agent 协议、节点执行能力、性能基准和执行面故障测试。

工作流 A 不修改：

- `/api/open/v1` 公开资源和权限语义。
- TeamLab 拓扑作者模型、比赛绑定和前端页面。
- Content 模板制作或外部制品流水线。
- 计分、比赛参与、培训和 Penetration 领域事实。

### 3.2 工作流 B：功能控制面

工作流 B 负责：

- TeamLab Domain、Application、公开 Contracts、API 和权限。
- 试运行、比赛、rollout、资源池和缓存用途引用的业务生命周期。
- 设备包、链路策略、现场连接器及对应产品流程。
- 管理端和外部 API 的状态、进度、错误、运维和观测体验。
- 数据库迁移、OpenAPI、前端生成代码、功能测试和产品文档。

工作流 B 不修改：

- Agent 内部网络命令、libvirt、OVS/OVN 和 Docker 执行实现。
- ImageDistribution 的物理下载、缓存目录和节点删除细节。
- 主站之外的模板制作、软件安装和镜像认证流程。

### 3.3 共享契约冻结

并行开发前先完成一个小型共享契约提交，之后两位协作者都不得随意修改：

1. `TeamLabExecutionPlanV2`：运行 ID、generation、节点分片、资产、端口、逻辑网络、路由、策略、现场连接器挂载、观测点和制品 digest 的不可变执行输入。
2. `ITeamLabNetworkControlProvider`：一次性提交、探测和清理全局逻辑网络期望状态。
3. `ITeamLabNodeAttachmentProvider`：在节点本地连接/断开容器 veth、VM TAP 和获批现场连接器，并上报 inventory。
4. `ITeamLabVirtualizationProvider`：define/start、pause、resume、destroy、inventory 和事件订阅接口。
5. `ITeamLabArtifactDistribution`：按用途取得和释放节点制品引用，不暴露存储路径。
6. `TeamLabExecutionEventV2`：runtime、generation、shard、asset、stage、outcome、safe detail 和发生时间。

共享契约只表达业务需要和节点事实，不包含 OVN 表结构、libvirt XML、shell、Docker 原始对象或前端 DTO。

### 3.4 文件所有权与合并规则

| 范围 | 唯一修改方 |
| --- | --- |
| `src/GZCTF.Agent` 的 TeamLab、KVM、OVS/OVN 执行代码 | 工作流 A |
| `src/GZCTF/Services/Fleet/ImageDistributionService.cs` 及节点缓存 Worker | 工作流 A |
| TeamLab 执行 Provider 的 Infrastructure 实现 | 工作流 A |
| `src/GZCTF/Modules/TeamLab/Domain`、公开 Contracts、Application、Api | 工作流 B |
| `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab` | 工作流 B |
| EF migration、OpenAPI、生成 API、功能说明 | 工作流 B |
| 共享 execution port/DTO | 先冻结；后续变更必须单独小提交并经双方确认 |
| DI 组合根和最终端到端脚本 | 工作流 B 在两条分支合并后统一接线 |

两位协作者从同一共享契约提交创建独立 worktree 和分支。不得在各自分支复制另一方实现、建立临时兼容层或修改同一 migration。合并顺序为：共享契约、工作流 A、工作流 B、集成修复与统一验收。

## 4. 共同目标架构

```text
Browser / External Platform
  -> TeamLab Public Contracts and Application
  -> topology/release/rollout/runtime/operation facts
  -> Runtime queue and capacity ledger
  -> global network intent + immutable TeamLabExecutionPlanV2 per node
  -> infrastructure and Agent execution providers
       - OVN central control provider
       - OVS node attachment provider
       - Docker Engine provider
       - native libvirt provider
       - artifact cache provider
       - observation provider
  -> Docker / QEMU / OVS data plane
```

主站负责权限、版本、计划、容量、调度、operation、审计和期望状态。Agent 负责本机可重复执行的已校验操作。OVS/QEMU/Docker 承担实际数据面，主站不进入运行流量路径。

# 第一部分：高性能底座改进

## 5. 网络数据面：OVN/OVS

### 5.1 决策

TeamLab 新运行主路径使用 OVN/OVS，替代当前逐运行环境构建 Linux bridge、router namespace、dnsmasq 和大量 shell 规则的方式。

生产环境由三成员 OVN Northbound/Southbound 数据库集群承载网络期望状态，每个执行节点运行 `openvswitch` 与 `ovn-controller`。主站通过 Infrastructure port 向 Northbound 提交全局网络意图；Agent 只负责本机容器 veth、VM TAP 和现场连接器接入 OVS，并回报 chassis、端口和本地 attachment 事实。开发或单机验收可使用单节点 OVN Central，但不改变接口和资源身份。

映射关系固定为：

| TeamLab 对象 | OVN/OVS 对象 |
| --- | --- |
| 网段/交换机 | logical switch |
| 路由器 | logical router |
| Docker/VM 网卡 | logical switch port |
| DHCP 地址和固定租约 | OVN DHCP options + logical port 地址 |
| DNS | OVN DNS records |
| 网络访问控制 | logical router/switch ACL |
| 地址转换与入口 | logical router NAT 或受控入口 Provider |
| 跨节点 | Geneve 隧道与 chassis binding |
| 流量元数据 | OVS/IPFIX 或等价受控导出 |
| 按需抓包 | 明确端口/网段 mirror + 有界 capture session |

### 5.2 执行方式

- 一个 runtime/generation 的网络变更在一个 Northbound 事务中提交。
- 通过配置版本和 chassis 收敛事实确认网络生效，不通过固定 sleep 推测。
- 跨节点隧道是节点常驻基础设施，不为每个队伍重复建立 WireGuard Fabric 状态机。
- 玩家入口与节点间数据面分离。WireGuard 继续负责玩家授权入口；节点 underlay 的加密由部署环境统一提供。
- OVS integration bridge 使用 fail-secure，控制面短时中断不破坏已下发流量转发。
- 统一 MTU，自动计算隧道开销，拒绝会造成静默分片的配置。

### 5.3 切换策略

不长期保留两套数据面。维护窗口前完成隔离节点验证；切换时先排空旧 TeamLab runtime，再启用 OVN Provider 创建新 runtime。旧 bridge/router namespace/dnsmasq 主路径在切换验收后删除。

## 6. VM 执行：原生 libvirt

### 6.1 决策

Agent 使用原生 libvirt API 定义、启动、暂停、恢复、查询和删除 VM，并订阅 domain 生命周期事件。`virt-install` 和 `virsh` 文本编排退出主路径。

### 6.2 稳定身份

- domain UUID、runtime ID、generation 和 asset key 形成稳定身份。
- 每次 destructive 操作同时核对 domain metadata、generation 旁车和数据库期望身份。
- UEFI/NVRAM、managed-save、overlay 和 domain 按同一资源清单销毁。
- 旧 generation 不能修改或删除新 generation。
- 多网卡、UEFI、Linux、Windows 和兼容设备模式由模板能力清单决定，不在 C# 中按 OS 版本增加分支。

### 6.3 启动和就绪

- 平台就绪分为 overlay 创建、domain defined、QEMU running、guest ready、service healthy。
- QEMU running 由 libvirt 事件确认。
- 已声明来宾信号能力的模板使用信号；未声明时只执行场景显式配置的端口健康检查。
- 不用 ICMP、固定等待或循环次数猜测所有模板已经就绪。

## 7. 制品分发、缓存与自动回收

### 7.1 责任边界

- 外部流水线制作并认证 Docker/VM 制品。
- Content 模板库保存主制品和元数据。
- TeamLab 发布版本只保存模板 ID 和发布时 digest，不持有文件。
- Fleet/Agent 负责按 digest 分发、本地缓存、校验和删除。

### 7.2 节点缓存用途引用

缓存引用改为明确用途，不再以“发布版本存在”代表永久占用：

| 引用 | 建立时机 | 释放时机 |
| --- | --- | --- |
| Runtime | 任意试运行、比赛队伍或外部 runtime 开始准备，记录用途 subtype | runtime 销毁并完成 inventory 核对 |
| CompetitionPreparation | 比赛赛前准备 | 比赛取消、解绑，或结束并完成所有队伍销毁 |
| Rollout | 外部 rollout 准备 | rollout drain 完成并归档 |
| ArtifactVerification | 外部认证结果登记期间需要节点核对制品字节 | 字节核对结束；不启动来宾做功能认证 |

发布版本本身只保留依赖清单，不无限持有节点缓存。现有固定 24 小时发布保留窗口从正确性主路径移除。

### 7.3 删除流程

1. 业务生命周期先释放用途引用。
2. 数据库在节点、模板、digest 维度确认引用计数为零，且不存在活动传输、容器、VM overlay backing 和未终结 runtime。
3. 记录 `CleanupPending`，由 Agent 删除节点 Docker 镜像或 VM base cache。
4. Agent 返回删除后的 inventory；数据库确认物理资源不存在后记录 `Cleaned`。
5. 删除失败保留真实原因和待回收状态。恢复 Worker 根据目标状态和 inventory 继续未完成步骤，不新建身份、不重复释放引用。

模板库主制品不会被 runtime 清理删除。多个比赛或试运行共享同一 digest 时，最后一个有效引用释放后才删除节点缓存。

### 7.4 零传输启动

- 赛前准备根据发布版本、预计队伍数和可调度节点计算制品副本。
- 正式比赛开放前，所有目标节点制品必须达到 Ready。
- runtime 创建正常路径传输量为零；缺失制品表示准备未完成，不占用 VM/Docker 启动通道。
- 调度优先选择已经具备完整制品集合的节点。
- VM 使用本地只读 base image 和每实例 qcow2 overlay，不复制完整系统盘。

## 8. 拓扑预编译与节点批量执行

- 发布时将逻辑拓扑编译为稳定的 release execution template。
- 创建 runtime 时只分配地址、运行身份、节点放置和制品引用，生成每节点 `TeamLabExecutionPlanV2`。
- 同一节点的网络、资产、观测和清理请求使用一个分片计划，而不是主站逐资产往返。
- 不同节点并行；单节点内部按网络、Docker、VM、观测的独立并发上限执行。
- 依赖图继续使用现有就绪批次，不建立第二套工作流引擎。
- 计划 digest 相同且 inventory 已符合时，Agent 直接返回已收敛事实。

## 9. 事件驱动与高并发控制

### 9.1 状态推进

- Docker Engine event、libvirt callback、OVN 配置版本、Agent inventory 和分发事件推动运行状态。
- PostgreSQL 保存期望状态、operation、ticket、runtime 和最终事实。
- Redis 或进程内通知只负责唤醒，不是事实来源。
- 低频 reconciliation 只处理进程中断和通知丢失，不作为正常运行轮询器。

### 9.2 并发模型

- `DeploymentQueueTicket` 保持唯一执行队列。
- 容量预留在 admission 事务中完成，包含 CPU、内存、Docker slot、VM slot、临时磁盘和制品空间。
- 网络事务、Docker、VM、制品传输、抓包和销毁分别限流，节点心跳上报各自并发能力。
- 同一 runtime/generation 只有一个生命周期目标；pause、resume、reset 和 destroy 在 admission 阶段处理冲突。
- 批量比赛操作由 rollout 协调，不循环调用无关联的单队前端接口。

### 9.3 故障原则

- 失败必须标明 validation、capacity、artifact、network、compute、guest、service、observation 或 cleanup 阶段。
- 自动恢复只能继续已提交的相同期望状态，不创建新 runtime、generation 或资源身份。
- 需要改变业务目标时必须由显式、可审计命令触发。
- 控制面短时故障不影响已运行 OVS、Docker 和 VM。

## 10. 性能验收目标

以下为目标硬件上的签收门槛，不是当前完成声明：

| 指标 | 目标 |
| --- | --- |
| 300 个 create ticket 批量 planning/claim | 5 秒内完成 |
| 已准备运行的启动期制品传输 | 0 字节 |
| 4 网段、20 端口网络收敛 p95 | 不超过 2 秒 |
| 8 网段、50 端口网络收敛 p95 | 不超过 5 秒 |
| Docker 已预热 create 到 healthy p95 | 不超过 5 秒 |
| VM overlay + define + QEMU running p95 | 不超过 5 秒，不含来宾开机 |
| 20 资产暂停或恢复 p95 | 不超过 10 秒 |
| 销毁后入口关闭 | 2 秒内 |
| 20 资产计算资源消失 p95 | 不超过 30 秒 |
| 20 资产全部网络、访问和引用核对 p95 | 不超过 60 秒 |
| 并发与中断后重复实例、容量超卖、跨 generation 误删 | 0 |

规模验收使用 S、M、L 三档：4 资产/2 网段、20 资产/4 网段、50 资产/8 网段，覆盖单队、10 队、50 队、100 队和控制面 300 个并发创建请求。

## 11. 高性能实施阶段

1. HP-A0：冻结执行契约、节点能力和资源身份。
2. HP-A1：重构用途引用、自动回收和零传输启动。
3. HP-A2：实现原生 libvirt Provider 并在排空旧 runtime 后切换。
4. HP-A3：实现 OVN/OVS Provider 并在维护窗口切换数据面。
5. HP-A4：节点批量计划、事件唤醒和分类并发限流。
6. HP-A5：执行 S/M/L、多队并发、故障中断和完整残留验收。

每个阶段必须删除被替代的旧主路径，不永久保留命令行/新 Provider 双轨。

# 第二部分：底座功能闭环与升级

## 12. 现有功能闭环原则

先建立一份可执行能力矩阵，每项只有三种结论：

- 已验收：代码、真实页面、真实 API 和真实基础设施均有证据。
- 待验收：代码已经存在，只补缺少的并发、中断、权限、页面或真实基础设施证据。
- 待建设：当前没有稳定领域对象或接口，按本设计新增。

不得把“缺少验收”转成重新实现。第 1 至第 3 层以补验收和修复事实缺口为主，第 4 层是主要新增工作。

## 13. 资源生命周期与比赛控制

### 13.1 生命周期

统一产品状态覆盖：

```text
Draft -> Validated -> Released
Released -> Preparing -> Prepared
Prepared -> Deploying -> Ready
Ready <-> Paused
Ready/Paused/Failed -> Resetting -> Deploying
Any active -> Destroying -> CleanupPending -> Destroyed
Release/rollout after drain -> Archived
```

每个状态必须有入口、前置条件、进度、终态、失败分类和可执行恢复动作。前端、公开 API、operation、部署票据和 runtime 投影使用同一语义。

### 13.2 批量比赛控制

- 比赛绑定发布版本后生成准备计划，不为每队重新编辑拓扑。
- 支持赛前准备、分批启动、全量启动、暂停、恢复、关闭入口、重建失败队伍、批量销毁和比赛结束清理。
- 批量操作返回一个 rollout operation，并提供目标级进度和失败原因。
- 默认 all-ready 后开放入口；部分失败时成功环境保持隔离，不自动开放。
- 比赛结束先关闭入口和运维会话，再销毁运行环境、释放容量和缓存引用，最后归档。

## 14. 商业资源池

资源池统一投影但不复制各模块主事实：

| 资源 | 需要呈现的事实 |
| --- | --- |
| 计算节点 | Docker/KVM/OVN 能力、容量、预留、健康、维护、执行并发和最后 inventory |
| 模板 | 类型、版本、digest、外部认证能力、大小和 Registry 主制品状态 |
| 节点缓存 | 模板、digest、节点、状态、用途引用、大小、最后使用和回收状态 |
| 现场连接器 | 类型、授权范围、健康、容量、占用 runtime、租约和回收状态 |
| 设备包 | 版本、制品引用、参数 schema、端口、资源、健康和事件能力 |

作者只选择模板、设备包和已授权连接器，不填写宿主机、缓存路径、OVN、libvirt 或 Registry 内部参数。

## 15. 第四层能力

### 15.1 设备包目录

设备包用于工业协议仿真、PLC、蜜罐、网络服务和工艺设备。平台不实现协议业务，只登记：

- 名称、版本、不可变 OCI/VM 制品引用。
- 支持的资产类型和操作系统能力。
- CPU、内存、存储和端口要求。
- 作者可配置的公开参数 schema、默认值和校验规则。
- TCP/HTTP/协议级健康声明。
- 可上报的脱敏协议事件类型。

设备包参数在发布时冻结，敏感平台凭据不进入场景 JSON。新增设备包不要求修改 TeamLab Controller。

### 15.2 链路和网络策略

首批稳定能力：

- VLAN/逻辑隔离、允许/拒绝规则和方向。
- SNAT/DNAT、入口端口和受控外部出口。
- 带宽、时延、抖动、丢包、重复包和断链。
- 策略启用、定时恢复、手工恢复和操作审计。
- 端口/网段镜像、流量元数据和按需 PCAP。

策略属于 network/link 对象，由 OVN/OVS Provider 执行，不写入来宾脚本。作者界面以常见任务提供简单配置，高级字段按需展开。

### 15.3 虚实连接

现场连接器是一段经过管理员登记和授权的真实资源，不是任意内网转发：

- 类型包括受管物理网卡、VLAN、网段、串口、USB/设备网关和专用外部网络。
- 一个独占连接器同一时间只属于一个 runtime；共享连接器必须显式声明安全共享能力。
- 场景只引用 connector ID，不保存真实地址、设备密码或宿主机路径。
- runtime 创建时取得租约，销毁或失败清理时释放；节点失联时保持占用事实，不能直接分配给第二个 runtime。
- 所有连接、断开、故障和回收进入审计。

### 15.4 场景库与复用

- 场景库保存拓扑、模板/设备包版本引用、资源需求、使用说明和已验证发布版本。
- 支持克隆、版本差异、归档、依赖清单和导入/导出逻辑定义。
- 导出不包含受版权保护的镜像、节点地址、平台密钥或运维密码。
- 场景库不保存运行中 VM 磁盘作为平台制作的新模板；需要预装系统状态时由外部流水线产生新模板版本后重新登记。

## 16. 运维与观测升级

### 16.1 运维

- 管理员可访问全部；比赛所有者只访问自己的比赛；授权运维人员只访问明确资源；选手无运维权限。
- Docker 终端、Linux SSH、Windows RDP 使用统一会话模型。
- 模板库配置静态运维账号或密钥引用，场景只显示“已配置/未配置”，不读取秘密。
- 会话创建、连接、超时、主动关闭、权限撤销、runtime 重置和销毁都必须回收通道并记录审计。
- 支持受控文件上传/下载时必须复用同一授权和审计，不向浏览器暴露节点地址或转发端口。

### 16.2 观测

- 日志、事件、流量、路径、PCAP、协议事件和运维会话统一关联 runtime、generation、shard、asset 和时间范围。
- 流量筛选支持地址、端口、常用协议、任意协议号、方向、网段、资产、可信度和时间。
- 端到端路径只基于可证实的分段流量关联，不能把推测关系标成确定事实。
- PCAP 有范围、时长、大小、保留和下载审计限制。
- 协议事件由设备包可选上报，底座不解析厂商业务语义。

## 17. 完整外部控制面

外部 API 继续使用 `/api/open/v1`、API token、scope、resource grant、幂等键、operation、cursor 和 webhook。

目标资源面：

| 资源 | 外部能力 |
| --- | --- |
| control scopes | 创建、授权、读取、归档 |
| topologies | 创建、更新、校验、克隆、读取差异 |
| releases | 发布、查询依赖和归档 |
| preparations | 创建用途准备、查询节点级安全投影、取消和释放 |
| rollouts | 目标快照、准备、开放、暂停、恢复、重建、drain 和归档 |
| runtimes | 创建、读取、暂停、恢复、重置和销毁 |
| resource pools | 查询可用资源、容量、准备和占用，不暴露执行面地址 |
| device packages | 查询版本、参数 schema 和能力 |
| connectors | 查询获授权资源、申请、占用和释放 |
| link policies | 应用、查询、恢复和审计 |
| remote sessions | 查询可用性、创建、关闭和审计 |
| observations | 事件、日志、流量、路径、PCAP 和协议事件 |

每个长操作只产生一个 ApiOperation；部署工作继续关联唯一 DeploymentQueueTicket。外部调用方不访问 Agent、Docker、libvirt、OVN 或数据库实体。

## 18. 使用者体验

- 场景首页围绕“设计、校验、发布、准备、试运行、比赛使用”组织，不暴露内部实现状态。
- 画布显式显示网段、地址池、入口、交换机、路由器、区域、资产数量和物理连接。
- 自动布局按入口、网络层级和路由关系呈中心辐射分布，布局变化不修改发布版本业务 revision。
- 节点与连线设置按基础、网络、健康、观测四组渐进展开；不恢复镜像 digest、启动命令、服务注入和发布时预制等已删除作者字段。
- 每个非直观字段提供中文“作用、何时使用、如何操作、结果”说明。
- 准备、部署、暂停、恢复、销毁和缓存回收显示真实阶段、节点进度和可执行恢复动作。
- 所有列表具备搜索、筛选、分页、空态、加载态、错误态和稳定刷新。

## 19. 权限、安全与配额

- 超管/管理员拥有全局管理能力。
- 比赛所有者拥有其比赛绑定、rollout、runtime、运维和观测能力。
- 授权运维人员只得到明确的 metadata、remote-session 或 lifecycle 权限。
- 选手只能访问自己队伍的公开入口和必要状态。
- scope/resource grant 同时约束外部 API；隐藏资源统一返回 404。
- 节点、连接器、缓存和运行容量进入统一配额与预留，避免 TeamLab 挤占普通 CTF、培训和 AWDP 未统计资源。
- 现场连接器、运维凭据、Registry 凭据和节点内部地址不能进入场景、日志或外部响应。

## 20. 功能验收

### 20.1 已有能力补验收

- API-token-only 完成 topology、validate、release、prepare、rollout、runtime、pause/resume、remote、traffic、capture、destroy 和 archive。
- Docker、Linux VM、Windows VM、多网段、跨节点和三队正式比赛。
- 管理员、比赛所有者、授权运维人员和选手四类权限。
- 重复请求、并发请求、客户端断连、Worker 重启、节点短时离线和部分清理恢复。
- SSH、RDP、容器终端和会话回收。
- 流量筛选、端到端路径、PCAP、日志和事件关联。

### 20.2 新增能力验收

- 一个外部设备包无需修改 TeamLab Controller 即可登记、选择、发布、运行和上报健康/事件。
- 在明确链路上应用延迟、丢包、限速和断链，随后恢复，流量和审计可解释。
- 一个现场连接器只能被一个 runtime 独占，销毁后可重新分配；节点失联期间不能误分配。
- 试运行销毁后无其他引用的节点缓存自动删除。
- 正式比赛结束、全部队伍销毁后比赛引用和节点缓存自动回收。
- 共享模板被另一比赛使用时不能误删。
- 资源池显示的容量、占用、缓存和 connector 状态与节点 inventory 一致。

## 21. 功能实施阶段

1. FC-B0：冻结公共资源、execution contract 和权限边界。
2. FC-B1：建立现有能力矩阵，补第 1 至第 3 层缺失验收与缓存用途生命周期。
3. FC-B2：完成比赛批量控制、资源池和结束清理。
4. FC-B3：完成设备包和链路策略。
5. FC-B4：完成现场连接器和虚实结合。
6. FC-B5：完成运维/观测增强、完整外部 API 和前端产品闭环。
7. FC-B6：与高性能执行面合并后执行完整商业验收。

## 22. 对标范围与非目标

本设计对标成熟平台的对象完整性、生命周期、资源池、网络能力、虚实连接、运维、观测和外部 API，不照搬其底层设施透传和界面堆叠。

本轮明确不做：

- 平台内制作模板、安装驱动、Sysprep、AD 提升或工业软件安装。
- 让场景作者填写宿主机、PVE、Docker、libvirt、OVN 或磁盘路径。
- 3D 拓扑、纯展示大屏和没有真实运行事实的统计。
- 第二套队列、第二套 runtime 状态机或通用事件总线。
- 通过固定等待、增加重试次数或吞掉错误提升表面成功率。
- 为保留旧错误路径长期维护双数据面或双 VM 执行实现。
- 在没有明确短时 Linux microVM 产品需求时引入 Firecracker/Kata。
- 整体迁移 Kubernetes/KubeVirt；未来只允许作为遵守相同执行契约的可插拔 Provider。

## 23. 集成、部署与退出门禁

### 23.1 合并门禁

- 两条分支各自通过所属单元、合同、架构和构建测试。
- 不在各自分支运行另一方尚未合并的全链路验收。
- A 合并后验证旧运行环境已排空和执行 Provider 可部署。
- B 合并后统一完成 DI、migration、OpenAPI 和前端生成。
- 集成分支只解决接口接线和真实验收问题，不顺带重构两条工作流内部代码。

### 23.2 真实验收

- 独立测试比赛、独立场景、三支队伍、Docker/Linux VM/Windows VM、至少四个网段。
- 多节点 OVN/OVS、管理员运维、流量/PCAP、暂停恢复、单队重建和比赛结束清理。
- 一项设备包、一项链路故障策略和一个模拟现场连接器。
- 50/100 队批量创建和销毁，300 个控制面并发创建请求。
- 主站重启、Redis 中断、OVN 控制面短时中断、单节点离线、Registry 不可达和存储变慢。
- 最终核对数据库、OVN、libvirt、Docker、overlay、网络、抓包、会话、容量、缓存引用和现场连接器无未归属残留。

### 23.3 完成定义

只有同时满足以下条件才可标记完成：

1. 已有能力矩阵中没有被误写为完成的未验收项。
2. 高性能目标在目标硬件上形成 p50/p95/p99 和资源曲线证据。
3. 外部 API 客户端不依赖浏览器 Cookie、数据库或 Agent 私有接口即可完成全生命周期。
4. 试运行和正式比赛结束后资源与节点缓存按用途引用自动收敛。
5. 新增设备包、链路策略和现场连接器通过真实运行验收。
6. 旧 shell 网络、命令行 VM 和错误缓存保留主路径已删除。
7. 文档、OpenAPI、迁移、部署、回滚和运维手册与运行事实一致。

## 24. 后续文档拆分

本设计获批后分别生成两个实施计划：

1. `TeamLab High-Performance Execution Foundation Implementation Plan`，只交给工作流 A。
2. `TeamLab Capability Closure and Commercial Upgrade Implementation Plan`，只交给工作流 B。

两个实施计划共同引用本设计的第 3 节共享契约和文件所有权，不复制彼此任务。任何跨边界需求先修改本设计并确认，再进入对应实施计划。
