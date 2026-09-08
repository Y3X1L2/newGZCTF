# PR #9 最终候选生产发布与验收

> 后续：用户已解除网络限制；只读复核临时题 614、实例和已知容器记录均不存在，活动运行任务为 0。固定发布标签 `stable-20260908` 已建立，最新版本、备份和节点核验以 [稳定基线说明](2026-09-08-stable-baseline.md) 为准。本文停止联网时的限制与证据缺口保留为阶段记录。

## 目标与授权

- 用户于 2026-09-08 明确授权：对 `.27` 做新鲜备份与恢复验证，维护窗口更新主站及本机 Agent，创建、验收并清理临时练习实例。
- 保留现有业务实例、数据和旧 release，不操作 203 网关、9091/18080，不启用 TeamLab/Fabric；远端 Worker 不因摘要不同盲目同步。
- 起始主线 `3a7012697cd3332f2eff1fd3bb409b8953ea313d`，任务分支 `codex/pr9-production-rollout`，使用既有 `611b/newGZCTF` worktree。
- 发布候选为已验证的 `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a`；后续主线提交仅为文档。详细 CI、994 文件 manifest 和归档摘要见 [候选交接](2026-09-07-pr9-runtime-convergence.md)。
- 用户随后要求停止网络连接并自行完成业务测试；最新反馈为“已经测试完成，并且删除了这个题目”。本轮只完成本地交接与 Git 提交，不恢复 SSH、浏览器或靶机测试，不执行远端推送。用户的测试结果与自动化观察分开记录。
- 后续用户明确授权 GitHub 同步和临时文件清理，仍不再测试。仅恢复 Git 远端操作及 `.27` 三个指定临时文件的 SSH 核验/清理；没有再次请求业务 API、浏览器或靶机。

## 执行计划

- [x] 复核远端主线、工作树、管理员会话、现网服务、空间和活动实例。
- [x] 上传并逐文件验证候选，准备独立 release 和显式旧版回退目标。
- [x] 新鲜备份 PostgreSQL、shared/files、配置、旧 release 与服务状态，检查摘要和 catalog。
- [x] 在隔离 PostgreSQL 恢复同一备份，运行候选 bundle，比较迁移与核心数据。
- [x] 维护窗口最终复查、停止写入、原子切换主站及匹配本机 Agent。
- [x] 服务、manifest、前端、关键 API、附件、节点、队列及日志复核，记录后台错误。
- [x] 自动化完成临时练习创建及真实容器入口访问；用户接手后确认测试完成、题目已删除。
- [x] 清理隔离数据库恢复资源和三个指定临时上传文件，提交并推送交接文档；不重复业务测试。

## 已核验现场

- `2026-09-08T07:26Z`：主站 PID 36118、本机 Agent PID 36120，均 active/running、NRestarts 0；未开始切换。
- 三节点 Online/Stable/schedulable，心跳报告运行容器与 VM 均为 0；活动部署票据为 0。数据库保留的 5 个旧 Container 行均为 Destroyed，不删除历史记录。
- `.27` 根卷可用约 47 GiB，shared/files 约 384 MiB，活动 release 约 461 MiB。
- 发布前生产为 `practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237/publish`。本次将该目录保存为回退目标，未沿用原来更旧的 `publish.previous`。
- 已知 health 历史迁移降级预计保留；禁止删除或伪造两条旧 Theory migration 来消除降级。

## 当前状态

生产发布完成；用户确认业务测试完成并删除临时题。追加授权后已清理三个远端临时上传文件并同步 GitHub 文档，没有重新检查删除后的数据库、容器、端口或队列，不把用户反馈写成自动化销毁证据。

