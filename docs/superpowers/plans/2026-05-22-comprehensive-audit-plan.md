# GZCTF 题目管理系统重构 — 终极审计计划

> **审计原则:** 逐行验证、白盒分析、不假设任何代码正确、不放过任何边界条件
> **审计方法:** 读代码 + grep + 静态推理，不运行、不修改
> **输出:** 每个发现标注文件:行号 + 严重级别 + 修复方案

---

## 一、数据模型完整性审计

### 1.1 Challenge 基类字段审计

**文件:** `Models/Data/Challenge.cs`

- [ ] `Environment` 字段默认值 `EnvironmentType.None`，确认所有旧数据（迁移前创建的题目）的默认值正确
- [ ] `ImageTemplateId` 为 nullable int，确认当 `Environment=Docker` 但 `ImageTemplateId=null` 时容器创建不会 NPE
- [ ] `ContainerImage` 字段仍存在（保留为 Docker 运行时默认值），确认 Docker 类型题目不从 `ImageTemplateId` 解析时回退到这个字段
- [ ] `FlagTemplate` 字段仍存在，确认动态容器的 flag 生成逻辑未被 FlagContext 重构破坏
- [ ] 验证 `ExerciseChallenge` 也继承并正确拥有 `Environment`/`ImageTemplateId` 字段

### 1.2 FlagContext 扩展字段审计

**文件:** `Models/Data/FlagContext.cs`

- [ ] 8 个新字段：逐一验证 `MaxLength` annotation 正确，`ScoreMode`/`AnswerType` 默认值合理
- [ ] `OrderIndex` 默认 0 —— 旧数据迁移后所有 Flag 的 OrderIndex=0，`OrderBy(f => f.OrderIndex)` 行为是否正确？
- [ ] `AttachmentHash` 用于 File 类型提交的 SHA256 比对 —— 验证 `VerifyAnswer` 中 `ToSHA256String()` 的调用和非 null 检查
- [ ] `AnswerType` 的默认值 `Flag` 确保旧数据（无此字段）向后兼容
- [ ] `MaxAttempts=0` 表示无限制 —— 验证 `VerifyAnswer` 中的 `if (targetFlag.MaxAttempts > 0)` 条件正确

### 1.3 FirstSolve 主键变更审计

**文件:** `Models/Data/FirstSolve.cs`

- [ ] 复合主键 `(ParticipationId, ChallengeId, FlagId)` —— 确认迁移中正确 DROP 旧 PK + ADD 新 PK
- [ ] `FlagId` 为 `int`（非 nullable）—— 旧 FirstSolve 数据的迁移脚本如何处理？
- [ ] 检查 GenScoreboard 中所有引用 `FirstSolve` 的 JOIN 条件都包含 FlagId

### 1.4 Submission 模型审计

**文件:** `Models/Data/Submission.cs`

- [ ] `FlagId` 为 nullable int —— 验证 `VerifyAnswer` 中对 null 的回退逻辑
- [ ] `SubmissionType` 从旧枚举 `ScoringSubmissionType` 改为 `string` —— 验证 DB 列已改为 `text`
- [ ] `Status` 列使用 `HasConversion<string>()` —— 确认 `AnswerResult` 枚举值与字符串的映射正确
- [ ] `FlagContext` 导航属性 —— 确认没有不必要的 AutoInclude 导致性能问题

### 1.5 已删除模型的残留引用

**对以下每个已删除模型执行 grep：**
- `DockerImage`, `Stage`, `ScenarioInstance`, `ScenarioTimelineEntry`, `IRCheckpoint`, `IRInstance`, `ScoringRule`, `DeploymentQueue`, `TimeSlot`

**检查范围：**
- [ ] 所有 `.cs` 文件（排除 `Migrations/` 目录）
- [ ] 所有 `.tsx` / `.ts` 文件
- [ ] `AppDbContextModelSnapshot.cs` 中无引用
- [ ] 任何 `using` 语句中无已删除命名空间
- [ ] 任何 DI 注册（`ServicesExtension.cs`）中无已删除服务

---

## 二、评分系统白盒审计

### 2.1 GenScoreboard 重写审计

**文件:** `Repositories/GameRepository.cs`

**关键路径逐行分析:**

