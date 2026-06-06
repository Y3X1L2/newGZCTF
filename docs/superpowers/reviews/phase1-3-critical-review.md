# Phase 1-3 关键审查报告

> 基于计划文档 (`2026-05-19-newGZCTF-refactor.md`) 与实际代码库的对比验证。
> 审查日期: 2026-05-19

---

## 目录

1. [Phase 1: 统一评分引擎](#phase-1-统一评分引擎)
2. [Phase 2: 安全加固](#phase-2-安全加固)
3. [Phase 3: VM Provider 抽象](#phase-3-vm-provider-抽象)
4. [交叉阶段问题](#交叉阶段问题)
5. [编译失败清单](#编译失败清单)
6. [循环依赖分析](#循环依赖分析)
7. [过度简化项](#过度简化项)
8. [会断裂的调用者](#会断裂的调用者)
9. [错误假设](#错误假设)

---

## Phase 1: 统一评分引擎

### 1.1 VerifyAutoExact 三层 fallback 策略模式覆盖不全

**位置**: `SubmissionController.cs:411-479`

当前 VerifyAutoExactAsync 有三条独立路径:

| 路径 | 行号 | 验证方式 | 数据来源 |
|------|------|----------|----------|
| Path 1 | 421-429 | SHA256 hash 比对 | `ScoringRule.ExpectedAnswerHash` |
| Path 2 | 431-442 | 明文比对 | `Stage.VerifyFlag()` (Scenario 专属) |
| Path 3 | 444-452 | 明文比对 | `FlagContexts.Flag` (传统 CTF 专属) |

计划的 `FlagHashVerification` 策略仅覆盖 **Path 1**（SHA256 哈希比对）。Path 2 和 Path 3 使用明文 Flag 比对（非哈希），且操作完全不同的数据模型（`Stage` 和 `FlagContext`），不是 `ScoringRule`。

**关键问题**: 如果 `UnifiedScoringEngine` 的 `ProcessSubmissionAsync` 通过 `IVerificationStrategy` 策略模式调度，而 `FlagHashVerification` 只实现了 SHA256 哈希校验，那么所有依赖 Path 2（Scenario 的 `Stage.VerifyFlag()`）和 Path 3（传统 CTF 的 `FlagContexts`）的现有挑战将在迁移后**全部验证失败返回 WrongAnswer**。

**ExpectedAnswerHash 为空的现有数据**:
- `ScoringRule.ExpectedAnswerHash` 是 `string?`（可为空），数据库中已存在大量记录此字段为 NULL
- 当前代码在 `ExpectedAnswerHash` 为空时优雅降级到 Path 2 或 Path 3
- 计划未提及如何迁移这些现有数据——需要做一次性的 `ExpectedAnswerHash` 回填迁移，或在策略模式中增加一个"兼容性策略"来处理空 hash 场景

### 1.2 SubmitCheckpoint 缺少 Submission 写入所需的 gameId/teamId/participationId

**位置**:
- `IRChallengeController.cs:524-596` — SubmitCheckpoint 方法
- `IREntities.cs:83-174` — IRInstance 模型

SubmitCheckpoint 的路由: `POST instances/{instanceId:guid}/checkpoints/{checkpointId:int}/submit`

提交方法当前可用的上下文:
```
instanceId (Guid)   → IRInstance
checkpointId (int)  → IRCheckpoint
model.Answer        → string
```

IRInstance 模型有:
```
ChallengeId (int)   → 可导航到 GameChallenge
UserId (Guid)       → 可导航到 UserInfo
TimeSlotId (int)
CreatedAt
EndedAt
```

IRInstance **缺失**的字段:
- `GameId` — 需要通过 `ChallengeId → GameChallenge.GameId` 查询
- `TeamId` — 需要通过 `UserId` 在 `Participations` 表中查找
- `ParticipationId` — 同上

计划说 `RecordIRCheckpointCompletionAsync` 需要 gameId、teamId、participationId 来写 Submission 记录。但要获取这些值，SubmitCheckpoint 需要额外的 DB 查询:

```
IRInstance.ChallengeId → GameChallenge → GameId
IRInstance.UserId → Participation (WHERE UserId == ? AND GameId == ?) → TeamId + ParticipationId
```

这是 **3 次额外的 DB 查询**（或 2 次 Include），计划未提及这些必要的数据获取步骤。

### 1.3 FlagChecker → ScoringEngine 委托存在架构不匹配

**位置**: `FlagChecker.cs:1-209`

FlagChecker 的当前架构:
```
Channel<Submission> (BackgroundService)
  → for each item in channelReader
    → create service scope
    → get repositories (IGameEventRepository, IGameInstanceRepository, etc.)
    → call instanceRepository.VerifyAnswer(item, token)
    → handle blood bonus / cheat detection / concurrency
    → write game events
    → flush scoreboard cache
    → handle DbUpdateConcurrencyException (re-queue)
```

FlagChecker 是一个**异步 Channel Worker 模式**，它:
1. 在 `HostedService.StartAsync` 中启动 1-4 个工作协程
2. 从 `Channel<Submission>` 中消费提交
3. 通过 `IServiceScopeFactory` 创建作用域（因为它是 Singleton，Repo 是 Scoped）
4. 处理复杂的传统 CTF 流程（blood bonus、作弊检测、并发重试）

计划说"委托 ScoringEngine（传统 CTF 路径）"。但 ScoringEngine（按计划描述）是一个同步验证管线，处理方式与 FlagChecker 完全不同。

**三个未解决的问题**:
1. FlagChecker 的 `Channel<Submission>` 模式需要保持——传统 CTF 的提交是先创建 Submission（status=unchecked），再异步验证。ScoringEngine 如果同步验证，则传统 CTF 路径的行为会改变（从异步变为同步）。
2. FlagChecker 负责的 blood bonus 逻辑（一血/二血/三血）、PostgreSQL advisory lock、cheat detection 不在 ScoringEngine 的范围内。这些逻辑移植到 ScoringEngine 会使其职责膨胀。
3. FlagChecker 的 DbUpdateConcurrencyException 处理（重新入队）是 Channel 模式特有的——同步模式无法直接重试。

如果要保留异步验证，ScoringEngine 要么：
- 也暴露异步 API（但 Channel/BackgroundService 的架构就是为此设计的）
- 或者 FlagChecker 内部使用 ScoringEngine 替代 VerifyAnswer（但 blood bonus 等逻辑仍需保留）

### 1.4 VerifyAnswer 不会被评分引擎替代，而是共存

**位置**: `GameInstanceRepository.cs:261-389`

GameInstanceRepository.VerifyAnswer 执行的逻辑（传统 CTF 用）:
1. 通过 ParticipationId + ChallengeId 查找 GameInstance
2. 获取 FlagContext.Flag 比对
3. PostgreSQL advisory lock (`pg_advisory_xact_lock`) 防竞争
4. 检查是否已解出（alreadySolved）
5. 检查比赛时间窗口和 deadline
6. 检查 Division 权限（blood bonus 权限）
7. 一血/二血/三血判定
8. 创建 FirstSolve 记录
9. 返回 SubmissionType（FirstBlood/SecondBlood/ThirdBlood/Normal/Unaccepted）

这些逻辑是**传统 CTF 独有**的——它不适用于 IR 或 Scenario。计划没有说明 VerifyAnswer 是否被替换。如果：
- **被替换**: blood bonus、advisory lock、time window 检查会丢失
- **共存**: SubmissionController 的验证路径和 FlagChecker 的验证路径变成两套独立系统，维护两倍的验证代码

目前代码库里实际上存在**两套并行的验证体系**:
- 传统 CTF 路径: `FlagChecker → VerifyAnswer` (成熟的)
- 新多类型路径: `SubmissionController.VerifySubmissionAsync → VerifyAutoExact/Regex` (新建的)

计划要在其上增加第三套 `UnifiedScoringEngine`，但没说怎么处理前两套的共存问题。

### 1.5 ScoreDecayCalculator 静态类无法消除 double-decay bug

**位置**:
- `SubmissionController.cs:524-533` — ApplyScoreDecay (第一次衰减)
- `ScoringService.cs:73-82` — ApplyScoreDecay (第二次衰减)
- `ScoringService.cs:50-51` — 在 CalculateTotalScoreAsync 中调用

double-decay 的原因是:
1. `SubmissionController.CreateSubmission` (line 113): 存入数据库时 `score = ApplyScoreDecay(raw, attemptIndex, rule)` — **这是第一次衰减**
2. `ScoringService.CalculateTotalScoreAsync` (line 51): 读取分数后 `ApplyScoreDecay(s.Score, attempt, rule)` — **这是第二次衰减**

如果第一次衰减后原始分 1000 变为 500（Half 策略），第二次衰减又把 500 当作基数再衰减为 250（仍然是 Half 策略）。

**计划创建的静态 `ScoreDecayCalculator.Apply` 无法解决这个问题**——问题不在衰减算法的实现，而在于**衰减在哪一层调用**。要么：
- 数据库存原始分（不衰减），ScoringService 负责衰减 → 修改 SubmissionController.CreateSubmission
- 数据库存衰减后分，ScoringService 不做衰减 → 修改 ScoringService.CalculateTotalScoreAsync

静态 utility class 只是代码复用，不是 bug fix。

---

## Phase 2: 安全加固

### 2.1 RateLimiter 已经存在但策略名不匹配

**位置**: `Services\RateLimiter.cs:1-141`

RateLimiter.cs 已经完整实现，在 `ServicesExtension.cs:114` 注册:
```csharp
builder.Services.AddRateLimiter(RateLimiter.ConfigureRateLimiter);
```

已定义的限流策略:
```
Global limiter:    150 req/min per user/IP
Concurrency:        1 concurrency
Register:          20 req / 150s sliding window
Query:             100 tokens / 10s replenish
Container:         120 tokens / 30 per 10s
Submit:            100 tokens / 50 per 5s
PowChallenge:       40 tokens / 5 per 30s
```

计划要求:
- 添加 `[EnableRateLimiting(Name = "FlagSubmission")]` 策略
- 速率: 10 次/分钟/用户

**问题**:
- 已有 `Submit` 策略（100 tokens, 50 per 5s），计划要求创建新的 `FlagSubmission` 策略（10/分钟）。两者功能重叠，为什么要新建？
- 如果新建 `FlagSubmission`，它必须在 `RateLimiter.cs` 中注册（但计划没说要改 RateLimiter.cs）
- 已有 `/api/v1/submissions` 端点未应用任何 `[EnableRateLimiting]` 属性——不是中间件缺失，而是属性缺失。计划只需要添加 `[EnableRateLimiting("Submit")]` 即可用现有策略。

计划错误地暗示不存在限流器，但实际已经存在。只需要为端点添加属性。

### 2.2 GuacamoleProxy 方法签名变更会破坏调用者

**位置**:
- `GuacamoleProxy.cs:59-61` — 当前签名: `CreateConnectionAsync(string vmName, string host, int port)`
- `GuacamoleProxy.cs:78` — 硬编码 `password = "password"`, `username = "player"`
- `IRChallengeController.cs:420` — 调用处

计划将方法签名改为:
```csharp
CreateConnectionAsync(string vmName, string host, int port, 
    string sessionUsername, string sessionPassword)
```

这会在 IRChallengeController.cs:420 产生**编译错误**，因为现有调用传了 3 个参数，新签名需要 5 个。

同时请注意: `GuacamoleProxy` 在 `ServicesExtension.cs:99` 注册为 `AddScoped`，而它内部使用 `IHttpClientFactory` + `IOptions<GuacamoleSettings>`。这个方法签名变更本身没问题，但调用处必须同步更新。

### 2.3 VmManager 参数注入防御 — 方法命名偏差

**位置**: `VmManager.cs:235-282`

计划说"VmManager.RunCommandAsync 中的文件名参数做白名单校验"并创建 `SanitizeVmName` 方法。

但 `RunCommandAsync` 是 **private** 方法（line 235），它的 `fileName` 参数是 `"virsh"` 和 `"qemu-img"` 这种硬编码值，不是用户输入。真正的用户输入是 `newVmName`（传给 CreateFromTemplate）和 `vmName`（传给 Start/Destroy/etc.）。

实际的命令注入风险在 `arguments` 参数拼接受 `vmName` 影响的地方，例如:
```csharp
// VmManager.cs:97
$"-c {_libvirtUri} start \"{vmName}\""
// VmManager.cs:116
$"-c {_libvirtUri} shutdown \"{vmName}\""
// VmManager.cs:135
$"-c {_libvirtUri} destroy \"{vmName}\""
```

vmName 通过 `"` 包围，但如果有换行符或 `&&` 仍可能绕过。`SanitizeVmName` 的逻辑应该放在 `CreateFromTemplate` / `Start` / `Destroy` 等 public 方法的入口处，而不是在 private 的 `RunCommandAsync` 里。

---

## Phase 3: VM Provider 抽象

### 3.1 VmManager 调用者分析

**搜索结果**: `Grep "VmManager" src/`

| 文件 | 行号 | 使用方式 | 计划是否列出修改 |
|------|------|----------|-----------------|
| `IRChallengeController.cs` | 31, 43 | field + ctor param | **未列出** |
| `ServicesExtension.cs` | 96 | DI 注册 | 未列出 |
| `EnvironmentService.cs` | 13, 20 | field + ctor param | 已列出 |
| `VmManager.cs` | 自身 | 实现 | 已列出 |

**共 4 个文件引用 VmManager，其中 IRChallengeController 未被计划列为 Phase 3 修改目标。**

IRChallengeController 对 VmManager 的使用（都在 `CreateEnvironmentAsync` 方法，`IRChallengeController.cs:391-460`）:
```
Line 411: _vmManager.CreateFromTemplate(templatePath, vmName)
Line 412: _vmManager.Start(vmName)  
Line 415: Task.Delay(30s) — 硬编码等待 ← 计划说要改为 IP 轮询
Line 418: _vmManager.GetIpAddress(vmName)
Line 419: _vmManager.GetVncPort(vmName)
```

如果 VmManager 被重构为 KvmProvider 委托（保持方法签名不变），则 IRChallengeController 可以继续工作。但如果 VmManager 的任何方法签名改变（例如改为 async 带 CancellationToken），IRChallengeController 就会编译失败。

### 3.2 EnvironmentService 重构面

**位置**: `EnvironmentService.cs:1-274`

EnvironmentService 对 VmManager 的调用:

| 方法 | 行号 | VmManager 调用 | 计划处理 |
|------|------|----------------|----------|
| `CreateStageEnvironmentAsync` | 89 | `_vmManager.CreateFromTemplate` | 换为 `vmProvider` |
| `CreateStageEnvironmentAsync` | 90 | `_vmManager.Start` | 换为 `vmProvider` |
| `CreateStageEnvironmentAsync` | 92 | `_vmManager.GetVncPort` | 换为 `vmProvider.GetConnectionInfoAsync` |
| `CreateStageEnvironmentAsync` | 93 | `_vmManager.GetIpAddress` | 换为 `vmProvider` |
| `DestroyStageEnvironmentAsync` | 191 | `_vmManager.Destroy` | 换为 `vmProvider` |
| `ResetEnvironmentAsync` | 240 | `_vmManager.SnapshotRevert` | 换为 `vmProvider` |
| `CreateStageEnvironmentAsync` | 99-103 | 返回 VNC 连接 (非 RDP) | 需改为 Guacamole RDP |

关键: `CreateStageEnvironmentAsync` 当前对 Windows VM 的返回是:
```csharp
connectionDetails.Add(new EnvironmentConnection {
    Type = "Windows",
    Host = ipAddress,
    Port = vncPort,         // VNC port, not RDP
    Protocol = "vnc"        // VNC, not RDP
});
```

计划说要改为使用 Guacamole RDP。这意味着返回的连接信息应该包含 `GuacamoleConnectionId` 和 `AccessUrl`，而不是原始的 VNC IP:Port。这是 `EnvironmentConnection` 模型的结构变化——该模型 (line 291-300) 没有 Guacamole 相关字段。

### 3.3 IRChallengeController 创建环境路径将变为两套不同的实现

当前:
- IRChallengeController.CreateEnvironmentAsync → 直接调 VmManager (完整 Windows VM 生命周期)
- EnvironmentService.CreateStageEnvironmentAsync → 也调 VmManager

如果:
- IRChallengeController 继续用 VmManager (保持原样)
- EnvironmentService 改用 IVirtualMachineProvider

则平台上存在两套 Windows VM 管理入口，一套经过 Provider 抽象，一套不经过。

如果:
- 两者都改为使用 IVirtualMachineProvider

则 IRChallengeController 必须列为 Phase 3 修改目标（目前未列出）。

### 3.4 GuacamoleProxy IRChallengeController 已经使用，但 EnvironmentService 未使用

**IRChallengeController.cs:420** 已经在 CreateEnvironmentAsync 中调用 `_guacamoleProxy.CreateConnectionAsync` 并返回 Guacamole 连接 URL 作为 AccessDetails。

但 **EnvironmentService.cs** 创建 Windows VM 后返回的是 `{ Host, Port: vncPort, Protocol: "vnc" }`——完全没有使用 GuacamoleProxy。

这意味着:
- IR 挑战（通过 IRChallengeController 创建）→正确使用 Guacamole RDP
- Scenario 阶段（通过 EnvironmentService 创建）→暴露 VNC 直连

计划要求 EnvironmentService 也接入 Guacamole RDP，这是正确的。但 EnvironmentService 的 `CreateStageEnvironmentAsync` 方法签名返回 `StageEnvironmentResult`，其中 `Connections` 是 `List<EnvironmentConnection>`——这个模型没有 Guacamole 连接 ID 和 Token 字段，需要扩展。

### 3.5 VmManager 缺少快照创建步骤

**位置**: `VmManager.cs:89-107 (Start), 151-163 (SnapshotRevert)`

代码库分析报告已指出: SnapshotRevert (line 151) 假定快照已存在，但没有任何代码创建过快照。

计划说 KvmProvider.StartAndSnapshotAsync 会在启动后创建快照:
```
StartAsync → WaitForBootAsync (IP polling, up to 120s) → CreateSnapshotAsync("clean")
```

这是正确的修复。但注意 IRChallengeController.cs 和 EnvironmentService 中调用 VmManager 的地方**从未创建过快照**，所以当前的 SnapshotRevert 在任何路径上都是静默失败的。

### 3.6 IRChallengeController 返回 AccessDetails 含敏感信息

**位置**: `IRChallengeController.cs:424-432`

```csharp
var accessDetails = new Dictionary<string, object?>
{
    ["GuacamoleConnectionId"] = connectionId,  // OK
    ["GuacamoleToken"] = guacToken,            // 敏感! Token 泄露
    ["VmName"] = vmName,                       // OK
    ["VmIp"] = vmIp,                           // 可能敏感
    ["AccessUrl"] = _guacamoleProxy.GetConnectionUrl(connectionId, guacToken),  // 含 Token
    ["OsType"] = "Windows"
};
```

`GuacamoleToken` 和 `AccessUrl`（内含 Token）都被写入数据库的 `IRInstance.AccessDetails` JSON 字段，并在 `GetInstance` 端点返回给客户端。

计划 Phase 2 要求响应脱敏，但 IRChallengeController 的代码继续把 Token 保存到 AccessDetails JSON 中。脱敏需要在**输出时拦截**（查询时不返回敏感字段），或在**存储时加密**。计划只说"过滤 SshPasswordHash、GuacamoleToken"，但没说明是在序列化 AccessDetails 时过滤还是在 API 返回模型时过滤。

---

## 交叉阶段问题

### 4.1 ChallengeSubmissionType 与 ScoringRule 的关系未定义

计划创建 `ChallengeSubmissionType` 实体:
```csharp
public class ChallengeSubmissionType {
    int ChallengeId          → FK to Challenge
    ScoringSubmissionType SubmissionType  → 同 ScoringRule.SubmissionType
    int MaxAttempts          → ScoringRule 已有此字段
    ScoreDecay ScoreDecay    → ScoringRule 已有此字段
}
```

现有 `ScoringRule` 已经有:
- `SubmissionType` (ScoringSubmissionType) — 完全相同的枚举
- `MaxAttempts` — 相同字段
- `ScoreDecay` — 相同枚举

**问题**: `ChallengeSubmissionType` 和 `ScoringRule` 在 `SubmissionType` 上是 1:1 关系吗？如果是，为什么要把提交配置从 `ScoringRule` 中提出来创建一个重复的实体？

根据计划，`ChallengeSubmissionType` 控制"允许的提交类型"（白名单），而 `ScoringRule` 控制"如何验证"。但两者共享 `SubmissionType` 字段，且 `MaxAttempts` 和 `ScoreDecay` 在两个实体中重复。

这会导致数据同步问题：管理员需要在 `ChallengeSubmissionType` 和 `ScoringRule` 中分别配置 `MaxAttempts`。

### 4.2 IRChallengeController 硬编码 30 秒等待 vs KvmProvider 的 120 秒轮询

**位置**: `IRChallengeController.cs:415`

```csharp
// Wait for VM to be ready
await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
```

计划说 KvmProvider.WaitForBootAsync 使用 `domifaddr` 轮询 IP，最长 120 秒。但如果 IRChallengeController 在调用 `Start` 后仍然 `Task.Delay(30s)`，则产生双重等待——先硬等 30 秒，然后再（如果改用了 KvmProvider）轮询至多 120 秒。

如果 IRChallengeController 改用 KvmProvider 的统一方法（如 `StartAndSnapshotAsync`），则此 `Task.Delay(30s)` 必须删除。但 IRChallengeController 未被列为 Phase 3 修改目标。

### 4.3 VNC 暴露在 0.0.0.0 安全性问题

**位置**: `VmManager.cs:323-325` (GenerateDomainXml 中)

```xml
<graphics type='vnc' port='-1' autoport='yes' listen='0.0.0.0'>
  <listen type='address' address='0.0.0.0'/>
</graphics>
```

VNC 监听所有网络接口且无认证配置。这是严重安全问题（代码分析报告也指出）。

计划 Phase 3 的 `KvmProvider` 应修复为只监听 127.0.0.1（通过 Guacd 转发），但计划中完全没有提及此修复。安全验收清单中有"VNC 端口仅在 localhost 监听"，但实现部分没有对应内容。

### 4.4 计划修改了 VmManager 但未更新 DI 注册

**位置**: `ServicesExtension.cs:96`

```csharp
builder.Services.AddSingleton<VmManager>();
```

如果 VmManager 被重构为依赖 `IVirtualMachineProvider`，DI 注册需要改为:
```csharp
builder.Services.AddSingleton<IVirtualMachineProvider, KvmProvider>();
// VmManager 变为可选的 facade，或者删除
```

计划没说要更新 `ServicesExtension.cs`。

---

## 编译失败清单

| # | 文件 | 原因 | 严重程度 |
|---|------|------|----------|
| 1 | `IRChallengeController.cs:420` | `GuacamoleProxy.CreateConnectionAsync` 签名从 3 参数变为 5 参数 | **硬编译失败** |
| 2 | `IRChallengeController.cs` (多处) | `VmManager` 如果被替换为 `IVirtualMachineProvider` 但 IRChallengeController 未更新 DI | **硬编译失败** |
| 3 | `ServicesExtension.cs:96` | `VmManager` 如果改名或变为非 public 但注册仍指向原类 | 可能编译失败 |
| 4 | `SubmissionController.cs:524` | `ApplyScoreDecay` 如果被 `ScoreDecayCalculator.Apply` 替换且原方法未保留 | 可能编译失败 |
| 5 | `ScoringService.cs:73` | 同上 | 可能编译失败 |
| 6 | `IRChallengeController.cs` | 如果 injected `ScoringEngine` 但构造函数未被计划列出修改 | **硬编译失败** |

## 循环依赖分析

| # | 依赖链 | 风险 |
|---|--------|------|
| 1 | `SubmissionController → UnifiedScoringEngine → IVerificationStrategy → AppDbContext` | 无循环，但 `SubmissionController` 同时保留 `ScoringService` 会形成两条计分路径 |
| 2 | `FlagChecker (HostedService) → IServiceScopeFactory → UnifiedScoringEngine` | FlagChecker 是 HostedService（Singleton），如果 ScoringEngine 生命周期是 Scoped，需要在 FlagChecker 内创建作用域——这是可行的，但计划未提及 |
| 3 | `UnifiedScoringEngine.RecordIRCheckpointCompletionAsync → SignalR → IHubContext` | 潜在循环：ScoringEngine 写 Submission → SignalR 广播 → 前端 → 用户再提交 → 又进 ScoringEngine。这是正常流程，不算严格的循环依赖，但意味着 ScoringEngine 需要 IHubContext |
| 4 | **真正的循环可能**: `ScoringService → UnifiedScoringEngine → ... → ScoringService.CalculateTotalScoreAsync` | 如果 `UnifiedScoringEngine.ProcessSubmissionAsync` 在写 Submission 后调用 `CalculateTotalScoreAsync` 做广播，而 `ScoringService` 的 `CalculateTotalScoreAsync` 内部又需要校验逻辑……这取决于实现细节，但目前的设计中没有证据表明会形成循环 |

## 过度简化项

### 1. 策略模式的调度键选错了维度

计划使用 `ScoringSubmissionType`（Flag/Writeup/IP/Credential/Custom）作为 `IVerificationStrategy.HandledType` 的调度键。但现有的验证调度键是 `VerificationMode`（AutoExact/AutoRegex/AutoScript/ManualReview）。

**为什么这是问题**: 一个 `ScoringRule` 实例同时有 `SubmissionType` 和 `VerificationMode`。同一个 `SubmissionType`（如 Flag）可以用不同的 `VerificationMode` 验证（AutoExact、AutoRegex 或 ManualReview）。反之，同一个 `VerificationMode`（如 AutoExact）可以用于不同的 `SubmissionType`。

```
  submissionType: Flag        → verificationMode: AutoExact
  submissionType: Flag        → verificationMode: AutoRegex     (同一个类型, 不同验证方式)
  submissionType: IP          → verificationMode: AutoExact     (同上)
  submissionType: Writeup     → verificationMode: ManualReview
```

策略模式按 `SubmissionType` 调度意味着 `FlagHashVerification` 需要处理所有 `VerificationMode` 为 AutoExact 的提交，无论其 SubmissionType 是什么。但 `FlagHashVerification.HandledType` 返回 `ScoringSubmissionType.Flag`——这意味着 IP 类型的 AutoExact 提交不会被 `FlagHashVerification` 处理。

当前的代码在 `SubmissionController.cs:455-466` 中明确为 IP 类型做了独立的 AutoExact 处理。按 SubmissionType 调度会丢失这个区分。

**结论**: 策略模式应该按 `VerificationMode` 调度（与现有代码一致），而不是按 `SubmissionType`。或者策略接口需要同时感知 `SubmissionType` 和 `VerificationMode`。

### 2. static ScoreDecayCalculator 不能消除 double-decay

如前所述，double-decay 是架构问题（衰减发生在两个层），不是算法问题。`static class ScoreDecayCalculator` 只是一个工具方法提取，没有解决"衰减了两次"这个根本问题。

### 3. ChallengeSubmissionType 与 ScoringRule 的职责重叠

两个实体的字段对比:
```
ChallengeSubmissionType         ScoringRule
─────────────────────────       ────────────────────────
ChallengeId (FK)                ChallengeId (FK)
SubmissionType                  SubmissionType
                                Weight
                                VerificationMode
                                VerificationConfig
MaxAttempts                     MaxAttempts (DUPLICATE!)
ScoreDecay                      ScoreDecay (DUPLICATE!)
                                ExpectedAnswerHash (DUPLICATE)
OrderIndex
Label
RequireFile
AcceptedFileExtensions
MaxFileSize
IsActive
```

`MaxAttempts` 和 `ScoreDecay` 在两个实体中重复。如果"白名单"概念和"评分规则"概念需要分离，应该让 `ChallengeSubmissionType` 只包含白名单相关的字段（OrderIndex, Label, RequireFile, AcceptedFileExtensions, MaxFileSize, IsActive），并将限制和衰减委托给 `ScoringRule`。

### 4. 分布式锁的 `SemaphoreSlim` 粒度问题

计划 Phase 2 的 `ConcurrencyLockService` 使用单个 `SemaphoreSlim(1, 1)` 作为全局锁：
```csharp
private readonly SemaphoreSlim _localLock = new(1, 1);

public async Task<IDisposable> AcquireSubmissionLockAsync(int challengeId, Guid userId)
{
    await _localLock.WaitAsync();
    return new LockReleaser(_localLock, challengeId, userId);
}
```

这是一个**全局互斥锁**——意味着所有用户的提交都会被序列化，不管是不是同一个 challenge/user。如果 100 个用户同时提交，它们会一个一个地串行执行。

而现有代码（VerifyAnswer 中，line 316）使用的是 PostgreSQL **行级 advisory lock**（`pg_advisory_xact_lock(participationId, challengeId)`），粒度更细——只有对同一个 participation+challenge 的提交才会互斥。

计划的设计退了回去。正确的实现应该是 `ConcurrentDictionary<(int challengeId, Guid userId), SemaphoreSlim>` 来支持细粒度锁。

## 会断裂的调用者

### 直接编译失败（方法签名变更）

| 调用者 | 位置 | 调用 | 风险 |
|--------|------|------|------|
| `IRChallengeController.CreateEnvironmentAsync` | 420 | `_guacamoleProxy.CreateConnectionAsync(vmName, vmIp, vncPort)` | **调用 3 参数，签名变 5 参数** |
| `IRChallengeController.ctor` | 43 | `VmManager vmManager` | 如果 VmManager 从 DI 移除，ctor 参数解析失败 |

### 功能断裂（行为变更但不报错）

| 调用者 | 位置 | 风险 |
|--------|------|------|
| `SubmissionController.VerifyAutoExactAsync` | 411-479 | 3 层 fallback 如果被策略模式替换，未迁移的数据返回 WrongAnswer |
| `FlagChecker.Checker` | 70-208 | 委托 ScoringEngine 后 blood bonus / cheat detection / advisory lock 丢失 |
| `ScoringService.CalculateTotalScoreAsync` | 26-58 | 如果 ScoringService 委托 ScoringEngine 但 double-decay 未修复，分数翻倍错误 |
| `EnvironmentService.ResetEnvironmentAsync` | 222-249 | 调用 SnapshotRevert 但快照从未创建 |

### 前端断裂（计划列出但修改可能不完整）

| 调用者 | 位置 | 风险 |
|--------|------|------|
| `IRChallengePlayer.tsx` | 120 | "RDP 通过外部链接打开而非嵌入 GuacamoleDesktop"——前端结构变化 |
| `SubmissionReview.tsx` | 92 | `dangerouslySetInnerHTML` → MarkdownRenderer 的修改 |
| `scenarios/new.tsx` | silent catch | `.catch(() => {})` 吞错误修复 |

## 错误假设

### 假设 1: 策略模式可以基于 SubmissionType 调度

**错在哪里**: `IVerificationStrategy.HandledType` 返回 `ScoringSubmissionType`，但现有验证逻辑按 `VerificationMode` 调度。这是两个独立的维度。`SubmissionType=Flag` 可以搭配 `VerificationMode=AutoExact`、`AutoRegex` 或 `ManualReview`，策略模式按 SubmissionType 调度意味着需要为每种 (SubmissionType, VerificationMode) 组合创建一个策略实例。

### 假设 2: 现有数据中 ExpectedAnswerHash 都已设置

**错在哪里**: ExpectedAnswerHash 是 `string?`（可为空），实际数据库中存在大量记录此字段为 NULL。这些记录依赖 Path 2（Stage.VerifyFlag）或 Path 3（FlagContexts）进行验证，而不是 SHA256 hash 比对。计划没有迁移方案。

### 假设 3: IRInstance 有完整的 game/team/participation 上下文

**错在哪里**: IRInstance 只有 `ChallengeId` 和 `UserId`。没有 `GameId`、`TeamId`、`ParticipationId`。写 Submission 记录需要额外 2-3 次 DB 查询来获取这些值。

### 假设 4: static ScoreDecayCalculator 就能修复 double-decay

**错在哪里**: double-decay 的根源是 SubmissionController 和 ScoringService 都在 ApplyScoreDecay，不是衰减算法的问题。static utility class 只是代码复用，不解决架构问题。

### 假设 5: FlagChecker 可以"委托"给 ScoringEngine 而不改变架构

**错在哪里**: FlagChecker 是 Channel-based BackgroundService，用于异步验证传统 CTF 提交。ScoringEngine 如果设计为同步验证管线，两者的集成需要解决异步/同步适配问题。

### 假设 6: 速率限制不存在，需要全新创建

**错在哪里**: `RateLimiter.cs` 已完整实现，`LimitPolicy.Submit` 已定义。只需要在 Controller 端点添加 `[EnableRateLimiting("Submit")]` 属性，无需新建策略。

### 假设 7: VmManager 重构后 IRChallengeController 不需要修改

**错在哪里**: IRChallengeController 直接注入并使用 VmManager（`_vmManager.CreateFromTemplate`、`Start`、`GetIpAddress`、`GetVncPort`），还有 30 秒硬编码等待。如果只改 VmManager/EnvironmentService 不改 IRChallengeController，平台上有两套 VM 管理路径，且 IR 的 Windows VM 路径保持旧的 VNC + 硬编码等待方式。

### 假设 8: VmManager 有 RunCommandAsync 作为 public 方法

**错在哪里**: 计划 Phase 2.2 说"VmManager.RunCommandAsync 中的文件名参数做白名单校验"。但 `RunCommandAsync` 是 `private` 方法（`VmManager.cs:235`），它的 `fileName` 参数是 `"virsh"` / `"qemu-img"` 这种硬编码内部值。真正的注入点在 `arguments` 参数，由 `vmName` 用户输入拼接而成。

---

## 总结

### 最高优先级修复项（实施前必须先解决）

1. **策略模式的调度键选择** — 使用 `VerificationMode` 而不是 `ScoringSubmissionType`，或策略接口同时感知两个维度
2. **IRChallengeController 列为 Phase 3 修改目标** — 否则平台上有两套不一致的 VM 管理路径
3. **ExpectedAnswerHash 迁移方案** — 为所有现有 ScoringRule 计算并回填 hash，或提供兼容性策略处理空 hash
4. **GuacamoleProxy 签名变更的调用处同步更新** — `IRChallengeController.cs:420`
5. **double-decay 的架构决定** — 明确"数据库存原始分，ScoringService 衰减"还是"数据库存衰减后分，ScoringService 不衰减"

### 中优先级

6. `IRInstance` 模型增加 `GameId`/`TeamId`/`ParticipationId` 或提供查询辅助方法
7. ChallengeSubmissionType 消除与 ScoringRule 的 `MaxAttempts`/`ScoreDecay` 重复
8. 分布式锁从全局 `SemaphoreSlim(1,1)` 改为基于 `(challengeId, userId)` 的细粒度锁
9. RateLimiter 直接复用现有的 `Submit` 策略名，不创建新的 `FlagSubmission`
10. EnvironmentService 的 `EnvironmentConnection` 模型增加 Guacamole 连接字段

### 低优先级

11. VNC 0.0.0.0 改为 127.0.0.1（安全验收清单有但实现没有）
12. IRInstance AccessDetails 中 GuacamoleToken 的存储和返回脱敏
13. 迁移 `Stage.PrerequisiteStageIds` JSON 字段到 `StageDependency` 关联表（Phase 6）
14. 迁移 `Stage.EnvironmentImageIds` JSON 字段到 `StageImageTemplate` 关联表（Phase 6）
