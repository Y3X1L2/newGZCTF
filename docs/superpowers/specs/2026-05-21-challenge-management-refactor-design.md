# GZCTF 题目管理系统重构 — 设计文档

> **状态:** 设计完成，待用户审核
> **日期:** 2026-05-21
> **关联:** 提交系统 / 镜像管理 / 节点分发 同步重构

## 1. 目标

将当前分散的三套题目系统（普通CTF题目、场景Scenario、IR题目）合并为统一的题目管理框架，消除重复代码和数据模型；将两套镜像系统（ImageTemplate、DockerImage）合并为单一环境模板管理；增加多Flag支持和有序阶段引导；完善VM镜像上传→分发→部署的完整链路。

## 2. 当前状态

### 2.1 待删除的冗余系统

| 删除项 | 文件 | 原因 |
|--------|------|------|
| Scenario 题目类型 | `ChallengeType.Scenario` | 由 FlagContext.OrderIndex 替代阶段推进 |
| IRChallenge 题目类型 | `ChallengeType.IRChallenge` | 由 EnvironmentType + FlagContext 替代 |
| ScenarioController | `Controllers/ScenarioController.cs` | 合并到 EditController |
| IRChallengeController | `Controllers/IRChallengeController.cs` | 合并到 EditController |
| DockerController | `Controllers/DockerController.cs` | 合并到 ImageTemplateController |
| SubmissionController | `Controllers/SubmissionController.cs` | 提交统一走 GameController |
| DockerImage 模型/表 | `Models/Data/DockerImage.cs` | 合并到 ImageTemplate |
| Stage 模型/表 | `Models/Data/ScenarioEntities.cs` | 由 FlagContext 替代 |
| ScenarioInstance 模型/表 | 同上 | 由 GameInstance 替代 |
| IRCheckpoint 模型/表 | `Models/Data/IREntities.cs` | 由 FlagContext 替代 |
| IRInstance 模型/表 | 同上 | 由 GameInstance 替代 |
| ScoringRule 模型/表 | `Models/Data/ScoringRule.cs` | Flag 自带计分配置 |
| DeploymentQueue 模型/表 | 合并到 DeploymentTarget | 简化队列系统 |
| UnifiedScoringEngine | `Services/Scoring/UnifiedScoringEngine.cs` | 不再需要策略模式 |
| IVerificationStrategy 及实现 | `Services/Scoring/*Verification*.cs` | 统一字符串比对 |
| FlagChecker 后台服务 | `Services/FlagChecker.cs` | 改为同步处理 |
| LeaderboardService | `Services/LeaderboardService.cs` | 统一用 GenScoreboard |

### 2.2 保留并扩展的模型

| 模型 | 文件 | 变更 |
|------|------|------|
| Challenge (基类) | `Models/Data/Challenge.cs` | 新增 EnvironmentType, ImageTemplateId |
| GameChallenge | `Models/Data/GameChallenge.cs` | 新增 SubmissionTypes 导航属性 |
| FlagContext | `Models/Data/FlagContext.cs` | 新增 OrderIndex, Description, ScoreMode, FixedScore, MaxAttempts |
| FirstSolve | `Models/Data/FirstSolve.cs` | PK 改为 (ParticipationId, ChallengeId, FlagId) |
| Submission | `Models/Data/Submission.cs` | 新增 FlagId |
| ImageTemplate | `Models/Data/ImageTemplate.cs` | 新增 OriginalArchiveName |
| GameInstance | `Models/Data/GameInstance.cs` | 不变 |
| Container | `Models/Data/Container.cs` | 不变 |
| WorkerNode | `Models/Data/WorkerNode.cs` | 不变 |
| VmInstance | `Models/Data/VmInstance.cs` | 接入实际生命周期 |

## 3. 数据模型设计

### 3.1 ChallengeType 枚举精简

