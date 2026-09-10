# TeamLab 与安可鲁能力差异及产品实现审查

更新时间：2026-09-07

代码基线：`origin/main` / `bbd5a5d`

审查状态：代码与本地可模拟链路已完成；KVM、OVN/OVS、WireGuard 数据面仍需 Linux 多节点实机验收

## 1. 结论摘要

YINYU TeamLab 不是安可鲁的简单缺功能版本。它在声明式拓扑、不可变发布、统一异步运行任务、细粒度权限、跨节点 WireGuard/OVN 设计、流量路径、链路策略和 Webhook 方面更完整；Docker、VM、SSH/RDP 和抓包也存在真实执行代码，不应判定为整体假实现。

当前主要问题集中在“运维闭环”和“虚实结合最后一公里”：

1. 容器远程终端不具备终端产品应有的协议和交互。本机真实 WebSocket 测试中，Alpine `/bin/sh` 发出终端状态查询后，现有文本框客户端不会像 xterm/PTTY 客户端那样响应，命令无回显；后端又可能把任意字节块直接作为完整 Text WebSocket 帧发送，UTF-8 多字节字符跨块时会形成非法文本帧。
2. 远程会话的“重试”、取消、Agent 重启恢复和 Guacamole 清理均未闭环；页面给出了用户会认为可用的操作，但失败后往往只能重新创建会话，部分路径还会遗留临时资源。
3. 连接器和设备包目前主要是目录、校验、冻结、租约和人工健康状态。它们没有驱动 VLAN、物理网卡、串口、USB 网关、外部网段接入，也没有让 Agent 下载并运行设备包，因此不能对外宣称已完成虚实结合。
4. 管理端缺少单资产启停/重启/重建、文件管理、VNC、运行时快照、端口映射、手工 reconcile、任务取消、会话集中管理等常规运维能力。这些不依赖 PVE，属于现有架构内可实现的产品缺口。
5. 混合 Docker/VM 组网的 V2 OVN/OVS、Docker attachment 和 libvirt VM attachment 有真实代码及历史实机证据，但当前本机只能验证 Docker；双 Worker 故障接管、复杂跨节点、规模和长期稳定性不能依据 mock 或历史记录判定为当前生产可用。

建议先把远程运维做成真正可依赖的工具，再扩展更多目录型能力。当前不应把“连接器存在”“设备包可登记”“远程会话可创建”写成能力已完成。

## 2. 审查范围与方法

- 安可鲁参照：外部 Swagger，共 156 个 path、192 个 operation。
- YINYU 动态 OpenAPI：本地运行 `bbd5a5d` 后共 84 个 path、110 个 operation，其中 TeamLab 59 个 path。
- 审查链路：OpenAPI/Controller -> Contracts/Application -> Agent -> vNext 页面 -> 自动化测试 -> 本机真实 Docker/WebSocket 行为。
- 数量只用于确认覆盖面，不把 operation 数量当成产品质量。
- 证据分为五档：代码存在、自动化测试通过、本机模拟通过、Linux 多节点实机通过、当前生产签收。低档证据不能替代高档证据。
- 排除 PVE 集群、PVE 存储、PCI/USB 直通管理面等由架构决定不提供的能力。

## 3. 能力映射

| 分类 | YINYU 当前情况 | 产品判断 |
| --- | --- | --- |
| YINYU 更强 | 声明式拓扑、不可变 release、统一 `DeploymentQueueTicket`、rollout、权限、WireGuard/OVN 设计、流量路径、链路策略、Webhook | 保留现有方向，不按安可鲁接口形态倒退重构 |
| 等价或近似 | Docker/VM 基础部署、SSH/RDP、抓包、运行状态与事件 | 后端基础较完整，但远程终端和抓包历史 UI 仍需补齐 |
| 已有但实现差 | 容器终端、远程会话重试/回收/恢复、抓包历史查看、创建流程取消 | 页面或接口存在，但不能作为可靠产品能力签收 |
| 可实现但缺失 | 文件管理、VNC、单资产生命周期、运行时/资产快照、手工 reconcile、运行任务取消、端口映射、全局运行时检索、远程会话列表与强制结束 | 不依赖 PVE，应进入产品路线图 |
| 当前是假闭环 | 物理连接器、设备包执行、远程审计文件 | 只有元数据或空模型，不应对外宣称完成 |
| 架构不适用 | PVE 集群、PVE 存储与硬件透传管理面 | 明确排除，不列为 TeamLab 缺陷 |

