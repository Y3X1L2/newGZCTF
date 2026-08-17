# TeamLab 高性能执行面审查与实机验收交接

## 任务目标

审查并验收工作流 A 的高性能执行面。目标是让主站提交不可变的节点分片计划，由 Agent 使用 OVN/OVS、Docker Engine 和原生 libvirt 执行资源生命周期；数据库和 Agent inventory 是事实来源。

本交接不要求新增产品功能。优先确认现有实现是否可靠、简洁、无旧路径混用，并在独立节点完成实机验收。

## 基线与范围

- 分支：`codex/teamlab-high-performance-a`
- 共享契约提交：`5550f3a feat(teamlab): freeze execution plan contracts`
- 执行面提交：`d09fa8d feat(teamlab): add high-performance execution plane`
- 候选已推送到同名远端分支，未合并到 `main`。
- V2 默认关闭：`TeamLabNetworkConfig.EnableExecutionPlanV2=false`。
- 不修改工作流 B 的权限、比赛、场景作者模型、公开 `/api/open/v1`、前端、迁移和设备包。
- 不制作、认证或改造模板；模板库只提供已认证制品和静态运维信息。
- 不引入 Kubernetes、KubeVirt、Ceph、Dragonfly、Firecracker、第二队列或第二状态机。

## 已实现的结构

1. `src/GZCTF.TeamLab.Contracts/Execution/`
   - `TeamLabExecutionPlanV2` 和事件契约只表达运行、代次、节点分片、网络、资产、制品摘要和观测意图。
   - 契约不含 OVN 表、libvirt XML、Docker 原始对象、Shell 命令或前端字段。

2. 主站执行路径
   - `TeamLabShardDeploymentService` 只在所有节点同时声明 V2、OVN/OVS、制品缓存以及所需 Docker/libvirt 能力时使用 V2。
   - 计划按节点分片，制品预热完成后并行提交。
   - 任一节点失败、取消或 inventory 缺失时，立即按同一不可变计划补偿已成功分片；仅全部分片完整后写入 `execution-plan-v2/{shardId}` 标记。
   - `TeamLabRuntimeCleanupService` 识别该标记，并重新编译同一 release、同一 generation 的计划执行 V2 cleanup，不会回落到旧逐资产清理。

3. Agent 执行路径
   - `TeamLabExecutionPlanExecutor` 在 `runtime + generation + shard` 粒度串行，按制品、网络、资产、健康检查、观测顺序执行。
   - `TeamLabOvnNetworkProvider` 和 `TeamLabOvsAttachmentProvider` 使用 OVSDB JSON-RPC 与 OVN/OVS 交互；不以 `ovn-nbctl` 或 `ovs-vsctl` 作为主路径。
   - `LibvirtTeamLabProvider` 通过 P/Invoke 使用 libvirt domain API 与事件 API；VM 使用 backing overlay，不复制基础盘。
   - Docker 先完成受控网络接入，再释放网络门并启动；来宾就绪只接受声明的 TCP/HTTP 健康检查。

4. 缓存与回收
   - 引用用途为 Runtime、CompetitionPreparation、Rollout、ArtifactVerification。
   - 零引用、无活动 VM backing chain 后才排队清理。
   - Agent 删除 Docker/VM 缓存后返回目标缓存 inventory；主站仅在 `Present=false` 时删除分发记录。残留会保留真实失败，不会伪造已清理。

## 审查重点

按以下顺序进行，发现问题先验证调用链和运行事实，不以增加重试或等待替代根因修复。

### P0：资源边界和销毁

- 确认 V2 apply 的部分成功、失败、取消、Agent 重启和 inventory 缺失都不会留下容器、domain、overlay、OVS 端口、OVN 逻辑资源、抓包或访问会话。
- 确认 generation N 的 apply/cleanup 绝不会操作 generation N+1；旧 generation 不得清理新 domain、overlay、NVRAM 或缓存引用。
- 检查 `TeamLabShardDeploymentService` 的补偿只清理本次成功计划，不会双重清理或覆盖错误状态。
- 检查 `TeamLabRuntimeCleanupService` V2 标记识别、计划重编译和 fallback 条件。V2 标记未持久化时，Agent 自身补偿必须仍能收敛。

### P0：网络实现

- 检查 OVSDB JSON-RPC 请求、事务、超时、monitor 和 identity 的实现，不应存在字符串拼接 Shell 主路径。
- 检查稳定命名是否包含 runtime、generation 和逻辑键，且长度、字符集符合 OVS/libvirt 约束。
- 确认 router、DHCP、DNS、ACL、NAT、路由和 chassis binding 的真实收敛证据来自 OVSDB/inventory，不来自固定等待或日志猜测。
- 确认 Docker veth、VM TAP 只接入获批 OVS 端口；WireGuard 仍是共享玩家入口，不为每个 runtime 新建跨节点 Fabric。

### P0：VM 与缓存

- 审查 `LibvirtNativeInterop` 的 P/Invoke 签名、内存释放、domain UUID、事件订阅解除和错误码映射。
- 确认 VM destroy/undefine/overlay/NVRAM 清理按同一 generation 围栏执行。
- 审查 `ImageDistributionService` 和 Agent `ImageController`：Docker 和 VM 物理缓存删除后必须以 inventory 确认；模板库 OCI 主制品不能被 runtime cleanup 删除。
- 验证缓存仍被 overlay backing chain 或运行中容器引用时，清理保持可解释的失败/等待状态。

