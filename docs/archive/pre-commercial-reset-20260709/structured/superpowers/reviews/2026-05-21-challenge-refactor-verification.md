# 题目管理系统重构 — 分模块验证方案

> **关联 Spec:** `docs/superpowers/specs/2026-05-21-challenge-management-refactor-design.md`
> **关联 Plan:** `docs/superpowers/plans/2026-05-21-challenge-management-refactor.md`
> **合并 Commit:** 36542eb (branch `002-challenge-refactor` → `001-ctf-scenario-engine`)

---

## 整体变更统计

| 维度 | 改进前 | 改进后 | 变化 |
|------|--------|--------|------|
| 题目类型数 | 6 (含 Scenario/IRChallenge) | 4 (纯 CTF) | -2 |
| 控制器数 | 14 个控制器 | 9 个控制器 | -5 |
| 服务数 | ~20 个服务 | ~12 个服务 | -8 |
| 数据模型表 | ~25 张表 | ~16 张表 | -9 |
| 评分系统 | 2 套并行 | 1 套统一 | 合并 |
| 镜像管理系统 | 2 套 (DockerImage + ImageTemplate) | 1 套 (ImageTemplate) | 合并 |
| Flag 模型 | 单 Flag / 单分值 | 多 Flag + 可选计分模式 + 阶段引导 | 扩展 |
| FirstSolve PK | (ParticipationId, ChallengeId) | (ParticipationId, ChallengeId, FlagId) | 扩展 |
| 前端管理页面 | 14 个标签页 | 10 个标签页 | -4 |
| 总代码行数 | 基准 | -3749 行 | 精简 |

---

## 模块 1: 枚举与类型系统

### 改进前
- `ChallengeType` 含 6 个值 (StaticAttachment/StaticContainer/DynamicAttachment/DynamicContainer/Scenario/IRChallenge)
- `VerificationType` 枚举 (AutoScript/AutoCommand/ManualAnswer/ManualReview) 仅 IR 使用
- `EnvironmentStatus` 枚举仅 IR 使用
- `ChallengeCategory` 含 Scenario 和 IR 分类
- `SubmissionType` 和 `ScoringSubmissionType` 命名冲突

### 改进方案
- `ChallengeType` 精简为 4 个值，删除 Scenario(4) 和 IRChallenge(8)
- 新增 `EnvironmentType` (None/Docker/WindowsVM)
- 新增 `FlagScoreMode` (InheritDecay/FixedScore)
- 新增 `AnswerType` (Flag/File/Custom)
- 删除 `VerificationType`、`EnvironmentStatus` 枚举
- `ChallengeCategory` 删除 Scenario 和 IR

### 检查要点
- [ ] `ChallengeType` 枚举只有 4 个值，位掩码正确 (0b00, 0b01, 0b10, 0b11)
- [ ] `EnvironmentType` 三值完整，JsonStringEnumConverter 注解存在
- [ ] `FlagScoreMode` 两值完整，JsonStringEnumConverter 注解存在
- [ ] `AnswerType` 三值完整，不与旧 `SubmissionType` 命名冲突
- [ ] `IsScenario()` 和 `IsIRChallenge()` 扩展方法已删除
- [ ] 无任何文件引用 `VerificationType`、`EnvironmentStatus`、`ChallengeCategory.Scenario`、`ChallengeCategory.IR`
- [ ] `IsDynamic()` 扩展方法对 `DynamicContainer` 仍返回 true

### 风险点
- 前端 `Shared.tsx` 中 `ChallengeType` 和 `ChallengeCategory` label map 是否同步更新
- 任何硬编码 `type === 'Scenario'` 或 `type === 'IRChallenge'` 的前端逻辑
- 数据库迁移是否正确处理旧 Scenario/IRChallenge 类型数据

---

## 模块 2: Flag 模型与管理 API