### 3.1 安可鲁有而 YINYU 应补的运维能力

| 能力 | 安可鲁 | YINYU | 建议 |
| --- | --- | --- | --- |
| 容器 start/stop/restart/recreate/pause/unpause | 有 | TeamLab 管理端主要只有 runtime reset/destroy；Open API 有 runtime pause/resume，但管理页面未形成等价入口 | P1，增加单资产生命周期与运行时暂停/恢复入口 |
| 容器日志与 inspect | 有 | 运行事件和日志有一定覆盖，缺少面向单资产的完整 inspect/实时日志工作台 | P1 |
| 容器文件上传/下载/目录管理 | 有 | 无 TeamLab 运维闭环 | P1，做受限路径和审计，不开放任意宿主机路径 |
| 容器交互终端 | 有 | 有接口和页面，但不是 xterm/PTTY，实测常见 shell 交互失败 | P0 |
| VM SSH/RDP/VNC | 有 | SSH/RDP 有，VNC 无 | SSH/RDP 先修可靠性；VNC 列 P2 |
| VM SFTP 文件管理 | 有 | 无 TeamLab 运维闭环 | P1/P2 |
| VM 与场景快照/回滚 | 有 | 通用 VM 留有 `SnapshotName` 字段，但 TeamLab 没有可用的快照 CRUD/回滚产品链路 | P2 |
| 单节点启停 | 有 | 主要以整 runtime reset/destroy 管理 | P1 |
| 端口映射 | 有 | 底层 Docker 可发布端口，但 TeamLab 运行时没有可管理、可审计的端口映射产品入口 | P1/P2 |
| 手工 reconcile 与状态 | 有 | 有后台恢复/事实对账服务，没有管理员可观察、可触发、可查看差异的入口 | P1 |
| 场景任务列表与取消 | 有 | 有统一 operation/ticket 查询，但运行中任务通常不能直接取消，管理 UI 也缺少取消工作流 | P1 |
| 批量启动 | 有 | 可通过 runtime/rollout 间接完成 | 不必复制接口，补批量选择和进度反馈即可 |
| 抓包 | 有 | 后端较完整，页面只管理最近一次任务 | P1，补历史、失败分段和多任务管理 |

## 4. 代码与产品问题

### P0-1 容器终端不是可用的交互式终端

- Agent 固定启动 `/bin/sh`，见 `src/GZCTF.Agent/Services/DockerService.cs:714`。
- 前端用 `<pre> + input + 发送按钮`，见 `RuntimeRemoteAccessPanel.tsx:281` 和 `:289`，没有 xterm、PTY resize、方向键、Tab、Ctrl-C、粘贴控制、终端控制序列处理或二进制协议。
- 本机在真实 Agent、Docker 和 Alpine 容器上完成 WebSocket 101 握手。首帧为提示符和 `ESC[6n` 终端状态查询；当前客户端不会响应，后续命令没有回显或结果。
- Agent 在 `DockerService.cs:743-751` 把每次最多 8192 字节的 Docker 输出块直接标成一个完整 Text WebSocket 消息。读边界不是 UTF-8 字符边界，中文三字节序列跨块时会生成非法 Text 帧或乱码。
- 现有测试只覆盖 `TeamLabTerminalSessionRegistry` 的重复连接和取消，不覆盖 shell I/O、控制序列、UTF-8 分块、resize 或浏览器端行为。

整改要求：使用 xterm.js；定义明确的终端子协议（输入、resize、signal、stdout/stderr/close）；后端使用二进制帧或有状态 UTF-8 decoder；允许镜像配置 shell 并回退到 `/bin/sh`；增加真实容器 WebSocket 集成测试。

