# YINYU 当前开发状态

更新时间：2026-08-10

本文件是跨会话的短期状态入口。长期规则见根目录 `AGENTS.md`，完整目标见 `docs/platform-commercialization-master-plan.md`。状态变化后应更新本文件，不通过追加整段聊天记录维护记忆。

## 1. 当前基线

| 项目 | 当前事实 |
| --- | --- |
| 主仓库 | `https://github.com/Y3X1L2/newGZCTF.git` |
| 稳定分支 | `main` |
| 本快照代码基线 | `1f8d5cca8da9a8491b9feefb3a2ba1f7879cbc2f` |
| 基线说明 | Phase 9 TeamLab 组网、远程操作基础和 vNext 管理前端，包含 Schema v1 草稿读取兼容 |
| 本机正式工作区 | `D:\Work\newGZCTF` |
| Git 结构 | 独立仓库，`.git` 位于正式工作区，不再依赖 linked worktree |
| 本机历史归档 | `D:\Work\newGZCTF-local-archive`，不属于源码事实 |

当前 HEAD 必须通过 `git rev-parse HEAD` 实时读取。开始新任务前仍须重新 `fetch`，不能假定本表中的代码基线或远端关系永久有效。

## 1.1 TeamLab 现场验收状态（2026-08-09）

- 候选提交 `0ee455b` 已部署到测试服务器的独立 release；主站、Agent、首页、`ApiOperationWorker` 和两个 Agent inventory 已通过发布后冒烟。
- 数据库已前向应用 `20260809041834_NormalizeImageDistributionReferenceIdentity`，修复多个 TeamLab 发布版本共享镜像引用时的覆盖风险。
- 当前混合验收场景的 v3 试运行仍处于真实 `Scheduled/已排队` 状态。原因是两个 VM 的存储声明各为 20480 MiB，而两台测试节点可用镜像空间分别约为 6.1 GiB 和 18.3 GiB；不满足调度器的容量契约。
- 为继续验收创建的低负载 v4 试运行完成了镜像准备、调度、网络、路由和基础设施阶段，但 Docker 创建阶段暴露了逻辑接口键被直接用作 Linux 设备名的错误；候选代码已改为固定的来宾 `eth0` 至 `eth7` 映射，仍待统一主站与 Agent 部署后复验。
- 2026-08-09 候选 release `teamlab-e2e-7f66871-r2-20260809` 已在 118 原子激活。真实页面创建 v4 试运行已到达 VM bootstrap，Docker 网络创建不再出现接口名超长错误；Linux 服务包因标准 tar 的 `./` 根目录条目被 Guest Supervisor 误判为非法相对路径而失败，失败运行已自动清理。修复将 `./` 仅作为归档语法规范化，仍拒绝绝对路径和路径穿越；定向回归 `Bootstrap_StandardTarDotPrefixIsAccepted` 已通过，待重新发布后复验。
- 修复提交为 `7f66871`，已推送至 `origin/codex/phase-09-teamlab-networking`。2026-08-09 本机未取得 118 的发布 SSH 认证，测试服务器仍运行此前候选；不得将旧制品上的 Docker 创建失败视为修复后的验收结论。
- 三队混合运行、服务注入实际执行、远程运维、流量/PCAP、暂停恢复和完整清理尚未签收。细节见 `docs/development/test-plans/2026-08-09-teamlab-usability-acceptance.md`。
- 后续验收优先验证不启用服务注入的混合运行、远程运维、观测和生命周期。Linux 服务注入失败已定位为模板 `79` 内置旧 Guest Supervisor 与当前运行时契约不匹配；处理方式是新的不可变模板导入、分发和认证，未通过前不将服务注入写为通过。

### 2026-08-10 已证实现状

