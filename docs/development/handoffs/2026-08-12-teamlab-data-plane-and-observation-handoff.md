# TeamLab 执行面修复与实机验收交接

日期：2026-08-12
范围：TeamLab 工作流 A，节点执行面、OVS/OVN 数据面、libvirt、镜像缓存引用、流量观测和 Agent 同步。
不在范围：前端、公开 TeamLab API、权限/比赛作者模型、模板制作、镜像改造、服务注入和工作流 B。

## 交接结论

本轮修复了审查报告中可在代码层确认的执行面问题，并完成 `git diff --check` 与 Release 构建。没有执行实机功能、压力、浏览器或故障注入测试；这些不是本轮职责，不能因代码构建通过而视为 V2 已可启用。

`EnableExecutionPlanV2` 必须继续为 `false`，直到下列实机验收全部通过。旧执行路径仍是当前运行路径。

## 本轮修复

### 节点同步与能力

- Agent 同步分为二阶段：先传输并重启二进制，观察到目标 SHA 和能力清单后才同步 VM 与 TeamLab 数据面配置。
- OVS/OVN 配置后会检查桥、服务和 Northbound 连接；未收敛时同步返回失败，节点继续保持不可调度，恢复任务也不会提前解封。
- 能力探测明确检查 `ovs-vsctl`、`ovsdb-client`、`ovn-controller`、`ovn-nbctl`、`ovn-sbctl`。没有完整就绪事实时不报告 V2 执行能力。

### 计划、网络与执行

- OVSDB JSON-RPC 改为受控复用连接：单会话串行、15 秒事务上限、greeting 处理、请求 ID 对应校验、连接异常后重建和服务释放。
- 执行计划重复提交按 `runtime + generation + shard + digest` 收敛；不同 digest 不会覆盖现有资源。
- 计划快照在向 Agent 发起执行前持久化。清理优先使用快照，避免释放网络/Fabric 租约后再从可变拓扑重编译。
- 健康检查配置缺少端口时在计划编译阶段拒绝，不再生成 `Port=0` 的无效计划。
- Docker 使用资源规格、不可变镜像摘要和运行身份标签；VM 使用稳定 UUID、计划摘要、overlay backing 校验和有界 `qemu-img` 调用。
- libvirt、Docker 和 OVS 部分创建失败均记录分类结果并触发受限清理。失败补偿仍覆盖所有已提交的节点分片：对超时和断连，主站无法证明 Agent 未接收请求，幂等清理是唯一不会遗漏资源的处理方式。

### 缓存与观测

- 镜像引用按运行、比赛准备、发布和认证用途区分；试运行/比赛结束释放运行引用，物理删除必须由 Agent inventory 确认后才记录完成。
- 流量观测批次在短暂上行拥塞时写入节点磁盘 spool；只有主站 Redis 原子接收成功才推进游标，避免有界内存 Channel 直接丢失记录。
- 计划事件对外只返回稳定阶段、分类和摘要；底层 socket、libvirt、Docker 异常原文保留在 Agent 结构化日志中，不通过事件契约扩散。

## 审查报告处理状态

审查基线见 `docs/development/handoffs/2026-08-11-teamlab-high-performance-a-review-report.md`。

已在本候选处理的重点：OVS 清理命名、OVSDB 有界性/连接复用/响应关联、Agent 同步隔离、数据面能力准确上报、流量缓冲、计划快照、VM 稳定身份及 backing 校验、Docker 资源和摘要、执行锁回收、空请求体保护、健康检查端口语义、事件异常脱敏。

仍需实机确认而非静态宣告的重点：OVN 多路由器事务、DHCP/DNS 实际物化、V2 inventory 与主站投影、跨节点 chassis binding、Docker/VM 并发上限、部分创建后的补偿、缓存物理删除、流量 spool 恢复和全部故障恢复路径。

没有为了覆盖报告中的每一条建议增加第二队列、第二状态机、Kubernetes 或额外公开接口。未被实际消费的字段和跨工作流内容保持不动，交由后续完整审查按真实调用链处置。

## 下一位验收人员的环境前置