### P0-2 “重试连接”复用已终止会话，语义上必然失败

- `ConnectSessionAsync` 只接受 `Ready`，随后立即改为 `Connected`，见 `TeamLabRemoteAccessService.cs:369-373`。
- WebSocket 请求结束后 Controller 的 `finally` 会结束会话；页面 `retryNonce` 仅重新连接同一个 session ID，见 `RuntimeRemoteAccessPanel.tsx:224-253`。
- 因此按钮名是“重试”，实际没有重新创建可连接会话。用户看到的是可恢复交互，服务端状态机却不允许恢复。

整改要求：二选一并固定契约：支持有次数和租约限制的 reconnect；或失败后明确调用 create 获取新 session，按钮改为“重新创建终端”。

### P1-1 创建期间取消会泄漏远程资源

- 页面在异步 create/get/connect 任一步返回后只检查 `cancelled.current` 并直接 return，见 `RuntimeRemoteAccessPanel.tsx:86-109`，没有对已经创建的 session 调用 end。
- VM 弹窗被浏览器拦截时，流程仍可能继续创建 Guacamole/relay 资源；预开的空白窗口也可能残留。

整改要求：把流程建模为可取消事务；一旦拿到 session ID，任何取消或后续失败都必须 best-effort end，并向用户显示清理结果。

### P1-2 Guacamole 清理会假成功

- `GuacamoleRemoteSessionService.DeleteAsync` 取不到管理 Token 时直接返回。
- 删除连接的布尔结果没有检查；删除临时用户不检查 HTTP 成功状态，并在 `DeleteUserAsync` 中吞异常，见 `GuacamoleRemoteSessionService.cs:59-65`、`:113-116`。
- 上层随后仍可把数据库会话标记为 `Ended`，实际临时用户或连接可能残留。

整改要求：清理结果必须结构化返回；失败进入 `CleanupPending` 并由后台重试；达到阈值后告警；管理端提供残留会话/用户检查。

### P1-3 Agent 重启后远程会话不可恢复

- relay 仅保存在 `RemoteAccessRelayService` 的进程内 `ConcurrentDictionary`，见 `RemoteAccessRelayService.cs:19`。
- terminal registry 同样是进程内状态。
- 主站数据库可能仍显示 `Ready/Connected`，但 Agent 重启后 relay 和 terminal 均消失；当前没有启动恢复、inventory 对账或按 session 重建 relay。

整改要求：主站和 Agent 在心跳/启动时对账会话；可恢复会话重建 relay，不可恢复会话进入失败/清理状态；UI 不显示虚假的“已连接”。

### P1-4 缺少集中会话运维面

当前页面围绕单个 runtime 创建/查询/结束会话，没有按用户、runtime、节点、状态和过期时间检索所有会话，也没有管理员强制结束、清理重试和残留检查。`TeamLabRemoteAuditFile` 只有实体、EF 配置和 migration，没有创建、查询、下载或生命周期服务。

### P1-5 缺少单资产生命周期和故障处置

`TeamLabAdminRuntimeController` 与 vNext 详情页主要暴露 reset/destroy；详情页操作判断见 `TeamLabRuntimeDetailPage.tsx:56-95`。管理员无法对一个 Docker/VM 资产执行 start、stop、restart、recreate，也无法在不重置整个实验的情况下修复单节点。

整改要求：所有动作仍通过统一 ticket；明确幂等、代次 fencing、权限、危险确认和审计；提供单资产状态、最后错误和任务进度。

### P1-6 抓包后端强、前端弱

- 后端支持任务列表、状态、停止、下载、分段上传、摘要校验和过期清理。
- 页面恢复时只取 `captures[0]`，见 `CapturePanel.tsx:47-48`。
- 用户无法查看历史任务、失败分段、每节点进度、保留期、多任务或清理状态。

整改要求：增加抓包任务表、状态过滤、分段详情、失败原因、下载可用性和清理状态；不要只显示最近一次。

### P1-7 缺少 KVM 时可能阻断 Docker 心跳

