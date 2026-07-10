# 题目管理系统重构 — 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将三套分散的题目系统（CTF/Scenario/IR）合并为统一的题目管理框架，增加多Flag支持，合并镜像管理，完善VM上传→分发→部署链路。

**Architecture:** 后端扩展 FlagContext 替代 Stage/IRCheckpoint，FirstSolve PK 改为三字段支持每 Flag 独立计分，GenScoreboard 改为 Flag 粒度迭代；前端合并场景/IR 创建页到挑战编辑页，玩家端改用箭头步骤条。

**Tech Stack:** .NET 10 / EF Core 10 / PostgreSQL 16 / React 19 + Mantine / KVM libvirt

**Spec:** `docs/superpowers/specs/2026-05-21-challenge-management-refactor-design.md`

---

## File Map

| 操作 | 文件 | 职责 |
|------|------|------|
| **修改** | `Utils/Enums.cs:280-450` | 删除 Scenario/IRChallenge 枚举值，新增 EnvironmentType/FlagScoreMode/AnswerType，删除 IR 相关枚举 |
| **修改** | `Models/Data/Challenge.cs` | 新增 EnvironmentType, ImageTemplateId 字段 |
| **修改** | `Models/Data/FlagContext.cs` | 新增 OrderIndex, Description, ScoreMode, FixedScore, MaxAttempts, AttachmentHash, AnswerType, CustomName |
| **修改** | `Models/Data/FirstSolve.cs` | 主键改为 (ParticipationId, ChallengeId, FlagId) |
| **修改** | `Models/Data/Submission.cs` | 新增 FlagId 字段 |
| **修改** | `Models/Data/ImageTemplate.cs` | 新增 OriginalArchiveName |
| **修改** | `Models/Data/GameChallenge.cs` | 新增 SubmissionTypes 导航属性；移除 ScoringRules 导航属性 |
| **修改** | `Models/Data/ChallengeSubmissionType.cs` | 简化为 AnswerType + Label + IsActive |
| **修改** | `Models/Data/ScoringRule.cs` | ~~删除~~ |
| **修改** | `Models/Data/ScenarioEntities.cs` | ~~删除~~ |
| **修改** | `Models/Data/IREntities.cs` | ~~删除~~ |
| **修改** | `Models/Data/DockerImage.cs` | ~~删除~~ |
| **修改** | `Models/Request/Edit/ChallengeUpdateModel.cs` | 新增 Environment/ImageTemplateId/SubmissionTypes 字段 |
| **修改** | `Models/Request/Edit/FlagCreateModel.cs` | 新增 OrderIndex/Description/ScoreMode/FixedScore/MaxAttempts/AnswerType |
| **修改** | `Models/Request/Game/FlagSubmitModel.cs` | 新增 FlagId |
| **修改** | `Controllers/EditController.cs` | 扩展 Flag CRUD，扩展 Challenge Update；移除 Scenario/IR 引用 |
| **修改** | `Controllers/GameController.cs` | Submit 改为同步处理 + 多 Flag 支持 |
| **修改** | `Controllers/ImageTemplateController.cs` | 新增 upload (压缩包) + register-docker 端点 |
| **删除** | `Controllers/ScenarioController.cs` | — |
| **删除** | `Controllers/IRChallengeController.cs` | — |
| **删除** | `Controllers/DockerController.cs` | — |
| **删除** | `Controllers/SubmissionController.cs` | — |
| **删除** | `Services/FlagChecker.cs` | 提交改为同步 |
| **删除** | `Services/Scoring/` (整个目录) | 策略模式不再需要 |
| **删除** | `Services/LeaderboardService.cs` | 统一用 GenScoreboard |
| **修改** | `Repositories/GameInstanceRepository.cs` | VerifyAnswer 支持 FlagId + 同步处理 |
| **修改** | `Repositories/GameRepository.cs` | GenScoreboard 改为 Flag 粒度 |
| **新增** | `Services/Vm/ArchiveExtractor.cs` | 压缩包解压 + VM 格式检测 + qemu-img 转换 |
| **修改** | `Services/Vm/KvmProvider.cs` | 接入 VmInstance 生命周期追踪 |
| **修改** | `Services/Fleet/ImageDistributionService.cs` | Agent 文件下载端点 |
| **修改** | `Services/Fleet/AgentProtocolService.cs` | Agent REST Pull + HMAC |
| **修改** | `ClientApp/src/utils/Shared.tsx` | 删除 Scenario/IRChallenge 类型 |
| **修改** | `ClientApp/src/components/admin/WithAdminTab.tsx` | 精简导航 |
| **修改** | `ClientApp/src/components/admin/ChallengeCreateModal.tsx` | 删除 Scenario/IRChallenge 类型选项 |
| **修改** | `ClientApp/src/pages/admin/games/[id]/challenges/[challengeId]/index.tsx` | 重构为单页面分区编辑 |
| **删除** | `ClientApp/src/pages/admin/scenarios/` (整个目录) | — |
| **删除** | `ClientApp/src/pages/admin/ir-challenges/` (整个目录) | — |
| **删除** | `ClientApp/src/pages/admin/DockerImages/` (整个目录) | — |
| **删除** | `ClientApp/src/pages/admin/Instances.tsx` | — |
| **修改** | `ClientApp/src/components/GameChallengeModal.tsx` | Flag 提交流程支持多 Flag + 阶段引导 |
| **修改** | `ClientApp/src/components/ChallengeModal.tsx` | Flag 区域改为箭头步骤条 |
| **删除** | `ClientApp/src/pages/game/ScenarioPlayer.tsx` | — |
| **删除** | `ClientApp/src/pages/game/IRChallengePlayer.tsx` | — |

---

## Phase A: 数据模型变更

### Task A1: 枚举清理与新增

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Utils\Enums.cs`

**Step 1: 删除 Scenario 和 IRChallenge 枚举值**

在 `ChallengeType` 枚举中：
```csharp
// 修改前:
[Flags]
public enum ChallengeType : byte
{
    StaticAttachment  = 0b00,
    StaticContainer   = 0b01,
    DynamicAttachment = 0b10,
    DynamicContainer  = 0b11,
    Scenario          = 0b100,   // 删除
    IRChallenge        = 0b1000,  // 删除
}

// 修改后:
[Flags]
public enum ChallengeType : byte
{
    StaticAttachment  = 0b00,
    StaticContainer   = 0b01,
    DynamicAttachment = 0b10,
    DynamicContainer  = 0b11,
}
```

同步更新扩展方法（`IsScenario()`, `IsIRChallenge()` → 删除）。

**Step 2: 新增 EnvironmentType 枚举**

```csharp
public enum EnvironmentType : byte
{
    None      = 0,
    Docker    = 1,
    WindowsVM = 2,
}
```

**Step 3: 新增 FlagScoreMode 枚举**

```csharp
public enum FlagScoreMode : byte
{
    InheritDecay = 0,
    FixedScore   = 1,
}
```

**Step 4: 新增 AnswerType 枚举（避免与现有 SubmissionType 冲突）**

```csharp
public enum AnswerType : byte
{
    Flag   = 0,
    File   = 1,
    Custom = 2,
}
```

**Step 5: 删除 IR 相关枚举**

删除 `VerificationType` 枚举（AutoScript/AutoCommand/ManualAnswer/ManualReview）和 `EnvironmentStatus` 枚举。

**Step 6: 从 ChallengeCategory 中删除 Scenario 和 IR**

```csharp
// ChallengeCategory — 删除 Scenario 和 IR 值
```

**Step 7: 编译验证**

Run: `dotnet build src/GZCTF/GZCTF.csproj --no-restore`
Expected: 编译错误（其他文件仍引用被删除的枚举值）— 正常，后续任务修复。

**Step 8: Commit**

```bash
git add src/GZCTF/Utils/Enums.cs
git commit -m "feat(model): remove Scenario/IRChallenge enums, add EnvironmentType/FlagScoreMode/AnswerType"
```

---

### Task A2: FlagContext 模型扩展

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\FlagContext.cs`