1. 仅使用独立 TeamLab 场景、独立比赛和独立运行时；不得修改既有比赛、镜像、VM、容器或历史数据。
2. 先确认所有参与节点已通过平台 Agent 同步，节点同步状态为稳定，且主站记录的 Agent SHA 与进程实际 SHA 一致。
3. 在控制节点安装并启动 OVS/OVN 后，确认 Northbound/Southbound 可用；其他节点确认 `ovn-controller`、`br-int` 和 chassis 连接正常。
4. 确认能力清单包含 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`、`teamlab.artifact-cache.v2`；包含 VM 时还需 `teamlab.libvirt.native.v1`。能力缺失时不得开启 V2。
5. 配置只在验收窗口将 `EnableExecutionPlanV2` 设为 `true`。验收失败应关闭开关、保留第一份结构化日志与 inventory，再按快照执行清理；不要用重试或延长等待掩盖问题。

## 必测矩阵

### 正常链路

- 单节点与双节点：Docker、Linux VM、Windows VM；4、20、50 资产；4 和 8 个网段。
- 验证顺序：镜像预热完成且启动阶段传输字节为零，OVN 网络与 DHCP/DNS/路由/ACL 收敛，Docker/VM 运行，声明的来宾信号或端口健康检查完成，观测点开始采集。
- 暂停、恢复、重置、销毁：地址、overlay、容量、计划代次和镜像引用必须符合设计；恢复不得重新下载镜像。
- 清理后用 Agent inventory 与主站事实同时核对：容器、domain、overlay、OVS port/interface、逻辑交换机/路由器、抓包、会话、队列、容量预留和运行引用均无残留。模板主制品缓存不得误删。

### 高风险链路

- 同一计划重复提交、同身份不同 digest、不同 generation 并行、创建中客户端断连、Agent 重启、主站重启、Redis 暂断、OVN 控制面短暂中断、Registry 不可达、节点离线。
- 并行启动 10、50、100 队和 300 个创建请求，检查统一 `DeploymentQueueTicket`、容量账本和 Agent 分类限流；不得出现重复运行时、容量超卖或跨代次清理。
- 创建失败和销毁/重建冲突时，确认主站返回明确终态或中文错误，快照清理可重入且不会触碰下一代资源。
- 流量生成 TCP、UDP、ICMP，停止主站上行或重启 Agent 后检查 spool 恢复、游标连续、筛选/路径/抓包引用同一 runtime/generation/资产/时间范围。

## 证据要求

- 保存每个阶段的主站事件、Agent 日志、OVN/OVS inventory、libvirt/Docker inventory、镜像引用记录和清理前后对比。
- 浏览器验收、截图和页面交互由验收人员执行；本交接不以 API 或构建代替用户侧验收。
- 记录 V2 开关、节点能力、完整 Git SHA、发布目录、数据库迁移头、测试资源名称和清理结果。任何凭据、Token、Cookie、私钥和完整连接串不得进入证据文档。

## 本轮门禁

- `git diff --check`：通过。
- `dotnet build src/GZCTF.slnx -c Release --no-restore`：通过，0 error；存在既有测试分析器 warning，未在本轮修改。
- 未执行单元、集成、实机、压测、浏览器和故障注入测试。此前定向测试宿主无输出而被停止，不能写为通过。

## 118 部署与节点同步记录

- 发布版本：`teamlab-hpa-ovsdb-jsonrpc-20260812`；发布目录：`/opt/gzctf/releases/teamlab-hpa-ovsdb-jsonrpc-20260812/publish`；发布包 SHA-256：`9d548ac0b3abde23d114d51f524fde40786f4bb420bdf8cfd01b8fb51099a6cf`。
- 先前问题的根因是 118 主站仍指向另一套 release，而 `/usr/local/bin/gzctf-agent` 被单独替换，主站计算的随附 Agent SHA 与实际进程不一致。此次已改为完整 release 原子切换，主站与随附 Agent 的 SHA 一致。
- 已通过主站节点管理接口同步 118、125，未手工替换 125。两个请求均返回成功；两个节点均上报 Agent SHA `498ff7e3fb555f0adc2e5648b94ad0b64f9b5176a85b464324f2d11734ec1689`，更新状态稳定、`IsSchedulable=true`、无不可调度原因。
- 两节点的隧道与 Fabric 均为健康状态，能力清单均含 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`、`teamlab.artifact-cache.v2`、`teamlab.libvirt.native.v1`，执行计划并发上限为 1。
- `gzctf.service`、`gzctf-agent.service` 均为 `active`，首页本机 HTTP 检查通过。未在部署过程中创建、修改或销毁 TeamLab 场景、比赛、运行时、VM、容器或测试资源。
- 2026-08-12 后续切换：release 已更新为 `teamlab-hpa-v2-enabled-20260812`，主站 `TeamLabNetworkConfig:EnableExecutionPlanV2=true`。该开关已纳入 `TeamLabDataPlaneSyncConfig`，平台同步会将相同配置写入 Agent，避免主站与节点半切换。
- 118、125 均已通过平台同步并重启 Agent。两节点以节点认证请求 `POST /api/teamlab/execution-plan/apply` 时均返回 `400 request.invalid`，没有返回 V2 关闭时的 `404`；因此 V2 Provider 已实际开启，新建 TeamLab runtime 将走执行计划路径。
- **仍待验收：** V2 正常运行、并发、故障与清理矩阵。上述未完成项是测试范围，不再阻止 V2 在独立验收环境启用。