- 预备备份目录：`/opt/gzctf/backups/pr9-rollout-pre-ab2bd54b-20260908T080802Z`，7 个备份/证据文件总计 1361850888 bytes；数据库 dump 为 787861510 bytes，catalog 2070 条目且包含 DataProtectionKeys。逐文件摘要保存在 root 保护的 `checksums.json`。
- 初次配置归档因 `.27` 不存在 `/etc/nginx` 失败；按实际服务结构修正后完成，数据库和既有附件备份经校验后复用，未重复或修改生产数据。203 网关不在本轮操作范围。
- `2026-09-08T08:27:26Z` 预备副本恢复通过：无默认路由的独立 PostgreSQL，134 条迁移/head `20260816192540_TeamLabCapabilityClosure`；候选 efbundle 报告无新迁移，恢复前后核心业务计数一致。主要耗时来自完整运维事件/日志历史的索引重建，未删减备份数据。临时恢复容器与数据目录已精确清理。
- 最终 `database-cutover.dump` 与 `shared-files-cutover.tar.gz` 已保存到同一备份目录。停止写入时间 `2026-09-08T08:29:37Z`，新服务于 `08:32:15Z` 可用，停机约 2 分 38 秒。最终数据库副本于 `08:40:36Z` 完整恢复并通过相同 bundle 的无迁移验证，`08:40:39Z` 发布任务成功结束；两次隔离恢复容器和数据目录均已清理。

## 发布与运行证据

- 当前 release：`/opt/gzctf/releases/pr9-converged-ab2bd54b7e16d454e3a8f54960c4bb4689047c4a-20260908/publish`。主站及本机 Agent 均通过 `/opt/gzctf/publish` 启动；没有修改 systemd 服务或网关配置。
- `publish.previous` 已原子指向旧 `practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237/publish`；`publish/files` 指向 `/opt/gzctf/shared/files`。旧 release 和备份保留，未执行真实应用回退演练。
- `08:46Z` 后置核验：manifest 994 个文件长度/摘要一致；主站 PID 143895、Agent PID 143896，均 active/running、NRestarts 0，实际 `/proc/<pid>/exe` 指向候选文件。6 个已服务前端代表文件（含管理练习、学员练习、入口和 API client）摘要与候选一致。
- 首页、登录路由、练习路由、配置 JSON、API docs 与 OpenAPI 正常；OpenAPI 为 83 条路径，未认证 `/api/exercise` 返回 401 JSON。浏览器已有管理员会话在切换后仍有效，未做退出后重新输入密码的登录测试。
- health 的真实端口为 `3001`：`/healthz` 返回 200 / Degraded；业务端口 `8080` 的该路径按当前源码契约返回 404。两条旧 Theory migration 的来源缺口保留，不伪造历史或降低健康门禁。
- Game 23 / Challenge 76 既有共享附件返回 200，253892 bytes，SHA-256 `291b802a28935b082fb3d46a5a2358c167a6f9912e21ef14ccc097a5b34981d6`，与文件记录一致。
- 切换备份、最终恢复副本及业务验收前生产核心计数一致：用户 172、战队 76、比赛 22、比赛题目 110、课程 29、练习 605、理论试卷 4、AWDP 服务 10、Files 217、Attachments 208、镜像 456、节点 3、VM 历史 99、Container 历史 5；迁移 134 条/head `20260816192540_TeamLabCapabilityClosure`。
- 业务测试前活动票据和运行容器均为 0，三节点均 Online/Stable/schedulable、心跳新鲜；未同步 `.30/.31` 的 Agent，保持已验证合同兼容范围。没有改动原有业务实例。
- 完整运行证据保存在服务器备份目录的 `restore-preflight.json`、`database-at-cutover.json`、`restore-cutover.json`、`post-rollout-probe.json`；root 状态文件为 `/opt/gzctf/backups/pr9-rollout-20260908-state.json`。最终备份摘要以该文件为准，本地文档没有手工抄写未回读的摘要。

## 业务验收及用户接手

