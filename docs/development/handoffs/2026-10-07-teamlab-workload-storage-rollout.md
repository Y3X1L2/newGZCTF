# TeamLab 分盘容量修复发布与现场验收（2026-10-07）

## 目标、基线和发布身份

用户批准合并、部署与真实验收。PR #16 两项 Quality 成功后正常合并到 `main 1ec26826cf96bcc834c264133c9be2e66e0c882b`，与已审候选 `3efb948cb503e747a6ce75742ca635681fa868f3` 同树；合并后 Quality 37615258348 也 completed/success。门禁详见[候选交接](2026-10-07-teamlab-workload-storage-candidate.md)：Release 构建、1311 单元、305 PostgreSQL 集成、118 前端文件/398 测试及完整前端与内部 OpenAPI 门禁通过。

本次工程收尾分支为 `codex/workload-storage-rollout`。原工作区、worktree、PVE 源机和快照、旧镜像、旧发布、备份与教学资源保留。学习、脚本和原始证据仍留库外。本次没有新迁移、Registry/PVE/公网网关修改。

- `.27` 活动发布：`/srv/yinyu-data/releases/workload-storage-3efb948cb503e747a6ce75742ca635681fa868f3-20261007/publish`，`/opt/gzctf/publish` 原子指向它，`files` 仍链接 `/opt/gzctf/shared/files`。
- Main DLL SHA256：`6a8c29799c6881d93611fdc4fa241a2b5f339a61ed074abf70708782229ebc85`；前端 gitSha `3efb948c`，manifest SHA256 `1659ce36579a74232eabedcadd7da4c0227e68538202da5e81fb9d9773b97b1c`。
- `.27/.30` 实际 Agent SHA256：`d5711da5b04647617a0233a17f72346eebdb076f17975c590d1a7ac4d1797c4a`；`.31` 保持旧 `cc6c5737` / SHA256 `9b2142e9731d77495322e20a100432a728cf3d86f87dc67abebf85ed58a9b3e8`。
- 完整干净 linux-x64 归档 168114049 字节，SHA256 `44c718b7573570f8e21528207ece6ca82cba4c2fc282cce7b51962d1751f508b`。经 GitHub prerelease 中转，服务器重新校验归档/Main/Agent/223 前端资源以及两个 ELF 的 0755 权限通过。

## 备份、切换和回退

新完整备份位于 `.27 /srv/yinyu-data/backups/workload-storage-3efb948c-20261007-pre`，仅 root 可读。包含两个数据库 custom-format dump、附件、完整旧发布、Agent/配置和服务单元。六项关键归档/dump/Agent 摘要校验及两个 dump 全文解码通过。Main 为一致备份暂停 `11:47:01Z–11:50:35Z`，214 秒，随后原 Main/API 恢复正常；Agent 与教学容器持续运行。

源 `gzctf` 数据库约 44.6 GB，完整恢复到仅本任务的 `network=none` PostgreSQL 容器，数据放数据盘。两库全部列结构一致、150 条迁移历史一致、六个核心表行数一致。当前候选声明 148 条迁移，生产多的两条历史 Theory 记录保留；候选无待应用迁移，未向生产恢复或删除迁移行。完整恢复主要耗时约 14 分钟于既有日志与索引。验证后仅销毁该临时容器与副本，新备份、旧发布和原数据库保留；没有在副本启动候选 Main，不能将结构/恢复验证表述为副本应用启动验收。

Main 先切换以支持旧 Agent 缺字段回退，再原子更新 `.27` Agent 入口。窗口 `12:07:56Z–12:08:18Z` 约 22 秒，通过。Main PID 3477834、Agent PID 3478165、均 active/NRestarts 0。回退记录与旧 Main/Agent 均在备份中；旧 Main 软链目标另保存在 `publish.before-workload-storage-3efb948c`，不原地覆盖旧 release。无迁移时回退应用与 Agent，不恢复数据库。

## 现场验收结果