**Step 1: 扩展 FlagContext**

在现有 `FlagContext` 类中新增以下字段（保留所有现有字段）：

```csharp
// 新增字段
public int OrderIndex { get; set; }
public string? Description { get; set; }
public FlagScoreMode ScoreMode { get; set; } = FlagScoreMode.InheritDecay;
public int FixedScore { get; set; }
public int MaxAttempts { get; set; }
public string? AttachmentHash { get; set; }
public AnswerType AnswerType { get; set; } = AnswerType.Flag;
public string? CustomName { get; set; }
```

**Step 2: 配置 MaxLength**

在 `AppDbContext` 的 `OnModelCreating` 中或通过 DataAnnotations 为 `Description` 添加 `[MaxLength(512)]`，为 `AttachmentHash` 添加 `[MaxLength(128)]`，为 `CustomName` 添加 `[MaxLength(64)]`。

**Step 3: Commit**

```bash
git add src/GZCTF/Models/Data/FlagContext.cs
git commit -m "feat(model): extend FlagContext with multi-flag support fields"
```

---

### Task A3: Challenge 基类扩展

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\Challenge.cs`

**Step 1: 新增字段**

```csharp
public EnvironmentType Environment { get; set; } = EnvironmentType.None;
public int? ImageTemplateId { get; set; }
public ImageTemplate? ImageTemplate { get; set; }
```

**Step 2: Commit**

```bash
git add src/GZCTF/Models/Data/Challenge.cs
git commit -m "feat(model): add EnvironmentType and ImageTemplateId to Challenge"
```

---

### Task A4: FirstSolve 主键变更

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\FirstSolve.cs`

**Step 1: 变更复合主键**

```csharp
// 修改前:
[PrimaryKey(nameof(ParticipationId), nameof(ChallengeId))]
// 修改后:
[PrimaryKey(nameof(ParticipationId), nameof(ChallengeId), nameof(FlagId))]

// 新增字段:
public int FlagId { get; set; }
public FlagContext? FlagContext { get; set; }
```

**Step 2: Commit**

```bash
git add src/GZCTF/Models/Data/FirstSolve.cs
git commit -m "feat(model): change FirstSolve PK to include FlagId"
```

---

### Task A5: Submission 新增 FlagId

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\Submission.cs`

**Step 1: 新增字段**

```csharp
public int? FlagId { get; set; }
public FlagContext? FlagContext { get; set; }
```

**Step 2: Commit**

```bash
git add src/GZCTF/Models/Data/Submission.cs
git commit -m "feat(model): add FlagId to Submission"
```

---

### Task A6: ImageTemplate 扩展 + DockerImage 删除

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\ImageTemplate.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\DockerImage.cs`

**Step 1: ImageTemplate 新增字段**

```csharp
public string? OriginalArchiveName { get; set; }
```

**Step 2: 删除 DockerImage.cs**

删除整个文件。

**Step 3: 从 AppDbContext 移除 DockerImages DbSet**

```csharp
// AppDbContext.cs — 删除:
// public DbSet<DockerImage> DockerImages { get; set; }
```

**Step 4: Commit**

```bash
git rm src/GZCTF/Models/Data/DockerImage.cs
git add src/GZCTF/Models/Data/ImageTemplate.cs src/GZCTF/Models/AppDbContext.cs
git commit -m "feat(model): extend ImageTemplate, remove DockerImage model"
```

---

### Task A7: 删除冗余模型文件

**Files:**
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\ScenarioEntities.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\IREntities.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\ScoringRule.cs`

**Step 1: 删除 ScenarioEntities.cs（Stage, ScenarioInstance, ScenarioTimelineEntry）**

```bash
git rm src/GZCTF/Models/Data/ScenarioEntities.cs
```

**Step 2: 删除 IREntities.cs（IRCheckpoint, IRInstance）**

```bash
git rm src/GZCTF/Models/Data/IREntities.cs
```

**Step 3: 删除 ScoringRule.cs**

```bash
git rm src/GZCTF/Models/Data/ScoringRule.cs
```

**Step 4: 从 AppDbContext 移除对应的 DbSet**

移除 `Stages`, `ScenarioInstances`, `IRCheckpoints`, `IRInstances`, `ScoringRules`, `ScenarioTimelineEntries` 的 DbSet 声明和相关配置。

**Step 5: Commit**

```bash
git commit -m "feat(model): remove Scenario/IR/ScoringRule models"
```

---

### Task A8: 简化 ChallengeSubmissionType

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\ChallengeSubmissionType.cs`

**Step 1: 简化模型**

保留核心字段，删除不再需要的字段：

```csharp
public class ChallengeSubmissionType
{
    public int Id { get; set; }
    public int ChallengeId { get; set; }
    public AnswerType Type { get; set; } = AnswerType.Flag;
    public int OrderIndex { get; set; }
    [MaxLength(64)]
    public string? Label { get; set; }  // AnswerType=Custom 时的显示名
    public bool IsActive { get; set; } = true;
    
    public GameChallenge? Challenge { get; set; }
}
```

删除原有字段：`RequireFile`, `AcceptedFileExtensions`, `MaxFileSize`（这些合并到 FlagContext 中或直接由后端处理）。

**Step 2: Commit**

```bash
git add src/GZCTF/Models/Data/ChallengeSubmissionType.cs
git commit -m "feat(model): simplify ChallengeSubmissionType to AnswerType/Label/IsActive"
```

---

### Task A9: 更新 GameChallenge 导航属性

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Data\GameChallenge.cs`

**Step 1: 删除 ScoringRules 导航属性，更新 SubmissionTypes**

```csharp
// 删除:
// public List<ScoringRule>? ScoringRules { get; set; }

// 确保 SubmissionTypes 导航属性存在:
public List<ChallengeSubmissionType>? SubmissionTypes { get; set; }
```

**Step 2: 更新 Update 方法**

从 `Update(ChallengeUpdateModel)` 方法中移除 ScoringRule 相关字段更新，添加 EnvironmentType/ImageTemplateId/SubmissionTypes 更新逻辑。

**Step 3: Commit**

```bash
git add src/GZCTF/Models/Data/GameChallenge.cs
git commit -m "feat(model): update GameChallenge nav properties for unified model"
```

---

### Task A10: 更新 Request/Response 模型

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Request\Edit\ChallengeUpdateModel.cs`
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Request\Edit\FlagCreateModel.cs`
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Request\Game\FlagSubmitModel.cs`
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Models\Request\Edit\ChallengeInfoModel.cs`

**Step 1: ChallengeUpdateModel 新增字段**

```csharp
public EnvironmentType Environment { get; set; } = EnvironmentType.None;
public int? ImageTemplateId { get; set; }
public List<ChallengeSubmissionTypeUpdateModel>? SubmissionTypes { get; set; }
```

**Step 2: FlagCreateModel 扩展**

```csharp
public class FlagCreateModel
{
    // 现有字段:
    [Required, MaxLength(Limits.MaxFlagLength)]
    public string Flag { get; set; } = string.Empty;
    public int? AttachmentId { get; set; }
    
