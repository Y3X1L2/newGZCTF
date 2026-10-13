# TeamLab 管理后台集成：main 合并与 .27 发布

## 任务目标与状态

用户明确授权合并 PR #11 到 main，并部署到 `10.24.0.27`。状态：**complete / VERIFIED（下述限定范围）**。学习笔记只留本地；本轮未提交学习资料、制品、镜像或凭据。

## Git 与发布身份

- PR：<https://github.com/Y3X1L2/newGZCTF/pull/11>，正常 merge，保留来源历史。
- main 发布提交：`3090b98a04eecb74fbe47de36d10d3e56d5c47b4`；与 PR head `b486e70358e7560b07a2d9f2425bc3edbf94046b` 的源码树相同，tree `35fc5718a22b68a9b6f919ee1ca7eb99260c0320`。
- 记录分支：`codex/teamlab-admin-redesign-rollout`，复用本会话附属 worktree `C:/Users/Cloud/.codex/worktrees/teamlab-windows-network-review/newGZCTF`。原主工作区、其它会话工作树与学习工作树保留。
- 当前活动目录：`/opt/gzctf/releases/teamlab-admin-3090b98a-20261004/publish`；经 `/opt/gzctf/publish` 原子链接切换。
- 主站 DLL SHA256：`2846d6f795a5e8796b7c3dca6997e30735ed52c62cde2f7639cc1ff68b0f332a`；前端 manifest gitSha 为上述 main 提交。
- .27 Agent 保留已部署的 `cc6c5737`，实际、进程与包内 SHA256 均为 `9b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8`；PID `1650376` 前后不变。Agent 与执行共享契约源码等价，不重复升级。`.30/.31` 未部署，仍为旧 Agent。
- 372 个制品文件经逐项校验。压缩包 SHA256：`3bda330f70b43c352c33a4503aa54200a4f5bc45ce81e8f4f521992e09763802`。统一关闭发布符号后完成 linux-x64 publish，修正嵌套 publish 删除共享 PDB 的构建参数问题，没有改源码。

## 备份、迁移和回退

- 新备份：`/opt/gzctf/backups/teamlab-admin-3090b98a-20261004`。两个数据库完整 dump 全文可读，附件/Agent 等五项摘要通过；运行配置仅留服务器受保护备份。旧发布与原备份保留。
- 发布前完整恢复到本任务新建验证库 `gzctf_admin_rollout_validation_20261004`。旧库已应用较新的受管网络迁移，但缺较早的 `20260930164900_AddSystemLogResourceIndex`。
- EF 全历史 idempotent SQL 在验证库解析旧 `20260808075003_AddTeamLabScopedWebhookEvents` 时，因 UPDATE 末尾缺分号失败。**该全历史脚本没有在生产执行**，没有修改旧 migration 或历史记录。
- 实际采用 EF 官方仅生成缺失索引 Up 的 idempotent delta：从 `20260927121625_AddTeamLabNetworkDnsAsset` 到 `20260930164900_AddSystemLogResourceIndex`。脚本 SHA256 `0a4303d2695a90ea81dfeba1e2b243b82352903f0734a084ebd97151ebcd83bb`，独立 migration-plan 绑定发布身份；不要使用包 manifest 中准备阶段全脚本摘要替代它。
- 副本升级 102.09 秒：迁移 149→150，pending 0，核心表行数和全列结构不变，父索引及五个分区索引 valid/ready。生产停主站写入后只应用此新增索引，原子切换及基本健康检查共 140.2 秒。Agent 和原 VM 持续运行。
- 验证完成后仅删除本任务新建的验证数据库，归属及无活跃连接已核对；生产库、所有原备份保留。**未删除日志。**
- 回退目录：`/opt/gzctf/releases/teamlab-nic-ready-cc6c5737-20261003/publish`。仅新增索引，旧主站兼容；回退切链接并启动旧主站即可，不删除索引、不降级数据库。已准备并 `bash -n` 检查 `/var/tmp/yinyu-admin-rollout-20261004/rollback.sh`；未进行实际回退演练。
- 原配置的内容、权限和所有者，以及 `files -> /opt/gzctf/shared/files` 均保持。公网网关、Registry 和 PVE 原机/快照未改。

## 验收结果