- [ ] `SolveSnapshot` record 包含 `FlagId` —— 验证所有引用点都已更新
- [ ] FirstSolves 查询的 Select 子句投影了 `x.fs.FlagId`
- [ ] `allFlags` 声明在事务块外 —— 之前有过作用域 bug，确认修复
- [ ] 按 `(ChallengeId, FlagId)` 分组的迭代逻辑：
  - [ ] `flagGroup.Key.FlagId` 在 `allFlags` 中查找到对应的 `FlagContext`
  - [ ] `flag.ScoreMode == FixedScore` 时跳过衰减公式
  - [ ] `flag.ScoreMode == InheritDecay` 时使用 `challengeMeta.OriginalScore` 或 `flag.FixedScore`
- [ ] 血牌分配：
  - [ ] 每个 Flag 独立计算 1st/2nd/3rd 血牌
  - [ ] 血牌资格检查（时间窗口、Deadline、`DisableBloodBonus`、Division权限）
  - [ ] 血牌数量不超过 3
- [ ] 团队总分 = 所有 Flag 得分之和（不是最大值或平均值）
- [ ] 动态衰减：
  - [ ] `acceptedCount` 正确统计有 `AffectDynamicScore` 权限的解题者
  - [ ] 衰减公式 `(minRate + (1-minRate) * exp((1-count)/difficulty))` 的输入参数正确
- [ ] 缓存逻辑：Redis 7 天缓存刷新条件正确

### 2.2 VerifyAnswer 重写审计

**文件:** `Repositories/GameInstanceRepository.cs`

- [ ] Flag 查找逻辑：
  - [ ] `submission.FlagId.HasValue` → 精确查找
  - [ ] 无 FlagId → `OrderBy(f => f.OrderIndex).FirstOrDefaultAsync()`
  - [ ] 无 Flag 时返回 `NotFound`
- [ ] AnswerType 路由：
  - [ ] `Flag` → 精确字符串比较
  - [ ] `File` → SHA256 哈希比较（非空检查 `AttachmentHash`）
  - [ ] `Custom` → 同 Flag
- [ ] 尝试次数限制：
  - [ ] `targetFlag.MaxAttempts > 0` 时统计 `(ParticipationId, ChallengeId, FlagId)` 的提交数
  - [ ] 统计包含 `Status != Accepted` 还是所有状态？
- [ ] 并发控制：
  - [ ] `pg_advisory_xact_lock` 的锁键计算是否正确
  - [ ] 锁键是否包含 FlagId
- [ ] FirstSolve 创建：
  - [ ] `new FirstSolve { ParticipationId, ChallengeId, FlagId, SubmissionId }` 四字段完整
  - [ ] 重复检查包含 FlagId
- [ ] 血牌资格：
  - [ ] `CountBloodEligibleSolves` 调用传入 `flagId`
  - [ ] 方法内部查询包含 `fs.FlagId == flagId`
- [ ] 旧代码路径：是否仍有只按 ChallengeId 过滤的遗留逻辑？

---

## 三、Flag 管理全链路审计

### 3.1 后端 CRUD

**文件:** `Controllers/EditController.cs`

- [ ] POST `/api/Edit/Games/{id}/Challenges/{cId}/Flags`：
  - [ ] `FlagCreateModel[]` 所有 8 个新字段被读取
  - [ ] `FlagContext` 构造时所有字段被赋值
  - [ ] `OrderIndex` 不会因批量添加而冲突
- [ ] PUT `/api/Edit/Games/{id}/Challenges/{cId}/Flags/{fId}`（新增）：
  - [ ] 更新所有 8 个扩展字段
  - [ ] 确认 `OrderIndex` 更新后不需要重排其他 Flag
- [ ] DELETE `/api/Edit/Games/{id}/Challenges/{cId}/Flags/{fId}`：
  - [ ] 级联影响：删除的 Flag 如果有 `FirstSolve` 引用会怎样？

**文件:** `Repositories/GameChallengeRepository.cs`

- [ ] `AddFlags` 方法：是否补全了所有扩展字段的赋值？
- [ ] `Update` 方法（ChallengeUpdateModel）：Environment/ImageTemplateId 是否被正确应用？

### 3.2 前端 Flag 编辑

**文件:** `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/index.tsx`
**文件:** `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx`

- [ ] Flag 列表展示：是否显示 ScoreMode、AnswerType、OrderIndex
- [ ] ScoreMode 下拉框：InheritDecay / FixedScore 选项
- [ ] FixedScore 输入框仅 ScoreMode=FixedScore 时显示
- [ ] AnswerType 下拉框：Flag / File / Custom
- [ ] POST 请求 body 包含所有 `FlagCreateModel` 字段
- [ ] PUT 请求完整（如果支持编辑）

### 3.3 玩家端 Flag 提交