    // 新增字段:
    public int OrderIndex { get; set; }
    [MaxLength(512)]
    public string? Description { get; set; }
    public FlagScoreMode ScoreMode { get; set; } = FlagScoreMode.InheritDecay;
    public int FixedScore { get; set; }
    public int MaxAttempts { get; set; }
    public string? AttachmentHash { get; set; }
    public AnswerType AnswerType { get; set; } = AnswerType.Flag;
    [MaxLength(64)]
    public string? CustomName { get; set; }
}
```

**Step 3: FlagSubmitModel 新增字段**

```csharp
public class FlagSubmitModel
{
    [Required]
    public string Flag { get; set; } = string.Empty;
    public int? FlagId { get; set; }  // 新增
}
```

**Step 4: ChallengeInfoModel 删除 Scenario 和 IRChallenge**

从有效类型列表中移除这些值。

**Step 5: Commit**

```bash
git add src/GZCTF/Models/Request/
git commit -m "feat(model): update request models for unified challenge system"
```

---

## Phase B: 删除冗余代码

### Task B1: 删除旧控制器

**Files:**
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\ScenarioController.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\IRChallengeController.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\DockerController.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\SubmissionController.cs`

```bash
git rm src/GZCTF/Controllers/ScenarioController.cs
git rm src/GZCTF/Controllers/IRChallengeController.cs
git rm src/GZCTF/Controllers/DockerController.cs
git rm src/GZCTF/Controllers/SubmissionController.cs
git commit -m "feat: remove Scenario/IR/Docker/Submission controllers"
```

---

### Task B2: 删除旧服务

**Files:**
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Services\FlagChecker.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Services\LeaderboardService.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Services\ScoringService.cs`
- Delete: `D:\newGZ\YINYU CTF平台\src\GZCTF\Services\Scoring\` (整个目录)

```bash
git rm src/GZCTF/Services/FlagChecker.cs
git rm src/GZCTF/Services/LeaderboardService.cs
git rm src/GZCTF/Services/ScoringService.cs
git rm -r src/GZCTF/Services/Scoring/
git commit -m "feat: remove FlagChecker/LeaderboardService/Scoring services"
```

---

### Task B3: 更新依赖注入注册

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Extensions\Startup\ServicesExtension.cs` 或相应 DI 注册文件

**Step 1: 移除已删除服务的注册**

从 `ServicesExtension.cs` 中删除：
```csharp
// 删除这些行:
// services.AddScoped<IScoringRuleRepository, ...>();
// services.AddSingleton<FlagChecker>();
// services.AddHostedService<FlagChecker>();
// services.AddScoped<IScoringService, ScoringService>();
// services.AddScoped<ILeaderboardService, LeaderboardService>();
// services.AddScoped<IUnifiedScoringEngine, UnifiedScoringEngine>();
// ...所有 IVerificationStrategy 实现注册...
```

**Step 2: 确保保留的服务正确注册**

保留：`IContainerManager`, `IVirtualMachineProvider`, `IFleetManager`, `INodeRepository`, `IImageStorage`, `INodeDeployService`

**Step 3: Commit**

```bash
git add src/GZCTF/Extensions/Startup/ServicesExtension.cs
git commit -m "feat(di): remove registrations for deleted services"
```

---

## Phase C: 扩展保留的控制器

### Task C1: 扩展 EditController — Flag CRUD

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\EditController.cs`

**Step 1: 新增 PUT Flag 端点**

在现有 POST/DELETE Flag 路由的基础上，新增一个 PUT 端点用于更新 Flag：

```csharp
[HttpPut("Games/{id:int}/Challenges/{cId:int}/Flags/{fId:int}")]
public async Task<IActionResult> UpdateFlag(int id, int cId, int fId, [FromBody] FlagCreateModel model)
{
    var challenge = await _context.GameChallenges
        .Include(c => c.Flags)
        .FirstOrDefaultAsync(c => c.GameId == id && c.Id == cId, HttpContext.RequestAborted);
    if (challenge is null) return NotFound();
    
    var flag = challenge.Flags?.FirstOrDefault(f => f.Id == fId);
    if (flag is null) return NotFound();
    
    flag.Flag = model.Flag;
    flag.OrderIndex = model.OrderIndex;
    flag.Description = model.Description;
    flag.ScoreMode = model.ScoreMode;
    flag.FixedScore = model.FixedScore;
    flag.MaxAttempts = model.MaxAttempts;
    flag.AttachmentHash = model.AttachmentHash;
    flag.AnswerType = model.AnswerType;
    flag.CustomName = model.CustomName;
    
    await _context.SaveChangesAsync(HttpContext.RequestAborted);
    return Ok();
}
```

**Step 2: 更新 POST Flag 端点以匹配新 FlagCreateModel**

修改现有 `AddFlags` 方法，使其接受扩展后的 `FlagCreateModel[]`：

```csharp
[HttpPost("Games/{id:int}/Challenges/{cId:int}/Flags")]
public async Task<IActionResult> AddFlags(int id, int cId, [FromBody] FlagCreateModel[] models)
{
    // 现有验证逻辑...
    
    foreach (var model in models)
    {
        var flag = new FlagContext
        {
            ChallengeId = cId,
            Flag = model.Flag,
            AttachmentId = model.AttachmentId,
            OrderIndex = model.OrderIndex,
            Description = model.Description,
            ScoreMode = model.ScoreMode,
            FixedScore = model.FixedScore,
            MaxAttempts = model.MaxAttempts,
            AttachmentHash = model.AttachmentHash,
            AnswerType = model.AnswerType,
            CustomName = model.CustomName,
        };
        challenge.Flags?.Add(flag);
    }
    // 现有保存逻辑...
}
```

**Step 3: Commit**

```bash
git add src/GZCTF/Controllers/EditController.cs
git commit -m "feat(api): extend EditController Flag CRUD with PUT and new fields"
```

---

### Task C2: 扩展 EditController — Challenge Update 扩展字段

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\EditController.cs`

**Step 1: 更新 UpdateGameChallenge 方法**

在 `UpdateGameChallenge` 方法（`PUT Games/{id}/Challenges/{cId}`）中，扩展支持的字段。`ChallengeUpdateModel` 已包含新增字段（Task A10），此处只需确保 `challenge.Update(model)` 能处理新字段。

**Step 2: 确保 Challenge.Update 方法处理新字段**

在 `GameChallenge.Update(ChallengeUpdateModel)` 方法中：
```csharp
Environment = model.Environment;
ImageTemplateId = model.ImageTemplateId;
```

**Step 3: Commit**

```bash
git add src/GZCTF/Controllers/EditController.cs src/GZCTF/Models/Data/GameChallenge.cs
git commit -m "feat(api): extend challenge update with environment/image template fields"
```

