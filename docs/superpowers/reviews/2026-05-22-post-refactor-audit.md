# 题目管理系统重构 — 后续审计任务

> **审计范围:** 重构完成后的安全隐患、遗漏、优化点
> **审计方法:** 只读分析（grep/build/read），不修改代码
> **输出:** 按 CRITICAL / HIGH / MEDIUM / LOW 分级的问题清单，每个问题附文件名+行号+修复建议

---

## 任务 1：模型引用完整性检查

**目标:** 确认所有已删除模型（DockerImage, Stage, ScenarioInstance, IRCheckpoint, IRInstance, ScoringRule, DeploymentQueue, TimeSlot）无残留引用。

**方法:**
```bash
# 对每个已删除的模型名执行 grep
grep -rn "DockerImage\|Class.*Stage[^d]\|ScenarioInstance\|ScenarioTimelineEntry\|IRCheckpoint[^s]\|IRInstance[^s]\|ScoringRule[^s]\|DeploymentQueue\b\|class TimeSlot" \
  src/GZCTF/ --include="*.cs" | grep -v "Migrations/" | grep -v ".Designer.cs" | grep -v "\.bak"
```
预期结果：**零匹配**（迁移文件除外）。

**重点检查:**
- `Models/AppDbContext.cs` — 确认无残留 DbSet 或 Fluent API 配置
- `Extensions/Startup/ServicesExtension.cs` — 确认无残留 DI 注册
- 任何 `using GZCTF.Services.Scoring` 或 `using GZCTF.Models.Data` 中对已删除类型的引用

---

## 任务 2：Submit 同步化验证

**目标:** 确认 `GameController.Submit` 已完全从 Channel/FlagChecker 异步模式转换为同步处理。

**检查点:**
- [ ] `GameController.cs` 中无 `Channel<Submission>`、`ChannelWriter<Submission>`、`using System.Threading.Channels`
- [ ] `GameController.cs` 的 Submit 方法中调用 `VerifyAnswer` 后直接返回结果（含 Status, Score, BloodType）
- [ ] `ServicesExtension.cs` 中无 `AddChannel<Submission>()`
- [ ] `FlagChecker.cs` 已删除
- [ ] 前端 `GameChallengeModal.tsx` 的提交轮询逻辑是否需要更新（现在同步返回，轮询不再必要）

**风险点:** 前端每 500ms 轮询 `gameStatus` 端点——现在 Submit 是同步的，轮询应该只需要一次就能拿到结果。建议确认 gameStatus 端点是否仍然存在且正确返回。

---

## 任务 3：GenScoreboard Flag 粒度正确性

**目标:** 验证 GenScoreboard 按 Flag 粒度迭代逻辑无 bug。

**文件:** `Repositories/GameRepository.cs`

**检查点:**
- [ ] `SolveSnapshot` record 包含 `FlagId`，且查询的 Select 中正确投影
- [ ] `allFlags` 在事务块外声明（之前有作用域 bug）
- [ ] `flagAcceptedCounts` 使用 `(ChallengeId, FlagId)` 复合键聚合
- [ ] 动态衰减对 `FixedScore` 模式的 Flag 正确跳过（直接返回固定分值）
- [ ] 血牌(1st/2nd/3rd) 按每个 Flag 独立分配（同一挑战的不同 Flag 可以有各自的一血）
- [ ] 团队总分 = 所有已解 Flag 得分之和（不是取最高分或取平均）
- [ ] 缓存刷新逻辑保持不变（7 天 Redis 缓存）

---

## 任务 4：前端 API 端点对齐

**目标:** 确认所有前端 fetch 调用对应的后端端点存在。

**方法:**
```bash
# 收集所有前端 fetch/axios 调用
grep -rn "fetch(\|api\." src/GZCTF/ClientApp/src --include="*.tsx" --include="*.ts" | grep -v node_modules | grep -v "\.bak"
```
对照后端路由表，逐一验证每个端点：
- [ ] `/api/v1/submissions/*` → 这些路由已删除，前端是否还有调用？
- [ ] `/api/v1/scenarios/*` → 已删除，前端是否残留？
- [ ] `/api/v1/ir-challenges/*` → 已删除，前端是否残留？
- [ ] `/api/v1/docker/*` → 已删除，前端是否残留？
- [ ] `/api/v1/timeslots/*` → 已删除，TimeSlotPicker 是否还存在？
- [ ] `SubmissionReview.tsx` 中对 `/api/v1/submissions/pending-review` 的调用 → 需要迁移到新端点

**注意:** `@Api` 自动生成的客户端（`src/GZCTF/ClientApp/src/Api.ts`）可能包含对已删除端点的引用，检查后确认是否需要重新生成。

---

## 任务 5：新 DB 部署可用性

**目标:** 确认全新空数据库部署时，迁移链路完整。