### 改进前
- `FlagContext` 仅含 `Id`, `Flag`, `IsOccupied`, `AttachmentId`, `ChallengeId`, `ExerciseId`
- 每个题目只有一个或多个平级 Flag，无顺序、无描述、无计分配置
- `ScoringRule` 独立表控制验证方式和衰减
- Flag API 仅支持 POST (批量添加) 和 DELETE

### 改进方案
- `FlagContext` 扩展 8 个字段: `OrderIndex`, `Description`, `ScoreMode`, `FixedScore`, `MaxAttempts`, `AttachmentHash`, `AnswerType`, `CustomName`
- 删除 `ScoringRule` 表，Flag 自带计分配置
- API 新增 `PUT /api/Edit/Games/{id}/Challenges/{cId}/Flags/{fId}` 更新端点

### 检查要点
- [ ] `FlagContext` 所有 8 个新字段存在，类型正确，MaxLength 注解到位
- [ ] `FlagCreateModel` 包含所有新字段，与 `FlagContext` 字段对应
- [ ] POST Flags 端点能接受含新字段的 `FlagCreateModel[]` 并正确存储
- [ ] NEW: PUT Flags 端点存在，能正确更新单个 Flag 的所有字段
- [ ] DELETE Flags 端点正常工作
- [ ] `OrderIndex` 字段被正确保存和返回（用于前端阶段排序）
- [ ] `AnswerType` 三值切换正确映射到验证逻辑（Flag→字符串比对, File→SHA256, Custom→字符串比对）
- [ ] `ScoreMode` 在 `VerifyAnswer` 中被正确读取和应用
- [ ] `MaxAttempts=0` 表示无限尝试，`>0` 时正确限制
- [ ] `AttachmentHash` 字段存在且验证逻辑正确使用
- [ ] 旧 `ScoringRule` 表在迁移中被删除

### 风险点
- 前端 Flag 编辑页 (`flags/index.tsx`) 是否展示新字段的编辑控件
- 挑战编辑页的 Flag 列表区域是否更新为支持 ScoreMode/AnswerType 选择
- 旧 Scenario/IR 的 Stage/Checkpoint 数据迁移是否正确映射到 FlagContext

---

## 模块 3: FirstSolve 与排行榜

### 改进前
- `FirstSolve` 复合主键为 `(ParticipationId, ChallengeId)` — 每题每队只有一个解
- `GenScoreboard` 按 Challenge 粒度迭代
- 动态衰减在 Challenge 级别计算，所有 Flag 共享同一衰减曲线
- 血牌(一血/二血/三血)在 Challenge 级别分配

### 改进方案
- `FirstSolve` 复合主键扩展为 `(ParticipationId, ChallengeId, FlagId)`
- `GenScoreboard` 改为按 `(ChallengeId, FlagId)` 粒度迭代
- 每个 Flag 独立计算衰减和血牌
- 团队总分 = 各 Flag 得分之和
- `SolveSnapshot` 包含 `FlagId` 字段
- `ChallengeItem` 新增 `SolvedFlags`/`TotalFlags` 展示

### 检查要点
- [ ] `FirstSolve` 主键为三字段: `[PrimaryKey(nameof(ParticipationId), nameof(ChallengeId), nameof(FlagId))]`
- [ ] `SolveSnapshot` 包含 `FlagId` 字段
- [ ] `GenScoreboard` 中 FirstSolves 查询的 Select 包含 FlagId
- [ ] 数组初始化解决 `allFlags` 作用域问题（在事务块外声明）
- [ ] `flagAcceptedCounts` 按 `(ChallengeId, FlagId)` 聚合而非仅 `ChallengeId`
- [ ] 动态衰减公式对 InheritDecay 模式的 Flag 使用正确的 `acceptedCount`
- [ ] FixedScore 模式的 Flag 直接返回 `flag.FixedScore`，不经过衰减
- [ ] 血牌(1st/2nd/3rd)按每个 Flag 独立分配
- [ ] 团队某题总分 = 该题所有已解 Flag 得分之和
- [ ] 缓存刷新逻辑保持不变（7天 Redis 缓存）
- [ ] Division 权限过滤逻辑保持不变