---

### Task C3: 重写 GameController.Submit — 同步处理

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\GameController.cs`

**Step 1: 将提交从异步 Channel 改为同步处理**

修改 `Submit` 方法（`POST {id}/Challenges/{challengeId}`），不再将 submission 写入 Channel 等待 FlagChecker 处理，而是直接调用 `GameInstanceRepository.VerifyAnswer` 同步返回结果：

```csharp
[HttpPost("{id:int}/Challenges/{challengeId:int}")]
[ServiceFilter(typeof(GameLifetimeCheckFilter))]
[EnableRateLimiting(RateLimitPolicy.Submit)]
public async Task<IActionResult> Submit(int id, int challengeId, [FromBody] FlagSubmitModel model)
{
    var context = await GetContextInfo(id, HttpContext.RequestAborted);
    // ... 现有验证逻辑 ...
    
    var submission = new Submission
    {
        Answer = configService.DecryptApiData(model.Flag),
        ChallengeId = challengeId,
        ParticipationId = context.Participation.Id,
        TeamId = context.Participation.TeamId,
        UserId = context.User.Id,
        GameId = id,
        FlagId = model.FlagId,  // 新增
        Status = AnswerResult.FlagSubmitted,
        SubmitTimeUtc = DateTimeOffset.UtcNow,
    };
    
    await submissionRepository.AddSubmission(submission, HttpContext.RequestAborted);
    
    // 同步验证（不再使用 Channel + FlagChecker）
    var result = await instanceRepository.VerifyAnswer(submission, model.FlagId, HttpContext.RequestAborted);
    
    return Ok(new { submission.Id, result.Status, result.Score });
}
```

**Step 2: 删除 Channel 写入逻辑**

移除 `Channel<Submission>` 的注入和写入代码。

**Step 3: 更新 SignalR 通知**

验证完成后直接通过 SignalR 广播结果，不再依赖 FlagChecker 的异步回调。

**Step 4: Commit**

```bash
git add src/GZCTF/Controllers/GameController.cs
git commit -m "feat(api): convert flag submit to synchronous processing"
```

---

### Task C4: 扩展 ImageTemplateController

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\ImageTemplateController.cs`

**Step 1: 新增 zip 上传端点**

```csharp
[HttpPost("upload")]
[RequestSizeLimit(60L * 1024 * 1024 * 1024)] // 60GB
[Authorize(Roles = "Admin")]
public async Task<IActionResult> UploadArchive(IFormFile file, CancellationToken token)
{
    if (file is null || file.Length == 0)
        return BadRequest(new { message = "No file provided" });
    
    var allowedExtensions = new[] { ".zip", ".tar.gz", ".tgz", ".tar.xz", ".txz" };
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    // 检查 .tar.gz 复合后缀
    if (file.FileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        ext = ".tar.gz";
    else if (file.FileName.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase))
        ext = ".tar.xz";
    
    if (!allowedExtensions.Contains(ext))
        return BadRequest(new { message = $"Unsupported archive format. Allowed: {string.Join(", ", allowedExtensions)}" });
    
    var result = await _archiveExtractor.ExtractAndRegisterAsync(file, ext, token);
    if (!result.Success)
        return BadRequest(new { message = result.Error });
    
    return Ok(result.Template);
}
```

**Step 2: 新增 register-docker 端点**

```csharp
[HttpPost("register-docker")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> RegisterDocker([FromBody] DockerRegisterModel model, CancellationToken token)
{
    var template = new ImageTemplate
    {
        Name = model.Name,
        OSType = model.OSType,
        ImageType = ImageType.Docker,
        RegistryUrl = model.RegistryUrl,
        Status = ImageStatus.Ready,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    
    _context.ImageTemplates.Add(template);
    await _context.SaveChangesAsync(token);
    
    return Ok(template);
}

public class DockerRegisterModel
{
    [Required, MaxLength(256)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(512)] public string RegistryUrl { get; set; } = string.Empty;
    public OSType OSType { get; set; } = OSType.Linux;
}
```

**Step 3: 注入新依赖**

在 `ImageTemplateController` 构造函数中注入 `IArchiveExtractor`。

**Step 4: Commit**

```bash
git add src/GZCTF/Controllers/ImageTemplateController.cs
git commit -m "feat(api): add zip upload and docker register to image templates"
```

---

## Phase D: 评分与排行榜重写

### Task D1: 重写 GameInstanceRepository.VerifyAnswer

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Repositories\GameInstanceRepository.cs`

**Step 1: 扩展 VerifyAnswer 签名**

```csharp
public async Task<VerifyResult> VerifyAnswer(Submission submission, int? flagId, CancellationToken token)
```

**Step 2: 按 FlagId 查找 FlagContext**

```csharp
var flag = flagId.HasValue
    ? await Context.FlagContexts.FindAsync(new object[] { flagId.Value }, token)
    : null;

// 如果没指定 FlagId 且是旧数据兼容模式（单Flag），取第一个 Flag
if (flag is null && !flagId.HasValue)
{
    flag = await Context.FlagContexts
        .Where(f => f.ChallengeId == submission.ChallengeId)
        .OrderBy(f => f.OrderIndex)
        .FirstOrDefaultAsync(token);
}

if (flag is null)
    return VerifyResult.NotFound;
```

**Step 3: 根据 AnswerType 路由验证**

```csharp
bool isCorrect;
switch (flag.AnswerType)
{
    case AnswerType.Flag:
        isCorrect = string.Equals(submission.Answer, flag.Flag, StringComparison.Ordinal);
        break;
    case AnswerType.File:
        var hash = submission.Answer.ToSHA256String();
        isCorrect = string.Equals(hash, flag.AttachmentHash, StringComparison.OrdinalIgnoreCase);
        break;
    case AnswerType.Custom:
        isCorrect = string.Equals(submission.Answer, flag.Flag, StringComparison.Ordinal);
        break;
    default:
        isCorrect = false;
        break;
}
```

**Step 4: 使用新 FirstSolve PK 检查重复**

```csharp
var existing = await Context.FirstSolves
    .FirstOrDefaultAsync(f =>
        f.ParticipationId == submission.ParticipationId &&
        f.ChallengeId == submission.ChallengeId &&
        f.FlagId == flag.Id, token);
if (existing is not null)
    return VerifyResult.AlreadySolved;
```

**Step 5: 计算 Flag 得分**

```csharp
int score;
if (flag.ScoreMode == FlagScoreMode.FixedScore)
{
    score = flag.FixedScore;
}
else
{
    var acceptedCount = await CountAcceptedForFlag(submission.ChallengeId, flag.Id, token);
    score = GameChallenge.CalculateChallengeScore(
        flag.FixedScore > 0 ? flag.FixedScore : challenge.OriginalScore,
        challenge.MinScoreRate, challenge.Difficulty, acceptedCount);
}
```

**Step 6: 计算血牌 + 写入 FirstSolve + 刷新缓存**

保持现有血牌逻辑（CountBloodEligibleSolves），改为按 FlagId 粒度。

**Step 7: Commit**

```bash
git add src/GZCTF/Repositories/GameInstanceRepository.cs
git commit -m "feat(scoring): rewrite VerifyAnswer for multi-flag support"
```

---

### Task D2: 重写 GenScoreboard — Flag 粒度

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Repositories\GameRepository.cs`