- 当前代码基线 `5ad761a159d87bb82866bfe4d1e484c5a2e4b755` 已推送到 `main`，并已在 118/125 验收环境以统一主站和 Agent 制品部署。主站、首页和两个节点 inventory 正常。
- 验收场景的 v5 设计校验与发布已通过；创建试运行后，网络与两台 VM 已成功完成创建及来宾就绪。唯一当前阻断是 Docker 资产在运行库存校验时为 `exited`，运行时随后完成自动清理。该结论来自真实页面事件时间线和运行时错误，尚未把混合试运行标记为通过。
- 后续首先定位 Docker 退出的根因；服务注入暂不阻塞其它验收。只有 Docker、Linux VM、Windows VM 均稳定运行后，才继续管理员运维、流量观测、生命周期和并发控制验收。

### 2026-08-10 r4 试运行成功基线（后续验收起点）

- v5 失败已完成根因确认：Docker 模板 `#114 Phase9 Immutable Portal` 启动时要求 `NM_AI_CONSOLE_API_URL`，场景未配置该业务镜像依赖，容器退出码为 `1`。该问题不属于 TeamLab 网络、分发或调度链路，未为单个镜像增加平台特判、等待或重试。
- 管理端将 Docker 资产替换为独立常驻镜像 `admin-Test-Web-v1 (#15)` 后，场景发布 v6（Release `019fe773-7565-7f3f-a250-ea0252d4ce02`），真实试运行 Runtime `019fe774-2f19-70ec-a8a0-7d3557022faf`（数据库 ID `117`）进入 `Ready`。页面、数据库、125 节点 Docker 与 libvirt 状态一致：Docker、Linux VM、Windows VM 均运行，网段为 `10.80.1.0/24`、`172.20.1.0/24`、`192.168.81.0/24`。
- 管理员容器终端曾无法输入。主站记录 Agent WebSocket 握手 `426 Upgrade Required`，根因是 `GZCTF.Agent` 未启用 WebSocket 中间件。已在认证之后、Controller 映射之前添加 `app.UseWebSockets()`；本地 Agent Release 构建通过，并仅在 125 部署该 Agent 二进制。新会话真实显示 shell 提示符，执行 `echo TEAMLAB_TERMINAL_OK` 已取得回显；旧失败会话正确以 `terminal_disconnected` 收敛。
- 通过受审计容器终端发送 ICMP：Docker 到 Linux VM 成功；Docker 到 Windows VM 未响应，当前符合 Windows 默认 ICMP 防火墙策略，不能据此判定网络失败。流量页面已经出现本运行时的 ICMP、UDP、ARP 元数据；端到端路径暂无数据，待继续核对采集关联条件与投影结果。
- 后续优先验收：流量筛选与详情、日志/事件关联、暂停/恢复/重置/销毁、终端会话回收和权限边界。服务注入保持低优先级，不阻塞上述验收。

## 2. 产品与代码状态

当前平台已形成以下主要能力：

- 普通 CTF：赛事、队伍、题目、附件、静态/动态 Flag、Docker、KVM/Windows VM、计分和榜单。
- AWDP：服务、轮次、Checker、攻击提交、修补、重置、恢复、停止和态势数据。
- 理论考试：题库、试卷、单选/多选/判断、草稿、最终提交、成绩和答题回顾。
- 培训：课程、共同教师、报名审核、章节、资源、课程题目、章节实验、课后理论、学习进度和学员详情。
- 管理端：赛事、理论题库、镜像、节点、队列、实例、日志、用户、战队、学员组和系统设置。
- 运行底座：统一部署队列、容量预留、多节点调度、镜像分发、Agent inventory、结构化事件、恢复和关联日志。
- 身份认证：本地账号和 Portal SSO；统一认证的对方项目不在本仓库内。

vNext 正式路由和实现状态以以下文件为准：