### 风险点
- 旧数据 `FirstSolve.FlagId` 默认为 0/空时的兼容处理
- `ChallengeItem` 模型中的 `TotalFlags` 是否正确从数据库加载
- 排行榜 `ScoreboardModel` 前端渲染是否正确展示 Flag 完成情况
- 旧提交(`FlagId = null`)在排行榜中的处理

---

## 模块 4: 提交系统

### 改进前
- 双通道: `GameController` (Channel→FlagChecker 异步) + `SubmissionController` (UnifiedScoringEngine 同步)
- `FlagChecker` 后台服务消费 Channel 异步处理
- `SubmissionController` 使用策略模式 + ScoringRule 表
- 旧通道处理血牌，新通道不处理
- `Channel<Submission>` + `ChannelWriter<Submission>` 在 GameController 中注入

### 改进方案
- 单通道: `GameController.Submit` 同步调用 `VerifyAnswer`
- 删除 `FlagChecker`、`SubmissionController`、整个 `Services/Scoring/` 目录
- `Submission` 新增 `FlagId` 字段
- `FlagSubmitModel` 新增 `FlagId` 字段
- 验证结果直接返回，不再轮询

### 检查要点
- [ ] `GameController.cs` 中无 `Channel<Submission>` 或 `ChannelWriter<Submission>` 引用
- [ ] `GameController.cs` 中无 `using System.Threading.Channels`
- [ ] `GameController.Submit` 调用 `VerifyAnswer` 后直接返回结果 (含 Status, Score, BloodType)
- [ ] `submission.FlagId = model.FlagId` 正确赋值
- [ ] `FlagSubmitModel.FlagId` 为 `int?` 类型
- [ ] `Submission.FlagId` 为 `int?` 类型
- [ ] `FlagChecker.cs` 已删除
- [ ] `SubmissionController.cs` 已删除
- [ ] `Services/Scoring/` 整个目录已删除
- [ ] `Services/ScoringService.cs` 已删除
- [ ] `Services/LeaderboardService.cs` 已删除
- [ ] DI 注册中无任何已删除服务的引用
- [ ] 无编译错误残留

### 风险点
- 前端 `GameChallengeModal.tsx` 中提交 polling 逻辑是否需要更新（现在同步返回）
- 旧版 gameStatus 轮询端点是否仍然需要
- 前端是否正确处理新的返回格式 `{ id, Status, Score, BloodType }`

---

## 模块 5: 镜像管理

### 改进前
- 两套并行系统: `DockerImage` 模型 + `ImageTemplate` 模型
- `DockerController` 处理 Docker JSON 创建
- `ImageTemplateController` 处理 VM 镜像上传 (.qcow2/.ova/.vmdk)
- 无压缩包上传支持，无 Docker 镜像注册到 ImageTemplate
- `DockerImageBuilder` 从 Dockerfile 文本构建

### 改进方案
- 统一为 `ImageTemplate` 模型 (扩展 `OriginalArchiveName` 字段)
- 删除 `DockerImage` 模型和 `DockerController`
- 新增 `POST /api/v1/image-templates/upload` (zip/tar.gz 上传)
- 新增 `POST /api/v1/image-templates/register-docker` (Docker 注册)
- 前端 DockerImages 页面删除，"VM 镜像"改名为"环境模板"

### 检查要点
- [ ] `DockerImage.cs` 已删除
- [ ] `DockerController.cs` 已删除
- [ ] `DockerImageBuilder.cs` 已删除
- [ ] `DockerComposeDeployer.cs` 已删除 (或保留但确认不依赖 DockerImage)
- [ ] `ImageTemplate.OriginalArchiveName` 字段存在 (MaxLength 256)
- [ ] `ImageTemplateController` 有 `POST upload` 端点 (60GB 限制)
- [ ] `ImageTemplateController` 有 `POST register-docker` 端点
- [ ] `DockerRegisterRequest` 模型存在 (Name/RegistryUrl/OSType)
- [ ] `IArchiveExtractor` 接口和 `ArchiveExtractor` 实现存在
- [ ] `ArchiveExtractor` 已注册到 DI (`ServicesExtension.cs`)
- [ ] 前端 `images/Index.tsx` 页面标题改为"环境模板"
- [ ] 前端 `DockerImages/` 目录已删除
- [ ] 管理导航"环境模板"标签存在

