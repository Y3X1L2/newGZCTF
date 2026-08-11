# TeamLab 数据面与流量观测交接

更新时间：2026-08-12（部署前候选）

本文交接给负责实机验收和问题发现的维护者。它只记录已实现的代码事实与待验证项目；未写明“实机通过”的内容均不得视为已签收。

## 本轮目标与边界

- 继续使用模块化主站加 Agent 执行面。主站只下发已校验的计划、保存状态和调度；节点 Agent 执行宿主网络、容器、虚拟机和观测动作。
- 不在场景创建、试运行或正式比赛启动路径安装软件包。
- 不启用 `EnableExecutionPlanV2`，不删除现有 bridge/router namespace/dnsmasq 路径。新执行面仅在真实 OVS/OVN、Docker、libvirt 与回收验收结束后切换。

## 已完成实现

### 1. 节点数据面准备与能力上报

- 新节点注册的依赖脚本会安装并检查 OVS/OVN 主机组件：`openvswitch-switch`/`ovn-host` 或对应发行版包；`ovs-vsctl` 与 `ovn-controller` 缺失时不会把依赖准备误判为完成。
- 平台“同步 Agent”下发 `TeamLabDataPlaneSyncConfig`。节点根据自身和中心节点的 WireGuard 地址确定工作节点或中心节点角色，自动完成缺包安装、服务启动、`br-int` 创建、OVN 连接配置和配置文件写入。
- 中心节点没有可用 Fabric 地址时只完成本地 OVS 准备，返回未就绪事实，不会监听公网或伪造 V2 可用。
- Agent 能力清单改为事实检测：`teamlab.ovs-ovn.v1` 只有本地 `br-int`、相应 OVN 服务和 Northbound OVSDB 实连都成功时才出现。原生 libvirt 与执行计划能力不再被 V2 开关本身隐藏；是否采用 V2 仍由主站开关决定。
- OVSDB TCP 端点同时兼容标准 `tcp:host:port` 与 `tcp://host:port`，避免原先只按 .NET URI 解释导致的连接错误。

主要文件：

- `src/GZCTF/Services/Fleet/NodeDeployService.cs`
- `src/GZCTF/Services/Fleet/TeamLabDataPlaneSyncConfiguration.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabDataPlanePreparationService.cs`
- `src/GZCTF.Agent/Services/AgentCapabilityService.cs`

### 2. 流量观测持续丢记录根因修复

此前持续丢记录并非单纯抓包问题，存在四个明确原因：

1. 主站已落库/接受的记录不会从 Agent 内存和磁盘队列移除，运行足够久后必然触发内存上限淘汰。
2. 主站每秒串行读取每个观测源最多 500 条，多个运行时持续流量时积压不可避免。
3. Redis 写入通过全局分布式锁和 25ms 循环竞争，增加了高并发背压。
4. Redis 异常时主站曾把记录放入易失内存后仍确认 Agent 游标；主站重启或该内存缓冲溢出会造成不可恢复丢失。

现已完成：

- 观测读取契约增加可选的 `acknowledgeThroughSequence`。主站仅在上一页已经被 Redis 或本地持久缓冲接受后，才随下一次读取确认给 Agent。
- Agent 确认后同时释放内存记录并原子重写对应磁盘队列。落盘工作携带每个运行时的落盘代次，确认/压缩后的旧异步写入会被围栏丢弃，不能把已确认或重复记录重新追加。
- 主站每源读取页增至 2,000 条，在 8 秒或 16,000 条的明确预算内连续排空。不同源使用独立 DI scope，最多并行四路，不并发使用同一个 `DbContext`。
- 若 Redis/本地缓冲发生真实丢弃，主站不再推进 Agent 游标，避免把未接受记录伪装成已送达。
- Redis 流容量保护改为单个 Lua 原子操作：裁剪、容量检查和整批 `XADD` 同一事务完成，删除全局锁及短轮询。新条目使用单个 JSON `payload` 字段；读取端兼容历史多字段条目。
- 主站不再保留 `TeamLabTrafficLocalBuffer` 易失回退。Redis 不可用或追加失败时，收集器不推进 Agent 游标；Agent 已存在的磁盘 spool 负责保留并在 Redis 恢复后重投。

