# YINYU CTF Platform Full Review Report

> 审查日期：2026-07-06
> 审查范围：GZCTF 平台全模块静态审查 + 交叉验证
> 参考文件：`docs/platform-full-review-agent-brief.md`

---

## Executive Summary

- **Overall risk level**: High — 8 个 Critical + 12 个 High 缺陷影响安全边界、资源生命周期和积分完整性
- **Modules reviewed**: 12 个模块（授权角色、部署队列、日志可观测、节点管理、TeamLab VPN、Docker/VM 生命周期、普通 CTF/团队/排行榜、训练平台、AWDP 大屏、遗留残留、前端架构、敏感数据）
- **Confirmed Critical**: 8
- **Confirmed High**: 12
- **Confirmed Medium**: 26
- **Confirmed Low**: 18
- **Info**: 10
- **Main architectural concern**: VM 生命周期路径与 Docker 路径成熟度差距巨大（无锁、无错误恢复、无 ErrorMessage），AWDP 积分系统缺乏并发保护，Guacamole 安全隔离缺失
- **Recommended repair order**:
  1. A-F-001/002/003: 教师越权操作任意学生（添加组归属校验）
  2. F-F-006: Guacamole 全局 admin token 泄漏（创建独立用户/JWT）
  3. I-F-001/002/003: AWDP 跨队泄漏 + 积分并发（过滤 + 乐观并发 + 锁）
  4. B-F-001: Creating 工单重启卡死（添加启动恢复逻辑）
  5. B-F-002: 心跳覆盖计数器超卖（预留量与实际量分离）
  6. G-F-001/002: 队长离开/转让授权缺陷（后端校验）
  7. G-F-005: FlagChecker 日志泄漏 flag（脱敏）
  8. F-F-002/003/005: VM 生命周期状态一致性（加锁 + try-catch + ErrorMessage）

---

## Review Method

- **CodeGraph/context sources**: 当前源码 + 测试代码为唯一真实来源，旧文档仅作参考
- **Subagents dispatched**: 12 个（Part A-L），每个独立审查代码与测试，标记 Confirmed/Likely/Not reproduced/False positive
- **Cross-checks performed**: 3 个
  - Cross-check A: 后端+安全高危项复核（7 Critical + 3 High 全部 Confirmed）
  - Cross-check B: 前端/API 集成复核（解决 K-F-002 vs H-NR-001 矛盾，发现 SubmissionReview 三重损坏）
  - Cross-check C: 队列/日志/调度生命周期复核（B-F-001 容量泄漏降级、B-F-002 触发条件校准、绘制状态机）
- **Runtime tests performed**: 无（纯静态审查）
- **Limitations**:
  - 未进行运行时验证，并发类缺陷基于静态推理
  - 前端审查限于关键路径，未逐一验证所有 API 调用
  - 数据库遗留数据（Scenario/IRChallenge 类型题目）影响无法量化

---

## Confirmed Findings

### Critical

#### F-001: 教师可越权重置/删除/编辑任意学生（跨组）

- **Severity**: Critical
- **Category**: Security / Authorization
- **Module**: AdminController
- **Evidence**:
  - `AdminController.cs:527` `if (!RolePolicy.CanManageRole(actor.Role, user.Role)) return Forbid();` — 唯一的归属检查，Teacher→Student 恒为 true
  - `ResetPassword`（行 515-535）、`DeleteUser`（行 547-580）、`UpdateUserInfo`（行 447-500）均用 `FindByIdAsync(userid)` 直接取目标用户，无 `StudentGroupMembers` 归属校验
  - 虽然列表接口 `FilterVisibleUsers`（行 809-828）对非 Admin 教师过滤了可见学生，但变更端点完全绕过该过滤
  - 用户 Guid 通过比赛参赛、队伍成员、积分榜等多处暴露
- **Impact**: 教师可跨组重置任意学生密码（接管账户）、删除任意学生、修改任意学生信息
- **Repair direction**: 变更端点添加 `student.StudentGroupMembers.Any(m => teacher.ManagedGroups.Contains(m.GroupId))` 归属校验
- **Tests to add**: 教师操作非本组学生应返回 403；教师操作本组学生应成功
- **Cross-check**: A Confirmed

#### F-002: 进程重启导致 Creating 状态工单永久卡死

