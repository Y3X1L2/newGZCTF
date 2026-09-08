# YINYU 当前开发状态

更新时间：2026-09-08

本文件只记录已经核对过的当前事实、已知缺口和下一任务入口。历史计划、阶段审查和现场流水放在 `docs/archive/implementation-records/`，不得用来判断当前代码或服务器状态。

## 1. 基线

| 项目 | 当前事实 |
| --- | --- |
| 仓库 | `https://github.com/Y3X1L2/newGZCTF.git` |
| 稳定分支 | `main` |
| 固定发布标签 | `stable-20260908` → `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a`，annotated tag 已推送 GitHub；定义可追溯的主站发布基线，不移动标签 |
| 当前生产基线 | 2026-09-08 已发布 `pr9-converged-ab2bd54b7e16d454e3a8f54960c4bb4689047c4a-20260908`；`09:49Z` 再次核对主站及本机 Agent 对应 `ab2bd54b`、manifest 994 文件匹配、shared/files 链接正确；数据库 134 条 migration，head `20260816192540_TeamLabCapabilityClosure`。用户确认测试完成、题目已删除；只读复核临时题/实例/容器记录均不存在、活动票据和运行容器为 0 |
| 应用回退基线 | `/opt/gzctf/publish.previous` 已指向 `practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237/publish`；新鲜备份 `/opt/gzctf/backups/pr9-rollout-pre-ab2bd54b-20260908T080802Z`，包含停止写入后的数据库与附件备份、恢复和后置报告；旧版本和备份均保留 |
| 当前开发基线 | `main`；PR #9 审计修复、`f856bc89` Agent 启动恢复与 `ab2bd54b` 练习写接口 DTO 修复已纳入本地及 GitHub 主线；`ab2bd54b` 同提交完整候选通过 CI 与隔离真实 Docker 验收。新任务仍须从实时 `origin/main` 创建任务分支，不将代码合并等同生产发布 |
| 工作树结构 | 不记录某台工作机的瞬时路径；并行任务按 `AGENTS.md` 使用独立 worktree 和分支，不将服务器目录作为代码基线 |
| 技术栈 | .NET 10、ASP.NET Core、EF Core、PostgreSQL、Redis、React 19、TypeScript、Vite、pnpm |

开始新任务必须重新执行 `git fetch origin --prune`、读取 `git status` 和 `git log`。本表中的 SHA 不替代实时 Git 状态。

## 2. 当前功能边界

### 已存在的代码能力

- **CTF 赛事**：赛事、战队、题目、附件、静态/动态 Flag、Docker、KVM/Windows VM、提交、计分和榜单。
- **理论考试**：题库、组卷、单选/多选/判断、草稿、最终提交、成绩和答案回顾。
- **培训课程**：课程、共同教师、报名审核、章节、资源、实验、课后理论、学习进度和学员详情。
- **自主练习**：`/practice`、题库浏览、筛选、来源导入、附件、多 Flag、Docker 实例、提交、统计和后台题库管理；实例继续复用 `DeploymentQueueTicket`。
- **AWDP**：服务、轮次、Checker、攻击、修补、重置、恢复、停止、计分和日志；真实攻击/修补流程按人工验收文档执行。
- **运行底座**：Docker/KVM 节点、镜像模板、镜像导入与分发、容量预留、统一部署队列、实例、事件、日志和恢复。
- **TeamLab**：场景草稿、校验、不可变发布版本、试运行、混合资产、执行计划 V2、OVN/OVS 数据面、访问授权、远程运维、链路策略、连接器、设备包、资源池、流量和抓包基础能力。
- **身份与通用管理**：本地登录、Portal SSO、用户、战队、学员组、系统设置、个人主页和主要管理页面。

### 前端事实

正式前端入口位于 `src/GZCTF/ClientApp/src/vnext`。路由注册以以下文件为准：

- `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- `src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`

新增页面必须使用 vNext 壳层、feature API adapter、CSS Module 和语义 Token；未实现能力显示真实空态，不加载旧页面套壳，不伪造数据。

## 3. 运行架构

```text
浏览器
  -> 主站 Contracts / Application / Domain
       -> PostgreSQL：业务和运行状态事实
       -> Redis：缓存、租约、协调和高频缓冲
       -> Runtime / Fleet / VM / TeamLab ports
            -> AgentClient -> GZCTF.Agent -> Docker / KVM / 网络工具