`HeartbeatWorker` 无条件调用 `kvm.GetVmCountAsync`，见 `HeartbeatWorker.cs:34`。Windows 本机实测中，KVM 服务固定调用 `/bin/bash` 导致 `Win32Exception`，整次心跳被 catch，Docker 数量和 capability manifest 也无法上报。这违反“Docker 与 KVM 能力独立判断；缺少 KVM 不能阻断 Docker 调度”的项目约束。

整改要求：按能力独立探测和采集；KVM 不可用时 VM 数量为 0 并记录降级状态，不能取消 Docker 心跳。增加无 libvirt/virsh 环境的 Agent 心跳测试。

### P2-1 Open 远程会话接口是不完整契约

未跟踪文件 `OpenTeamLabRemoteSessionsController.cs` 可创建、查询和结束会话，但故意不提供 connect/terminal。外部调用方创建后没有实际使用入口，只会制造临时 relay/Guacamole 资源。在定义外部终端代理协议前不应合入。

### P2-2 运行时检索和操作管理不足

运行时列表支持服务端分页，但缺少按 release、创建人、节点、资产、错误码、状态组合检索的全局运维视图。统一 operation/ticket 已存在，但页面缺少任务取消、重试资格、阻塞原因和手工 reconcile 差异展示。

## 5. 虚实结合审查

### 5.1 已真实实现的部分

- V2 执行面存在 OVN Northbound、OVS attachment、Docker 多网卡接入、libvirt VM 网卡接入、代次 fencing 和资源 inventory 代码。
- 运行时规划会校验并租用连接器，`ConnectorId` 会进入拓扑和 runtime 元数据；这部分不是纯前端占位。
- 历史交接中有单机和部分组网实机证据，但历史证据只说明曾运行，不能替代当前生产签收。

### 5.2 尚未实现的物理闭环

- `TeamLabConnectorService` 只管理数据库资源、人工健康状态、并发容量和 runtime 租约；`AttachmentReference` 没有转换成 Agent 可执行的接入计划。
- `ConnectorId` 虽进入规划和运行时元数据，但没有生成物理网卡/VLAN/串口/USB 网关/外部网段的 attach、probe、detach 命令。
- “健康/不可达”由管理接口写库，不是 Agent 主动探测物理设备、链路或网关得到的事实。
- 设备包的制品、端口声明、参数 schema、健康声明和协议事件类型会被保存、冻结和校验，但 Agent 不下载、不启动、不监督设备包；运行资产仍依赖普通 `ImageTemplate`。

因此，当前可对外表述为“具备物理连接器和设备包的控制面模型及租约基础”，不能表述为“已支持虚实结合运行”。

### 5.3 完成虚实结合的最小验收标准

1. Connector provider contract：`prepare/attach/probe/detach/inventory`，动作必须幂等并带 runtime/generation fencing。
2. 至少实现一种真实 provider，例如受管 VLAN + 物理 NIC，或串口到 TCP 网关；人工健康状态不能作为成功依据。
3. 设备包采用受信制品、摘要校验、参数渲染、受限权限、生命周期监督、健康探测和日志/事件回传。
4. runtime destroy、reset、Agent 重启和主站重启后能对账、回收或恢复租约与物理接入。
5. 在授权 Linux Worker 上完成 Docker + VM + 物理端点三者互通、隔离、抓包、故障恢复和清理验收。

## 6. 本地模拟环境与验证

### 6.1 当前运行环境

- 主站：`http://localhost:8080`，首页、API 文档和两份 OpenAPI 均返回 200。
- vNext 开发前端：`http://127.0.0.1:63000`，使用当前源码并代理到本地主站。
- PostgreSQL 16：可用，数据库共有 132 条 migration，head 为 `20260816192540_TeamLabCapabilityClosure`。
- Redis 7：`PONG`。
- guacd 1.6.0：健康探针最终为 `healthy`，监听 4822。
- Linux Agent 模拟容器：`gzctf-agent-local-sim`，监听 5001，挂载 Docker Desktop socket；TeamLab Linux 数据面执行关闭，节点保持不可调度。
- Agent 心跳：主站连续返回 200；manifest 只报告 Docker，不报告 KVM，`VmCreates=0`、`TeamLabNetworkOperations=0`。
- 临时 Alpine runtime：通过 Agent `api/containers/create` 创建并启动，inventory 正确返回 runtime ID、generation、asset key 和 running 状态。