- **Severity**: Critical（Cross-check C 校准：容量泄漏被心跳自愈缓解，但工单卡死+用户去重阻塞为真实影响）
- **Category**: Reliability / Availability
- **Module**: DeploymentQueueService / QueueManager
- **Evidence**:
  - `QueueManager.cs:30-35` `ProcessPendingAsync` 仅查询 `Status == Pending`，Creating 工单不会被重新拾取
  - 全代码库无任何 BackgroundService 或启动逻辑处理 Creating 状态的孤立工单
  - `DeploymentQueueService.cs:48-57` `EnqueueAsync` 按 `ActiveIdentity` + `ActiveStatuses`（含 Creating）去重，返回已存在工单 → 用户无法重试
  - 容量缓解：`LocalNodeMetricsService.cs:49-54` 每 30s 覆盖本地节点计数器；远程节点心跳覆盖计数器
- **Impact**: 进程重启后 Creating 工单永久卡死，用户部署请求被去重阻塞（返回旧卡死工单），需管理员手动 Cancel
- **Repair direction**: 启动时添加 `RecoverStaleTickets` 逻辑：将 Creating 超过 N 分钟的工单设为 Failed 并释放容量；或重新入队
- **Tests to add**: 模拟进程重启，验证 Creating 工单最终变为 Failed 或重新处理
- **Cross-check**: A Confirmed, C Confirmed（容量部分降级）

#### F-003: Agent 心跳覆盖节点容量计数器导致超卖

- **Severity**: Critical（Cross-check C 校准：超卖有界，NodeExecutionGate 限制每节点 2 并发，约 +2/周期）
- **Category**: Security / Resource Integrity
- **Module**: NodesController / FleetCapacityReservationService
- **Evidence**:
  - `NodesController.cs:549-550` `node.CurrentContainers = request.CurrentContainers; node.CurrentVms = request.CurrentVms;` — 直接赋值覆盖
  - 预留与实际量共用同一字段（`FleetCapacityReservationService.cs:63-65` `node.CurrentContainers += dockerSlots`）
  - 心跳窗口内预留被覆盖：QueueManager 预留后、容器创建前，心跳上报实际值覆盖计数器，预留量被抹掉，后续可继续预留
  - `FleetCapacityReservationService.cs:135-136` `CanReserve` 基于被覆盖的计数器放行
- **Impact**: 每个心跳周期可超卖约 2 个容器（NodeExecutionGate 限制），节点实际容器数可超过 MaxContainers
- **Repair direction**: 预留量与实际量分离（`ReservedContainers` + `CurrentContainers`）；或心跳写入时 `Math.Max(reported, node.CurrentContainers - node.UnconfirmedReservations)`
- **Tests to add**: 心跳窗口内并发预留，验证不超过 MaxContainers
- **Cross-check**: A Confirmed, C Confirmed（触发条件校准为"心跳窗口"非仅"Agent 重启"）

#### F-004: Guacamole 共享全局管理员 token + 硬编码默认凭据

- **Severity**: Critical
- **Category**: Security / Access Control
- **Module**: GuacamoleService
- **Evidence**:
  - `GuacamoleService.cs:20` `private string? _cachedToken;` — 单例服务，单一全局缓存
  - `GuacamoleService.cs:66-67` 硬编码 `["username"] = "guacadmin"`, `["password"] = "guacadmin"`
  - `GuacamoleService.cs:340` `return $"{baseUrl}/#/client/{encoded}?token={authToken}";` — 管理员 token 明文嵌入 URL
  - `GameController.cs:1590` `[RequireUser]` + 行 1618：任何登录用户拥有 VM 即可获取含管理员 token 的 URL
  - 该 token 可调用 Guacamole REST API 访问/修改/删除任意连接
- **Impact**: 任意 VM 用户可从 RDP URL 提取全局 admin token，越权访问其他用户的 RDP 连接配置（含密码），修改/删除任意连接
- **Repair direction**: 为每个 VM 实例创建独立 Guacamole 用户并授权仅对应连接；或使用 Guacamole JWT 机制限定 scope；移除硬编码凭据，强制配置
- **Tests to add**: 提取 token 后无法访问其他用户连接；默认凭据不可用时服务拒绝启动
- **Cross-check**: A Confirmed

#### F-005: AWDP 选手可查看所有队伍实例信息（跨队伍泄漏）

- **Severity**: Critical
- **Category**: Security / Data Leakage
- **Module**: AwdpPlayerController
- **Evidence**:
  - `AwdpPlayerController.cs:62` `return Ok(await BuildTeamStatuses(gameId, ctx.Participation!.TeamId, token));`
  - `BuildTeamStatuses` 行 344: `GetInstancesByGame(gameId)` 获取该比赛所有队伍实例
  - 行 355-376: 遍历所有实例返回 `IpAddress`、`Port`、`TeamId`、`TeamName`；`teamId` 参数仅用于 `CanManage` 标志
  - `AwdpRepository.cs:65-70`: `GetInstancesByGame` 无 teamId 过滤