### P1：契约、并发和恢复

- 审查 `TeamLabExecutionPlanV2.IsValid`、digest 计算和编译器的确定性。相同输入必须得出相同 digest；不同 generation 或分片不得互认。
- 同一 `runtime + generation + planDigest` 重复提交应返回已收敛 inventory；同身份不同 digest 必须拒绝覆盖。
- 审查节点分类限流与统一 `DeploymentQueueTicket`、容量账本的关系，不得新建隐蔽队列或导致超卖。
- 确认 Redis/进程通知只是唤醒，重启后能以数据库和 Agent inventory 恢复，不从日志文本重建状态。

### P1：边界和代码质量

- 确认主站只编译计划和投影事实，Agent 只执行本机资源；Controller 不直接编排 Docker、OVN 或 libvirt。
- 检查旧 bridge/router namespace/dnsmasq 与 `virsh`/`virt-install` 只在开关关闭时保留；V2 成功后不允许继续执行旧逐资产 DAG。
- 删除或标记任何无调用、重复 DTO、重复状态机、固定 sleep、无界轮询或自动重试掩盖故障的代码。
- 审核日志、事件和异常中不含 token、密码、私钥、镜像认证信息、flag 或 user-data。

## 实机验收计划

### 环境前置

1. 使用独立的 OVN/OVS/KVM 节点，不能使用有运行中 TeamLab 环境的节点。
2. 准备已分发的小型 Docker 制品和一个已认证 VM 制品；记录 digest、节点和可用容量。
3. 节点能力必须包含：
   - `teamlab.execution-plan.v2`
   - `teamlab.ovs-ovn.v1`
   - `teamlab.artifact-cache.v2`
   - VM 节点额外包含 `teamlab.libvirt.native.v1`
4. 先确认旧 TeamLab runtime、部署队列和分发 claim 已排空，再临时启用 V2。
5. 全程使用独立 runtime、release、网络名称和操作记录；不得清理既有资源。

### 基础闭环

使用 `scripts/validation/teamlab/run-high-performance-a-acceptance.ps1`，传入独立 Agent URI、计划 JSON 和临时 Agent token，验证：

1. 首次 apply 成功，inventory 资产数等于计划资产数。
2. 同计划重复 apply 返回 `alreadyApplied=true`，不重复创建。
3. cleanup 成功，计划 generation 的容器和 VM inventory 为零。

随后必须额外检查：domain、overlay、NVRAM、OVS port/interface、OVN logical switch/port/router、capture、会话、容量预留、队列和缓存引用均已清理；模板主制品与其他场景共享缓存不得误删。

### 规模、故障与并发

按 S/M/L 场景（4、20、50 资产）分别验证单队、10 队、50 队和 100 队运行；300 个创建请求通过统一队列 admission。采集网络 4/8 网段收敛、Docker 预热启动、VM overlay/define/start、暂停恢复与销毁耗时。

必须注入并记录以下情况：

- 同计划重复提交、不同 generation、创建中客户端断连。
- 部分节点 apply 成功后另一节点失败。
- cleanup 与重建冲突。
- 主站重启、Agent 重启、Redis 中断、OVN 控制面短暂中断、节点离线、Registry 不可达。

通过标准：无重复 runtime、无容量超卖、无跨 generation 误删、无无限等待；失败必须以 `validation/capacity/artifact/network/compute/guest/service/observation/cleanup` 分类显示并可由显式操作恢复。

## 当前验证证据

| 项目 | 结果 |
| --- | --- |
| `git diff --check` | 通过 |
| `dotnet build src/GZCTF.slnx -c Release --no-restore` | 通过，0 警告、0 错误 |
| TeamLab + 镜像分发定向 `vstest` | 284/284 通过 |
| 缓存 inventory 定向回归 | 20/20 通过 |
| 全量单元测试 | 未签收；此前 `dotnet test` 宿主在本机超时未退出 |
| 集成测试 | 未签收；本机 Docker Engine 未运行，Testcontainers 无法启动 |
| OVN/OVS/KVM 实机、规模、故障注入 | 未执行 |

## 部署状态与阻断

- 未部署到 `10.0.7.118`。
- 2026-08-11 只读检查显示其当前主站与 Agent 均 active，现有 release 保持不变。
- 根分区可用空间约 `848 MiB`，`/tmp` 可用约 `911 MiB`，不足以安全上传、解压、保留旧 release 并执行可回退部署。
- 不得删除历史 release、镜像、runtime 或数据库备份来腾空间，除非负责人明确指定可删除对象并先核对引用。

## 接手后的第一步

1. 在独立验收节点准备至少可容纳新 release、旧 release、压缩包和备份的空间。
2. 审查本分支相对 `origin/main` 的两次提交及本文件列出的 P0/P1 项。
3. 在满足前置条件后按 `docs/commercialization/runbooks/teamlab-high-performance-execution.md` 构建、部署，并运行验收脚本和实机矩阵。
4. 只有所有实机证据通过后，才允许启用 V2；旧数据面删除属于 HP-A6，必须另行提交和复核。