- `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- `src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`

截至本快照，首页、赛事、战队、培训、认证、个人主页、理论、TeamLab 及主要通用管理页面已经接入 vNext。以下入口仍未标记为完整实现：

- `/practice`：自主练习产品切片尚未进入稳定主线。

不得用旧页面套壳、mock 数据或隐藏跳转把未实现入口描述成已完成。TeamLab 的真实双 Worker 故障注入、远程操作效率和长期流量保留仍属于现场验收，不因页面完成而视为签收。

## 3. 商业化阶段状态

| 阶段 | 当前结论 | 未闭环部分 |
| --- | --- | --- |
| Phase 0 | 代码、迁移和清理门禁完成 | 生产升级仍遵守数据库备份和迁移门禁 |
| Phase 1 | 模块边界、领域/API 契约和外部题目 API 完成 | 后续模块必须持续遵守边界测试 |
| Phase 2 | vNext 前端底座、Token、组件边界和构建门禁完成 | 页面垂直切片仍按产品模块逐步完善 |
| Phase 3 | TeamLab 独立模型、release/plan/runtime/API 基础代码完成 | 外部与 Penetration 两条真实纵向链路仍需持续验收 |
| Phase 4 | 数据库索引、分区、聚合和生命周期代码完成 | 生产容量和长期保留策略需现场数据验证 |
| Phase 5 | Redis、租约、缓存失效和高频缓冲代码完成 | 双主、k6 和基础设施断网演练属于预发布验收 |
| Phase 6 | 统一队列、调度、容量、并发和 Agent 能力代码完成 | 500-owner/300-create、双主接管和目标硬件吞吐未形成最终签收 |
| Phase 7 | 结构化事件、日志、关联、Agent inventory 和恢复代码完成 | 生产观测链和真实故障恢复仍需持续验收 |
| Phase 9 | TeamLab 拓扑、发布、运行时、组网、远程操作审计基础和 vNext 管理端已部署 | 双 Worker 故障注入、QGA 大文件效率、长期流量留存和规模验收未签收 |
| Phase 8、10-14 | 尚未按总纲全部完成 | VM 统一、内容资产/出题 API、练习、培训理论增强、AWDP 展示和商业验收 |

阶段文档中的“代码完成”不等于已经通过真实生产容量、故障和多节点验收。

## 4. 近期已完成修复

当前主线包含：

- 动态 Flag 按实例隔离，并清理多 Flag `0` 等历史污染问题。
- 培训动态 Flag 不再暴露管理员测试步骤。
- 过期培训实例恢复和运行引用安全重载。
- 培训理论提交后的答案回顾。
- Windows VM 创建和凭据注入链路稳定化；当前没有合格、已认证的新 Windows 基础镜像。
- 首页编排动画和丝带绘制时序优化。
- 镜像存储导入完成后模板进入 `Ready`，节点分发不再与模板 `Importing` 状态互锁。
- 永久失败的镜像分发任务不再无限自动重试，只有 `Retryable=true` 才重试。
- TeamLab vNext 提供场景库、拓扑设计、发布、运行时、比赛绑定和受审计远程操作入口。
- 旧 Penetration 迁移产生的 Schema v1 拓扑可以打开，下一次保存时由编辑器统一编译为 Schema v2；未知 Schema 仍拒绝加载。

镜像状态修复验收记录：后端全量单元测试 `531/531`、镜像分发专项 `8/8`、PostgreSQL 集成测试 `1/1`。线上 `ClosureChallenge` 已从长期“导入中”恢复为 Ready，并完成三个节点分发。

## 5. 已知环境

| 用途 | 地址/说明 |
| --- | --- |
| 主要平台服务器 | `10.24.0.27:8080` |
| 公网平台入口 | `106.52.207.52:42755` |
| 内网镜像 Registry | `10.24.0.28:5000` |
| 已验证 WorkerNode | `10.24.0.30`、`10.24.0.31` |
| 备用测试服务器 | `10.0.7.118:8080`，是否可用必须在任务开始时重新确认 |
| 本机网络代理 | 必要时使用 `http://127.0.0.1:10808` 访问 GitHub |