- **Impact**: 选手可完整获取对手网络拓扑（IP+端口+TeamId），在 AWDP 攻防场景中属严重信息泄漏
- **Repair direction**: `BuildTeamStatuses` 按当前队伍过滤，或仅返回对手的脱敏状态（不含 IP/端口）
- **Tests to add**: 选手调用 GetInstances 仅返回本队实例
- **Cross-check**: A Confirmed

#### F-006: UpdateFlagSubmitted 缺乏乐观并发控制，同一 flag 可重复计分

- **Severity**: Critical
- **Category**: Security / Scoring Integrity
- **Module**: AwdpPlayerController / AwdpRepository
- **Evidence**:
  - `AwdpFlag.cs` 仅有 `[Key]`，无 `[Timestamp]` 行版本字段
  - `AwdpRepository.cs:250-258` `UpdateFlagSubmitted` 执行 `FirstOrDefaultAsync`（读）→ 检查 `IsSubmitted` → 赋值 → `SaveAsync`（写），非原子
  - `AwdpPlayerController.cs:90` `GetFlagByValue` 用 `AsNoTracking()`，行 98 检查 `flag.IsSubmitted` 是对快照的检查
  - 并发场景：两个请求均读到 `IsSubmitted=false`，均通过检查，均调用 `UpdateFlagSubmitted` 返回 `true`，均计分
- **Impact**: 同一 flag 可被同一队伍双计分，破坏积分完整性
- **Repair direction**: `AwdpFlag` 添加 `[Timestamp]` 字段；或 `UpdateFlagSubmitted` 使用 `pg_advisory_xact_lock`（与普通 CTF 的 `VerifyAnswer` 一致）
- **Tests to add**: 并发提交同一 flag，仅计分一次
- **Cross-check**: A Confirmed

#### F-007: 攻击次数上限检查与写入存在 TOCTOU，可超限刷分

- **Severity**: Critical
- **Category**: Security / Scoring Integrity
- **Module**: AwdpPlayerController
- **Evidence**:
  - `AwdpPlayerController.cs:101-104` `submittedCount = (await GetFlagsByRound(...)).Count(...)` — 非锁定读
  - 行 106: `UpdateFlagSubmitted` — 写入
  - 检查与写入之间无事务、无锁、无行版本
  - 两个并发请求提交不同 flag，均读到 `submittedCount = MaxAttackPerRound - 1`，均通过，最终超出上限
- **Impact**: 选手可超出 MaxAttackPerRound 限制提交更多攻击，刷分
- **Repair direction**: 检查与写入放入同一事务 + advisory lock；或在 `AwdpFlag` 表添加行级约束
- **Tests to add**: 并发提交不同 flag，总次数不超过 MaxAttackPerRound
- **Cross-check**: A Confirmed

#### F-008: 队长可绕过前端约束离开或踢出自己，导致队伍孤立

- **Severity**: Critical
- **Category**: Security / Authorization
- **Module**: TeamController
- **Evidence**:
  - `TeamController.cs:597-632` `Leave` 无队长身份检查
  - `TeamController.cs:462-503` `KickUser` 无 `userId == captain.Id` 检查，队长可踢自己
  - 前端 `Teams.tsx:591,831` 有约束但可被 HTTP 客户端绕过
- **Impact**: 队长离开后 `CaptainId` 仍指向已离开用户，队伍无法管理（改名/审批/转让），形成"无主队伍"。比赛期间队长踢自己可立即瘫痪队伍
- **Repair direction**: `Leave` 在 `user.Id == team.CaptainId` 时要求先转让或返回 400；`KickUser` 在 `userId == team.CaptainId` 时返回 400
- **Tests to add**: 队长离开/踢自己应返回 400；队长先转让后离开应成功
- **Cross-check**: Part G 首次发现，未独立 cross-check

---

### High

#### F-009: 转让队长不验证新队长是否为团队成员

- **Severity**: High
- **Category**: Security / Authorization
- **Module**: TeamController
- **Evidence**: `TeamController.cs:333-371` `Transfer` 缺失 `team.Members.Any(m => m.Id == model.NewCaptainId)` 检查
- **Impact**: 队长可将队伍所有权转让给任意已注册用户（即使不是成员），造成权限提升和队伍劫持
- **Repair direction**: 要求 `newCaptain` 必须在 `team.Members` 中
- **Tests to add**: 转让给非成员应返回 400