```csharp
// Enums.cs — 删除 Scenario=4, IRChallenge=8
[Flags]
public enum ChallengeType : byte
{
    StaticAttachment  = 0b00,  // 静态附件
    StaticContainer   = 0b01,  // 静态容器
    DynamicAttachment = 0b10,  // 动态附件
    DynamicContainer  = 0b11,  // 动态容器
}
```

### 3.2 新增：EnvironmentType

```csharp
public enum EnvironmentType : byte
{
    None      = 0,  // 无环境（附件题）
    Docker    = 1,  // Linux Docker 容器
    WindowsVM = 2,  // Windows KVM 虚拟机 (Guacamole RDP)
}
```

### 3.3 Challenge 基类新增字段

```csharp
// Challenge.cs
public EnvironmentType Environment { get; set; } = EnvironmentType.None;
public int? ImageTemplateId { get; set; }
public ImageTemplate? ImageTemplate { get; set; }
// 以下容器字段保留作为 Docker 运行时默认值:
public string? ContainerImage { get; set; }   // 从 ImageTemplate.RegistryUrl 自动填充
public int? MemoryLimit { get; set; }
public int? StorageLimit { get; set; }
public int? CPUCount { get; set; }
public int? ExposePort { get; set; }
```

### 3.4 FlagContext 扩展（核心变更）

```csharp
// FlagContext.cs — 保留所有现有字段，新增以下字段:
public int OrderIndex { get; set; }              // 阶段顺序，0-based
public string? Description { get; set; }         // 阶段引导描述（箭头步骤条文本）
public FlagScoreMode ScoreMode { get; set; }     // InheritDecay=0, FixedScore=1
public int FixedScore { get; set; }              // ScoreMode=FixedScore 时的分值
public int MaxAttempts { get; set; }             // 该 Flag 最大尝试次数，0=无限
public string? AttachmentHash { get; set; }      // File 类型提交：期望文件 SHA256
public SubmissionType AcceptedType { get; set; } // Flag=0, File=1, Custom=2
public string? CustomName { get; set; }          // AcceptedType=Custom 时的显示名称
```

### 3.5 提交类型简化

```csharp
// 替代原 ScoringSubmissionType 和 ChallengeSubmissionType 的复杂模型
public enum SubmissionType : byte
{
    Flag   = 0,  // 字符串比对
    File   = 1,  // SHA256 哈希比对
    Custom = 2,  // 自定义名称，本质也是字符串比对
}
```

`ChallengeSubmissionType` 模型保留，简化为：`SubmissionType`, `OrderIndex`, `Label`(Custom时自定义), `IsActive`。

### 3.6 FirstSolve PK 变更

```csharp
// FirstSolve.cs — 新复合主键
[PrimaryKey(nameof(ParticipationId), nameof(ChallengeId), nameof(FlagId))]
public class FirstSolve
{
    public int ParticipationId { get; set; }
    public int ChallengeId { get; set; }
    public int FlagId { get; set; }
    public int SubmissionId { get; set; }
    // ...
}
```

### 3.7 Submission 新增字段

```csharp
// Submission.cs
public int? FlagId { get; set; }        // 提交针对的 Flag，null=旧数据
public FlagContext? FlagContext { get; set; }
```

### 3.8 ImageTemplate 扩展

```csharp
// ImageTemplate.cs — 新增
public string? OriginalArchiveName { get; set; }  // 上传压缩包原始文件名
// 保留所有现有字段不变
```

### 3.9 删除的 ChallengeCategory

从 `ChallengeCategory` 枚举中删除 `Scenario` 和 `IR`，统一使用现有分类。

## 4. API 设计

### 4.1 题目管理（保持 api/Edit/ 约定）