不得在本文记录服务器密码、IAM token、Cookie、SSH 私钥或数据库连接串。

当前生产发布事实：

- release：`phase09-1f8d5cc`
- Git SHA：`1f8d5cca8da9a8491b9feefb3a2ba1f7879cbc2f`
- 发布目录：`/opt/gzctf/releases/phase09-1f8d5cc/publish`
- 回退目录：`/opt/gzctf/releases/phase09-b8ec1b2/publish`
- 数据库迁移头：`20260730095038_AddTeamLabRemoteAccessSchema`
- 本次数据库备份：`/opt/gzctf-vnext/backups/phase09-1f8d5cc/database.dump`
- 主站、本机 Agent、内网入口和公网入口已通过冒烟；3 个 WorkerNode 在线，活动部署队列为 0。
- 发布构建通过前端 `214/214`、locale、lint、TypeScript、架构和 production build 门禁；Phase 9 合并提交此前通过后端单元 `758/758` 和集成 `265/265`。
- 发布后完成 35 项只读业务冒烟及 12 项通用管理写入验收；临时用户、战队、学员组和系统页脚均已清理或恢复。
- 赛事 23 的 Docker 题目 19 已完成真实创建、入口探测和销毁：调度节点为 `10.24.0.30`，公网实例入口返回 HTTP 200，创建和销毁票据均进入 `Succeeded`，结束后无活动队列或实例残留。
- AWDP 比赛 96 的服务、状态、实例、榜单、攻击日志和补丁六个只读管理接口均返回 JSON 200；真实攻击、修补和故障流程仍按人工验收文档执行。
- CTF 与培训自动验收脚本的队列终态判断已对齐当前枚举：`Succeeded=4`、`Failed=5`、`Cancelled=6`，避免把成功任务误报为失败。
- 发布后主站当前激活周期没有 Error 级 journal；内网与公网首页复核均为 HTTP 200。

生产库仍有一个 2026-07-24 创建的 admin Windows VM 测试实例。它使用 Phase 9 之前的随机 libvirt UUID，不符合当前稳定运行身份校验，不能用于验证 Agent 重启后的 RDP 代理恢复。不要修改数据库绕过身份校验；需要验收时应销毁该测试实例并用当前版本重新创建。

## 6. 现行缺口入口

- vNext 已知 API/验收缺口：`docs/yinyu-vnext-deferred-contract-gaps.md`
- 商业化总计划：`docs/platform-commercialization-master-plan.md`
- 商业化阶段进度：`docs/platform-commercialization-audit-progress.md`
- AWDP 人工验收：`docs/yinyu-awdp-manual-acceptance.md`
- Windows VM 简明规范：`docs/operations/windows-vm-quick-deployment-guide.md`
- 生产发布手册：`docs/operations/vnext-maintenance-window-rollout.md`

`docs/yinyu-vnext-production-baseline-20260721.md` 是 2026-07-21 的历史采样，不代表当前服务器仍停留在其中记录的版本。

## 7. 下一任务起点

当前 Phase 9 发布代码已提交；若本文档之后产生状态提交，生产二进制 SHA 仍以第 5 节为准。新任务应：

1. `git fetch origin --prune` 并确认本地基线。
2. 从 `origin/main` 建立新的 `codex/<task-name>` 分支。
3. 读取对应模块的现行契约和缺口文档。
4. 先证明一条真实端到端链路，再扩大改动范围。
5. 完成代码、测试、文档和必要部署验收后更新本文件。

推荐后续产品方向需要由负责人选择，不应由旧聊天自动延续：

- 完成自主练习模块的独立产品切片。
- 完成 TeamLab 双 Worker 故障注入、远程操作效率和长期流量留存验收。
- 按 Phase 8-14 继续商业化主线。
- 优先处理线上明确复现的稳定性问题。

---

## 2026-08-07 会话追加：TeamLab 外部控制面收尾（计划 2026-08-02-teamlab-external-control-plane.md Tasks 2/6/7/8/9/10）