**Step 1: 调整 SolveSnapshot 结构体**

```csharp
private record SolveSnapshot(int ChallengeId, int FlagId, int ParticipantId, DateTimeOffset SubmitTimeUtc, string UserName);
```

**Step 2: 更新查询以包含 FlagId**

```csharp
solveSnapshots = await Context.FirstSolves
    .Join(Context.Participations, fs => fs.ParticipationId, p => p.Id, (fs, p) => new { fs, p })
    .Where(x => x.p.GameId == game.Id)
    .Join(Context.Submissions, x => x.fs.SubmissionId, s => s.Id, (x, s) => new SolveSnapshot(
        x.fs.ChallengeId, x.fs.FlagId, x.fs.ParticipationId, s.SubmitTimeUtc, s.UserName))
    .ToListAsync(token);
```

**Step 3: 按 Flag 粒度迭代**

```csharp
// 加载所有 Flag 的配分置
var allFlags = await Context.FlagContexts
    .Where(f => challenges.Keys.Contains(f.ChallengeId))
    .ToListAsync(token);

// 按 (ChallengeId, FlagId) 分组
var flagGrouped = solveSnapshots
    .GroupBy(s => (s.ChallengeId, s.FlagId))
    .ToList();

foreach (var flagGroup in flagGrouped)
{
    var flag = allFlags.FirstOrDefault(f => f.Id == flagGroup.Key.FlagId);
    if (flag is null) continue;
    
    var flagScore = flag.ScoreMode == FlagScoreMode.FixedScore
        ? flag.FixedScore
        : GameChallenge.CalculateChallengeScore(/* ... */);
    
    foreach (var solve in flagGroup.OrderBy(s => s.SubmitTimeUtc))
    {
        // 分配血牌（该 Flag 的 1st/2nd/3rd）
        // 累加到团队总分
    }
}

// teamScore[participantId] = sum of all flag scores
```

**Step 4: 更新 ScoreboardItem.ChallengeItem**

```csharp
// 原: item.Score = challengeScore
// 新: item.Score = teamFlagScores[participantId][challengeId]
//     item.SolvedFlags = teamSolvedFlags[participantId][challengeId]
//     item.TotalFlags = challengeFlagCounts[challengeId]
```

**Step 5: Commit**

```bash
git add src/GZCTF/Repositories/GameRepository.cs
git commit -m "feat(scoring): rewrite GenScoreboard for per-flag granularity"
```

---

## Phase E: VM 镜像上传与转换

### Task E1: 新增 ArchiveExtractor 服务

**Files:**
- Create: `D:\newGZ\YINYU CTF平台\src\GZCTF\Services\Vm\ArchiveExtractor.cs`

**Step 1: 创建 IArchiveExtractor 接口**

```csharp
// Services/Vm/IArchiveExtractor.cs
public interface IArchiveExtractor
{
    Task<ArchiveExtractResult> ExtractAndRegisterAsync(IFormFile file, string extension, CancellationToken token);
}

public class ArchiveExtractResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ImageTemplate? Template { get; set; }
}
```

**Step 2: 实现解压逻辑**

```csharp
public class ArchiveExtractor : IArchiveExtractor
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArchiveExtractor> _logger;
    
    public async Task<ArchiveExtractResult> ExtractAndRegisterAsync(IFormFile file, string ext, CancellationToken token)
    {
        // 1. 生成临时 GUID
        var guid = Guid.NewGuid().ToString("N");
        var incomingDir = Path.Combine(_storagePath, "incoming", guid);
        Directory.CreateDirectory(incomingDir);
        
        // 2. 暂存压缩包
        var archivePath = Path.Combine(incomingDir, $"archive{ext}");
        await using (var stream = file.OpenReadStream())
        await using (var fs = File.Create(archivePath))
            await stream.CopyToAsync(fs, token);
        
        // 3. 解压（根据格式选择工具）
        var extractDir = Path.Combine(incomingDir, "extracted");
        Directory.CreateDirectory(extractDir);
        
        var extractResult = ext switch
        {
            ".zip" => await ExtractZipAsync(archivePath, extractDir, token),
            ".tar.gz" or ".tgz" => await ExtractTarGzAsync(archivePath, extractDir, token),
            ".tar.xz" or ".txz" => await ExtractTarXzAsync(archivePath, extractDir, token),
            _ => (false, "Unknown format")
        };
        
        if (!extractResult.Success)
            return new ArchiveExtractResult { Success = false, Error = extractResult.Error };
        
        // 4. 扫描识别 VM 格式
        var vmFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
        var vmFormat = DetectVmFormat(vmFiles);
        
        // 5. 转换为 qcow2
        var templateDir = Path.Combine(_storagePath, "templates", guid);
        Directory.CreateDirectory(templateDir);
        var qcow2Path = Path.Combine(templateDir, "disk.qcow2");
        
        var convertResult = await ConvertToQcow2Async(vmFormat, vmFiles, qcow2Path, token);
        if (!convertResult.Success)
            return new ArchiveExtractResult { Success = false, Error = convertResult.Error };
        
        // 6. 检测 OS 类型
        var osType = DetectOSType(vmFiles, file.FileName);
        
        // 7. 计算 SHA256
        var hash = await ComputeSha256Async(qcow2Path, token);
        var fileSize = new FileInfo(qcow2Path).Length;
        
        // 8. 清理临时文件
        Directory.Delete(incomingDir, true);
        
        // 9. 创建 ImageTemplate 记录
        var template = new ImageTemplate
        {
            Name = Path.GetFileNameWithoutExtension(file.FileName),
            OSType = osType,
            ImageType = ImageType.Qcow2,
            LocalFilePath = qcow2Path,
            ImageHash = hash,
            OriginalArchiveName = file.FileName,
            FileSize = fileSize,
            Status = ImageStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        
        _context.ImageTemplates.Add(template);
        await _context.SaveChangesAsync(token);
        
        return new ArchiveExtractResult { Success = true, Template = template };
    }
    
    private VmFormat DetectVmFormat(string[] files) { /* 检测逻辑 */ }
    private async Task<(bool Success, string? Error)> ConvertToQcow2Async(VmFormat format, string[] files, string output, CancellationToken token) { /* qemu-img convert */ }
    private OSType DetectOSType(string[] files, string archiveName) { /* 启发式检测 */ }
    private async Task<string> ComputeSha256Async(string path, CancellationToken token) { /* SHA256 */ }
}
```

**Step 3: 实现 ExtractZipAsync/ExtractTarGzAsync/ExtractTarXzAsync**

```csharp
private async Task<(bool Success, string? Error)> ExtractZipAsync(string archivePath, string destDir, CancellationToken token)
{
    try
    {
        System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destDir);
        return (true, null);
    }
    catch (Exception ex)
    {
        return (false, $"ZIP extraction failed: {ex.Message}");
    }
}

private async Task<(bool Success, string? Error)> ExtractTarGzAsync(string archivePath, string destDir, CancellationToken token)
{
    return await RunExtractCommandAsync("tar", $"-xzf \"{archivePath}\" -C \"{destDir}\"", token);
}

private async Task<(bool Success, string? Error)> ExtractTarXzAsync(string archivePath, string destDir, CancellationToken token)
{
    return await RunExtractCommandAsync("tar", $"-xJf \"{archivePath}\" -C \"{destDir}\"", token);
}

private async Task<(bool Success, string? Error)> RunExtractCommandAsync(string cmd, string args, CancellationToken token)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }
    };
    
    process.Start();
    await process.WaitForExitAsync(token);
    
    return process.ExitCode == 0
        ? (true, null)
        : (false, await process.StandardError.ReadToEndAsync(token));
}
```