```
GET    api/Edit/Games/{id}/Challenges                     # 列表（不变）
POST   api/Edit/Games/{id}/Challenges                     # 创建（不变）
GET    api/Edit/Games/{id}/Challenges/{cId}               # 详情（不变）
PUT    api/Edit/Games/{id}/Challenges/{cId}               # 更新（扩展字段）
DELETE api/Edit/Games/{id}/Challenges/{cId}               # 删除（不变）

# Flag CRUD（扩展现有端点）
GET    api/Edit/Games/{id}/Challenges/{cId}/Flags         # 列表（不变）
POST   api/Edit/Games/{id}/Challenges/{cId}/Flags         # 添加（扩展 FlagCreateModel）
PUT    api/Edit/Games/{id}/Challenges/{cId}/Flags/{fId}   # 更新（新增）
DELETE api/Edit/Games/{id}/Challenges/{cId}/Flags/{fId}   # 删除（不变）

# 容器测试（不变）
POST   api/Edit/Games/{id}/Challenges/{cId}/Container     # 管理员测试容器
DELETE api/Edit/Games/{id}/Challenges/{cId}/Container     # 销毁测试容器
```

### 4.2 玩家 Flag 提交（保持 api/Game/ 约定）

```
POST   api/Game/{id}/Challenges/{cId}                     # 提交 Flag（扩展 FlagId）
GET    api/Game/{id}/Challenges/{cId}/Status/{submitId}   # 查询结果（不变）
```

请求体扩展：`{ flag: "flag{...}", flagId: 1 }` — `flagId` 指定提交哪个 Flag。

### 4.3 容器/VM 生命周期（保持 api/Game/ 约定）

```
POST   api/Game/{id}/Container/{cId}                      # 创建环境
POST   api/Game/{id}/Container/{cId}/Extend               # 延长
DELETE api/Game/{id}/Container/{cId}                      # 销毁
```

后端根据 `challenge.Environment` 自动路由到 DockerManager 或 KvmProvider。

### 4.4 环境模板管理（api/v1/image-templates）

```
GET    api/v1/image-templates                             # 列表（支持 ?osType=&imageType=）
POST   api/v1/image-templates/upload                      # 上传 VM 压缩包
POST   api/v1/image-templates/register-docker             # 注册 Docker 镜像
POST   api/v1/image-templates/import-local                # 本地路径导入（不变）
DELETE api/v1/image-templates/{id}                        # 删除
```

### 4.5 删除的路由

```
✗ api/v1/scenarios/*
✗ api/v1/ir-challenges/*
✗ api/v1/docker/*
✗ api/v1/submissions/*       (提交统一到 api/Game)
✗ api/v1/phases/*            (如果 game phases 不再需要，待确认)
✗ api/v1/timeslots/*         (由题目级时间窗口替代)
```

## 5. 评分流程（合并后）

### 5.1 Flag 提交流程

```
POST api/Game/{id}/Challenges/{cId}
Body: { flag: "flag{...}", flagId: 1 }
       │
       ▼
  GameController.Submit()  [同步处理，不再使用 Channel+FlagChecker]
       │
       ▼
  GameInstanceRepository.VerifyAnswer(submission, flagId)
       │
       ├─ 查找 FlagContext by flagId
       ├─ SubmissionType 路由:
       │    Flag   → 字符串精确比对
       │    File   → SHA256 哈希比对
       │    Custom → 同 Flag，字符串比对
       ├─ pg_advisory_xact_lock(ParticipationId, ChallengeId, FlagId)
       ├─ 检查 FirstSolves 是否已存在
       ├─ 计算得分:
       │    FixedScore  → 直接使用 Flag.FixedScore
       │    InheritDecay → 查题目衰减曲线: OriginalScore × (minRate + (1-minRate) × exp((1-count)/difficulty))
       ├─ 计算血牌（该 Flag 的 1st/2nd/3rd 解）
       ├─ 写入 FirstSolve (ParticipationId, ChallengeId, FlagId, SubmissionId)
       ├─ 刷新排行榜缓存
       └─ 返回结果 (Accepted/WrongAnswer/CheatDetected)
```

### 5.2 排行榜变更

`GenScoreboard` 改为按 `Flag` 粒度迭代：