### 已完成并验证（候选分支 `codex/phase-09-teamlab-networking`，服务器统一发布待执行）

- **Task 6（镜像准备 + 服务目录外部契约）**：`GET/POST /api/open/v1/teamlab/preparations/releases/{id}`（planAvailable/preparing/readyToStart/blocked/notStarted 状态机 + per-template 就绪投影 + `ReleasePreparation` 操作 kind）；`GET /api/open/v1/teamlab/service-profiles[/{id}?version=]`（参数 schema/默认值/执行特性/SecretSupply=runtime-overlay，绝不暴露脚本与密钥）；capabilities 翻 `ServiceProfiles=true`；发布校验缺失/下架 profile 报稳定错误码 `service_profile_not_found`（404）。
- **Task 7（观测/权限投影）**：四级权限统一评估（StateRead/MetadataRead/RemoteSessionOperate/LifecycleManage）收敛到 `TeamLabAuthorizationService`，浏览器管理员/属主行为不变；事件查询支持 generation/stage 过滤；批量 remote availability 端点 `GET /api/admin/teamlab/runtimes/{id}/remote-access` 替代逐资产扇出。
- **Task 9（Penetration 迁移）**：`ITeamLabUsageProjectionProvider` 边界（TeamLab 空实现 + Penetration 适配实现，DI 后注册生效）；`TeamLabAdminQueryService` 不再引用任何 Penetration DbSet（架构测试守护）。
- **Task 10（Webhook）**：scope 级订阅（DataProtection 加密密钥、HTTPS-only + SSRF 校验含 DNS rebinding/重定向防护、事件类型白名单 422、FromEventId 起始游标、HMAC-SHA256 至少一次投递、失败指数退避 + 有界失败记录 + 重放端点）、迁移 `20260806123654_AddTeamLabWebhookSubscriptions`（ApiOperationId 唯一索引防崩溃重复建行）、`TeamLabWebhookDeliveryWorker` 投递（每订阅 advisory 锁 + `(Active, NextDeliveryAt)` 索引）。
- **Task 2 残余**：capabilities 补 `PauseResume=true`/`EditorLayoutVersion`；`service_profile_not_found` 发布校验。
- **Task 8（前端网络区域工作台）**：网络区域容器渲染（成员包围盒派生/拖拽带动成员/折叠保留交换机/双击聚焦/缩放持久化）、`networkLayouts` 文档模型 + 命令 + mapper/compiler 持久化（editor.networks 兼容）、确定性自动布局生成区域、帮助系统（FieldHelpButton/teamLabFieldHelp）、服务目录选择器（ServiceProfilePicker）、空画布 Shift 拖拽框选提示。
- **Task 7 前端**：日志 cursor 分页 + "加载更早"、事件 generation/stage 筛选、批量 remote availability 轮询接入、终端错误态与重试、连接取消竞态与弹窗拦截修复。
- **多 agent 交叉审查**：后端 2 份（并发/错误处理 + 使用者闭环）、前端 2 份（体感 + 架构），P1 级发现全部修复并补回归测试（含 SWR 缓存隔离、区域命令不可变、分页 identity 竞态、双归属 router 不随区域移动）。

### 验证结果（全量）

- 后端：`dotnet test src/GZCTF.Test` Release **787 通过 / 0 失败**（含新增 28 个 webhook/准备/目录单元测试 + 架构守护测试）；`dotnet build src/GZCTF.slnx -c Release` 通过。
- 前端（ClientApp）：`validate:locales` / `lint:check` / `check` / `check:architecture` / `test`（**222 通过**）/ `build` 全部通过。
- 迁移：`20260806123654_AddTeamLabWebhookSubscriptions` 已生成（含快照），**尚未在生产库应用**。

### 已知限制（记录，未处理）