**文件:** `ClientApp/src/components/GameChallengeModal.tsx`
**文件:** `ClientApp/src/components/ChallengeModal.tsx`

- [ ] 多 Flag 步骤条：解锁逻辑、当前步骤高亮、已完成步骤勾
- [ ] `activeFlagId` 正确传递给 Submit API
- [ ] 单 Flag 挑战（`flags.length === 1`）不显示步骤条
- [ ] 提交后同步处理（不再轮询 `gameStatus`）

---

## 四、提交与排行全链路审计

### 4.1 GameController.Submit 同步化

**文件:** `Controllers/GameController.cs`

- [ ] `Channel<Submission>` 已完全移除
- [ ] `ChannelWriter<Submission>` 已完全移除
- [ ] `using System.Threading.Channels` 已移除
- [ ] `FlagChecker` 后台服务已删除
- [ ] 提交后直接调用 `VerifyAnswer` 并返回 `{ id, Status, BloodType }`
- [ ] 异常处理：`VerifyAnswer` 抛出异常时是否正确 rollback？

### 4.2 前端提交流程

**文件:** `ClientApp/src/components/GameChallengeModal.tsx`

- [ ] `onSubmit` 调用 `checkDataFlag(res.data.id, res.data.status)` 直接处理结果
- [ ] 无 `setInterval` 或 `useEffect` 轮询 `gameStatus`
- [ ] Accepted 时自动销毁动态容器实例
- [ ] Wrong 时显示随机提示
- [ ] `mutate(data, false)` 防止 revalidation 覆盖

### 4.3 排行榜

**文件:** `Repositories/GameRepository.cs` — GenScoreboard

- [ ] `ScoreboardModel` 包含 `SolvedFlags` / `TotalFlags` 字段
- [ ] `ChallengeItem` 展示 Flag 完成数（而非仅总分）
- [ ] 前端排行榜渲染是否使用新字段

---

## 五、环境模板与镜像管理审计

### 5.1 后端 API

**文件:** `Controllers/ImageTemplateController.cs`

- [ ] 所有 admin 端点使用 `[RequireAdmin]`（非 `[Authorize(Roles)]`）
- [ ] `register-docker` 端点：
  - [ ] `DockerRegisterRequest` 包含 `RegistryAuth` 字段
  - [ ] `ImageTemplate.Status` 设为 `Ready`
- [ ] `upload` 端点：
  - [ ] `RequestSizeLimit(60GB)` 实际有效
  - [ ] 委托给 `IArchiveExtractor.ExtractAndRegisterAsync()`
  - [ ] temp 目录在 finally 块中清理
- [ ] `Delete` 端点：
  - [ ] 检查模板是否被题目引用（`Challenge.ImageTemplateId`）
  - [ ] 被引用时拒绝删除并返回错误信息

### 5.2 ArchiveExtractor 安全审计

**文件:** `Services/Vm/ArchiveExtractor.cs`

- [ ] **命令注入风险：**
  - [ ] `RunCommandAsync("tar", $"-xzf \"{archivePath}\" ...")` — `archivePath` 来自用户上传的文件名
  - [ ] `RunCommandAsync("qemu-img", $"convert ... \"{inputPath}\" \"{outputPath}\"")` — 路径来自文件系统扫描
  - [ ] **验证所有传入命令行的路径参数都经过 sanitize 或使用绝对路径**
- [ ] VM 格式检测优先级：
  - [ ] `.vmx` + `.vmdk` 同时存在 → VMware
  - [ ] `.qcow2` → KVM
  - [ ] `.ova` → 解包
- [ ] `OSType` 检测启发式规则充分覆盖常见场景
- [ ] SHA256 计算使用 `async` 流式读取，不会 OOM

### 5.3 前端环境模板

**文件:** `ClientApp/src/pages/admin/images/Index.tsx`

- [ ] `data?.items` 正确访问分页响应（不是 `data.map`）
- [ ] 注册 Docker Modal：所有字段验证 + loading 状态
- [ ] 上传压缩包：`FormData` + `POST /api/v1/image-templates/upload`
- [ ] 删除：确认对话框 + `mutate()` 刷新
- [ ] 本地导入 Modal：路径验证
- [ ] 状态 Badge：Ready(green) / Importing(yellow) / Error(red)

---

## 六、节点与部署审计

### 6.1 后端 API

**文件:** `Controllers/NodesController.cs`

- [ ] `Detail` 端点：
  - [ ] 返回脱敏 DTO（排除 `AuthToken`）
  - [ ] 确认不再返回完整 `WorkerNode` 实体