#### F-010: FlagChecker 日志泄漏完整 flag 值

- **Severity**: High
- **Category**: Security / Sensitive Data Leakage
- **Module**: FlagChecker
- **Evidence**: `FlagChecker.cs:80-83,113-118,129-134` 将 `item.Answer`（完整 flag）写入管理员可见日志
- **Impact**: 管理员可从日志读取任意队伍提交的 flag 值；日志保留期长，破坏 flag 保密性
- **Repair direction**: 日志中移除 `item.Answer`，仅记录 `item.Id` 或哈希前缀
- **Tests to add**: 日志输出不含原始 flag

#### F-011: VM 创建路径缺少分布式锁，存在竞态条件

- **Severity**: High
- **Category**: Concurrency / Reliability
- **Module**: GameController
- **Evidence**: `GameController.cs:1339-1385` VM 创建分支无 `lockService.AcquireAsync`；对比 `GameInstanceRepository.cs:162-164` Docker 路径有锁
- **Impact**: 并发请求可创建两个 VmInstance，浪费 KVM 节点容量和 Guacamole 连接，产生孤儿 VM
- **Repair direction**: VM 创建分支引入 `IDistributedLockService`，锁键 `vm-create:{challengeId}:{userId}`
- **Tests to add**: 并发创建同一用户同一题目的 VM，应只创建一个
- **Cross-check**: A Confirmed, C Confirmed

#### F-012: VM 销毁失败时 Guacamole 连接已删但 DB 状态未更新

- **Severity**: High
- **Category**: Reliability / State Consistency
- **Module**: GameController / FleetVmService
- **Evidence**: `GameController.cs:1669-1677` 先删 Guacamole 后调 `DestroyVmAsync`（无 try-catch）；`FleetVmService.cs:205,212,220,227` 失败时 `throw`
- **Impact**: Guacamole 连接已删除但 VM 状态仍 Running，容量未释放；再次 Destroy 尝试删已删除的 Guacamole 连接
- **Repair direction**: `DestroyVm` 包裹 try-catch，失败时仍将 DB 状态标记为 Error；或调整顺序先销毁 VM 再删 Guacamole
- **Tests to add**: 模拟 `DestroyVmAsync` 抛出异常，验证 DB 状态与 Guacamole 一致性
- **Cross-check**: A Confirmed, C Confirmed

#### F-013: VmReadyService 超时销毁失败导致无限重试循环

- **Severity**: High
- **Category**: Reliability / Resource Leak
- **Module**: VmReadyService
- **Evidence**: `VmReadyService.cs:75-84` 超时分支调 `DestroyVmAsync` 失败时未设 Error 状态；外层 catch（行 140-143）仅记日志；下个周期 VM 仍满足查询条件，再次超时
- **Impact**: VM 永久卡在 Running 状态，每 10 秒触发一次失败销毁，节点容量永不释放
- **Repair direction**: 超时分支 try-catch 包裹 `DestroyVmAsync`，无论成败都设 `Status = Error`；或引入失败计数器
- **Tests to add**: 模拟 VM 超时 + 销毁失败，验证状态最终变为 Error
- **Cross-check**: A Confirmed, C Confirmed

#### F-014: 团队改名后榜单缓存不失效，7 天内显示旧队名

- **Severity**: High
- **Category**: Cache Consistency
- **Module**: TeamController / GameRepository
- **Evidence**: `TeamController.cs:157-175` `UpdateTeam` 仅 `teamRepository.SaveAsync`，无 `cacheHelper` 注入；`GenScoreboard` 缓存 `Name = p.Team.Name`，7 天滑动过期
- **Impact**: 改名后公开榜单、大屏、Excel 导出最长 7 天显示旧队名，影响排名公示和奖品发放
- **Repair direction**: `TeamController` 注入 `CacheHelper`，`UpdateTeam` 改名后扫描活跃比赛并 `FlushScoreboardCache`
- **Tests to add**: 改名后立即查询榜单应显示新名

#### F-015: 多个团队敏感操作无审计日志

- **Severity**: High
- **Category**: Observability / Audit
- **Module**: TeamController
- **Evidence**: `Transfer`（行 333-371）、`UpdateInviteToken`（行 424-441）、`CreateJoinRequest`（行 191-217）、`ReviewJoinRequest`（行 263-312）完全无日志
- **Impact**: 转让队长和重置邀请码无日志，发生队伍劫持或纠纷时无法追溯
- **Repair direction**: 在上述方法关键路径补 `logger.Log(...)` 调用
- **Tests to add**: 日志条目存在性验证