主要文件：

- `src/GZCTF.Agent/Services/Observation/ObservationBatchSpool.cs`
- `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/RedisTeamLabTrafficIngestor.cs`

## 已完成代码验证

- `git diff --check`：通过。
- `dotnet build src/GZCTF.slnx -c Release --no-restore`：通过，项目自身无新增错误。
- `dotnet vstest src/GZCTF.Test/bin/Release/net10.0/GZCTF.Test.dll --TestAdapterPath:src/GZCTF.Test/bin/Release/net10.0`：`830/830` 通过。
- 当前部署前候选：Release build 通过；全量单元 `831/831` 通过，新增覆盖 Redis 不可用时必须延后接收而不能确认 Agent 游标，以及半完成 Agent 更新不能被恢复任务误标为稳定。
- 新增单元覆盖：确认游标只释放已确认记录；注册脚本包含 OVS/OVN 依赖与命令检查。

本机 Docker Engine 不可用，集成测试的 Testcontainers PostgreSQL 无法启动，因此没有把集成测试记为通过。

## 实机验收清单

在独立 TeamLab 场景、独立运行时和可清理节点上执行。不要改动现有比赛或运行资源。

1. 在 118/125 分别执行平台“同步 Agent”，检查操作事件、Agent 心跳和能力清单。确认 OVS、OVN、`br-int` 与服务状态；没有 Fabric 地址或中心连接时必须是明确未就绪，而非出现 `teamlab.ovs-ovn.v1`。
   - 125 当前仍是旧 Agent。必须先部署本交接候选，再从节点管理界面发起同步，验证先二进制后配置的两阶段事件顺序；旧 Agent 不得在第一阶段执行 OVS/OVN 或防火墙配置。
2. 在 Fabric 地址齐全后再次同步，验证中心仅监听 Fabric 地址，125 通过 Fabric 到达 Northbound/Southbound；检查能力清单出现 `teamlab.ovs-ovn.v1`、`teamlab.execution-plan.v2`，但保持 V2 开关关闭。
3. 在隔离环境开启 V2，完成 Docker、Linux VM、Windows VM 的 apply、重复 apply、pause/resume、cleanup 与 Agent inventory 复核。任一阶段失败时验证没有跨代次资源或无主 overlay。
4. 制造持续 TCP、UDP、ICMP 流量并持续至少 30 分钟。记录 Agent 内存队列、spool 字节、`droppedRecords`、主站游标、Redis 流长度和 PostgreSQL 写入。正常压力下 `droppedRecords` 不应持续增加；人为让 Redis 不可用时游标不得前进，恢复后应继续排空。
5. 测试超过 2,000 条单页和多个节点/运行时并发流量，确认不同观测源并行且每个源不超过预算；没有重复流、漏序、无限等待或 500。
6. 测试 Agent 重启、主站重启、Redis 短暂中断、OVN 控制面短暂中断、节点离线、销毁与重建冲突。以数据库和 Agent inventory 为事实检查收敛。
7. 试运行与正式比赛结束后，检查容器、domain、overlay、命名空间、veth、抓包会话、容量预留和运行时镜像引用均清理；模板库主制品和其他运行时正在使用的缓存不得删除。

## 仍未签收的事项

- OVS/OVN 安装、跨节点连接、V2 apply/cleanup 尚未实机验证。
- 4/20/50 资产、10/50/100 队、高压流量和故障注入矩阵尚未执行。
- Docker Engine 不可用导致本机集成测试未执行。
- 旧网络路径仍保留，只有上述验收全部通过且旧运行时排空后才可开始切换和删除。