### 风险点
- `ContainerOrchestrator` 服务是否仍引用 `DockerImage`
- `DockerManager.CreateContainerAsync` 使用的 `ContainerConfig.Image` 仍为裸字符串，是否正确
- 前端环境模板页面是否需要新增 Docker 注册入口 UI

---

## 模块 6: 控制器路由

### 改进前
- 14 个控制器，路由分散在 `api/Edit/`、`api/Game/`、`api/v1/` 三套约定下
- ScenarioController (`api/v1/scenarios`)
- IRChallengeController (`api/v1/ir-challenges`)
- DockerController (`api/v1/docker`)
- SubmissionController (`api/v1/submissions`)
- TimeSlotController (`api/v1/timeslots`)
- LeaderboardController (独立)

### 改进方案
- 统一到 `EditController` (管理端) + `GameController` (玩家端) + `ImageTemplateController` (镜像)
- 删除 6 个控制器
- 保持现有路由约定 (api/Edit/ 和 api/Game/ 和 api/v1/image-templates)

### 检查要点
- [ ] 无 `ScenarioController.cs`
- [ ] 无 `IRChallengeController.cs`
- [ ] 无 `DockerController.cs`
- [ ] 无 `SubmissionController.cs`
- [ ] 无 `TimeSlotController.cs`
- [ ] 无 `LeaderboardController.cs`
- [ ] `EditController` 无编译错误
- [ ] `GameController` 无编译错误
- [ ] `ImageTemplateController` 无编译错误
- [ ] 所有已删除控制器对应的 DI 注册已移除
- [ ] 前端无对 `/api/v1/scenarios`、`/api/v1/ir-challenges`、`/api/v1/docker`、`/api/v1/submissions` 的 fetch 调用

### 风险点
- 前端 `Api.ts` 是否清理了旧 API handler
- `GamePhaseController` 是否依赖 Scenario (需确认 game phases 是否保留)
- 任何 SignalR hub 是否引用已删除的 Scenario/IR instance

---

## 模块 7: 前端页面结构

### 改进前
- 14 个管理标签页，含独立的场景管理、IR 题目、Docker 镜像、实例管理页面
- 场景/IR 创建使用独立的多步 Stepper 页面
- 玩家端有 ScenarioPlayer 和 IRChallengePlayer 独立页面
- 题目创建 Modal 支持 6 种类型选择

### 改进方案
- 10 个管理标签页，场景/IR/Docker/实例管理全部删除
- 挑战编辑页统一处理所有题目类型
- 玩家端 ChallengeModal 增加 Flag 步骤条
- 题目创建 Modal 只显示 4 种类型

### 检查要点
- [ ] `WithAdminTab.tsx` pages 数组只有 10 项（仪表盘/比赛/团队/用户/环境模板/节点/部署队列/提交评审/日志/系统设置）
- [ ] 删除的页面目录: `scenarios/`, `ir-challenges/`, `DockerImages/`, `Instances.tsx`, `ScenarioPlayer.tsx`, `IRChallengePlayer.tsx`
- [ ] `ChallengeCreateModal.tsx` 类型下拉无 Scenario 和 IRChallenge
- [ ] 挑战编辑页有"环境配置"卡片（EnvironmentType 选择）
- [ ] 挑战编辑页 Flag 区域有 ScoreMode 选择
- [ ] 玩家 `ChallengeModal.tsx` 有多 Flag 步骤指示器
- [ ] 玩家 `GameChallengeModal.tsx` 提交时传递 `flagId`
- [ ] `pnpm build` 成功