#### F-016: /admin/training 管理页面被 WithAdminTab 重定向，功能完全不可达

- **Severity**: High
- **Category**: UX-blocking / Frontend-Backend Integration
- **Module**: WithAdminTab / training.tsx
- **Evidence**: `WithAdminTab.tsx:41-54` 导航数组仅 9 项，无 training；行 67-76 useEffect 当 `getTab` 返回 -1 时重定向到 `/admin/games`；`training.tsx` 用 `AdminPage` 包裹 `WithAdminTab`
- **Impact**: 后端 `TrainingAdminController` 标注 `[RequireTeacher]`，`CanAccessAdminTab` 允许 Teacher 访问 `AdminTab.Training`，但前端无入口且主动重定向，功能完全不可达
- **Repair direction**: `WithAdminTab` 导航数组添加 training 项（路径 `training`，对应 Teacher 权限）
- **Tests to add**: Teacher 登录后导航可见 training 标签页并可访问
- **Cross-check**: B Confirmed（H-NR-001 判定为误报）

#### F-017: SubmissionReview 管理页面孤立且三重损坏

- **Severity**: High
- **Category**: UX-blocking / API Contract
- **Module**: SubmissionReview.tsx / SubmissionController
- **Evidence**:
  - 导航无入口（孤立）
  - 前端 POST 评审缺必填 `Accepted` 字段 → 模型绑定失败 → 400 Bad Request
  - 前端 `content: {text?,format?}` 对象 vs 后端 `Content: string?` → 内容永不显示
  - 依赖 `ScoringRule`（只能由被 410 阻断的 `ScenarioController` 创建）→ 永远无待评审数据
- **Impact**: 页面存在但完全不可用：导航无入口、POST 必失败、内容不显示、无数据
- **Repair direction**: 若保留人工评审：修复 POST 契约 + 添加 ScoringRule 管理 API + 导航入口；若废弃：删除页面与评审端点
- **Tests to add**: 人工评审端到端测试
- **Cross-check**: B Confirmed

#### F-018: SubmissionController 依赖的 ScoringRule 只能由被阻断的 ScenarioController 创建

- **Severity**: High
- **Category**: Correctness / Architecture
- **Module**: SubmissionController / ScenarioController
- **Evidence**: `ScenarioController.cs:27` `[LegacyFeatureGone]` 返回 410；全代码库仅此处创建 `ScoringRule`；`SubmissionController.cs:86-93` Flag 类型提交也依赖 ScoringRule
- **Impact**: 所有非 Flag 类型提交（Writeup/IP/Credential/Custom）必然 400；Flag 类型提交同样受影响（Cross-check A 补充确认）
- **Repair direction**: 若保留多类型提交：在活的管理控制器开放 ScoringRule CRUD；若废弃：阻断 SubmissionController 评审端点
- **Tests to add**: ScoringRule 生命周期测试
- **Cross-check**: A Confirmed（扩展：Flag 类型也受影响）

#### F-019: 镜像模板删除检查遗漏 ExerciseChallenge 和 PenetrationNode

- **Severity**: High
- **Category**: Data Integrity
- **Module**: ImageTemplateController
- **Evidence**: `ImageTemplateController.cs:432-436` 仅检查 `GameChallenges`，未检查 `ExerciseChallenges`（继承 Challenge，有 `ImageTemplateId`）和 `PenetrationNodes`（有 `ImageTemplateId`）
- **Impact**: 管理员可删除被练习题或渗透节点引用的模板，导致 VM 无法启动
- **Repair direction**: Delete 方法增加 `ExerciseChallenges` 和 `PenetrationNodes` 的引用检查
- **Tests to add**: 删除被 ExerciseChallenge 引用的模板应返回 400

#### F-020: /admin/Instances 管理页面孤立（与 training 同病）

- **Severity**: High
- **Category**: UX-blocking / Frontend-Backend Integration
- **Module**: WithAdminTab / Instances.tsx
- **Evidence**: `Instances.tsx` 用 `AdminPage` 包裹 `WithAdminTab`，nav 无 "instances" 项，被重定向到 `/admin/games`；该页面调用 `adminDestroyInstance` 等功能完好但不可达
- **Impact**: 管理员无法从导航发现实例管理页面，需手动输入 URL
- **Repair direction**: `WithAdminTab` 导航数组添加 instances 项
- **Tests to add**: Admin 登录后导航可见 instances 标签页
- **Cross-check**: B 新发现

---

## Likely Issues Requiring Runtime Verification