## 2026-08-13 Review-2 修复部署

### 发布物

- 发布版本：`teamlab-hpa-review2-fixes-20260813-2`。
- 发布包 SHA-256：`0a95e872f0758d414034fdb2f4d2714118b862c34da7837d1fc24bad8810e54c`。
- 发布目录：`/opt/gzctf/releases/teamlab-hpa-review2-fixes-20260813-2/publish`；`/opt/gzctf/publish` 已原子切换指向该 release。
- 发布物由当前工作树构建，包含 review-2 修复；`release-manifest.json` 的 `gitCommit` 仍为 `b90005d`，不代表工作树未提交改动未包含在包内。
- 切换前已备份数据库：`/opt/gzctf-vnext/backups/gzctf-before-vnext-20260812T172824Z.dump`，SHA-256 `c2e1a696cbf09818a7c2a494f786c5b702426756df868478c95c9dfde8ed3694`。
- 迁移已应用至 `20260812113416_AlignTeamLabExecutionRuntimeSchema`；旧 release `teamlab-hpa-v2-enabled-20260812` 保留在 `/opt/gzctf/publish.previous`，可用于回滚。
- `gzctf.service`、`gzctf-agent.service` 均为 `active`，`http://127.0.0.1:8080/` 返回 200。

### 配置与节点同步

- 部署后核对发现 08-12 `v2-enabled` release 的主站 `appsettings.json` 实际未包含 `TeamLabNetwork` 节；本次已在 `/opt/gzctf/publish/appsettings.json` 与 `/opt/gzctf/persistent/appsettings.json` 显式写入 `TeamLabNetwork.EnableExecutionPlanV2=true`，并重启主站确认生效。
- 已通过平台节点管理接口同步 118、125；两节点均回报 `success=true`、状态稳定、`IsSchedulable=true`。
- 118、125 的 Agent SHA-256 均为 `be5aae78b600f9aa34cc3ddf0cd5bffb16d5888c7483ce1d90d67d152779c961`，与主站 bundled Agent 一致。
- 两节点能力清单均包含 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`、`teamlab.artifact-cache.v2`、`teamlab.libvirt.native.v1`，执行计划并发上限 1，无不可调度原因。
- 118 Agent 配置 `TeamLab.EnableExecutionPlanV2=true`；两节点以节点认证请求 `POST /api/teamlab/execution-plan/apply` 均返回 `400 request.invalid`，确认主站与 Agent 两侧 V2 Provider 同时开启。
- 未创建、修改或销毁任何 TeamLab 场景、比赛、运行时、VM、容器或测试资源；现有 runtime 119 等旧路径资源未受影响。

### 待验收

- V2 实机矩阵仍未执行：正常 apply/cleanup、4/20/50 资产、多网段、Docker/VM、暂停恢复、并发、故障注入、清理残留与观测回放。
- 验收人员按上文“必测矩阵”执行，并使用独立场景与独立比赛；验收失败时按快照清理并保留日志，不自动重试或延长等待。

## 2026-08-13 125 V2 能力间歇缺失根因修复（本节的节点同步结论已被更新）

- 实测 125 的 V2 能力不是始终缺失，而是 V2/NO_V2 交替。根因是 OVSDB 半开连接与闲置 `echo` 残留；同步阶段还存在安装文件哈希与运行进程哈希不一致造成的假阳性。
- 修复内容、部署事实与测试入口见 `docs/development/handoffs/2026-08-13-125-v2-root-cause-fixed.md`。
- 当前 118、125 的 Agent SHA-256 均为 `c4835cb5945590674c51027f51aee9c3daa2f05e59940fd56fb32b4b3a8e99ce`，两节点能力清单均含 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`，状态 Stable、可调度。