### 风险点
- `~react-pages` 文件系统路由是否因删除目录产生死路由
- 旧页面组件被其他文件 import 导致编译错误
- 前端 `useChallengeTypeLabelMap` 和 `useChallengeCategoryLabelMap` 是否清理
- 前端 SignalR hub 连接是否引用旧 scenario/ir 逻辑

---

## 模块 8: 数据迁移

### 改进前
- 多张独立表: `Stages`, `ScenarioInstances`, `IRCheckpoints`, `IRInstances`, `ScoringRules`, `DockerImages`, `DeploymentQueues`, `StageDependencies`
- `FirstSolve` 双字段主键
- `Submission` 无 FlagId

### 改进方案
- EF Core Migration `UnifiedChallengeRefactor`
- 删除 8 张旧表
- 新增列: FlagContext(7列), Challenge(2列), FirstSolve(FlagId), Submission(FlagId), ImageTemplate(OriginalArchiveName), GameChallenge(Environment+ImageTemplateId)

### 检查要点
- [ ] 迁移文件存在于 `Migrations/20260521111658_UnifiedChallengeRefactor.cs`
- [ ] Designer 文件存在且包含所有表变更
- [ ] Up 方法中 DROP TABLE: Stages, ScenarioInstances, IRCheckpoints, IRInstances, ScoringRules, DockerImages, DeploymentQueues, StageDependencies
- [ ] Up 方法中 ADD COLUMN: FlagContext (OrderIndex, Description, ScoreMode, FixedScore, MaxAttempts, AttachmentHash, AnswerType, CustomName)
- [ ] Up 方法中 ADD COLUMN: Challenge (Environment, ImageTemplateId)
- [ ] Up 方法中 ADD COLUMN: FirstSolve (FlagId), Submission (FlagId)
- [ ] Up 方法中 ADD COLUMN: ImageTemplate (OriginalArchiveName)
- [ ] Down 方法正确回滚（删除新增列，重建旧表）
- [ ] `AppDbContextModelSnapshot.cs` 不包含已删除的表

### 风险点
- 旧数据迁移脚本 (SQL 插入 FlagContext 等) 未包含在迁移中 (需手动执行)
- 非空列新增 (FirstSolve.FlagId) 是否有默认值处理
- 迁移在生产数据库上执行的安全性和幂等性

---

## 模块 9: 依赖注入与编译

### 改进前
- 14+ 服务注册在 `ServicesExtension.cs`
- 多个 HostedService (FlagChecker)
- 策略模式工厂 (IVerificationStrategy 的 4 个实现)

### 改进方案
- 删除所有冗余注册
- 新增 `IArchiveExtractor` 注册
- 保留核心服务 (IContainerManager, IVirtualMachineProvider, IFleetManager 等)

### 检查要点
- [ ] `dotnet build` 0 错误 (允许预存的 VmManager 弃用警告)
- [ ] `dotnet test` 通过 (>200 测试, 0 失败, 0 跳过)
- [ ] `ServicesExtension.cs` 无对已删除服务的注册
- [ ] `ServicesExtension.cs` 有 `builder.Services.AddScoped<IArchiveExtractor, ArchiveExtractor>()`
- [ ] `Program.cs` 无 `AddHostedService<FlagChecker>()`
- [ ] 无 using 引用已删除的命名空间 (`GZCTF.Services.Scoring`)
- [ ] `pnpm build` 成功 (前端 ClientApp)

### 风险点
- 预存警告 (VmManager 弃用) 确认无害
- Test 项目中被删除的 3 个 Scoring 测试文件不会在 CI 中被引用

---

## 验证执行说明

每个模块的检查要点使用 `grep`、`dotnet build`、`dotnet test`、文件读取等方式逐项验证。
检查者需要对每个 `[ ]` 给出 PASS / FAIL / WARN 判定，并在发现问题时给出具体文件名和行号。