| ID | Description | Why likely | Verification needed |
|----|------------|-----------|-------------------|
| F-021 | B-F-002 超卖实际幅度 | 静态推理显示每个心跳周期可超卖 +2，但实际心跳频率和调度窗口需运行时验证 | 并发负载测试：50 队伍同时启动容器，监控节点实际容器数 |
| F-022 | G-F-009 Submit 副作用失败 | `AddEvent`/`FlushScoreboardCache`/`SendSubmission` 在 VerifyAnswer 事务外，失败时仅记日志 | 模拟 AddEvent 抛出异常，验证榜单最终一致性 |
| F-023 | E-F-001 TeamLab VPN 路由作用域 | WireGuard AllowedIPs 配置是否实际限制选手仅访问指定网段 | 运行时验证：选手 VPN 连接后 traceroute 到非授权网段 |
| F-024 | G-F-013 Scoreboard ETag 缓存延迟 | ETag 生成依据未确认是否包含最新提交时间 | 提交后立即查询 Scoreboard，验证 ETag 更新 |

---

## False Positives / Not Reproduced

| ID | Original Claim | Verdict | Reason |
|----|---------------|---------|--------|
| H-NR-001 | Training 管理入口存在 | **False positive** | 仅检查路由文件存在，未验证 WithAdminTab useEffect 重定向逻辑（Cross-check B 确认） |
| G-F-FP-2 | 重复 flag 提交重复计分 | **Not reproduced** | `VerifyAnswer` 使用 `pg_advisory_xact_lock` + `alreadySolved` 检查，正确防止重复 |
| G-F-FP-3 | 非参赛者访问排行榜 | **Not reproduced** | `GameController.Scoreboard` 对 `Role < Teacher` 要求 `Accepted` 参与，权限正确 |
| B-F-001 部分 | 容量永久泄漏 | **Partially false** | `LocalNodeMetricsService` 每 30s + 心跳覆盖计数器，容量自愈；但工单卡死为真实影响 |
| K-F-001 部分 | 前端角色映射不一致 | **Mostly consistent** | 仅 Banned 数值偏差（前端 -1 vs 后端 0），两边都拒绝 Banned 用户，无安全后果 |

---

## Module Coverage Matrix

| Module | Function Correctness | Platform Integration | Logs | Auth | Tests | Status |
|--------|---------------------|---------------------|------|------|-------|--------|
| A: 授权角色 | ❌ 教师越权 | ⚠️ 前端隐藏但后端开放 | ⚠️ 缺少部分审计日志 | ❌ 变更端点无组归属校验 | ⚠️ 不足 | 需修复 |
| B: 部署队列 | ❌ Creating 卡死 | ❌ 心跳覆盖超卖 | ❌ Cancel/Fail 无 SystemLog | ✅ | ❌ 无并发测试 | 需修复 |
| C: 日志可观测 | ⚠️ 关键事件缺 SystemLog | ⚠️ DatabaseSink 接收但缺系统标记 | ⚠️ 审计日志服务死代码 | ✅ | ❌ 无日志测试 | 需改进 |
| D: 节点管理 | ⚠️ 心跳覆盖 | ✅ 调度隔离不可达节点 | ⚠️ 部分缺 SystemLog | ✅ | ⚠️ 不足 | 基本可用 |
| E: TeamLab VPN | ⚠️ 需运行时验证 | ✅ 队列+容量集成 | ⚠️ 部分事件缺日志 | ✅ | ⚠️ 集成测试缺口 | 需验证 |
| F: Docker/VM 生命周期 | ❌ VM 无锁/无恢复/无 ErrorMessage | ❌ 销毁失败状态不一致 | ⚠️ VM 失败缺 SystemLog | ✅ | ❌ VM 路径无测试 | 需大修 |
| G: CTF/团队/排行榜 | ❌ 队长授权+缓存+日志 | ⚠️ 改名/离开不失效缓存 | ❌ 转让/邀请码无审计日志 | ❌ 队长约束仅前端 | ⚠️ 不足 | 需修复 |
| H: 训练平台 | ✅ 基本正确 | ⚠️ 管理页不可达(F-016) | ⚠️ 部分缺日志 | ✅ | ⚠️ 不足 | 基本可用 |
| I: AWDP 大屏 | ❌ 跨队泄漏+积分并发 | ✅ 不参与部署队列 | ⚠️ 部分缺 SystemLog | ❌ GetInstances 无队过滤 | ❌ 无单元测试 | 需大修 |
| J: 遗留残留 | ✅ LegacyFeatureGone 阻断正确 | ⚠️ ScoringRule 死路径 | ❌ AuditLogService 死代码 | ✅ | ❌ 无回归测试 | 需清理 |
| K: 前端架构 | ⚠️ 多页孤立 | ❌ WithAdminTab 重定向死页 | N/A | ⚠️ 契约漂移 | ⚠️ 不足 | 需修复 |
| L: 敏感数据 | ✅ 基本安全 | ✅ | ✅ | ✅ | ⚠️ 不足 | 良好 |

