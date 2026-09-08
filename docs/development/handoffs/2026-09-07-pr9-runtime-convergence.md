# PR #9 开发基线同步与运行验收

更新时间：2026-09-07

> 后续状态：2026-09-08 已获授权并发布 `ab2bd54b` 到 `.27`；两份备份恢复验证通过，用户确认测试完成、临时题已删除。当前交接以 [生产发布记录](2026-09-08-pr9-production-rollout.md) 为准。本文“尚未部署/待授权”等表述保留为发布前历史证据。

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
- [x] 准备同提交完整发布物及隔离业务验收。
- [x] 记录上线条件、回退步骤、最终基线和未验收项。

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
- 最终复核 `.31` PID 705090、NRestarts 0，重复恢复不改变网桥 ifindex，原 VM 仍 shut off。网桥 flags 包含 UP；无来宾连接时的 NO-CARRIER/operstate DOWN 不代表地址恢复失败。未重启整机。
- `2026-09-07T15:27Z` 再次只读核验：三个节点均 Online、Stable、schedulable，Fabric 均 Disabled；`.30` 心跳摘要仍为 `3747f3535da88623...`，心跳报告 1 个容器（此前为 2 个）。本任务未向 `.30` 下发停止、删除或同步命令，不为摘要一致强制重启业务。

## 发布包实测与新增阻断

- `f856bc89` 完整发布包构建成功，前后端 manifest SHA 一致，994 个文件摘要全部通过。其 CI run `34132973273` 完整通过。
- 本机隔离 PostgreSQL、Redis、嵌套 Docker 29.8.0 和发布包主站/Agent 实测通过：Linux bundle 创建 132 条迁移、主站无默认出口路由、真实 Agent 心跳、管理员登录、资产上传/幂等/冲突/限制、动态 Docker 创建、TCP 入口、实例 Flag 提交和销毁。
- **P1 阻断，代码已修复**：练习创建/更新返回 EF 实体，带 Flag 时响应形成 `Flags.Exercise.Flags` 循环；事务已提交但 HTTP 返回 500，可能诱发重复创建。实际发布包的容器转附件流程复现；新增两项真实 HTTP 集成测试在旧代码上均得到 500，DTO 修复后 2/2 通过。
- 修复使用既有 `ExerciseManagementModel.FromExercise` 作为创建和更新响应，与管理详情契约一致。原 `f856bc89` 包不得用于主站正式切换；修复验证后重新构建独立候选。
- 本机 Docker Desktop 启动曾受失效 Unix socket 阻塞；保留两个仅含零字节 socket 的目录为 `run.pr9-backup-20260907` 和 `docker-secrets-engine.pr9-backup-20260907`，用户重新打开后引擎正常。未恢复出厂设置或删除镜像、数据卷。

## 最终候选与验证