**检查点:**
- [ ] `PrelaunchHelper.RunPrelaunchWorkAsync()` 中的 `MigrateAsync()` 能正确处理所有迁移
- [ ] `EntityConfigurationProvider.LoadAsync()` 中的 `MigrateAsync()` 同样处理
- [ ] 两处 `ConfigureWarnings` 均忽略 `PendingModelChangesWarning`
- [ ] 所有迁移文件（`Migrations/*.cs`）的 Designer 快照与当前模型一致
- [ ] `AppDbContextModelSnapshot.cs` 与当前模型无差异（运行 `dotnet ef migrations list --no-connect` 确认无 pending changes）

**验证方法:**
```bash
# 生成从零开始的 SQL 脚本
dotnet ef migrations script 0 -o /tmp/full.sql
# 检查脚本中是否包含所有需要的表/列/索引
```

---

## 任务 6：Flag 编辑独立页面功能缺失

**文件:** `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/flags/index.tsx`

**问题:** 此页面仅有 `FlagInfo { id, flag }` 的简单展示和添加，缺少新增的 7 个字段（ScoreMode, AnswerType, OrderIndex, Description, FixedScore, MaxAttempts, CustomName）。

**评估:** 
- 主编辑页（`challenges/[challengeId]/index.tsx`）已经支持完整 Flag 配置
- 独立 Flag 页是辅助入口，标记为 MEDIUM 优先级
- 需要决策：是否将此页面重定向到主编辑页，还是补齐新字段

---

## 任务 7：ConfigureWarnings 抑制的最终清理

**目标:** 理想情况服务器运行时无任何 EF Core 警告需要抑制。

**当前状态:**
- `DatabaseExtension.cs` — `ConfigureWarnings` 在两处 (AddDbContext + AddEntityConfiguration)
- `EntityConfigurationProvider.cs` — `ConfigureWarnings` 在 CreateAppDbContext

**验证:**
- [ ] 运行 `dotnet ef migrations list --no-connect` 确认无 pending changes
- [ ] 确认当前数据库 `__EFMigrationsHistory` 表中最新的迁移名与本地迁移文件一致
- [ ] 如果模型和 DB 完全一致，理论上可以移除 ConfigureWarnings 抑制

**注意:** `ConfigureWarnings` 抑制本身是安全的，不是必须移除。但如果模型和 DB 有差异而被抑制掉，运行时访问缺失列会报 500。

---

## 任务 8：Container.GenerateMetadata 死代码

**文件:** `Models/Data/Container.cs`

**问题:** 删除了 `ExerciseInstance` 导航属性和 `ExerciseMetadata` record，但 `GenerateMetadata()` 方法仍有 ExerciseInstance 分支的残留注释或引用。

**检查:**
- [ ] GenerateMetadata 方法仅包含 GameInstance 分支
- [ ] `ExerciseMetadata` record 已删除
- [ ] `JsonSerializerContext.cs` 中 `[JsonSerializable(typeof(ExerciseMetadata))]` 已删除
- [ ] 无任何文件引用 `ExerciseMetadata`

---

## 任务 9：ImageTemplateController.UploadArchive 集成验证

**文件:** `Controllers/ImageTemplateController.cs`

**问题:** 已改为委托 `IArchiveExtractor`，但 ArchiveExtractor 依赖 `qemu-img` 命令行工具。

**检查:**
- [ ] `IArchiveExtractor` 已注册到 DI
- [ ] UploadArchive 正确调用 `_archiveExtractor.ExtractAndRegisterAsync()`
- [ ] 异常处理完善（temp file 清理、错误返回）
- [ ] ArchiveExtractor 中命令注入风险（`tar` 和 `qemu-img` 的参数来自用户输入的文件名，需验证转义）

**安全关注:** `ArchiveExtractor.RunCommandAsync` 中 `archivePath` 来自用户上传的文件名，直接拼入 shell 命令。检查是否已经做了路径转义。

---

## 任务 10：前端 Shared.tsx 和 Api.ts 枚举同步

**文件:** `utils/Shared.tsx`, `Api.ts`

**检查:**
- [ ] `useChallengeTypeLabelMap` 只包含 4 种类型 (无 Scenario/IRChallenge)
- [ ] `useChallengeCategoryLabelMap` 无 Scenario/IR 分类
- [ ] `Api.ts` 中 `ChallengeType` 枚举与后端一致
- [ ] `Api.ts` 中 `ChallengeCategory` 枚举与后端一致
- [ ] 缺少的枚举（`EnvironmentType`, `FlagScoreMode`, `AnswerType`）是否需要前端定义

---

## 任务 11：ChallengeEditCard 清理

**文件:** `components/admin/ChallengeEditCard.tsx`

**检查:**
- [ ] 是否有对已删除类型（Scenario/IRChallenge）的特殊处理逻辑
- [ ] 进度条/分数显示是否适用于新模型（多 Flag 时代码可能需要展示 Flag 完成数）

---

## 输出格式

对每个任务报告：
```
## 任务 N: [标题]
**状态:** PASS / ISSUES FOUND
**问题:** [具体问题描述，含文件名+行号]
**建议:** [修复方案]
```

整体汇总：按 CRITICAL / HIGH / MEDIUM / LOW 分类所有发现的问题。