- [ ] `Heartbeat` 端点：
  - [ ] 心跳写入 DB 的频率是否合理
  - [ ] 离线检测（`MarkStaleNodesOfflineAsync`）的 timeout 是否配置
- [ ] `Register` / `RegisterHere` 端点：
  - [ ] Admin 权限正确
  - [ ] SSH 部署命令无注入风险

### 6.2 前端节点管理

**文件:** `ClientApp/src/pages/admin/nodes/Index.tsx`
**文件:** `ClientApp/src/pages/admin/nodes/[id]/Detail.tsx`
**文件:** `ClientApp/src/components/admin/DeployButton.tsx`
**文件:** `ClientApp/src/components/admin/CleanupButton.tsx`

- [ ] 节点列表：15 秒轮询已实现
- [ ] DeployButton：内联表单收集 IP/用户名/密码 → `POST /api/v1/nodes`
- [ ] CleanupButton：获取节点列表 → 逐个 DELETE 离线节点
- [ ] Detail 页：不展示 `authToken`
- [ ] 删除按钮：`DELETE /api/v1/nodes/{id}` → 刷新列表

---

## 七、部署队列审计

### 7.1 后端

**文件:** `Controllers/NodesController.cs`（`DeploymentTargetsController`）

- [ ] `GET /api/v1/deployment-targets` 端点：
  - [ ] 支持 `status` 过滤
  - [ ] 支持分页 `page` / `pageSize`
  - [ ] `[Authorize]` 权限
- [ ] `DELETE /api/v1/deployment-targets/{id}` 端点：
  - [ ] 只能取消 Pending/Running 状态的任务
  - [ ] `[RequireAdmin]` 权限

### 7.2 前端

**文件:** `ClientApp/src/pages/admin/queue/Index.tsx`

- [ ] 使用 SWR 10 秒轮询
- [ ] 状态筛选下拉框（Pending/Running/Completed/Failed/Cancelled）
- [ ] 取消按钮仅对 Pending/Running 显示
- [ ] 错误信息 Tooltip 展示
- [ ] 总记录数展示
- [ ] 时间格式化

---

## 八、数据库迁移完整性审计

### 8.1 迁移文件检查

- [ ] `20260522013352_UnifiedChallengeRefactor`（旧版，已被移除）：
  - [ ] 确认该迁移已在服务器 DB 的 `__EFMigrationsHistory` 中标记为 applied
- [ ] `20260522112016_FixSubmissionTypeColumn`（最新）：
  - [ ] `Up()` 方法：`ALTER TABLE "Submissions" ALTER COLUMN "SubmissionType" TYPE text`
  - [ ] `Down()` 方法：正确回滚
- [ ] `AppDbContextModelSnapshot.cs`：
  - [ ] 无已删除模型的 Entity 定义
  - [ ] `Submission.SubmissionType` 为 `string` 类型（无 `.HasConversion<byte>()`）
  - [ ] `FirstSolve` 主键包含 `FlagId`
  - [ ] `FlagContext` 包含所有 8 个新字段

### 8.2 迁移一致性

**对比 DB schema vs Model vs Snapshot:**

- [ ] 执行 `dotnet ef migrations list --no-connect` 确认无 pending changes
- [ ] `SELECT tablename FROM pg_tables WHERE schemaname='public'` 确认已删除的表不存在
- [ ] 确认新列存在：
  - [ ] `FlagContexts.OrderIndex, ScoreMode, FixedScore, MaxAttempts, AnswerType, Description, CustomName, AttachmentHash`
  - [ ] `Challenges.Environment, ImageTemplateId` (GameChallenges + ExerciseChallenges)
  - [ ] `FirstSolves.FlagId`
  - [ ] `Submissions.FlagId`
  - [ ] `ImageTemplates.OriginalArchiveName`
- [ ] 确认已删除的表不存在：`Stages`, `ScenarioInstances`, `IRCheckpoints`, `IRInstances`, `ScoringRules`, `DockerImages`, `DeploymentQueues`, `StageDependencies`

---

## 九、ConfigureWarnings 抑制源审计

**审计目标:** 确认所有 3 处 `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` 都存在并正确配置

| 位置 | 文件 | 状态 |
|------|------|------|
| AddDbContext | `Extensions/Startup/DatabaseExtension.cs` | [ ] 验证 |
| AddEntityConfiguration | `Extensions/Startup/DatabaseExtension.cs` | [ ] 验证 |
| CreateAppDbContext | `Providers/EntityConfigurationProvider.cs` | [ ] 验证 |