- 临时题 ID `614`，标题 `PR9-release-acceptance-20260908`。自动化通过管理员 UI 成功创建、编辑并从学员页面发起真实 Docker 生命周期。
- 首次使用旧题目中的 `10.24.0.28:5000/challenges/web-jwt-no-token-easy:v1` 引用，被 Ready 模板门禁拒绝，未创建容器。只修正临时题为模板 154 的正式引用 `gzctf-internal://ctf/imports/01a004de92317f658e7258b646267f2a/02117bc002da3b883cadf753/37466b3f3193f6b5:latest` 后重试成功；没有修改原有题目或模板。
- 成功 create ticket `01a0803a-49e1-7239-b972-bd6995e6519b` 为 Succeeded/Ready，调度到 `.30`；Container 行 `01a0803a-6ef2-759c-b7eb-b21d32f34de4`，内部入口 `.30:42762`，平台公开入口 `203.195.157.191:30000`，EntryStatus=Ready。公网首页和同一实例内部目标页均实际返回页面；端口是该次实例的历史证据，不应当作当前仍可用地址。
- 访问临时靶机认证场景后，用户报告平台安全风险提示并明确要求停止；自动化未提交 Flag、未执行销毁或删除。未确认风险提示的具体触发机制，不把推测作为已证实原因。
- 用户随后确认“已经测试完成，并且删除了这个题目”，作为人工验收完成和题目删除的证据。没有提供逐项提交结果、销毁票据或端口回收输出，因此不宣称自动化已观察到 Accepted 或底层资源清理完成。需要资源级清理审计时，由用户查验题 614 的销毁票据、运行实例和入口回收状态。

## 已知风险与待办

1. 数据保留任务仍有 `teamlab-flow` 分区清理 SQL `42601` 错误。旧版在发布前 24 小时内同类错误按小时出现，两处 logger 各 24 次；本次相关实现相对 `9eef8ac` 没有变更。未调整生产保留策略或删除数据；应在独立缺陷任务中复现并修复 SQL 生成。
2. `08:38:22Z` 指标持久化出现一次 `DbUpdateConcurrencyException`，`08:44Z` 后续心跳和指标时间持续推进，已观察到恢复。该代码也未随本次发布变化，但发布前所查 24 小时未找到同类日志，不能声称已有现场复现。需要后续补并发回归及失败批次重试核验。
3. 后置报告记录 0 条 systemd error、3 条已分类应用 Error（指标 1 条、保留任务 2 条），不是“没有错误”。启动后的两条 node.offline 随即恢复为 node.online；不能将维护窗口过期心跳当作持续离线。
4. SSH.NET 依赖风险、Blob 自动 GC、历史 Theory migration 来源，以及 `.31` 整机重启、KVM/Windows VM、跨节点 TeamLab、AWDP、Portal SSO、回退演练仍沿用现有缺口，未扩大本次验收结论。
5. 追加授权后，核对 `/tmp/gzctf-pr9-rollout.py`、`/tmp/gzctf-pr9-post-probe.py`、`/tmp/gzctf-pr9-rollout-20260908.tar.gz` 均为普通文件，长度分别为 15198、5855、267408582 bytes，SHA-256 与本地副本完全相同；只删除这三个文件并确认均不存在。正式 release、root 备份、`.31` 恢复脚本/drop-in 和本地候选归档均保留。本地副本可重新上传，SSH 辅助会话已正常关闭。
6. 本轮只修改交接与状态文档，差异和本地链接检查通过，不重跑候选已通过的全量构建/测试。追加授权后 fetch 与 ls-remote 确认 GitHub main 仍为 `3a701269...`，本地交接提交 `db989511` 已推送到 `codex/pr9-production-rollout`；清理记录随同文档按正常快进流程同步主线，无 force push。运行源码与发布候选 `ab2bd54b` 无差异，最终文档提交号以 Git 为准。
7. 任务 worktree 暂留以交付忽略目录内的发布包和重放工具，不因任务分支已合并而删除发布物。日常 PVE 独立改动继续保留，其他历史 worktree 不作清理。后续自动化业务/靶机测试仍停止；本次授权仅涵盖 GitHub 同步和指定临时文件清理。