**Step 4: 注册 DI**

在 `ServicesExtension.cs` 中：
```csharp
services.AddScoped<IArchiveExtractor, ArchiveExtractor>();
```

**Step 5: Commit**

```bash
git add src/GZCTF/Services/Vm/
git commit -m "feat(vm): add ArchiveExtractor for zip upload and VM format conversion"
```

---

## Phase F: 容器/VM 环境路由

### Task F1: GameController 容器创建路由到 VM

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\Controllers\GameController.cs`

**Step 1: 扩展 CreateContainer 方法**

在 `CreateContainer`（`POST {id}/Container/{challengeId}`）中，检测 `challenge.Environment`：

```csharp
if (challenge.Environment == EnvironmentType.WindowsVM)
{
    // 委托给 FleetManager → KvmProvider
    var target = await _fleetManager.TryScheduleVmAsync(challenge, participation, token);
    if (target is null)
        return StatusCode(503, new { message = "No available VM node" });
    
    // 创建 VmInstance 追踪记录
    var vmInstance = new VmInstance
    {
        ChallengeId = challengeId,
        UserId = context.User.Id,
        VmName = $"vm_c{challengeId}_u{context.User.Id}",
        ProviderName = "KVM",
        OSType = OSType.Windows,
        Status = VmInstanceStatus.Creating,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    _context.VmInstances.Add(vmInstance);
    await _context.SaveChangesAsync(token);
    
    // 返回 RDP 连接信息
    return Ok(new { 
        status = "Creating", 
        instanceEntry = $"Creating VM... (check back in 30s)",
        vmInstanceId = vmInstance.Id,
    });
}
else
{
    // 现有 Docker 容器逻辑
}
```

**Step 2: Commit**

```bash
git add src/GZCTF/Controllers/GameController.cs
git commit -m "feat(api): route container creation to KVM for WindowsVM challenges"
```

---

## Phase G: 数据迁移

### Task G1: 创建 EF Core 迁移

**Files:**
- Create: EF Core 自动生成迁移文件

**Step 1: 生成迁移**

```bash
cd src/GZCTF
dotnet ef migrations add UnifiedChallengeRefactor --context AppDbContext
```

**Step 2: 手动补充迁移中的数据处理**

在生成的迁移 `Up` 方法中，添加数据转换逻辑：

```csharp
// 1. DockerImage → ImageTemplate
SQL(@"INSERT INTO ""ImageTemplates"" (""Name"", ""OSType"", ""ImageType"", ""RegistryUrl"", ""Status"", ""CreatedAt"", ""FileSize"")
     SELECT ""Name"", ""OSType"", 0, ""ImageTag"", ""Status"", ""CreatedAt"", ""FileSize""
     FROM ""DockerImages""");

// 2. Scenario Stage → FlagContext
SQL(@"INSERT INTO ""FlagContexts"" (""ChallengeId"", ""Flag"", ""OrderIndex"", ""Description"", ""ScoreMode"", ""FixedScore"")
     SELECT ""ScenarioId"", """", ""OrderIndex"", ""Title"", 0, 0
     FROM ""Stages""");

// 3. IRChallenge Checkpoint → FlagContext  
SQL(@"INSERT INTO ""FlagContexts"" (""ChallengeId"", ""Flag"", ""OrderIndex"", ""Description"", ""ScoreMode"", ""FixedScore"")
     SELECT ""ChallengeId"", """", ""OrderIndex"", ""Description"", 1, ""Score""
     FROM ""IRCheckpoints""");

// 4. GameChallenge Type=Scenario → Type=StaticAttachment, Environment=Docker
SQL(@"UPDATE ""Challenges"" SET ""Type"" = 0 WHERE ""Type"" = 4");
SQL(@"UPDATE ""Challenges"" SET ""Type"" = 0 WHERE ""Type"" = 8");

// 5. FirstSolve — 为旧数据设置默认 FlagId（取该 Challenge 的第一个 FlagId）
SQL(@"UPDATE ""FirstSolves"" SET ""FlagId"" = (
     SELECT ""Id"" FROM ""FlagContexts"" 
     WHERE ""FlagContexts"".""ChallengeId"" = ""FirstSolves"".""ChallengeId"" 
     ORDER BY ""Id"" LIMIT 1) 
     WHERE ""FlagId"" IS NULL");
```

**Step 3: 删除旧表**

```csharp
migrationBuilder.DropTable("Stages");
migrationBuilder.DropTable("ScenarioInstances");
migrationBuilder.DropTable("ScenarioTimelineEntries");
migrationBuilder.DropTable("IRCheckpoints");
migrationBuilder.DropTable("IRInstances");
migrationBuilder.DropTable("ScoringRules");
migrationBuilder.DropTable("DockerImages");
migrationBuilder.DropTable("DeploymentQueues");
```

**Step 4: Commit**

```bash
git add src/GZCTF/Migrations/
git commit -m "feat(db): add migration for unified challenge refactor"
```

---

## Phase H: 前端变更

### Task H1: 管理导航精简

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\ClientApp\src\components\admin\WithAdminTab.tsx`

**Step 1: 更新 pages 数组**

```typescript
const pages = [
  { icon: mdiMonitorDashboard, title: '仪表盘', path: '' },
  { icon: mdiFlagOutline, title: t('admin.tab.games.index'), path: 'games' },
  // { icon: mdiTarget, title: '场景管理', path: 'scenarios' },        ← 删除
  // { icon: mdiShieldHalfFull, title: 'IR 题目', path: 'ir-challenges' }, ← 删除
  { icon: mdiAccountGroupOutline, title: t('admin.tab.teams'), path: 'teams' },
  { icon: mdiAccountCogOutline, title: t('admin.tab.users'), path: 'users' },
  // { icon: mdiPackageVariantClosed, title: '实例管理', path: 'instances' }, ← 删除
  { icon: mdiServerNetwork, title: '节点管理', path: 'nodes' },
  // { icon: mdiDocker, title: 'Docker 镜像', path: 'dockerimages' },  ← 删除
  { icon: mdiImageOutline, title: '环境模板', path: 'images' },          // 重命名
  { icon: mdiClipboardListOutline, title: '部署队列', path: 'queue' },
  { icon: mdiClipboardCheckOutline, title: '提交评审', path: 'submissionreview' },
  { icon: mdiFileDocumentOutline, title: t('admin.tab.logs'), path: 'logs' },
  { icon: mdiSitemapOutline, title: t('admin.tab.settings'), path: 'settings' },
]
```

**Step 2: Commit**

```bash
git add src/GZCTF/ClientApp/src/components/admin/WithAdminTab.tsx
git commit -m "feat(ui): simplify admin navigation - remove Scenario/IR/Docker/Instances"
```

---

### Task H2: 删除前端页面目录

**Files:**
- Delete: `ClientApp/src/pages/admin/scenarios/`
- Delete: `ClientApp/src/pages/admin/ir-challenges/`
- Delete: `ClientApp/src/pages/admin/DockerImages/`
- Delete: `ClientApp/src/pages/admin/Instances.tsx`
- Delete: `ClientApp/src/pages/game/ScenarioPlayer.tsx`
- Delete: `ClientApp/src/pages/game/IRChallengePlayer.tsx`

```bash
git rm -r src/GZCTF/ClientApp/src/pages/admin/scenarios/
git rm -r src/GZCTF/ClientApp/src/pages/admin/ir-challenges/
git rm -r src/GZCTF/ClientApp/src/pages/admin/DockerImages/
git rm src/GZCTF/ClientApp/src/pages/admin/Instances.tsx
git rm src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx
git rm src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx
git commit -m "feat(ui): delete Scenario/IR/Docker/Instances admin pages"
```

---

### Task H3: 更新 ChallengeCreateModal

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\ClientApp\src\components\admin\ChallengeCreateModal.tsx`

**Step 1: 从类型下拉中删除 Scenario 和 IRChallenge**

```typescript
// ChallengeType options — 只保留 4 种:
const typeOptions = [
  { value: 'StaticAttachment', label: '静态附件' },
  { value: 'StaticContainer', label: '静态容器' },
  { value: 'DynamicAttachment', label: '动态附件' },
  { value: 'DynamicContainer', label: '动态容器' },
]
```

**Step 2: 从分类下拉中删除 Scenario 和 IR**

```typescript
// ChallengeCategory options — 删除 Scenario 和 IR
```

**Step 3: Commit**

```bash
git add src/GZCTF/ClientApp/src/components/admin/ChallengeCreateModal.tsx
git commit -m "feat(ui): remove Scenario/IRChallenge from challenge create modal"
```

---

### Task H4: 重构挑战编辑页面 — 基础信息 + 环境配置

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\ClientApp\src\pages\admin\games\[id]\challenges\[challengeId]\index.tsx`

**Step 1: 扩展 ChallengeEditData 接口**

```typescript
interface ChallengeEditData {
  // ... 现有字段 ...
  environment: EnvironmentType;  // 新增
  imageTemplateId: number | null; // 新增
}
```

**Step 2: 新增环境配置区**

在 Container Config 区域前，添加环境类型选择：

```tsx
{/* 环境配置 */}
<Card shadow="sm" padding="md" mt="md" withBorder>
  <Text fw={700} mb="sm">环境配置</Text>
  <Select
    label="环境类型"
    data={[
      { value: 'None', label: '无环境（附件题）' },
      { value: 'Docker', label: 'Linux Docker 容器' },
      { value: 'WindowsVM', label: 'Windows 虚拟机 (RDP)' },
    ]}
    value={editData.environment}
    onChange={(v) => setEditData({ ...editData, environment: v as EnvironmentType })}
  />
  
  {editData.environment !== 'None' && (
    <Select
      label="镜像模板"
      data={imageTemplateOptions}
      value={editData.imageTemplateId ? String(editData.imageTemplateId) : null}
      onChange={(v) => setEditData({ ...editData, imageTemplateId: v ? Number(v) : null })}
      mt="sm"
      searchable
    />
  )}
  
  {editData.environment === 'Docker' && (
    <>
      <TextInput label="容器镜像" value={editData.containerImage} ... mt="sm" />
      <NumberInput label="内存 (MB)" value={editData.memoryLimit} ... mt="sm" />
      <NumberInput label="CPU" value={editData.cpuCount} ... mt="sm" />
      <NumberInput label="存储 (MB)" value={editData.storageLimit} ... mt="sm" />
      <NumberInput label="端口" value={editData.exposePort} ... mt="sm" />
    </>
  )}
</Card>
```

**Step 3: Commit**

```bash
git add src/GZCTF/ClientApp/src/pages/admin/games/\[id\]/challenges/\[challengeId\]/index.tsx
git commit -m "feat(ui): add environment type and image template selector to challenge edit"
```

---

### Task H5: 重构挑战编辑页面 — Flag 阶段条

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\ClientApp\src\pages\admin\games\[id]\challenges\[challengeId]\index.tsx`

**Step 1: 替换简单 Flag 列表为阶段编辑器**

```tsx
{/* Flag 阶段配置 */}
<Card shadow="sm" padding="md" mt="md" withBorder>
  <Text fw={700} mb="sm">Flag 阶段配置</Text>
  
  {flags.map((f, i) => (
    <Card key={i} shadow="xs" padding="sm" mt="sm" withBorder>
      <Group justify="space-between" mb="xs">
        <Badge size="lg" variant="light">
          Step {i + 1} {i > 0 && '→'}
        </Badge>
        <Group gap="xs">
          {i > 0 && (
            <ActionIcon variant="subtle" onClick={() => moveFlag(i, i - 1)}>↑</ActionIcon>
          )}
          {i < flags.length - 1 && (
            <ActionIcon variant="subtle" onClick={() => moveFlag(i, i + 1)}>↓</ActionIcon>
          )}
          <ActionIcon color="red" onClick={() => removeFlag(i)}>×</ActionIcon>
        </Group>
      </Group>
      
      <TextInput
        label="阶段描述（玩家可见的引导文字）"
        value={f.description ?? ''}
        onChange={(e) => updateFlag(i, 'description', e.currentTarget.value)}
      />
      
      <Group mt="sm">
        <Select
          label="提交类型"
          data={[
            { value: 'Flag', label: 'Flag 提交' },
            { value: 'File', label: '文件提交 (哈希比对)' },
            { value: 'Custom', label: '自定义' },
          ]}
          value={f.answerType}
          onChange={(v) => updateFlag(i, 'answerType', v)}
        />
        {f.answerType === 'Custom' && (
          <TextInput
            label="自定义名称"
            value={f.customName ?? ''}
            onChange={(e) => updateFlag(i, 'customName', e.currentTarget.value)}
          />
        )}
      </Group>
      
      <TextInput
        label="Flag / 答案"
        data-testid={`flag-input-${i}`}
        value={f.flag}
        onChange={(e) => updateFlag(i, 'flag', e.currentTarget.value)}
        mt="sm"
      />
      
      <Group mt="sm">
        <Select
          label="计分模式"
          data={[
            { value: 'InheritDecay', label: '跟随衰减' },
            { value: 'FixedScore', label: '固定分值' },
          ]}
          value={f.scoreMode}
          onChange={(v) => updateFlag(i, 'scoreMode', v)}
        />
        {f.scoreMode === 'FixedScore' && (
          <NumberInput
            label="分值"
            value={f.fixedScore}
            min={0}
            onChange={(v) => updateFlag(i, 'fixedScore', Number(v) || 0)}
          />
        )}
        <NumberInput
          label="最大尝试次数 (0=无限)"
          value={f.maxAttempts}
          min={0}
          onChange={(v) => updateFlag(i, 'maxAttempts', Number(v) || 0)}
        />
      </Group>
    </Card>
  ))}
  
  <Button mt="md" variant="outline" onClick={addFlag}>+ 添加 Flag 阶段</Button>
</Card>
```

**Step 2: 实现 helper 函数**

```typescript
const addFlag = () => setFlags([...flags, {
  flag: '', description: '', orderIndex: flags.length,
  scoreMode: 'InheritDecay', fixedScore: 0, maxAttempts: 0,
  answerType: 'Flag', customName: null,
}])

const removeFlag = (i: number) => setFlags(flags.filter((_, idx) => idx !== i))

const moveFlag = (from: number, to: number) => {
  const u = [...flags];
  [u[from], u[to]] = [u[to], u[from]];
  setStages(u.map((s, i) => ({ ...s, orderIndex: i })))
}

const updateFlag = (i: number, key: string, value: any) => {
  const u = [...flags];
  (u[i] as any)[key] = value;
  setFlags(u);
}
```

**Step 3: Commit**

```bash
git add src/GZCTF/ClientApp/src/pages/admin/games/\[id\]/challenges/\[challengeId\]/index.tsx
git commit -m "feat(ui): add flag stage editor with arrow stepper to challenge edit"
```

---

### Task H6: 更新 ChallengeModal — 玩家端阶段条

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\ClientApp\src\components\ChallengeModal.tsx`

**Step 1: 新增 Flag 阶段步骤条组件**

```tsx
const FlagStepper: React.FC<{
  flags: ChallengeFlag[];
  solvedFlags: number[];
  currentFlagIndex: number;
  onFlagSelect: (index: number) => void;
}> = ({ flags, solvedFlags, currentFlagIndex, onFlagSelect }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
    {flags.map((f, i) => (
      <React.Fragment key={i}>
        <div
          onClick={() => onFlagSelect(i)}
          style={{
            display: 'flex', alignItems: 'center', gap: 4,
            padding: '6px 12px', borderRadius: 20,
            cursor: i <= Math.max(...solvedFlags, -1) + 1 ? 'pointer' : 'not-allowed',
            backgroundColor: solvedFlags.includes(i) ? '#d4edda' :
              i === currentFlagIndex ? '#cce5ff' : '#f5f5f5',
            border: `2px solid ${solvedFlags.includes(i) ? '#28a745' :
              i === currentFlagIndex ? '#007bff' : '#ddd'}`,
            opacity: i <= Math.max(...solvedFlags, -1) + 1 ? 1 : 0.5,
          }}
        >
          <span style={{ fontWeight: 'bold' }}>Step {i + 1}</span>
          {solvedFlags.includes(i) && <span>✅</span>}
        </div>
        {i < flags.length - 1 && <span>→</span>}
      </React.Fragment>
    ))}
  </div>
);
```

**Step 2: 替换原有单 Flag 输入区域**

```tsx
{/* 替代原 lines 250-293 的单 Flag 输入 */}
{challenge?.flags && challenge.flags.length > 1 && (
  <FlagStepper
    flags={challenge.flags}
    solvedFlags={solvedFlagIds}
    currentFlagIndex={activeFlagIndex}
    onFlagSelect={setActiveFlagIndex}
  />
)}

{activeFlag && (
  <div style={{ ... }}>
    <Text size="sm" c="dimmed" mb="xs">{activeFlag.description}</Text>
    <form onSubmit={onSubmitFlag}>
      <TextInput
        value={flag}
        onChange={(e) => setFlag(e.currentTarget.value)}
        placeholder={/* ... */}
      />
      <Button type="submit">提交</Button>
    </form>
    <Text size="xs" c="dimmed" mt={4}>
      {activeFlag.maxAttempts > 0 
        ? `剩余尝试: ${activeFlag.maxAttempts - attemptsForFlag[activeFlagIndex]}`
        : '无限尝试'}
    </Text>
  </div>
)}
```

**Step 3: Commit**

```bash
git add src/GZCTF/ClientApp/src/components/ChallengeModal.tsx
git commit -m "feat(ui): add flag stepper arrow UI to player challenge modal"
```

---

### Task H7: 更新 ChallengeModal props — 传递多 Flag 信息

**Files:**
- Modify: `D:\newGZ\YINYU CTF平台\src\GZCTF\ClientApp\src\components\GameChallengeModal.tsx`

**Step 1: 更新 flag 提交逻辑以包含 FlagId**

```typescript
// 提交时传递 FlagId
const onSubmitFlag = async () => {
  const encrypted = encryptApiData(t, flag, config.apiPublicKey);
  const res = await api.game.gameSubmit(gameId, challengeId, { 
    flag: encrypted, 
    flagId: activeFlag?.id   // 新增
  });
  // ... 现有轮询逻辑 ...
}
```

**Step 2: 解析挑战的 Flag 列表 + 已解 Flag 列表**

```typescript
// 从 challenge.flags 获取所有 Flag
// 从 API 返回的 challenge.solvedFlags 获取已解 Flag ID 列表
const flags = challenge?.flags ?? [];
const solvedFlagIds = challenge?.solvedFlags ?? [];
const nextUnsolvedIndex = flags.findIndex(f => !solvedFlagIds.includes(f.id));
```

**Step 3: Commit**

```bash
git add src/GZCTF/ClientApp/src/components/GameChallengeModal.tsx
git commit -m "feat(ui): update game challenge modal for multi-flag submission"
```

---

### Task H8: 删除旧路由文件 + 清理导入

**Files:**
- 查找并清理所有对已删除控制器的前端 API 调用

**Step 1: 搜索旧 API 引用**

```bash
grep -r "scenarios" src/GZCTF/ClientApp/src --include="*.ts" --include="*.tsx"
grep -r "ir-challenges" src/GZCTF/ClientApp/src --include="*.ts" --include="*.tsx"
grep -r "docker" src/GZCTF/ClientApp/src --include="*.ts" --include="*.tsx" | grep -v node_modules | grep -v image-template
```

**Step 2: 删除旧 API handler**

从 `@Api` 生成的客户端中删除对应的 handler（如果是手动维护的 API 文件）。如果使用自动生成，重新生成。

**Step 3: 清理 Shared.tsx**

从 `useChallengeTypeLabelMap` 和 `useChallengeCategoryLabelMap` 中删除 Scenario/IRChallenge 条目。

**Step 4: Commit**

```bash
git add src/GZCTF/ClientApp/src/
git commit -m "feat(ui): remove old API references and stale route files"
```

---

## Phase I: 最终集成与编译

### Task I1: 全项目编译

```bash
cd src/GZCTF
dotnet build src/GZCTF/GZCTF.csproj --no-restore
# 修复所有编译错误
```

### Task I2: 前端构建

```bash
cd src/GZCTF/ClientApp
pnpm build
# 修复所有 TypeScript 错误
```

### Task I3: 运行现有测试套件

```bash
dotnet test src/GZCTF.Tests/GZCTF.Tests.csproj
# Expected: 修复因模型变更导致的测试失败
```

### Task I4: 数据库迁移

```bash
# 在开发环境执行迁移
dotnet ef database update --context AppDbContext
```

---

## Execution Order

```
Phase A (模型) → Phase B (删除冗余) → Phase C (扩展控制器)
    → Phase D (评分) → Phase E (VM上传) → Phase F (环境路由)
    → Phase G (迁移) → Phase H (前端) → Phase I (集成)

A→B→C must be sequential (B depends on A, C depends on B).
D can overlap with E/F (different services).
H should start after C is done (API contracts stable).
```
