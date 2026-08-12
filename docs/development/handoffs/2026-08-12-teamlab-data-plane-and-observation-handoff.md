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