- [ ] `using Microsoft.EntityFrameworkCore.Diagnostics;` 在以上文件中存在

---

## 十、前端路由与页面完整性审计

### 10.1 管理导航

**文件:** `ClientApp/src/components/admin/WithAdminTab.tsx`

- [ ] pages 数组包含 9 项（仪表盘/比赛/团队/用户/环境模板/节点/部署队列/日志/设置）
- [ ] 无 Scenario、IR、Docker、Instance 相关路由
- [ ] 无 SubmissionReview 路由（已删除）

### 10.2 页面存在性

- [ ] 以下页面目录已删除：
  - [ ] `pages/admin/scenarios/`
  - [ ] `pages/admin/ir-challenges/`
  - [ ] `pages/admin/DockerImages/`
  - [ ] `pages/admin/Instances.tsx`
  - [ ] `pages/admin/SubmissionReview.tsx`
  - [ ] `pages/game/ScenarioPlayer.tsx`
  - [ ] `pages/game/IRChallengePlayer.tsx`
  - [ ] `components/scenario/`
  - [ ] `hooks/useScenario.ts`, `hooks/useIRChallenge.ts`, `hooks/useNodes.ts`, `hooks/useSubmission.ts`

### 10.3 前端类型与后端同步

**文件:** `ClientApp/src/Api.ts`

- [ ] `ChallengeType` 枚举只有 4 个值（无 Scenario/IRChallenge）
- [ ] `ChallengeCategory` 枚举无 Scenario/IR
- [ ] `EnvironmentType` 枚举存在（None/Docker/WindowsVM）
- [ ] `FlagScoreMode` 枚举存在（InheritDecay/FixedScore）
- [ ] `AnswerType` 枚举存在（Flag/File/Custom）
- [ ] `FlagSubmitModel` 包含 `flagId?: number`

---

## 十一、安全审计

### 11.1 命令注入

- [ ] `ArchiveExtractor.cs` — `tar` / `qemu-img` 参数来自用户输入 → 必须验证 escape
- [ ] `NodeDeployService.cs` — SSH 命令参数 → 已有白名单验证，确认未被重构破坏

### 11.2 认证与授权

- [ ] 无 `[Authorize(Roles = "Admin")]` 残留（全部改为 `[RequireAdmin]`）
- [ ] `[Authorize]` 正确用于需要登录的只读端点
- [ ] `NodesController.Detail` 不返回 `AuthToken`

### 11.3 信息泄露

- [ ] Flag 在响应中是否被遮蔽（SHA256 哈希 vs 明文）
- [ ] 异常消息不暴露连接字符串或内部路径
- [ ] Swagger/OpenAPI 不暴露敏感端点

### 11.4 并发与竞态

- [ ] `VerifyAnswer` 使用 `pg_advisory_xact_lock` — 确认锁键包含 FlagId
- [ ] `Submit` 的 retry 循环（最多 3 次）正确处理 `DbUpdateConcurrencyException`

---

## 十二、已知架构级缺失（不在本次修复范围，但需记录）

1. **Agent 程序未实现** — 心跳端点存在但无人调用，节点 CPU/内存指标永远为 0
2. **QueueManager 只分配不执行** — 需要后台服务执行实际的容器/VM 部署
3. **FleetManager 未被业务代码调用** — 调度器已实现但未接入
4. **VM 生命周期不完整** — `Vnstance` 模型创建了但没有被更新（Running/Destroyed 状态未同步）
5. **TimeSlots 表仍在 DB 中** — 模型已删除但迁移未 DROP TABLE（需补充迁移）
6. **`Dotnet-ef` 工具在服务器上不可用** — .NET 10 主机只装了 runtime 没装 SDK，导致 `dotnet ef` 无法运行
7. **Docker Hub 匿名拉取限流 (429)** — 依赖外部镜像的题目可能失败
8. **SWR mutate(false) 修复不完整** — Destroy 调用后 UI 可能仍需要手动刷新

---

## 十三、审计输出格式

每个检查项输出：

```
[检查项编号] [严重级别] [文件:行号]
描述: 一句话问题描述
影响: 对用户/系统的影响
修复: 具体修复建议
```

**严重级别定义:**
- **P0:** 导致崩溃/500/数据丢失/安全漏洞
- **P1:** 功能不可用或数据错误
- **P2:** 可用但有缺陷（UI 不刷新、性能下降）
- **P3:** 代码卫生（死代码、命名不一致、缺注释）