```csharp
// 原逻辑: FirstSolves 按 (ParticipationId, ChallengeId) 迭代
// 新逻辑: FirstSolves 按 (ParticipationId, ChallengeId, FlagId) 迭代

foreach (var solve in solves.GroupBy(s => s.FlagId))
{
    // 每个 Flag 独立计算动态衰减
    var flagScore = flag.ScoreMode == FlagScoreMode.FixedScore
        ? flag.FixedScore
        : GameChallenge.CalculateChallengeScore(
            flag.FixedScore > 0 ? flag.FixedScore : challenge.OriginalScore,
            challenge.MinScoreRate, challenge.Difficulty, acceptedCount);

    // 每个 Flag 独立计算血牌
    // 团队某题总分 = 该题所有 Flag 得分之和
}

// ScoreboardItem.ChallengeItem 改为:
//   SolvedFlags / TotalFlags
//   Score = 各 Flag 得分之和
```

## 6. VM 镜像生命周期

### 6.1 上传与转换流程

```
管理员上传 .zip / .tar.gz
       │
       ▼
  接收暂存到 uploads/ ← [RequestSizeLimit(60GB)]
       │
       ▼
  检测压缩格式（ZIP / tar.gz / tar.xz）
       │
       ▼
  解压到 images/incoming/{guid}/
       │
       ▼
  扫描识别 VM 格式:
    ├─ .vmx + .vmdk  → VMware  → qemu-img convert -f vmdk → .qcow2
    ├─ .vdi           → VirtualBox → qemu-img convert → .qcow2
    ├─ .vhdx          → Hyper-V → qemu-img convert → .qcow2
    ├─ .qcow2         → KVM → 直接使用
    └─ .ova           → tar 解包 → 检测内部格式 → 按上述规则处理
       │
       ▼
  移动 qcow2 到 images/{templateId}/disk.qcow2
       │
       ▼
  检测 OS 类型（默认 Windows，文件名包含 "linux"/"ubuntu"/"centos" → Linux）
       │
       ▼
  计算 SHA256 → 创建 ImageTemplate 记录:
    ImageTemplate {
        Name = 原始压缩包名(去后缀),
        OSType = 检测结果,
        ImageType = ImageType.Qcow2,
        LocalFilePath = "images/{id}/disk.qcow2",
        ImageHash = SHA256,
        OriginalArchiveName = "xxx.zip",
        FileSize = qcow2文件大小,
        Status = Ready,
    }
       │
       ▼
  触发节点分发 → ImageDistributionService.DistributeToCapableNodesAsync(template)
```

### 6.2 节点分发流程

```
ImageDistributionService
       │
       ▼
  查找有 KVM 能力的在线节点
       │
       ▼
  为每个节点创建 DeploymentTarget:
    { Type = Vm, Action = Create, Payload = { imageId, hash, localPath } }
       │
       ▼
  节点 Agent 轮询获取待处理 DeploymentTarget
       │
       ▼
  Agent 检测本地是否已有该 hash 的镜像:
    有 → 跳过
    无 → HTTP GET /api/v1/image-templates/{id}/download (从主节点拉取)
       │
       ▼
  Agent 执行: virsh pool-refresh → 注册到 libvirt
       │
       ▼
  Agent 上报完成 → DeploymentTarget.Status = Completed
```

### 6.3 VM 实例生命周期

```
创建实例:
  DeploymentTarget { Action=Create, Type=Vm, Payload={ templateId, vmName, memory, cpu } }
  → Agent: cp template → qemu-img create -b template → virsh define → virsh start
  → Agent: 获取 VNC 端口 + IP → 创建 Guacamole RDP 连接
  → 返回 RDP URL 给玩家
  → 创建 VmInstance 记录 (Status=Running)

销毁实例:
  DeploymentTarget { Action=Destroy, Type=Vm, Payload={ vmName } }
  → Agent: virsh destroy → virsh undefine → rm instance dir
  → VmInstance.Status = Destroyed

快照恢复:
  DeploymentTarget { Action=SnapshotRevert, Type=Vm, Payload={ vmName } }
  → Agent: virsh snapshot-revert --current
```