- 代码提交 `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a` 已推送；其父提交 `f856bc89` 包含节点启动恢复。后续文档提交不改变候选运行代码或 manifest 身份。
- 归档：`artifacts/releases/pr9-converged-ab2bd54b-20260907/pr9-converged-ab2bd54b-20260907.tar.gz`，267408582 bytes。
- 归档 SHA-256：`08eabb9ea00040e30cfcd10ec2a53ef68b3679dd970e36a90f41d3d2f4288c31`。
- 发布 manifest 与前端 manifest 均标记完整 `ab2bd54b...`；994 个文件长度和 SHA-256 全部匹配。Linux 主站、Agent、传感器、Supervisor 和真实 Linux efbundle 均由完整发布流程生成。
- 正确候选 Quality run [34137354005](https://github.com/Y3X1L2/newGZCTF/actions/runs/34137354005) 全部成功：Release build、973 项单元、278 项集成、6 项网络恢复测试、迁移模型、7 组查询计划、OpenAPI 向后兼容、前端 87 文件/280 项及构建预算。误触发旧 head 的 run `34137275723` 已取消，不作为候选证据。
- 使用最终发布包重新完成全部隔离验收，输出 `RELEASE_VALIDATION_PASSED`：独立 Docker 29.8.0、空库 bundle 132 条迁移、主站无默认出口路由、真实 Agent 心跳、管理员登录、资产上传/幂等/冲突/授权/下载、动态练习 Docker 创建/TCP 入口/实例 Flag 提交 Accepted/正式销毁，以及容器改附件、多 Flag、共享文件删除一题后仍可读、引用中资产删除返回 409。
- 可重放脚本 `artifacts/pr9-runtime-convergence/validate_release.py` 与结构化报告 `artifacts/pr9-runtime-convergence/release-validation-result.json` 保留在本机忽略目录，不提交制品或测试认证数据。复跑命令为 `python -u artifacts/pr9-runtime-convergence/validate_release.py artifacts/releases/pr9-converged-ab2bd54b-20260907/publish`。测试使用独立嵌套 Docker，不向主站挂宿主 Docker socket；资源前缀仍为 `pr9v-f856bc89`，报告中的被测提交为 `ab2bd54b...`。
- 测试容器、网络和数据卷已清理并复核无残留。本机 Docker 引擎保持运行。
- 相对生产 `9eef8ac...` 无 migration/Designer/snapshot 变化；本轮未删除两条旧 Theory migration，也未改变 health 判定。既有 SSH.NET NU1903 告警和 Blob 自动 GC 缺口继续单列处理。

## 主站发布方案与回退

以下为待批准的维护窗口方案，不是已执行的生产操作。

1. 确认 `.27` 主站及本机 Agent 的停机窗口、验收人和回退决策人；另行明确允许创建并清理一题临时练习及其 Docker 实例。重新核对活动票据、镜像工作、容器、VM 和节点状态；保留现有业务，不自动排空 `.30`。
2. 在 `.27` 保存新鲜 PostgreSQL custom dump、迁移历史、核心计数、shared/files、应用配置与服务配置、当前 release 身份及必要入口状态；备份由受限权限保护，不输出凭据。检查大小、摘要和 `pg_restore -l`，在隔离副本实际恢复，并用这份候选的 efbundle 验证前向结果和核心数据。不能用本轮空库测试替代新鲜生产备份恢复验证。
3. 上传上述已验证归档，校验归档摘要，解包至以完整 `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a` 标识的独立 release。核对 994 个受控文件，配置从当前活动环境受控继承；`publish/files` 必须指向 `/opt/gzctf/shared/files`，不得原地覆盖旧目录。
4. 确认生产当前为 134 条历史、head `20260816192540_TeamLabCapabilityClosure`。本次没有新迁移，预期 bundle 无待应用项；若与副本结果不符立即停止。保留两条未恢复的旧 Theory 历史，不伪造、删除历史或修改 health 门禁。
5. 在窗口内停止写入及主站/本机 Agent，原子切换 release 后启动与候选匹配的本机服务；检查 PID、重启次数、manifest、首页、登录、真实 JSON API、附件摘要、日志、迁移、节点 inventory、队列和镜像状态。health 的已知历史迁移 `Degraded` 预期保留，但任何新降级或故障必须单独判断，不能只看 HTTP 200。
6. 在批准范围内完成真实生产练习创建、Docker 入口访问、Flag 提交、销毁及测试内容清理，核对现有实例不受影响。远端 Agent 按合同兼容性分阶段同步；`.31` 保留启动恢复 drop-in，`.30` 不为 SHA 相同盲目重启。203 网关、9091/18080、TeamLab/Fabric 不在当前变更范围。
7. 回退目标明确为当前 `/opt/gzctf/releases/practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237/publish`，不是当前更旧的 `publish.previous=3e5526dc...`。切换前保存该目标；失败时停止写入、保留证据、原子切回并恢复匹配配置与本机 Agent。无数据库结构变化时不为应用回退覆盖新业务数据；若出现不兼容数据变化，须按批准的数据库恢复方案处理。

## 完成边界与下一步

- 已在推送前重新核对远端未前进，将验证后的代码与文档快进推入 `main`；核对时本地 main、GitHub main 与任务分支均为 `8aff8f33bd25f2c50d856d95daa176c6ce85116f`。之后仅追加收尾文档，不改候选 `ab2bd54b` 的运行代码；文档提交使用 `[skip ci]`，完整候选 CI 证据仍为 run `34137354005`。
- 日常 `codex/pve-storage-safeguards` 已正常 merge 主线，保留独立提交 `e65b7d5f` 与 3 行 PVE 现场记录，源码与主干一致；该功能分支未推送，不删除或强制对齐其 SHA。其他历史 worktree 未改动。
- 已删除本任务在 `.27` 上传的 `/tmp/gzctf-pr9-migration-probe.dll` 与 `.31` 的 `/tmp/gzctf-pr9-guest-network.py`、`.conf`，本地原文件和正式安装可用于恢复；正式脚本、drop-in、root 备份及发布归档均保留，SSH 诊断会话已关闭。
- `.31` 在线恢复和平台正式同步已完成；`.27` 主站未部署、未重启，PID 36118、本机 Agent PID 36120、NRestarts 均保持 0。生产仍是 `9eef8ac...`，未包含本次 DTO 修复和 PR 审计后的运行修复。
- 发布候选已完成完整 CI 和隔离真实 Docker 链路，不等同于生产已切换或所有基础设施签收；三方尚未统一。
- 尚待：主站正式切换授权、新鲜生产备份副本恢复验证、生产练习写入/实例链路验收和回退演练；`.31` 整机重启持久性、KVM/Windows VM、跨节点 TeamLab、AWDP、Portal SSO 与公网入口专项验收不在本轮完成范围。
- 原任务 worktree 暂留以交付忽略目录中的发布归档、报告和重放脚本；在完成制品交接前不得按普通已合并分支清理流程删除它。