| 项目 | 证据与结果 |
| --- | --- |
| main CI | [Quality run 37188421017](https://github.com/Y3X1L2/newGZCTF/actions/runs/37188421017)，success，head 3090b98a |
| 后端 | 合并候选完整 Release 构建、单元 1284/1284、集成 304/304 通过；main 源码树相同，main CI 再次通过；本地没有重复完整后端测试 |
| 前端 | 本地 locale/lint/type/architecture/113 文件 376 测试/build/manifest/体积门禁通过 |
| 数据库 | 新鲜完整副本恢复、缺失索引升级、生产六索引 valid/ready、迁移 150、pending 0 |
| 服务 | Main/Agent active、NRestarts 0；首页和 Config 200；部署以来 systemd journal 中 Exception/fail 行计数均为 0（非所有日志系统全量审计） |
| API | 管理员登录；场景、发布版本、运行状态、控制入口、对象日志、节点、镜像 519–522 Ready 通过 |
| 远程入口 | DC01 与旧 Windows 的本任务 VNC 会话正常创建、取得连接地址、关闭；未保存连接 Token URL，未做桌面登录 |
| 保留四机 | 同一 runtime `01a1022f-7e91-7ad3-8309-833af4771430`、generation 4，四 VM 原生身份不变；部署前后 QGA/MAC/IP/DNS/路由及限定 TCP 通信均 passed |
| Docker 真实链 | 模板 495，通过正常场景校验/发布/试运行创建独立私网实例，仅 .27；HTTP health 200；本任务 /tmp 文件上传下载逐字节一致、删除；正常销毁且 Docker 容器残留 0；.30/.31 调度原值恢复 |
| 新版 GUI | 实际管理员登录、场景设计、试运行列表、运行概览、资产、网络与流量、活动记录正常显示；四资产地址/状态一致，键盘 Enter 切换资产页通过；390/1366/1920/2560 宽度 document 横向溢出均为 false；日夜主题实看、浏览器 error/warn 0 |

Docker 最终验收 runtime 为 `01a106ae-0451-7ef4-890a-4513137c196b`，已 destroyed。本任务临时拓扑 `01a10635-378e-703f-a7a1-c6db6e0cfed5` 及不可变发布版本保留作复用记录，没有运行资源。过程中试验脚本曾漏幂等 header、误用 VM 身份规则检查 Docker、过早检查 HTTP；这些失败轮次均正常销毁。最终脚本按 Docker 标签校验真实归属、设置私网请求不走代理并有界等待，第三次 HTTP 检查通过。没有据此声称平台需要代码修复。

原四机没有重置或销毁。web 为 `10.66.0.15/24` + `172.22.1.15/23`，DC `172.22.1.2/23`、DNS `127.0.0.1`，OA `172.22.1.18/23`，旧 Windows `172.22.1.21/23`、DNS `172.22.1.2`；均无默认网关，平台没有新增跨段路由器。

最终读取：队列 0；根盘约 77.7 GiB 可用，独立 images/teamlab 盘约 80.2 GiB 可用。节点 API 全在线、可调度，远端 VM 占用和预留为 0。

## 证据位置与限制

- 服务器：`/var/tmp/yinyu-admin-rollout-20261004`；核心 JSON 为 `deployment-proof.json`、`clone-proof.json`、`clone-cleaned.json`、`api-acceptance.json`、`vm-network-before/after.json`、`final-preflight.json`、`final-closure.json`、`smoke/result.json`。
- 本地仓库外：`D:/Work/YINYU-TeamLab-Admin-Rollout-20261004`，选定非敏感回执在 `evidence/`，UI 截图与复核结果也在该目录。制品、现场脚本与原始回执不进 Git。
- 本轮未做四机重新创建/重置、AD 普通用户业务登录、图形 RDP/VNC 登录、跨节点故障接管、公网服务入口或 AWDP/SSO 验收。原四机连续性验证和临时 Docker 生命周期不能替代这些验收。
- 现场 UI 是运行工作区的限定检查，不是完整端到端视觉回归。浏览器 full-page 截图不可用，保存视口截图并核对实际图片尺寸；超宽截图可受捕获区域限制。reduced-motion 仅沿用上游 fixture 证据，未在现场重复。
- `.30/.31` 的旧 Agent 不具备本次逐网卡策略能力；新进阶网络场景继续明确使用 .27。
- 全历史 idempotent SQL 解析缺口、Agent 容量上报读取根盘而非镜像盘等原缺口仍存在，本次未扩范围修复。下一位执行维护任务前先核对运行链接、远端 SHA、存储与队列，不直接沿用历史记录。