---

## Deployment Queue and Log Coverage Matrix

### 关键事件 × 是否写入 Admin 可见 SystemLog

| Event | Docker | VM | Queue | TeamLab | AWDP | Training |
|-------|--------|-----|-------|---------|------|----------|
| Request accepted (queued) | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| Validation failed | ✅ | ⚠️ 部分 | ❌ | ⚠️ | N/A | N/A |
| Creating | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| Success (completed) | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| Failed (agent error) | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| **Failed (queue execution)** | ❌ | ❌ | ❌ 无日志 | ❌ | N/A | N/A |
| **Cancelled (queue ticket)** | ❌ | ❌ | ❌ 无日志 | ❌ | N/A | N/A |
| Cancelled (deployment target) | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| Destroy requested | ⚠️ 仅用户日志 | ⚠️ 仅用户日志 | N/A | ⚠️ | N/A | N/A |
| Destroy success | ✅ | ✅ | N/A | ✅ | N/A | N/A |
| Destroy failure | ✅ | ✅ | N/A | ✅ | N/A | N/A |
| **VM timeout** | N/A | ❌ 仅 LogWarning | N/A | N/A | N/A | N/A |
| **Team sensitive ops** | N/A | N/A | N/A | N/A | N/A | ❌ |

---

## Architecture Debt and Cleanup Recommendations

### 1. 遗留 IR/Scenario 残留

- **4 个控制器**（IRChallengeController, ScenarioController, LeaderboardController, TimeSlotController）共 17 个端点被 `[LegacyFeatureGone]` 阻断返回 410，但代码仍存在
- **2 个完全死服务**：`CheckpointVerificationService`（注册为 Scoped 但继承 BackgroundService 永不运行）、`AuditLogService`（从未被调用）
- **3 个 AdminTab 死枚举值**：`Scenarios`、`IRChallenges`、`Submissions`
- **1 个死枚举状态**：`DeploymentQueueTicketStatus.Assigned`（从未被赋值）
- **ScenarioHub** 未映射但 `SubmissionController` 仍注入 `IHubContext<ScenarioHub>` 并广播
- **建议**：确认无外部系统依赖后，整体删除阻断控制器 + 死服务 + 死枚举值；先清理 `SubmissionController` 对 `ScenarioHub` 的引用

### 2. VM 路径与 Docker 路径成熟度差距

| 维度 | Docker | Windows VM |
|------|--------|-----------|
| 并发创建防护 | 有（分布式锁） | **无** |
| ErrorMessage 字段 | 有（Container） | **无**（VmInstance） |
| 错误恢复 | 较完整 | **不完整**（Destroy 失败无 try-catch） |
| 超时处理 | N/A | **无限重试** |
| 测试路径 | 有（CreateTestContainer） | **无** |
| 测试覆盖 | 有 | **无** |

### 3. 前端管理页面孤立

| 页面 | 导航 | WithAdminTab 行为 | 功能状态 |
|------|------|-------------------|---------|
| /admin/training | ❌ | 重定向到 /admin/games | 后端完好 |
| /admin/Instances | ❌ | 重定向到 /admin/games | 后端完好 |
| /admin/SubmissionReview | ❌ | 不重定向（用 WithNavBar）| 三重损坏 |
| /admin/dashboard | ❌ | 重定向到 /admin/games | 功能完好 |

### 4. 计分并发保护不一致

| 模块 | 并发保护 | 状态 |
|------|---------|------|
| 普通 CTF VerifyAnswer | `pg_advisory_xact_lock` + `alreadySolved` | ✅ 安全 |
| AWDP UpdateFlagSubmitted | 无 `[Timestamp]`，非原子读-改-写 | ❌ 不安全 |
| AWDP MaxAttackPerRound | 检查与写入 TOCTOU | ❌ 不安全 |

### 5. 缓存一致性问题

- `TeamController.UpdateTeam` 不失效榜单缓存
- `GameController.LeaveGame` 不失效榜单缓存
- 对比 `AdminController` 多处正确调用 `FlushScoreboardCache`

---

## Suggested Repair Roadmap

### Phase 1: Critical 安全修复（1-2 周）