| 项目 | 已证实结果 |
| --- | --- |
| 分盘指标 | `.27` Docker 根卷约 69.5 GiB、VM bind 数据盘约 176–177 GiB，独立 statvfs 与新 manifest 吻合；两 VM 目录同 `/dev/sdb1`，不能相加 |
| 四机 | 正常创建 `01a11644-670e-7c8f-894b-a6c7601a2589`，generation 1，四资产均在 `.27`；现有效 release `01a10261-890a-718f-ae86-250e4db86746`，未改草稿/版本/镜像 |
| 来宾网络 | 五张当前 MAC、接口名、原 IP、前缀、DNS、无默认路由全部回读通过；web/OA 到内网限定 SSH/RPC/RDP TCP 成功，OA 到入口不通，平台 router 0 |
| 启动后调整 | 未手工修网、重启来宾或重置四机。第一次网络验收的 `ip -j -4 addr` 不提供 MAC，属于验收脚本假失败；保留首份失败证据，改为读取含 MAC 的地址信息后仅只读复验通过 |
| Docker | 首次正常调度 `.30` 被其旧 `993f00` Agent 的计划摘要兼容问题拒绝，保留 `01a11648-3986-79a6-b51a-f19c504c8ade` 失败证据。必要升级后正常清理到 Destroyed，未绕摘要/改数据库 |
| Docker 复测 | 新稳定幂等 ledger 正常创建 `01a11652-b1d0-70ef-9892-1941f8c46e75`，在 `.30` Ready；私网 HTTP 200、平台文件往返一致并删除、正常销毁、按 runtime 标签容器残留 0、队列 0 |
| 原资源 | `.30` 原八个容器 ID/PID/开始时间保持；四机 native UUID/磁盘 backing/运行状态保持；模板 519–522 Ready，Registry 四 manifest HEAD 200，`.27` 四缓存仍在 |

原执行范围因实际 Docker 失败收口扩到必要的 `.30` Agent 兼容更新。经正式 `POST /api/v1/nodes/{id}/sync-agent` 同步捆绑候选，自动临时暂停调度并恢复 Stable/schedulable；没有手工 PATCH 调度或数据库。`.30` 旧二进制/配置/服务及库存备份在 `/var/lib/gzctf/agent-backups/workload-storage-d5711da5-20261007`。实际新进程 PID 768859、SHA 一致、active/NRestarts 0；Docker 根卷约 79.4 GiB，KVM 设备不存在，VM 指标 0 不阻断 Docker。本轮 Docker 生命周期验证在 `.30`，不能声称 `.27` 跑过 Docker 新建。

## 本任务镜像预分发副作用与收回

正常四机创建前的发布版本准备流程会向所有在线、可调度且有 KVM 能力的节点预分发，而非仅最终选择的节点。`.31` 此前没有 519–522 分发记录，本次 `12:09:03Z` 新建四条，`12:09:50Z–12:12:27Z` Ready；四文件 mtime 与此一致，已分配约 14.56 GB。新四机实际只运行在 `.27`，但 `.31` 根卷/images/teamlab 同卷，可用量降到 0。这是本任务触发的副作用，不能归为无关旧节点问题。

先独立读取 Agent inventory、libvirt 全部定义、磁盘/backing 与 worker-specific 引用；`.31` VM 定义为 0，四个新增缓存没有运行引用，只有本次新加的 release preparation 引用。`.27` 四图有当前 Runtime 145 的运行引用，旧 release 引用也保留。通过正常幂等 `DELETE /api/open/v1/teamlab/preparations/releases/{releaseId}` 撤销本任务新增的八条准备引用；正式分发清理仅使无剩余引用的 `.31` 四副本进入安全回收。临时最小 scope Token 仅内存使用，正常撤销；没有直接写数据库、全局模板删除、手工 rm 缓存或跳过 Agent backing 检查。

操作 `01a11665-7004-718a-97fc-198759afcf3c` Succeeded 后，`.31` 四条分发记录与四个本次新增文件消失，释放 14561443840 字节，实际可用恢复约 10.73 GiB。其余十五个缓存的路径/长度/分配块/mtime 完全保持，旧 Agent SHA/PID 1898995 不变，没有升级、重启、扩容或清原资源。Registry 主副本及 `.27` 四图缓存/运行保留。

**自动全节点预分发、下载峰值预算与可取消准备的策略缺口尚未修源码。再次创建仍可能触发同样预分发。** 当前指标与调度修复不等同于镜像分发空间保护；不得据此宣称所有容量问题已修复。

## 最终状态和限制

首页、Config、manifest 200；150 条迁移不变、部署队列 0、三节点在线/可调度/Fabric healthy。Main 新 PID 日志窗口有三条 Error，与首次 `.30` 旧摘要拒绝对应；Main/本机 Agent 未处理或 Fatal 为 0，不能写成所有日志 0。

四机保留运行供用户验收，两次自有 Docker 实例正常 Destroyed。没有验收靶机业务、AD 账号/共享、图形登录、公网、双副本、四机重置/销毁或跨节点 VM。新版 GUI 的真实四宽度/主题/键盘验收未完成，不能以旧截图代替。

后续应将“选定节点准备镜像”“临时下载/解压/校验峰值空间”“节点本地 overlay 空间”和运行容量预算衔接；跨节点启动仍需单独修改组放置与实际数据面验收。混合资源请求及两类活动预留仍使用保守规则，可能低估可用空间。库外证据入口：`D:/Work/YINYU-TeamLab-Workload-Storage-20261007/evidence`；服务器受保护 staging/evidence 与原备份保留。学习文档不提交远端。