- 隐式交换机首次保存/加载后相对区域出现 48px 偏移（P2-7，第二份前端审查）；日志过滤变化时首帧会以旧 cursor 发一次请求（P2-9）；事件列表上限 500 条（有界保留）。
- 尚未跑：PostgreSQL 生产副本前向迁移验证、双节点真实基础设施验收（webhook 端到端、区域工作台人工复核）、OpenAPI JSON 生成对比。
- 本轮所有改动均在工作区未提交（含协作者既有未提交改动），需在合并前统一提交整理。

## 2026-08-08 会话追加：外部控制面部署与验收

- 测试服务器 `10.0.7.118` 已原子切换到 `/opt/gzctf/releases/controlplane-20260808-1835/publish`，回退 release 为 `controlplane-20260808-0922`；发布前数据库备份位于 `/opt/gzctf-vnext/backups/20260808T183700Z/gzctf.dump`。
- 数据库迁移头为 `20260808085200_NormalizeApiOperationActorIdempotency`。主站、Agent、首页和两个节点 inventory 均正常，`ApiOperationWorker` 已启动；验收结束时 `ApiOperations` 与 `TeamLabRuntimeOperationJobs` 的等待/运行数量均为 0。
- API-token-only 实测通过：管理员通配授权 token 可创建并归档 control scope；受限 token 仅能读取其获授 scope。对同一 webhook 请求并发提交 5 次只得到同一个 operation；同一幂等键但不同请求体稳定返回 `409`；创建和撤销均由 Worker 到达 `Succeeded`。验收 token、测试 webhook 与活动记录已精确清理。
- 本地聚焦验证通过：TeamLab 单元用例 `233/233`；operation/worker PostgreSQL 用例 `10/10`；发布门禁通过前端 `78/78` 测试文件、`222/222` 用例、locale、lint、TypeScript、架构和 production build。
- 浏览器实测管理端场景设计页在 1366/1920 宽度没有横向溢出或重叠；登录后没有相关 API `401/404` 或控制台错误。Cookie 管理端服务目录 `GET /api/admin/teamlab/service-profiles` 返回 `200`，已不再错误调用 Token-only Open API。
- 本轮只完成服务端控制面与界面基本验收。计划要求的外部 token-only 完整双节点场景链路，以及 operation/rollout 终态 webhook 事件模型，仍需单独设计和现场验收，不得将其标记为完成。

## 2026-08-10 TeamLab 现场验收更新

- 旧 Agent 遗留的第 1 代 UEFI Windows VM 缺少 generation 旁车，重置的第 2 代清理请求曾被身份围栏拒绝。兼容规则仅允许销毁路径处理稳定 UUID 匹配、域代次严格早于请求代次且旁车缺失的旧域；创建、暂停、恢复仍为严格身份校验。
- 现场又确认 UEFI NVRAM 会使旧 undefine 命令失败。销毁逻辑已改为受检的 managed-save、storage、NVRAM 一体清理，不再吞掉正式删除错误；定向单元 22/22 通过，125 已部署 Agent 修复。
- 新试运行 019fe8e0-700e-7471-8381-eec77b9404ca 已两次 Ready（重置后为第 2 代）。真实页面完成容器终端命令、终端回收审计、Docker 到 Linux VM ICMP、流量协议筛选、日志/事件阶段筛选。服务注入未参与本轮，也不阻塞验收。
- 调试授权创建和撤销已在同一运行时真实执行，撤销后页面不存在有效授权或下载入口；当前第 2 代保持 Ready。
- 该运行时已完成最终销毁；页面与 125 节点均确认不存在 tl118-* 域、overlay、旁车或容器残留。单队混合试运行的创建、重置和销毁链路已通过。
- 当前仍待：端到端路径关联、Linux SSH/Windows RDP（模板尚未配置静态运维账号）、正式比赛三队与权限边界、并发命令、销毁后的完整资源核对。125 上发现 tl106 至 tl116 历史 VM 域，必须先以数据库事实核对后再清理，禁止按名称删除。