### 6.2 测试结果

| 验证 | 结果 | 说明 |
| --- | --- | --- |
| TeamLab 后端单元测试 | 317/317 通过 | 覆盖 TeamLab namespace；测试完成后 coverlet 收尾未退出，手工终止收尾进程 |
| 抓包 PostgreSQL 持久化测试 | 1/1 通过 | 首次并行编译发生共享 `obj` 文件锁，串行且关闭覆盖率后通过 |
| vNext 抓包与 runtime API 测试 | 11/11 通过 | 不包含远程终端组件测试 |
| Agent -> 主站心跳 | 通过 | Linux 容器连续 HTTP 200 |
| Agent Docker create/inventory | 通过 | 真实 Docker Desktop socket |
| 容器终端 WebSocket 握手 | 通过 | HTTP 101 |
| 容器终端交互 | 失败 | Alpine shell 发出控制查询后，当前非 xterm 客户端无法完成常规命令交互 |
| Windows 原生 Agent 心跳 | 失败 | 无条件 KVM 采集调用 `/bin/bash`，阻断整次心跳 |

未执行或不能在本机等价验证：完整前后端全量门禁、KVM/libvirt、OVN/OVS、WireGuard 数据面、双 Worker 故障接管、Guacamole 浏览器 RDP/SSH、物理连接器、设备包执行、生产数据库升级/恢复。

## 7. 远端运维记录

- 已确认公网中继上的实际服务为 `wg-quick@wg-gzctf`，服务 active、接口存在、IPv4 转发开启，到 `10.0.0.0/8` 的路由存在。
- 其他 WireGuard peer 有近期握手；承载目标内网的 peer 已超过两天无握手。
- 从中继探测 `10.252.0.2`、`10.0.7.1`、`10.0.7.118` 均不可达；本地对 `10.0.7.118` 和 `10.0.7.125` 的 ICMP/TCP 22 也不可达。
- 未对 `10.0.7.118` 或 `10.0.7.125` 执行磁盘清理、服务启动或任何写操作。用户已要求先转为本地模拟测试。

## 8. 整改优先级与验收顺序

### P0：先让远程运维可用

1. xterm.js + 明确 PTY/WebSocket 子协议 + resize/signal + UTF-8 安全传输。
2. 修正 reconnect 状态机、创建取消补偿和 Agent 重启会话对账。
3. Guacamole 清理改为可观测、可重试的 `CleanupPending` 工作流。
4. 增加真实 Alpine/Ubuntu 容器终端和 Guacamole 集成测试。

### P1：补齐日常运维闭环

1. 单资产 start/stop/restart/recreate，runtime pause/resume 页面入口。
2. 容器日志/inspect、受限文件管理、VM SFTP。
3. 抓包历史、分段状态、失败诊断和清理状态。
4. operation 列表、取消/重试资格、手工 reconcile 与差异预览。
5. 全局 runtime/资产/会话检索和管理员强制结束。
6. 心跳能力独立降级，确保无 KVM 不阻断 Docker。

### P2：扩展能力

1. VNC、运行时/资产快照与回滚、受审计端口映射。
2. 实现至少一种 connector provider 和设备包执行器，再声明虚实结合完成。
3. 定义可真正消费的 Open 远程访问协议；在此之前不要发布半套接口。

## 9. 签收原则

- Controller、DTO、数据库表或页面按钮存在，不等于能力完成。
- mock 通过只能证明应用编排，不证明 Docker/KVM/网络/Guacamole 执行正确。
- 历史实机证据不能替代当前 commit、当前配置和当前生产环境的复验。
- 所有危险动作必须具备幂等、代次 fencing、权限、审计、进度、失败重试和残留对账。
- 虚实结合签收必须包含真实物理端点，而不是人工修改“健康”字段。
