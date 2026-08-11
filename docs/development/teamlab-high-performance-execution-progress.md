# TeamLab 高性能执行面进度

更新时间：2026-08-11

## 工作约束

- 以不可变节点执行计划、Agent 事件和 inventory 为事实，不用固定等待、无界重试或日志猜测状态。
- 保持主站控制面与 Agent 执行面分离；不把模板制作、镜像改造或前端职责放入本工作流。
- 不新建第二套队列、状态机或公开 TeamLab API；继续使用现有部署队列、容量账本和公开控制面。
- 前后端继续通过公开契约解耦；本工作流不修改工作流 B 的权限、比赛、作者模型、前端或设备包。
- 实现阶段避免重复运行全量门禁；所有实现收束后统一构建、测试、实机验收和独立质量审查。
- 发现计划与官方接口或当前运行事实不一致时，先记录并修订，不用临时分支掩盖问题。

## 阶段记录

| 阶段 | 状态 | 证据与结论 |
| --- | --- | --- |
| HP-A0 共享契约 | 完成 | 提交 `5550f3a` 创建 `GZCTF.TeamLab.Contracts`，主站与 Agent 均引用 V2 执行计划/事件契约。 |
| HP-A1 节点能力与限流 | 代码完成，待实机验收 | 已增加 V2、OVN/OVS、原生 libvirt、缓存能力标识及执行/清理分类限制；V2 Agent 入口在开关关闭时返回 404。 |
| HP-A2 OVN/OVS Provider | 代码完成，待实机验收 | 已有 OVSDB JSON-RPC、逻辑网络/端口身份和 OVS bridge-port-interface 接入/回收；修正路由端口网关地址、静态路由输出端口 UUID 和清理引用顺序。 |
| HP-A3 原生 libvirt | 代码完成，待实机验收 | 已用 libvirt P/Invoke 完成 define/start/pause/resume/destroy/undefine、生命周期事件、overlay 与 inventory。 |
| HP-A4 制品引用与回收 | 代码完成，待实机验收 | Runtime、CompetitionPreparation、Rollout、ArtifactVerification 均有明确引用入口；发布预热引用有 24 小时边界。清理只有零引用、无活动 VM backing chain 后才进入队列；Agent 删除后返回目标缓存 inventory，主站确认 `Present=false` 才移除记录。 |
| HP-A5 批量计划与事件 | 代码完成，待实机验收 | Agent 已提供计划 apply/cleanup；同一 runtime/generation/shard 串行、成功后 inventory 回读、非 running 资源拒绝、失败补偿清理。主站在任何节点失败、取消或 inventory 不完整时立即补偿已成功分片，全部事实完整后才持久化 V2 provider 标记。默认关闭。 |
| HP-A6 切换与旧路径删除 | 未开始 | 仅在旧 TeamLab runtime 排空、OVN/libvirt/回收实机验收通过后执行。 |

## 已知阻断

1. 当前生产/验收配置默认关闭 V2，因此仍走旧 `AgentTeamLabNodeExecutor` 逐资产路径；能力门控的 V2 编译与提交代码已接入，但尚未在真实节点开启验收。
2. 尚未对具备 OVN、OVS 和 libvirt 的节点执行真实 S/M/L、并发、重启和控制面短暂中断验收。
3. 旧 bridge/router namespace/dnsmasq 路径在切换完成前必须保留，不能提前删除。

## 本轮实现事实

- V2 资产同时携带不可变 `ImageDigest` 和 Docker 的已解析 `ImageReference`；不能用制品摘要直接作为 Docker 镜像名。
- V2 主站路径由 `TeamLabNetworkConfig.EnableExecutionPlanV2` 控制，并且每个节点必须声明执行计划、OVN/OVS 以及所需 Docker/libvirt 能力；不满足时使用既有路径。
- V2 Docker 采用创建但不启动、完成 Linux veth 与 OVS 接入后再启动，避免依赖固定等待或启动后补网卡。
- 计划失败会按同一 runtime、generation、shard 做一次补偿清理，并以 Agent inventory 作为清理后事实；不会自动重新提交计划。
- V2 合同已拒绝非 SHA-256 计划/制品摘要、未知端口资产引用、重复路由器网络归属；定向测试当前 `7 passed / 0 failed`。
- 缓存清理的 Agent 响应现在携带目标 Docker/VM 缓存 inventory；主站遇到残留会保留分发记录并写入真实失败，不会把“删除请求已返回”当成物理清理完成。
- 多节点 V2 apply 不会先写成功标记：任一分片失败、取消或 inventory 缺失时，已成功分片按相同不可变计划补偿清理，避免外层销毁错误回落到旧路径。

## 本轮门禁证据

- `dotnet build src/GZCTF.slnx -c Release --no-restore`：最终复核通过，0 警告、0 错误。
- `dotnet vstest ... --TestCaseFilter:"FullyQualifiedName~TeamLab|FullyQualifiedName~ImageDistributionServiceTests"`：通过 `284/284`。
- `dotnet vstest ... --TestCaseFilter:"FullyQualifiedName~ImageDistributionServiceTests|FullyQualifiedName~TeamLabExecutionPlanV2Tests"`：通过 `20/20`。
- `git diff --check`：通过。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build`：180 秒未退出，未记录为通过；已停止本次测试宿主。
- `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build`：因本机 Docker Engine 未运行，Testcontainers PostgreSQL 无法连接 `npipe://./pipe/docker_engine`，未形成有效集成测试结论。
- OVN/OVS、KVM、Agent 重启、并发和验收脚本尚未在真实节点执行，不能写为通过。
- 独立质量审查 Agent 因本次运行环境返回额度不足而未启动；本轮仅完成维护者静态复核，仍需在实机验收前补一次独立审查。

## 下一步

1. 在独立节点开启 `TeamLabNetworkConfig.EnableExecutionPlanV2`，完成 OVN/OVS、Docker、VM 和库存回读实机验收。
2. 在独立运行时上完成 V2 apply/repeat/cleanup 和 Agent inventory 实机验收。
3. 完成制品引用释放后的 Agent inventory 确认，再安排旧路径排空与切换。
