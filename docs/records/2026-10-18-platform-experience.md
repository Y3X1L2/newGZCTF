# 平台显示与交互改进

## 目标与授权

本轮根据用户提供的九张截图改进运行概览、节点详情、侧栏滚动条、全站按钮反馈、管理员删除用户和 TeamLab 拓扑及启动页面。用户授权在 `10.24.0.27` 部署与验证；本轮不得合入 `main`，等待用户验收。只对隔离测试数据执行业务冒烟，不清理用户现有测试账号或运行环境。

## 基线与分工

- 起始 `origin/main`：`be2d321f`，2026-10-18 重新 fetch。
- 集成分支：`codex/platform-experience-20261018`；独立 managed worktree。
- 概览与节点：`codex/experience-dashboard-20261018`。
- TeamLab：`codex/experience-teamlab-20261018`。
- 用户删除：`codex/experience-user-delete-20261018`。
- 集成负责人负责共享按钮、侧栏、练习/培训操作反馈、总体验证、PR 与部署。子任务各自独立 worktree，使用 GPT-6.1 Sol / high。

## 交付状态

状态：实现、全量测试、部署和限定业务验收完成。[Draft PR #35](https://github.com/Y3X1L2/newGZCTF/pull/35) 保持待人工验收，不合并主线。已发布源码为 `1c9941ba46be2d3235e8240a86b075cf2572eda8`，后续文档提交不改变制品来源；代码检查基线 `72c65b07`（仅多一笔 Provisioning 文档）远端 Quality 前后端均通过。

1. 运行概览使用真实节点指标、容量及可说明采样范围的趋势；低价值空区块收敛为摘要，技术能力详情按需展开。
2. 共享交互增加按压、焦点、等待状态与重复提交保护；耗时实例操作明确反馈，侧栏保持可滚动但移除粗大滚动条。
3. 管理员删除用户正确处理战队与历史引用，补后端和数据库回归；不直接删除生产账号。
4. TeamLab 简化连接路径、移除卡片彩色侧条、整理版本启动操作层级。
5. 集成后执行前后端与文档门禁，检查日夜主题、键盘、减少动效和 390/1366/1920/2560 宽度。
6. 推送候选，备份及恢复验证后使用独立 release 原子切换，回读服务/API/节点/队列并验证可清理业务链路。出现发布阻断则回退并记录。

## 验证证据

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| 截图与基线 | VERIFIED | 九张用户截图、实时 origin/main、当前模块文档 |
| 前端门禁 | PASS | `1c9941ba` 全量 `pnpm build`：locale、lint、严格 TypeScript、架构、128 文件 / 469 测试、Vite、manifest 与包预算通过 |
| 后端 | PASS | Release solution build：0 error、100 条现有警告；单测 1531/1531；实际 PostgreSQL Testcontainers 全量集成 374/374、0 skip |
| 删除数据库 | PASS | 子任务实际 PostgreSQL/HTTP 回归覆盖普通成员、队长交接、空队保留、历史分数、匿名身份、用户名复用、Cookie/API token 失效、事务后撤销失败和迁移恢复；集成门禁另记 |
| 文档自动检查 | PASS | 静态、模块索引、影响映射及 `git diff --check` 通过，最终发布后再核验 |
| 浏览器 | PASS | 本地候选连接真实 API；概览日夜、390/1366/1920/2560 无横向溢出；3 秒延迟下刷新显示 busy/disabled；键盘焦点与 reduced-motion 通过。最终四机拓扑避开卡片并分离双网卡长共线；生产新版概览与设计页已回读 |
| 部署 | PASS | 双库/附件备份、完整隔离恢复、同字节 SQL 151→152 演练通过；北京时间 10-18 03:15:06–03:15:20 切换主站，stop→HTTP 14.041 秒 |
| 真实账号删除 | PASS | 独立新建普通账号及其唯一战队，队长删除 200；旧 Cookie 401、新登录 401、管理查询 404，空队自动锁定；随后清理唯一测试战队，SQL 验证匿名行、凭据/个人资料清除及退队 |
| 真实 Docker 链路 | PASS | 课程4 / 题88 Header-Relay：创建202、统一队列Succeeded、入口Ready且HTTP200、当前实例Flag重复提交Accepted200；停止202、8秒后Succeeded，API和数据库均无实例/活跃目标任务 |

## 实现决定与边界

- 概览 CPU/内存来自 60 秒内在线节点心跳；趋势仅记录本次打开期间最多 10 分钟的采样，节点覆盖变化会重置。没有历史监控 API，不展示虚构历史。
- `ActionButton` 等待调用方返回的 Promise，立即显示忙碌状态并防止重复提交；实例停止等服务端后续过程由显式 `loading` 延续。83 个旧回调改为返回原 Promise，16 个表单提交按钮另接入既有 loading 状态，业务接口不变。失败保留明确反馈，减少动效设置禁用位移与旋转。
- 删除账户采用匿名墓碑，保留历史外键和排行榜分数；退队并交接队长，空战队锁定。清理凭据/个人资料、撤销 API token、旧 Cookie、SignalR 和 TeamLab 远程访问。跨主站进程 SignalR 最长 5 秒回读；不可达节点的远程清理由持久 `Ending` 状态重试，不把请求成功写成已经断开。运行实例沿正常业务生命周期释放。
- 本轮不自动删除用户已有测试账号，也不重启或销毁现有四台 TeamLab VM。
- TeamLab 使用正交线路与相对方向端口，优先避免穿过节点，再减少共享长线和弯折；按稳定节点键计算，不改保存的场景坐标。节点类型用图标和类型标签区分，移除卡片左侧色条；版本页明确检查、发布、启动顺序。
- 节点资源列表隐藏失效历史入口；活动资源只显示“打开入口”，校验 HTTP(S) 且拒绝脚本和带用户信息的 URL，避免把长查询令牌直接显示为文字。
- 战队批量导入执行时排除已匿名删除的账户，不能通过旧 ID 把其重新加入战队；个人排队创建任务在执行前拒绝已删除账户并释放预留。
- Audit、AWDP、Content、CTF、Theory 命中文档影响映射的改动仅为已有按钮返回原异步结果，未变更模块接口或业务流程；共享行为统一记入前端组件边界，模块专题无需重复说明（`no-doc-needed`）。

## 发布身份、备份与恢复

发布位置：`/srv/yinyu-data/releases/platform-experience-1c9941ba46be2d3235e8240a86b075cf2572eda8-20261018/publish`。只替换 Main 和前端；原有 5 个 Agent/GuestSupervisor 发布文件逐字节保留，本机 Agent PID `2695400` 与摘要 `87e011cf…` 保持，未升级两台子节点。

| 证据 | 值 |
| --- | --- |
| 源码 tree | `1b40a6c316cc1ff7841fa4ea4d7ec7d88107bbd8` |
| 发布包 | 78,411,617 B；SHA256 `226a860f69c5892901b1e32726b40712a41d70a02f2248b3b2949176878282cf` |
| Main DLL | SHA256 `fe24f2e28b337f9ba56e58a94b5efbb9899b7d907d0a33fd4df757d4c2d013b6` |
| 前端 manifest | SHA256 `3de9d021f749b49db8bdeedd73e2d691a4f6ba3f4df0bbdca56b301c7eb4ea01`；226 文件逐项校验 |
| 前向 SQL | SHA256 `3dc4aec451f6c8fcd39b9cd37ed65ad221a995dc30067c7561019cd3eaa335f1`；245 B |
| 发布证明包 | SHA256 `52897477e2b0a4e7ed4e6f944538061ba71d7c546ba0a818d8432eaf1cd21d2e` |

备份目录 `/srv/yinyu-data/backups/platform-experience-20261018-pre-20261017T184600Z`，北京时间 02:46:06–02:50:06 完成：

| 内容 | 大小 | SHA256 |
| --- | --- | --- |
| 主库 dump | 1,417,627,013 B | `b9a269c703b07b53fb4c48658d28b907c78a1df2ca1bb8b401d66b46ca87a79c` |
| Guacamole dump | 95,455 B | `26e41a41c45742f0130cd280e33fb9da582dd8bd0895bcf9672bfab3fa32767a` |
| 附件备份 | 218,894,445 B | `9ebb1a7a10c2ab1d6dd02349db877d1649123888a5c74250528bfcbb82c6b2f2` |

隔离 PostgreSQL 16 恢复使用无网络、无端口的独立容器，2 CPU / 4 GiB；13 分 47 秒恢复主库和 Guacamole。主库 197 张 public 表，同 SQL 升级后 152 条迁移、26 张业务表计数不变；Guacamole 23 表、2 用户、7 连接。生产执行同字节 SQL，新增 `AspNetUsers.DeletedAtUtc` 可空 timestamp，无默认值；head 为 `20261017184129_AddUserDeletionTombstone`。

业务验收结束后，北京时间 03:25:52–03:25:55 精确删除本次隔离恢复容器及 `/srv/yinyu-data/rehearsals/platform-experience-20261018` 专属约33GB数据；先核对容器镜像、标签、无网络、独占挂载及绝对真实路径，未执行全局 prune。备份、证明和旧 release 保留，清理后服务和三节点再次回读通过。

current 原子切换；previous 为原 `logs-ui-966ca6081000035c6ac46b4289f44d13dfebf75d-main-927ceef8-20261016/publish`。旧 release、备份及配置/附件共享链接保留。应用回退应原子恢复 previous 并仅重启 Main，保留可空新增列，禁止直接降级数据库；真实应用回退未执行。回退旧版本会失去新删除界面和过滤能力，必要时先停止管理员删除操作。

发布后 Main PID `3762284`、Main/Agent active/running、NRestarts=0；首页和 Config 200，匿名管理 API 401，已知附件 15,872 B 及摘要保持。3 节点 Online，`.31` 4 台 VM、其他节点 0 VM；切换时队列只有终态（3399 成功 / 142 失败 / 53 取消），504 镜像 Ready，235 分发 Ready / 1 既有 Failed。指标端口 healthz 200/Degraded 与基线相同，未声称历史问题已解决。切换后首次回读 journal priority error 为 0。

Docker 验收北京时间 03:23:30–03:23:55，创建票据 `01a14b51-c31a-7408-8d1b-926b25d4b3df`，停止票据 `01a14b51-f856-738d-ba33-827c58a4c112`；实例 `01a14b51-ca53-7cd4-8851-7de154b70981` 已清理。选择管理员此前已解课程题，重复 Accepted 留下一条正常提交/审计记录（1→2），未发送 chapterId；未增加新题解题数。最终全平台活跃队列0，成功票据3401，所有节点Docker0，原4VM保持。

追加只读核验：目标 Agent 的 `/api/runtime/inventory` 200、Docker 可用，按同一实例稳定命名算法核全状态清单匹配0；精确 Container 行0、活跃预留0，实例 ContainerId 为空。当前 SolveTimeUtc 仍为2026-07-24，但原证明未保存完整前值，故不作为严格前后相等断言。旧端口与 leaseId 未在停止前记录，不能独立证明精确 Redis 旧端口租约删除；不将数据库/Agent 清理等同于该项实测。

## 测试修正与证据位置

首轮全量集成发现历史 migration 测试使用当前 User 实体向旧 schema 插入数据，因新增列而失败。已改成参数化旧字段 fixture，保留原升级与恢复断言，未修改历史 migration，最终 374/374 全过。拓扑避障扩展一度超过组件 650 行架构预算，已提取纯 edge projection 模块，最终架构门禁通过。

本机 Git 外证据目录：`D:/Work/platform-experience-validation-20261018/` 保存前后端日志/TRX、文档报告、截图及脱敏账号冒烟证明；`D:/Work/platform-experience-release-20261018/` 保存发布包、准备/暂存/切换/回读证明。账号冒烟行 `01a14b4b-2d45-7ffa-95e0-37b9a768dab6` 已匿名化，测试战队 `124` 已删除；保留匿名行和审计记录，未删除任何既有用户。凭据、Cookie、Flag 和远程访问令牌不进入证据或提交。

远端代码门禁：[Quality 39061621324](https://github.com/Y3X1L2/newGZCTF/actions/runs/39061621324)，前端1分43秒、后端10分35秒成功；[Documentation 39062102162](https://github.com/Y3X1L2/newGZCTF/actions/runs/39062102162) 成功。初次 Documentation 因 PR 中 Theory 无需修改理由缺具体定位而失败，补充 `TheoryExamWorkbench` 和469测试依据后通过，并同步补全 Provisioning 删除账户边界。最终文档提交仍执行静态、索引、影响复核与 diff 检查。

## 交接与限定范围

本轮不合并主线，待用户在生产页面验收后再决定是否合并。运行概览趋势只代表打开页面后的实际采样；没有伪造跨日监控。账户删除默认匿名保留排行榜历史，本轮不提供批量清空历史分数。远程撤销失败的持久重试、跨进程 SignalR 撤销已有自动化回归，但没有为了验收杀断用户正在使用的 Guacamole/VM。AWDP 攻防、Portal SSO、Windows 冷缓存和节点整机重启未在本轮执行；既有缺口仍见[后续事项](../backlog.md)。