## 7. 前端变更

### 7.1 管理导航精简

```
重构前 (14项):                重构后 (10项):
├ 仪表盘                      ├ 仪表盘
├ 比赛管理                    ├ 比赛管理
├ 场景管理          ✗         ├ 团队管理
├ IR 题目           ✗         ├ 用户管理
├ 团队管理                    ├ 环境模板          (VM镜像 + Docker镜像 合并)
├ 用户管理                    ├ 节点管理
├ 实例管理          ✗         ├ 部署队列
├ 节点管理                    ├ 提交评审
├ Docker 镜像       ✗         ├ 日志
├ VM 镜像           → 合并     └ 系统设置
├ 部署队列
├ 提交评审
├ 日志
└ 系统设置
```

### 7.2 题目创建/编辑页面

单页面分区管理（替代原来的多页面/多步骤）：

- **基础信息区**：标题、分类、类型、描述（Markdown）、启用开关
- **环境配置区**（仅容器/VM 类型显示）：环境类型下拉 → 镜像模板下拉（Docker显示已注册镜像名，VM显示已上传模板名）→ CPU/内存/存储/端口
- **Flag 阶段区**：可视化的箭头步骤条，每步可编辑描述/Flag/计分模式/尝试次数，支持拖拽排序
- **提交类型区**：Flag / File / Custom 三选一或组合，File 类型支持设置接受后缀
- **评分配置区**：原始分值、最低分率、难度系数

### 7.3 玩家端 Flag 提交

箭头步骤条展示阶段进度，当前阶段显示 Flag 输入框，已完成的显示绿色勾，未解锁的显示锁定图标。

## 8. 迁移兼容

| 旧数据类型 | 迁移方式 |
|-----------|---------|
| Scenario(GameChallenge Type=Scenario) | 转为普通 GameChallenge，Stage → FlagContext(OrderIndex) |
| IRChallenge(GameChallenge Type=IRChallenge) | 转为 GameChallenge(Environment=Docker/WindowsVM)，IRCheckpoint → FlagContext(OrderIndex) |
| DockerImage 记录 | 迁移到 ImageTemplate(ImageType=Docker, RegistryUrl) |
| ScoringRule 记录 | 迁移到 FlagContext(ScoreMode/ScoreDecay) |
| FirstSolve 旧数据 | FlagId 设为 null，通过 Challenge 关联（单Flag场景兼容） |
| Submission 旧数据 | FlagId 设为 null |

## 9. 不做的事项（明确排除）

- 不保留自动脚本/命令验证（AutoScript/AutoCommand）
- 不保留阶段级多容器/多VM环境隔离
- 不保留 Writeup/IP/Credential 作为独立提交类型（统一为 Flag/File/Custom）
- 不保留 SignalR 实时推送场景进度（阶段推进改为前端本地状态管理）
- 不保留 TimeSlot 时间槽机制（由题目级时间窗口替代）
- 不实现 Guacamole 代理的多用户并发桌面（按需后续扩展）

## 10. 风险与依赖

| 风险 | 缓解 |
|------|------|
| DB 迁移复杂（多表删除+字段扩展） | 先在开发环境验证，生成幂等迁移脚本 |
| FirstSolve PK 变更影响 GenScoreboard | 逐段重写 GenScoreboard，保持缓存逻辑不变 |
| 前端大量页面删除需保证路由不残留 | 用 ~react-pages 文件系统路由，删除文件即删除路由 |
| VM 格式转换依赖 qemu-img | 部署文档注明需要 qemu-utils 包 |
| Agent 协议需要完整实现 | 作为独立子任务：Agent REST Pull + HMAC 签名 + 文件传输 |