```

- Controller 只处理协议、授权、用例调用和 HTTP 映射。
- 跨模块读取使用公开 query contract，写入使用 application command。
- Docker、VM、培训、AWDP 和 TeamLab 运行任务共用 `DeploymentQueueTicket`。
- Agent 只执行已校验的本机操作，不读取比赛、课程、计分或权限实体。
- 运行恢复以数据库事实和 Agent inventory 为依据，不从日志文本反推业务状态。

## 4. 已知缺口

这些事项不能在文档中写成“已上线”或“已签收”：

1. 自主练习审计修复和 DTO 修复已随 `ab2bd54b` 切入 `.27`。自动化观察到临时题创建/编辑、`.30` Docker 创建和实际入口访问；用户接手后确认测试完成并删除题目。网络限制解除后，只读复核题 614、实例及已知容器记录均不存在，节点报告运行容器为 0；没有重新提交 Flag 或复测公网端口。回退演练和内容运营验收仍未执行。
2. Phase 09 TeamLab networking 已在 10.24 前向迁移，`9eef8ac` 已包含的相关修复继续保留在当前生产 `ab2bd54b`；Game 23 Docker 创建、入口和销毁链路已实测。双 Worker 故障接管、长期流量留存、复杂服务注入、规模并发和完整跨节点 TeamLab 场景仍需现场签收。
3. Windows VM 仅按比赛场景支持；平台使用镜像内固定 RDP 账号，不要求普通比赛使用 Cloudbase-Init。仍需对合格镜像完成双实例、RDP/Guacamole、剪贴板、隔离和销毁清理验收。
4. AWDP 的真实攻击、修补、异常恢复和安全软件干扰场景由授权测试人员按 `docs/yinyu-awdp-manual-acceptance.md` 手工执行。
5. 统一认证对接方的门户源码不在本仓库；平台保留 Portal SSO 适配，跨网联调需在目标环境验证。
6. `main` 已恢复经历史 DLL 证实的 `20260814075023_AddAssetAndChallengeOwnership` 与 `20260815012026_AddExerciseCreatorTracking`；`20260604165857_AddTheoryExamEntities`、`20260604193010_SyncTheoryExam` 仍未在源码、可达 Git 历史或保留 DLL 中恢复，禁止伪造。生产已通过 TeamLab 生命周期销毁经授权的 `qqqtest1` 两条测试 runtime，并完成 Phase 09 前向发布；今后数据迁移仍必须先在新鲜生产备份副本验证。
7. `teamlab-flow` 分区保留任务存在 SQL `42601` 语法错误，发布前已按小时发生，本次未修复。另在新服务启动后观察到一次指标持久化并发冲突，之后心跳/指标恢复；需要独立并发回归。既有 SSH.NET 依赖风险与 Blob 自动 GC 缺口继续保留。

## 5. 已验证环境事实

- 2026-08-25 核对 10.24 环境：活动 release 的 manifest 标记提交为 `d2cf79b`，但实际 `GZCTF.dll` 摘要与 manifest 不一致，说明该环境曾进行后端制品热替换；该 release 不作为开发基线。
- 同日发现活动 release 的 `files` 错误指向旧 release 的私有目录，导致数据库仍有记录、shared 中也存在实体文件，但 `/assets/*` 返回 404。在线链接已原子修正为 `/opt/gzctf/shared/files`，发布脚本已固定 shared 路径并增加回归断言。
- Game 23 共核对 31 个本地附件，shared 中缺失数为 0；题目 76 附件和示例 `challenge.md` 均从客户端返回 200，内容长度与 SHA-256 正确。
- 2026-08-31 已复核统一发布：10.24 的 `release-manifest.json.gitCommit` 等于 `stable-20260831` 所指提交，manifest 内主站和 Agent 文件摘要与磁盘一致，`publish/files` 指向 shared，主站与 Agent 无重启循环。
- 2026-09-01 已将 Phase 09 TeamLab networking 合并提交 `1a390432b1135da055a5a8488575fd10015f0bbd` 推入 `main`；本地 Release build、905 项后端单元测试、275 项前端测试、前端生产构建和 OpenAPI 生成契约测试通过。完整集成测试因本机 Docker Desktop 无法启动而未完成。
- 同日只读复核 10.24：主站与 Agent 服务为 `active/running`，首页、健康端点和公开 OpenAPI 返回 200；运行前端 SHA 仍为 `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`，公开 OpenAPI 为 69 条路径，尚未包含本次新增的 connectors、resource-pools 和 device-packages 路由。
- 2026-09-03 已在 `codex/migration-drift-reconciliation` 从历史 DLL 恢复两条 creator migration，并在 PostgreSQL 16 副本中完成生产备份恢复、空库完整 bundle 和生产备份前向 bundle 验证。详细结论见 `docs/development/handoffs/2026-09-02-migration-drift-reconciliation.md`。
- 同日已在发布前备份 `/opt/gzctf/backups/teamlab-release-pre-migration-20260903T080343Z` 后，以完整事务将 6 个授权 `EnvironmentJson` 配置值清为空对象、4 个授权 `RoutingEnabled` 配置值设为 false；未删除 TeamLab 行、比赛绑定、runtime、队列或其他业务数据。清理后备份在隔离 PostgreSQL 16 容器成功恢复并执行当前 `main` bundle 至 `20260816192540_TeamLabCapabilityClosure`，核心业务表计数保持一致；隔离容器和 volume 已删除。
- 2026-09-03/04：获得追加授权后，`qqqtest1` 的两条测试 runtime 均通过平台生命周期销毁；原 pending create ticket 为 Cancelled，destroy ticket 为 Succeeded。生产库无 pending TeamLab ticket，所有现存 TeamLab runtime 为 Destroyed。
- 同期建立并校验新鲜回滚备份 `/opt/gzctf/backups/teamlab-runtime-converged-pre-migration-20260903T122601Z`；用其隔离副本复跑 `d90e2d1b` 发布包的真实 glibc bundle，成功从 124 条 migration 前向至 134 条和 `20260816192540_TeamLabCapabilityClosure`。生产随后用同一 bundle 执行 10 条前向 TeamLab migration，并原子切换至 `teamlab-phase09-d90e2d1b-20260903T1228Z`。release manifest 的 994 个文件及本机 Agent 摘要均已核对一致，主站、Agent、PostgreSQL、Redis、首页、health、OpenAPI、API docs、共享附件、节点 inventory 和队列均正常。
- 发布后生产的用户 172、比赛 22、比赛题目 110、课程 29、理论试卷 4、AWDP 服务 10、附件 217 与新鲜备份一致。`ExerciseChallenges` 为 590，而新鲜备份为 446；152 条 ID 大于 446 的新增行均为公共练习题，且本次 10 条 TeamLab migration 不向该表插入或回填数据。该业务增量未作处置，不能表述为 migration 导致的数据变化。
- 2026-09-04 已修复 Game 23 / Challenge 19 Docker provisioning 卡死：运行执行改为有界独立 in-flight，execution context 可跨嵌套 scope 传播，镜像引用和 cleanup 退避闭环，前端增加准备态最终超时。生产 ticket `01a06bda-d552-7c35-bd90-8bc21372ac39` 在约 3 秒内完成并调度至 `worker-10.24.0.30`，页面显示入口且 10.24 内网端口返回 200；Stop ticket `01a06be2-5347-7232-bd85-77122ee955b9` 成功，相关测试 Container 行和 Docker 资源均已清理。
- 同次验收发现并修复 Agent 两阶段同步门禁、心跳 `xmin` 与审计保存竞争、TeamLab `Enable` 配置持久化，以及本机 Docker 缺少 Agent inventory 标签的问题。三个节点均为 Online、Stable、schedulable，Agent SHA 前缀均为 `3747f3535da88623`；历史 image cleanup 记录从 301 条收敛为 0。生产本机 TeamLab control-plane 目标仍明确禁用，远端 Fabric 状态因此为 Disabled，不能表述为 TeamLab Fabric 已验收。
- 本次发布前回滚备份为 `/opt/gzctf/backups/agent-sync-pre-0a3e1c63-20260904T080316Z`；custom dump SHA-256 `03f7e38a120dcb586f5095b3cf6e7b1c22d7ebbae37a780ef51e0021d819d088`，`pg_restore -l` 可读 2,041 个条目，134 条 migration。最终核心计数为用户 172、战队 76、比赛 22、比赛题目 110、课程 29、练习题 605、理论试卷 4、AWDP 服务 10、附件 217，与该备份一致。
- 203 公网网关的 Nginx、WireGuard、动态 port-map timer 与 9091/18080 业务独立；本次只更新网关同步器所需配置，不重启或改动 9091/18080 进程。
- 2026-09-06：`codex/practice-deployment-validation` 的运行候选
  `9eef8ac12c626672081e81fadbde39946e7d2237` 在 fork Actions run
  `33979724855` 通过前端 280 项、后端单元 971 项、集成 275 项、迁移模型、
  7 组查询计划、OpenAPI 向后兼容和完整 Linux release 构建。发布 manifest
  精确覆盖 372 个文件，前端与 release SHA 一致；发布脚本已修复 Linux dotfile
  漏记问题。
- 同一候选在 `10.24.0.27` 的无路由 network namespace 中完成隔离验收：空库
  bundle 生成 132 条实际可发现迁移，主站以 `www-data` 且无 Docker/libvirt/KVM
  文件描述符启动，首页、health、OpenAPI、注册登录、权限拒绝、资产上传幂等/
  冲突、练习导入 operation 和附件摘要回读通过。经 SHA256SUMS 验证的
  `agent-sync-pre-0a3e1c63-20260904T080316Z` 备份副本保持 134 条历史迁移，
  候选 bundle 报告无待应用迁移，迁移前后用户 172、战队 76、赛事 22、赛题
  110、课程 29、练习 605、理论试卷 4、AWDP 服务 10、文件 217、镜像 456
  均不变。
- `20260802023000_RemoveDestroyedTeamLabUdpMappings.cs` 缺少 Designer/迁移元数据，
  不属于上述 132 条 bundle migration；不能把文件存在误报为可执行迁移。严格
  隔离下内部 `/api/Exercise` 会因 Controller 构造时初始化 Docker provider 而在
  无 socket 条件返回 500，因此生产副本上的既有练习列表与真实 Docker/KVM/
  TeamLab 执行链仍为 `NOT_RUN`。本次未挂生产 Docker socket，未切换生产；所有
  测试进程、容器、volume、release 和目录已删除，随后生产 release、PID、服务
  重启次数、HTTP 状态、迁移与核心计数复核不变。
- 2026-09-06 生产维护窗口：在确认活动部署票据、容器、VM 和 TeamLab runtime
  均为 0 后，停止主站与 Agent，于服务器保存发布前备份
  `/opt/gzctf/backups/practice-deployment-pre-9eef8ac-20260906T032027Z`。备份总计
  1,359,448,361 bytes，数据库 dump 776,051,891 bytes、2,063 个 catalog 条目，
  SHA-256 为 `fa62f3473dc3693fb23a1be4ae1c0285e6800289b3a2670160b6c9904d26284d`；
  同时保存 schema、迁移历史、核心计数、旧 release、共享文件、应用配置、
  systemd 和 Nginx，`SHA256SUMS` 全部通过且 catalog 包含 DataProtectionKeys。
- 同一窗口中候选 bundle 报告 `No migrations were applied`，迁移保持 134/head
  `20260816192540_TeamLabCapabilityClosure`，备份前后核心计数一致；随后原子切换
  `/opt/gzctf/publish` 至 `practice-validation-9eef8ac.../publish`，旧 `3e5526dc`
  release 保留在 `/opt/gzctf/publish.previous`。服务停止于 `03:20:27Z`，新 release
  于 `03:26:46Z` 健康，维护窗口约 6 分 19 秒。
- 发布后主站 PID 36118、Agent PID 36120，均 active/running、NRestarts 0；主站、
  登录页、练习路由、配置、OpenAPI、API docs、health、metrics 和共享附件摘要检查
  通过。manifest 372 个文件，主站/Agent/前端摘要与提交 `9eef8ac` 一致；数据库仍为
  用户 172、战队 76、赛事 22、赛题 110、课程 29、练习 605、理论试卷 4、AWDP
  服务 10、文件 217、镜像 456，活动票据和镜像工作均为 0，journal 无 error。
- 节点现场状态为 Local Server 与 `worker-10.24.0.30` 在线可调度；
  `worker-10.24.0.31` 心跳已中断约 25.9 小时，早于本次发布，不归因于切换。
  browser-harness 因现有 Edge 未允许 remote debugging 而无法附着，未改用其他
  浏览器工具；因此真实登录和 Docker 练习实例仍为 `NOT_RUN`。本次只切换
  `10.24.0.27` 主站与本机 Agent，未同步远端 Worker Agent。
- 2026-09-07 PR #9 独立审计完成：原 head `bd1e546e` 以 merge commit
  `c615e61d` 纳入 `main`，并追加 `3e4bd99f` 修复容器题转附件题时服务端残留
  运行字段、`51c2884f` 强化 PostgreSQL 并发上传、OpenAPI 敏感字段、隐藏文件
  manifest 和空白门禁。Release build、973 项单元、276 项集成、前端 87 文件/
  280 项测试和生产构建均通过；main push run `34094573626` 也完整成功，没有
  migration、Designer 或 snapshot 变化。
- 同日获得 sudo 授权后完成生产特权复核：主站 PID 36118、本机 Agent PID 36120
  均保持 `active/running`、`NRestarts=0`；372 个 manifest 文件全部匹配，主站/Agent/
  前端仍为 `9eef8ac` 制品，shared 链接正确。活动 DLL 的迁移判定为 expected 132、
  applied 134、pending/newer 均为空；health `Degraded` 已证实来自保留的两条旧
  Theory 历史，不是有待应用迁移。未改写历史或降低健康门禁。
- `.31` 离线根因为主机重启丢失 `gzmgt0 / 100.127.0.1/16`，GuestManagement HTTPS
  绑定失败导致 Agent 重启循环。保存 `/var/backups/gzctf-agent-guest-network-20260907`
  后安装启动前网桥/专属 nft 恢复脚本与 systemd drop-in，并经已登录管理员页面调用
  正式 Agent 同步流程，清除阻止调度的 Failed 状态。最终 PID 705090、NRestarts 0，
  Online/Stable/schedulable，原关机 VM 保留；未整机重启，Fabric 保持 Disabled。
- `2026-09-07T15:27Z` 三节点均 Online/Stable/schedulable；`.27/.31` Agent SHA
  前缀同为 `2f12bca5b9befb6d`，`.30` 仍为 `3747f3535da88623` 且有 1 个心跳报告的
  容器。未同步 `.30` 或操作其现有实例；版本差异按兼容性管理，不为 SHA 一致停业务。
- 最终发布候选 `ab2bd54b` 修复内部练习 POST/PUT 返回 EF 实体造成的循环序列化
  500，两项新增真实 HTTP 回归在旧代码上失败、修复后通过。Quality run
  `34137354005` 全部成功：973 单元、278 集成、6 网络恢复测试、前端 280 项、
  迁移模型、7 组查询计划与 OpenAPI 向后兼容。完整 Linux 发布物 994 个文件全部
  摘要匹配；隔离 PostgreSQL/Redis/真实 Agent/嵌套 Docker 完成登录、资产授权、动态
  练习创建/入口/提交/销毁、附件转换与共享 Blob 引用保护验收，测试资源已清理。
  上述为发布前候选验证记录，详情和维护窗口方案见
  [运行收敛交接](handoffs/2026-09-07-pr9-runtime-convergence.md)。
- 2026-09-08 授权维护窗口：预备备份和停止写入后的最终备份均在无默认路由的独立
  PostgreSQL 实际恢复，候选 bundle 无待应用迁移，134 条历史及核心计数一致。主站和
  本机 Agent 于 `08:29:37Z` 停止、`08:32:15Z` 恢复，停机约 2 分 38 秒；最终副本于
  `08:40:36Z` 验证通过，原子回退指针保存旧 `9eef8ac`。两次恢复临时资源均已清理。
- `08:46Z` 核验新主站 PID 143895、本机 Agent PID 143896、NRestarts 0；994 文件
  manifest、实际进程文件、6 个前端代表文件与候选一致，已知附件 253892 bytes 摘要
  匹配。OpenAPI 83 路径、JSON API 授权拒绝正常，管理员已有会话有效。指标端口
  `3001/healthz` 为 200 / Degraded，业务端口 `8080/healthz` 按源码限制为 404。
  数据保留错误及一次指标并发冲突已分类记录，不能写成“日志无错误”。
- 临时练习 614 首次因旧镜像地址未注册 Ready 模板而创建失败；只修正临时题为模板
  154 正式引用后，create ticket `01a0803a-49e1-7239-b972-bd6995e6519b` 成功，`.30`
  容器入口实际可访问。用户要求停止联网后，自行完成测试并确认删除该题。之后只做
  本地文档收尾，没有再次请求靶机、提交 Flag 或检查远端清理。
  完整备份、运行证据、人工接手范围和待办见
  [生产发布交接](handoffs/2026-09-08-pr9-production-rollout.md)。
- 用户追加授权 GitHub 同步和临时文件清理后，仅核验并删除 `.27` 的三个本任务
  `/tmp` 上传文件，摘要与本地副本一致，删除后确认不存在，SSH 会话正常关闭。
  正式 release、备份和本地归档保留；文档按正常 Git 流程同步，不再执行业务测试。
- 用户随后解除网络限制并要求建立稳定基线。`09:49Z` 只读复核 release、994 个文件、
  实际进程、回退指针和最终备份摘要均一致；主站 PID 143895、Agent PID 143896、
  NRestarts 0。三节点 Online/Stable/schedulable、心跳新鲜，题 614 与对应实例/容器
  记录均已删除，活动票据和运行容器为 0。已推送固定标签 `stable-20260908` 指向
  `ab2bd54b`；main 与该提交仅文档不同，未打开靶机或修改生产。Worker Agent 仍为
  混合版本（`.27` `d93cf212...`、`.30` `3747f353...`、`.31` `2f12bca5...`），已知 health
  降级与数据保留 SQL 错误仍存在。详见 [稳定基线说明](handoffs/2026-09-08-stable-baseline.md)。

## 6. 当前有用文档

- 总体架构和目标：[平台架构与产品总纲](../platform-commercialization-master-plan.md)
- 文档入口：[文档导航](../README.md)
- AI 交接：[AI 开发与交接规范](../development/ai-development-playbook.md)
- 模块边界：[模块边界图](../commercialization/module-boundary-map.md)
- 外部接口：[Open API v1 指南](../commercialization/open-api-v1-guide.md)
- 生产发布：[生产发布与回滚手册](../operations/vnext-maintenance-window-rollout.md)
- Windows VM：[简明部署指南](../operations/windows-vm-quick-deployment-guide.md)
- AWDP：[人工验收指南](../yinyu-awdp-manual-acceptance.md)
- TeamLab：[功能说明](../commercialization/teamlab-networking-feature-guide.md)

## 7. 新任务起点

当前接手入口为 [stable-20260908 稳定基线](handoffs/2026-09-08-stable-baseline.md)，部署证据见 [生产发布交接](handoffs/2026-09-08-pr9-production-rollout.md)。主站运行代码与本地/GitHub 主线一致，main 后续 SHA 差异来自文档；远端 Worker Agent 保持兼容的混合版本。网络限制已由用户解除，本轮仅版本和运行状态只读核验，不开展靶机测试。后续优先用独立任务修复数据保留 SQL 缺陷、补指标并发回归，按新候选、新标签发布，不原地覆盖当前稳定基线。

1. 同步远端并确认当前分支、工作树和 HEAD。
2. 阅读本文件、`docs/README.md`、`AGENTS.md` 以及任务涉及模块的现行契约。
3. 先从源码、真实路由、API 和测试确认事实，不引用归档文档中的旧路径或旧状态。
4. 代码、测试和必要文档在同一提交中闭环；部署时记录备份、发布物、冒烟和回滚信息。
5. 任务结束后只更新本文件的当前事实，不追加聊天流水。
