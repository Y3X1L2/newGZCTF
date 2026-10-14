# 镜像分发与回收第一批候选

## 任务目标与基线

用户要求把压力分到执行节点，收口镜像分发、存储和回收，并至少同时启动两套测试环境；允许分步骤完成。代码由子代理实现，主会话协调、审计和整合。可靠制品按题目需要保存，不要求主站永久保留重复文件。代码部署仍须候选可审查后由用户确认。

起点 `origin/main 2ba6e8d8`，整合分支 `codex/image-lifecycle-integration`，worktree 为 `C:/Users/Cloud/.codex/worktrees/workload-storage-integration/newGZCTF`。原 `D:/Work/newGZCTF` 的 `codex/pve-storage-safeguards` 未改。子任务分支和工作树保留，尚未合并 main，不清理未合并工作。

## 当前状态

第一批实现和代码门禁 `IMPLEMENTED / VERIFIED`；节点迁移 `VERIFIED`；新候选部署和真实双环境生命周期 `NOT_RUN`。不把下载容量修复称为旧 Windows 来宾网络故障的唯一根因或修复证明。

| 环节 | 本期结果 | 下一步 |
| --- | --- | --- |
| 普通流程按需分发 | 导入、发布、CTF/课程绑定、试运行及 rollout 不隐含全节点铺镜像；原 selected-node Ensure 和统一队列保留 | 现场证明未选节点没有新分发记录、文件和临时字节 |
| 下载与失败 | 实际文件系统原子并发预算、VM 分块守卫、续传/命中缓存、Docker 保守预算及期限；错误类型贯通运行票据 | 现场低空间、并发、冷/热缓存验收 |
| 使用关系与回收 | 正常/重复销毁释放尾部，重置与后续管理员预热保护，热加资产保护正式来源；共享 writer 完结清记录 | 真实两消费者、销毁再建及实际底盘保护 |
| 执行节点容量 | `.31` 500 GiB 独立数据盘迁移和冷启动通过，保持停调度 | 部署匹配 Agent，核验容量/契约/fabric 后恢复 |
| 完整退役与保留期 | 已明确缺口，未实施 | 旧模板退役/迁移、有限期预热与冷缓存、受控 Registry GC |

主站负责目录、使用关系、调度与统一任务；节点从独立 Registry 直接获取制品，负责本机执行与安全清理。仍可启动的题目必须有可靠来源；最后一个运行实例销毁不等于题目退役。不会把可被驱逐的 Worker 缓存当作唯一来源。

## 实现及审计依据

- 按需准备 `963fff96`：[子任务交接](2026-10-08-image-demand-distribution.md)。外部准备状态新增 `onDemand`；缓存计数保留真实事实，`ReadyToStart` 允许提交按需启动，精确容量与放置仍由部署队列判定。管理员页面使用真实 planner 判断。
- 引用回收 `bd9b6815/fe3fb255`：[子任务交接](2026-10-08-image-cache-reclamation.md)。正式制品保护与节点需求分开，保持实际 VM/backing 最后检查；当前不可变历史版本仍无限保护制品，退役须另立契约。
- Agent 容量、错误链和 writer 期限 `5edcff59/810b660c/318657b0/a42b60e4`：[子任务交接](2026-10-08-agent-image-storage-guard.md)。VM `.part` 改名不算两份；实际已落盘空间和未来新增字节不重复计。Docker 8 GiB 是可配保守预算，不是任意镜像展开峰值保证。
- 共享任务生命周期 `7ace9c98/d91a74f8`，测试同步 `435c8f1f`：[交接](2026-10-08-image-singleflight-lifecycle.md)。实际 writer 自清理，不依靠等待者；预取消请求不创建 writer。唯一等待者退出后不会永久复用旧完成结果。测试等待实际共享 Task 终态，修正首次全量的非确定完成判断；20 次独立进程压力回归由子任务记录。
- OpenAPI `99721302`：从实际 TestServer 重新生成，仅说明文本变化，现行两份 API 文档同步 `onDemand` 等语义；无 DTO 形状、迁移或前端生成类型变化。
- `.31` 基础设施 `aaee8ca9`：[迁移交接](2026-10-08-node31-data-storage-migration.md)。数据盘、路径、备份、完整校验、精确重复回收、冷启动和回退均在该记录。

