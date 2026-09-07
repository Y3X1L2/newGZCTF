# PR #9 开发基线同步与运行验收

更新时间：2026-09-07

## 目标与授权

- 从 `main@f158a359637e5707f5f450eda7a20c20a201f6f2` 接续 PR #9 审计。
- 同步本地开发基线，保留未合并功能提交。
- 特权只读核验 `10.24.0.27` 的制品、数据库历史和 health；用户确认既有密码可用于 sudo。
- 用户明确授权 SSH 检查并修复 `10.24.0.31`；凭据不保存至脚本、Git、日志或本文。
- 准备主站发布候选、隔离验收和回退方案；正式切换前完成发布范围确认。
- 保留现有实例与业务数据，不操作 203 网关和 9091/18080，不自动启用 TeamLab/Fabric。

## 阶段

- [x] 核对当前 worktree、远端和已有审计记录，创建 `codex/pr9-runtime-convergence`。
- [x] 同步本地 main 与日常功能分支，保留独立提交。
- [x] 核验主站 release、manifest、主站/Agent/前端摘要与数据库历史。
- [x] 确认 health 降级的具体组件与原因。
- [x] 检查 .31 故障，实施最小修复并验证心跳和调度事实。
- [ ] 准备同提交完整发布物及隔离业务验收。
- [ ] 记录上线条件、回退步骤、最终基线和未验收项。

## 当前证据

- 初始 GitHub main 与本任务工作区为 `f158a359`；本地 main 为 `bbd5a5d4`。
- 日常分支 `codex/pve-storage-safeguards` 有独立提交 `e65b7d5f`，工作树干净。
- 本地 main 已快进到 `f158a359`；日常分支以 merge commit `8ff0d4c1` 同步主干，保留 PVE 文档与 `e65b7d5f`，应用源码与主干一致。
- 主站活动目录为 `practice-validation-9eef8ac.../publish`，shared files 链接正确，manifest 的 372 个文件长度与 SHA-256 全部匹配，无缺失或不匹配。
- 主站 DLL SHA-256：`165a76d2978a87aa80dc73bdeef8a7194e861ec0ef234c95073bf56221aff2f3`；本机 Agent：`2f12bca5b9befb6d0312e5e1ab76d0f1d517338b63071df716f5999e6cdfb7b8`。
- 只读查询数据库并反射活动 DLL 的 migration attributes，调用该 DLL 的 `DatabaseHealthCheck.EvaluateMigrationState`：expected 132、applied 134、pending/newer 均为空，`Degraded` 原因是保留两条旧 Theory 历史。未启动另一主站或执行 migration。
- `.31` 自 2026-09-05 启动后持续报 `SocketException (99)`；配置要求监听 `100.127.0.1:5443`，但 `gzmgt0` 与地址已随重启消失。现场无 Docker 容器，有一台已关机 VM，均保留。
- 在 `.31` 备份 `/var/backups/gzctf-agent-guest-network-20260907` 后，安装配置驱动的恢复脚本和 `20-guest-network.conf` drop-in。Agent 启动恢复，认证 status 200、PID 704786、NRestarts 0，主站重新收到心跳。
- 恢复后发现主站 AgentUpdateState 仍为 Failed，实际调度仍被阻止；用户提供已登录的管理员浏览器，从正式节点页提交 `.31` 同步。
- 同步使用配置中的旧公网下载地址，下载阶段较慢，最终于 `2026-09-07T14:21:45Z` 成功；另通过内网维护接口复核 Agent/传感器已经是相同摘要，返回 already up to date。主站节点状态为 Online、IsSchedulable=true、AgentUpdateState=Stable，心跳摘要与本机 Agent 同为 `2f12bca5...`，Fabric 仍 Disabled。未直接改写节点数据库状态。
- 新脚本 6 项单元测试通过，并加入 Quality CI；只恢复来宾管理网络，不启用 TeamLab/Fabric。

## 发布包实测与新增阻断

- `f856bc89` 完整发布包构建成功，前后端 manifest SHA 一致，994 个文件摘要全部通过。其 CI run `34132973273` 完整通过。
- 本机隔离 PostgreSQL、Redis、嵌套 Docker 29.8.0 和发布包主站/Agent 实测通过：Linux bundle 创建 132 条迁移、主站无默认出口路由、真实 Agent 心跳、管理员登录、资产上传/幂等/冲突/限制、动态 Docker 创建、TCP 入口、实例 Flag 提交和销毁。
- **P1 阻断，代码已修复**：练习创建/更新返回 EF 实体，带 Flag 时响应形成 `Flags.Exercise.Flags` 循环；事务已提交但 HTTP 返回 500，可能诱发重复创建。实际发布包的容器转附件流程复现；新增两项真实 HTTP 集成测试在旧代码上均得到 500，DTO 修复后 2/2 通过。
- 修复使用既有 `ExerciseManagementModel.FromExercise` 作为创建和更新响应，与管理详情契约一致。原 `f856bc89` 包不得用于主站正式切换；修复验证后重新构建独立候选。
- 本机 Docker Desktop 启动曾受失效 Unix socket 阻塞；保留两个仅含零字节 socket 的目录为 `run.pr9-backup-20260907` 和 `docker-secrets-engine.pr9-backup-20260907`，用户重新打开后引擎正常。未恢复出厂设置或删除镜像、数据卷。
