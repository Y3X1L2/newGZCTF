# Game 23 Docker 实例卡死排查与修复

更新时间：2026-09-04

## 任务目标

- 修复 10.24.0.27 上 Game 23 / Challenge 19 创建 Docker 实例后长期停留在“正在准备运行环境”的问题。
- 保持 `DeploymentQueueTicket` 为唯一队列事实，失败必须收敛并向前端显示可读状态。
- 使用 10.24 内网完成一次最小创建、入口和销毁验证，不修改公网网关或其他运行实例。

## 基线

- 起始分支：`main`
- 源码修复分支：`codex/fix-game23-docker-provisioning`
- 现场收敛分支：`codex/fix-agent-sync-concurrency`
- Worktree：`D:\Work\newGZCTF-fix-game23-docker`、`D:\Work\newGZCTF-fix-agent-sync`
- 起始提交 / `origin/main`：`9b8261f6a1c4767703f434f0a075ab1450e319c0`
- 最终 `main` / 生产提交：`3e5526dc1ce336ac5545faacd49a9c0d1ec7ab58`
- 生产 release：`docker-provisioning-inventory-3e5526dc-20260904T093342Z`
- 生产数据库 migration：134 条，head `20260816192540_TeamLabCapabilityClosure`

## 当前状态

- `VERIFIED`：页面调用 `POST /api/Game/23/Container/19` 后创建 ticket `01a06a1a-b41a-74ca-b4c3-862e9fdb80b2`，分配到 `worker-10.24.0.30`，但停在 `Scheduled / NodeExecutionWaiting`，没有 Container 行或入口。
- `VERIFIED`：前序 Exercise Docker ticket `01a069ef-bd0d-79e5-9e72-99a0b2793201` 从 2026-09-04 01:01 UTC 起保持 `Running / NodeExecutionWaiting` 并持续续租 claim；之后的 Challenge 19 ticket 未被执行 worker claim。
- `VERIFIED`：`RuntimeExecutionService.ExecuteScheduledAsync` 对整批 ticket 使用 `Task.WhenAll`；一条长时间不返回会阻止唯一 worker 继续扫描后续 `Scheduled` ticket。
- `VERIFIED`：前序 Exercise 599 的镜像分发记录被反复改为 cleanup；434 条 cleanup 记录因旧远端 Agent 返回不含 inventory 的 200 响应触发 `ArgumentNullException` 热循环，最大尝试数已超过 80,000。
- `VERIFIED`：`DeploymentExecutionContextAccessor` 为 scoped，但 `FleetContainerManager` 创建新 scope，导致 ticket ID 丢失，镜像等待阶段不能投影为 `ImagePreparing/Pulling`，也不能附加当前运行引用。
- `VERIFIED`：Challenge 19 的 registry manifest 与 tag 存在；已通过平台管理界面建立 Ready Docker 模板 `471 / game23-ssti` 并保存题目，使当前内部镜像引用可由 legacy matching 精确识别，未直接修改数据库。
- `VERIFIED`：`worker-10.24.0.30` 的 status 与 runtime inventory 接口返回 200，声明 Docker 与 Docker pull 能力；现场问题不是节点离线或容量不足。
- `IMPLEMENTED`：execution context 改为 AsyncLocal 单例；运行 worker 使用有界独立 in-flight；执行 ticket 自动附加镜像生命周期引用；补齐 Exercise 引用 reconcile；旧 Agent 缺 inventory 时 fail closed 并退避；前端增加与后端最长镜像准备时间一致的超时兜底。
- `VERIFIED`：首次生产部署后，两个远端 Agent 的两阶段同步暴露了三个额外问题：首阶段错误要求第二阶段才产生的 VM managed capabilities；失败审计会携带 stale WorkerNode `xmin`；目标 data-plane 明确 disabled 时仍强制 Fabric Healthy。三项均已修复并保留失败闭环。
- `VERIFIED`：Agent 配置同步现持久化 `TeamLab.Enable`；本机 `DockerManager` 现写入 `ManagedBy=GZCTF`、runtime generation 等 inventory 标签，避免新容器被 recovery 误判 missing。
- `VERIFIED`：最终完整后端单测 917/917，完整前端 276/276，Release solution build 和 production publish 通过；定向 Runtime/Image/Player、Fleet/Agent 和本机 Docker 标签测试均通过。
- `VERIFIED`：三节点均为 Online、Stable、schedulable，Agent SHA 前缀均为 `3747f3535da88623`；旧 image cleanup 记录从 301 条收敛为 0。
- `VERIFIED`：用户创建 ticket `01a06bda-d552-7c35-bd90-8bc21372ac39` 在约 3 秒内 Succeeded/Ready，调度到 `worker-10.24.0.30`；页面显示入口，10.24 内网端口返回 200。Stop ticket `01a06be2-5347-7232-bd85-77122ee955b9` Succeeded，相关 Container 与 Docker 资源已清理。
- `VERIFIED`：标签修复前创建的本机孤儿容器由用户第二次创建时触发的既有 pre-create 生命周期先行销毁；最终两个测试 Container GUID 均无数据库行，目标和全局 active ticket 为 0。
- `NOT_RUN`：本机 Docker Desktop 不可用，PostgreSQL/Docker Testcontainers integration suite 未运行；未触发 AWDP、Windows VM 或新 TeamLab runtime。
- `OPERATOR_ONLY`：用户确认页面返回入口并打开平台提供的公网入口；Codex 只验证 10.24 内网端口，不访问或修改公网网关。