## 验证

| 项目 | 结果 |
| --- | --- |
| `git diff --check` | 通过 |
| solution Release build | 通过；现有依赖/分析器共 48 条告警，不是本任务新依赖 |
| 全量单元 | 1385/1385，0 skip |
| 全量集成 | 306/306，0 skip，含真实 PostgreSQL/Testcontainers；首次两处 OpenAPI 说明不一致已修复后完整重跑 |
| 前端 | 完整 locale/lint/严格类型/架构/测试/Vite/体积门禁通过；后续变化为后端与文档，未重复前端测试 |
| Agent 专项真实设施 | Linux tmpfs/路径别名共同预算；隔离 Docker 正常 pull、digest 命中、空间拒绝；故意卡流 2 秒超时后下一 pull 成功，daemon 记录取消 |
| `.31` 迁移 | 全部 23 文件完整双 SHA/大小/权限/属主/mtime；自动挂载与冷启动、旧 Agent status/inventory、主站 DB Online/false/节点票据 0 |
| 新候选两套实际场景 | NOT_RUN：须部署确认后进行，不能用上述专项代替 |

原始验证和脚本只保存在 `D:/Work/YINYU-Image-Lifecycle-20261008`、`D:/Work/YINYU-TeamLab-Image-Storage-Guard-20261008` 及服务器受保护目录；完整发布物和凭据不进入 Git。

## 发布与两套环境验收

发布准备位于仓库外的 `D:/Work/YINYU-Image-Lifecycle-20261008/deployment-prep/`。先固定源码身份、生成一次完整 Linux publish、核主站使用的 Agent 摘要与包内字节一致、前端 manifest 和制品完整性，提交推送后再请求部署确认。部署使用新完整备份、已实际恢复验证的数据库副本、新 release 目录和原子软链接，保留旧发布及回退；不执行历史一键脚本。

部署主站和 `.27/.30/.31` Agent 后，`.31` 在实际容量、摘要/契约和 fabric 检查通过前保持 `IsSchedulable=false`。新 `.31` 数据盘实际约 391.1 GiB 可用；旧 Agent 当前仍误报根盘约 99.45 GB，扩盘不代表调度已经就绪。

验收目标：

- AD 新版 DHCP 场景：topology `01a0fb68-906f-7169-84f6-d185625e13d1`，release `01a0fb6b-20bf-7db4-a25a-796aec18142b`，模板 504/505；每机 2 CPU/4096 MiB/20480 MiB。节点 `.31`，地址池 `192.168.216.0/22` 划 `/24`，DNS source `dc01`，主机 offset 10/20。仍使用旧导入格式，未因此删除源文件。
- Lab2：模板 519–522，原 web 双网卡、其余内网、平台不额外打通两网段，节点 `.27`。读冻结执行版本后核对资源与地址池，不依赖旧文档猜参数。

源码目前未消费 create constraints 的偏好节点字段，不能伪造 PreferredNode。测试脚本用正常管理员节点调度设置逐套选择，并保存/恢复原设置；同时保留两个实例，再验网络、限定 TCP/HTTP、重置、销毁后再建。用户已允许关闭当前测试实例；具体需释放的旧地址由平台正常操作处理并记录，源 PVE 机器和快照保留。未验域业务或图形登录时明确标 `NOT_RUN`。

## 回收后续与旧格式清单

17 个旧格式模板实际本地约 81.94 GiB，14 个旧 Registry 元数据可查到；2/4/117 未查到。完整清单只在本地 `legacy-image-review.md`。ID 1 仍被题目 40 等五题、四练习和历史场景引用；用户允许关闭实例不等于批准退役这些内容，本轮未删除任何模板。

下一批先确认保留哪些旧题目/AD版本，对要保留的迁成标准制品；对要退役的提供影响预览和不可再启动状态，再收口旧本地导入路径。随后补预热 owner/期限/取消、冷缓存保留、临时文件对账、运行增长与下载预算协同及受控 Registry GC。Registry manifest 删除不等于 blob 空间立即释放；PVE thin pool 也不能仅靠 guest `df` 守卫。

同一 LAN 组内资产跨节点放置仍是独立后续能力。本期两套环境分别运行在两节点，不能称已实现一套四机拆分或故障接管。