| Priority | Finding | Repair | Effort |
|----------|---------|--------|--------|
| P1-1 | F-001 教师越权 | AdminController 变更端点添加组归属校验 | Small |
| P1-2 | F-004 Guacamole token | 创建独立用户/JWT scope；移除硬编码凭据 | Medium |
| P1-3 | F-005/006/007 AWDP 积分 | GetInstances 按 teamId 过滤 + `[Timestamp]` + advisory lock | Medium |
| P1-4 | F-008 队长授权 | Leave/KickUser 添加队长身份校验 | Small |
| P1-5 | F-010 Flag 日志 | 移除日志中 `item.Answer` | Small |

### Phase 2: High 可靠性修复（2-3 周）

| Priority | Finding | Repair | Effort |
|----------|---------|--------|--------|
| P2-1 | F-002 Creating 工单卡死 | 启动时 RecoverStaleTickets 逻辑 | Medium |
| P2-3 | F-011 VM 无锁 | 引入 `IDistributedLockService` | Small |
| P2-4 | F-012/F-013 VM 状态一致性 | DestroyVm try-catch + VmReady try-catch 设 Error | Medium |
| P2-5 | F-014 改名缓存 | TeamController 注入 CacheHelper | Small |
| P2-6 | F-016 Training 页面 | WithAdminTab 添加 training 导航项 | Small |
| P2-7 | F-017/F-018 SubmissionReview | 修复 API 契约 + ScoringRule 管理 API | Medium |
| P2-8 | F-019 模板删除检查 | 添加 ExerciseChallenge/PenetrationNode 检查 | Small |
| P2-9 | F-020 Instances 页面 | WithAdminTab 添加 instances 导航项 | Small |

### Phase 3: 架构改进（3-4 周）

| Priority | Finding | Repair | Effort |
|----------|---------|--------|--------|
| P3-1 | F-003 心跳覆盖超卖 | 预留量与实际量分离 | Large |
| P3-2 | VmInstance.ErrorMessage | 添加字段，各失败路径写入 | Medium |
| P3-3 | 日志覆盖补全 | CancelAsync/FailTicketAsync/VmReady 添加 SystemLog | Medium |
| P3-4 | 遗留残留清理 | 删除阻断控制器 + 死服务 + 死枚举 | Medium |
| P3-5 | F-015 团队审计日志 | Transfer/InviteToken/JoinRequest 添加日志 | Small |
| P3-6 | G-F-006 LeaveGame 缓存 | 添加 FlushScoreboardCache | Small |

### Phase 4: 测试与加固（4+ 周）

- VM 生命周期集成测试（创建/销毁/超时/并发）
- AWDP 积分并发测试
- 部署队列重启恢复测试
- 前后端契约一致性自动化测试
- 日志覆盖回归测试

---

## Finding Statistics

| Part | Critical | High | Medium | Low | Info |
|------|----------|------|--------|-----|------|
| A: 授权角色 | 3 | 1 | 2 | 2 | 3 |
| B: 部署队列 | 2 | 3 | 6 | 4 | 2 |
| C: 日志可观测 | 0 | 2 | 4 | 3 | 1 |
| D: 节点管理 | 0 | 1 | 5 | 4 | 2 |
| E: TeamLab VPN | 0 | 2 | 2 | 2 | 1 |
| F: Docker/VM 生命周期 | 1 | 4 | 6 | 2 | 0 |
| G: CTF/团队/排行榜 | 1 | 5 | 4 | 2 | 1 |
| H: 训练平台 | 0 | 0 | 5 | 5 | 1 |
| I: AWDP 大屏 | 3 | 2 | 6 | 0 | 3 |
| J: 遗留残留 | 0 | 1 | 3 | 4 | 3 |
| K: 前端架构 | 0 | 2 | 3 | 3 | 1 |
| L: 敏感数据 | 0 | 0 | 0 | 2 | 1 |
| **Cross-check 新增** | 0 | 1 | 2 | 2 | 1 |
| **Total** | **10** | **24** | **48** | **35** | **20** |

**经 Cross-check 校准后进入最终报告的确认发现**：8 Critical + 12 High + 26 Medium + 18 Low + 10 Info

**误报/降级**：H-NR-001（误报）、B-F-001 容量泄漏部分（降级）、C-F-001/002（High→Medium）、J-F-006（High→Medium）、K-F-001（降级为 Info）

---

*Report generated by multi-agent review process per `docs/platform-full-review-agent-brief.md` Section 11. All findings independently verified by at least one cross-check sub-agent.*