## 技术方案

- 使用 AsyncLocal 单例传播 execution context，不跨异步请求串扰。
- 将运行执行改为有界、可观察的独立 in-flight 任务；单 ticket 阻塞不再停止后续扫描。
- 从当前 ticket 推导 Game/Exercise/Training/AWDP 镜像引用，并补齐 Exercise 引用 reconcile。
- 对缺失 inventory 的旧 Agent 响应返回明确协议失败并进入退避，不把未知状态伪造为已清理。
- 前端增加准备时长兜底；服务端一旦投影 Failed/Error 即立即停止轮询。
- 通过平台镜像管理/API注册 Challenge 19 的既有 registry 引用；不直接修改生产业务行。
- Agent 更新先验证二进制固有能力，配置和 managed artifacts 下发后再验证完整能力；审计写入不携带被心跳并发更新的 WorkerNode。
- Fabric 健康门禁以实际下发的 `TeamLabDataPlane.Enabled` 为准；目标明确 disabled 时不伪造 Healthy，也不阻断 Docker/KVM Agent 同步。
- 本机 Docker 和远端 Agent 创建路径使用同一 inventory 标签契约。

## 本地验证

- `dotnet build src/GZCTF.slnx -c Release --no-restore`：通过，0 error；保留既有 SSH.NET NU1903 警告。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore -p:CollectCoverage=false`：917/917 通过。
- Runtime/Image/Player projection 定向后端测试：51/51 通过。
- Fleet/Agent、本机 Docker 标签定向后端测试：36/36 通过。
- `pnpm build`：locale、lint、strict TypeScript、architecture、276 项前端测试、production build 与 bundle budget 全部通过；每次正式 release/delta publish 也重复通过这些前端门禁。
- 本机 Docker Desktop 不可用，Testcontainers integration suite 未运行；真实 Docker 链路在 10.24 内网验证。
- `dotnet format --verify-no-changes` 命中仓库既有大量 whitespace/import 规则偏差；未批量格式化或修改无关文件。

## 生产发布与验收

- 新鲜回滚备份：`/opt/gzctf/backups/agent-sync-pre-0a3e1c63-20260904T080316Z`；数据库 dump SHA-256 `03f7e38a120dcb586f5095b3cf6e7b1c22d7ebbae37a780ef51e0021d819d088`，732 MB，`pg_restore -l` 可读 2,041 个条目；release、shared files、schema、migration history 和 SHA256SUMS 均校验通过。
- 完整 release `docker-provisioning-final-f78361e6-20260904T083529Z` 以 archive SHA-256 `58edbb67e17db4786c743370c33d14120fe0c860996f0b086e849cd7d8ce601a` 原子发布；随后两个受控 delta 只替换经 SHA 校验的 `GZCTF.dll` 与 manifest。
- 最终 release `docker-provisioning-inventory-3e5526dc-20260904T093342Z`，manifest Git SHA `3e5526dc1ce336ac5545faacd49a9c0d1ec7ab58`，994 个 manifest 文件；当前 `GZCTF.dll` SHA-256 `c85a1e35822fb31e009927591441cc218b158ff6006a1dd099f8db6ad1f6241c`。
- 所有 bundle 执行均返回 `No migrations were applied`；生产保持 134 条 migration，head `20260816192540_TeamLabCapabilityClosure`。
- 发布后 `/`、`/health`、OpenAPI、API docs 均返回 200；主站和本机 Agent active，PostgreSQL accepting connections，Redis `PONG`，发布后主站/Agent error 级日志为 0。
- 登录管理员会话有效；Game 23 附件下载返回 200、16,504 bytes，内容 SHA-256 与数据库记录一致。
- 最终核心计数：用户 172、战队 76、比赛 22、比赛题目 110、课程 29、练习题 605、理论试卷 4、AWDP 服务 10、附件 217，与本次新鲜备份一致。

## 禁止事项与回滚

- 不直接修改 ticket、Container、ImageDistributionRecord 或 Redis 锁状态，不删除数据库记录。
- 不触碰 `203.195.157.191`、9091、18080，不影响其他比赛实例。
- 生产发布使用独立 release 和原子切换；当前紧急应用回退点为 `/opt/gzctf/releases/docker-provisioning-converged-77ae1757-20260904T091328Z/publish`，更早完整 release 和 `d90e2d1b` release 仍保留。该回退版本会重新引入本机 Docker inventory 标签缺口，回退后必须暂停新实例创建。
- 数据级最终回滚点为 `/opt/gzctf/backups/agent-sync-pre-0a3e1c63-20260904T080316Z`；本次没有 schema 变化，正常回滚只切换应用 release，不执行 EF Down、手改 migration history 或生产 restore。
- 生产本机 TeamLab control-plane 当前明确 disabled，远端 Fabric 也为 Disabled；本次 Agent 同步成功不等于 TeamLab Fabric 已验收。
