# Game 23 Docker 实例卡死排查与修复

更新时间：2026-09-04

## 任务目标

- 修复 10.24.0.27 上 Game 23 / Challenge 19 创建 Docker 实例后长期停留在“正在准备运行环境”的问题。
- 保持 `DeploymentQueueTicket` 为唯一队列事实，失败必须收敛并向前端显示可读状态。
- 使用 10.24 内网完成一次最小创建、入口和销毁验证，不修改公网网关或其他运行实例。

## 基线

- 起始分支：`main`
- 当前任务分支：`codex/fix-game23-docker-provisioning`
- Worktree：`D:\Work\newGZCTF-fix-game23-docker`
- 起始提交 / `origin/main`：`9b8261f6a1c4767703f434f0a075ab1450e319c0`
- 生产 release：`teamlab-phase09-d90e2d1b-20260903T1228Z` / `d90e2d1b65cca693d500a9ee4fb21f9bed6026aa`
- 生产数据库 migration：134 条，head `20260816192540_TeamLabCapabilityClosure`

## 当前状态

- `VERIFIED`：页面调用 `POST /api/Game/23/Container/19` 后创建 ticket `01a06a1a-b41a-74ca-b4c3-862e9fdb80b2`，分配到 `worker-10.24.0.30`，但停在 `Scheduled / NodeExecutionWaiting`，没有 Container 行或入口。
- `VERIFIED`：前序 Exercise Docker ticket `01a069ef-bd0d-79e5-9e72-99a0b2793201` 从 2026-09-04 01:01 UTC 起保持 `Running / NodeExecutionWaiting` 并持续续租 claim；之后的 Challenge 19 ticket 未被执行 worker claim。
- `VERIFIED`：`RuntimeExecutionService.ExecuteScheduledAsync` 对整批 ticket 使用 `Task.WhenAll`；一条长时间不返回会阻止唯一 worker 继续扫描后续 `Scheduled` ticket。
- `VERIFIED`：前序 Exercise 599 的镜像分发记录被反复改为 cleanup；434 条 cleanup 记录因旧远端 Agent 返回不含 inventory 的 200 响应触发 `ArgumentNullException` 热循环，最大尝试数已超过 80,000。
- `VERIFIED`：`DeploymentExecutionContextAccessor` 为 scoped，但 `FleetContainerManager` 创建新 scope，导致 ticket ID 丢失，镜像等待阶段不能投影为 `ImagePreparing/Pulling`，也不能附加当前运行引用。
- `VERIFIED`：Challenge 19 配置为 `10.24.0.28:5000/ctf/web/test-ssti:v1`，其 OCI manifest 与 tag 存在，但未关联或匹配 Ready `ImageTemplate`；当前严格镜像分发路径会明确失败，不能通过手改数据库状态绕过。
- `VERIFIED`：`worker-10.24.0.30` 的 status 与 runtime inventory 接口返回 200，声明 Docker 与 Docker pull 能力；现场问题不是节点离线或容量不足。
- `IMPLEMENTED`：execution context 改为 AsyncLocal 单例；运行 worker 使用有界独立 in-flight；执行 ticket 自动附加镜像生命周期引用；补齐 Exercise 引用 reconcile；旧 Agent 缺 inventory 时 fail closed 并退避；前端增加与后端最长镜像准备时间一致的超时兜底。
- `VERIFIED`：定向后端测试 51/51、完整后端单元测试 912/912、完整前端测试 276/276、Release solution build 与前端 production build 通过。
- `NOT_RUN`：生产部署、远端 Agent 同步、Challenge 19 镜像模板绑定、完整创建到销毁验收。

## 技术方案

- 使用 AsyncLocal 单例传播 execution context，不跨异步请求串扰。
- 将运行执行改为有界、可观察的独立 in-flight 任务；单 ticket 阻塞不再停止后续扫描。
- 从当前 ticket 推导 Game/Exercise/Training/AWDP 镜像引用，并补齐 Exercise 引用 reconcile。
- 对缺失 inventory 的旧 Agent 响应返回明确协议失败并进入退避，不把未知状态伪造为已清理。
- 前端增加准备时长兜底；服务端一旦投影 Failed/Error 即立即停止轮询。
- 通过平台镜像管理/API注册 Challenge 19 的既有 registry 引用；不直接修改生产业务行。

## 本地验证

- `dotnet build src/GZCTF.slnx -c Release --no-restore`：通过，0 error；保留既有 SSH.NET NU1903 警告。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore`：912/912 通过。
- Runtime/Image/Player projection 定向后端测试：51/51 通过。
- `pnpm build`：locale、lint、strict TypeScript、architecture、276 项前端测试、production build 与 bundle budget 全部通过。
- 本机 Docker Desktop 不可用，Testcontainers integration suite 未运行；真实 Docker 链路在 10.24 内网验证。
- `dotnet format --verify-no-changes` 命中仓库既有大量 whitespace/import 规则偏差；未批量格式化或修改无关文件。

## 禁止事项与回滚

- 不直接修改 ticket、Container、ImageDistributionRecord 或 Redis 锁状态，不删除数据库记录。
- 不触碰 `203.195.157.191`、9091、18080，不影响其他比赛实例。
- 生产发布使用独立 release 和原子切换；应用回退到当前 `d90e2d1b` release。若产生测试 runtime，只通过现有生命周期销毁。
