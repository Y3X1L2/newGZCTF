# newGZCTF 代码库全面分析报告（深度挖掘汇总）

> 生成日期: 2026-05-19
> 分析范围: 深层扫描 5 个板块（VM/容器基础设施、评分验证管线、API 表面、前端 UI、数据模型）
> 分析文件清单: `docs/analysis-*.md`（5 份独立深度报告）

---

## 核心发现（一句话版）

**七大验证体系并行互不协调导致分数不写入排行榜、VM 全程使用 VNC 但 RDP 代理代码已存在却未接入、数据层 10+ 字段以 JSON 字符串存储无法查询、前端新功能全部用裸 fetch() 且 4 个页面是死代码、分布式管理代码完全不存在但这是项目的核心目标。**

---

## 一、VM 与容器基础设施（agent-1 报告）

### 1.1 VM 管理框架性缺陷

| 缺陷 | 位置 | 严重程度 |
|---|---|---|
| 仅 KVM，无 Hyper-V 支持 — 硬编码 `<domain type='kvm'>` | `VmManager.cs:294` | P0 |
| 无 IVirtualMachineProvider 接口抽象 | 完全缺失 | P0 |
| VNC 协议而非 RDP — connection 返回 `Protocol = "vnc"` | `EnvironmentService.cs:102` | P0 |
| GuacamoleProxy 完全孤立 — 未被 EnvironmentService 调用 | `GuacamoleProxy.cs` 未引用 | P0 |
| VNC 暴露在 0.0.0.0 且无认证 | `VmManager.cs:323` | **Security** |
| Guacamole RDP 密码硬编码 `"password"` | `GuacamoleProxy.cs:78` | **Security** |
| destroy 后无 `virsh undefine` — 域定义残留、磁盘残留 | `VmManager.cs:132-143` | P1 |
| 无快照创建步骤 — SnapshotRevert 假定快照已存在 | `VmManager.cs:151` | P1 |
| 30s 硬编码等待 VM 启动，无 IP 轮询 | `IRChallengeController.cs:415` | P1 |
| GenerateCredentials() 生成的密码未应用到 VM | `EnvironmentService.cs:266-272` | P1 |
| 重置/销毁时 VM 名字从模式重建而非读取持久化字段 | `EnvironmentService.cs:186-196` | P1 |

### 1.2 容器基础设施

| 组件 | 当前限制 | 影响 |
|---|---|---|
| DockerProvider | 连接单一 Docker daemon（单个 URI） | 无法管理多主机 |
| KubernetesProvider | 连接单一 K8s 集群 | 同上 |
| ContainerOrchestrator | 仅本地 Docker API | 不能远程操作 |
| ImageStorage | 本地文件系统存储 | 镜像无法分发到多节点 |

### 1.3 分布式管理

**零代码存在。** 全局搜索 Node/Fleet/Agent/Distributed/Remote/Cluster 返回的不相关结果（Redis、K8s NodePort 等）之外，没有任何节点注册、心跳、远程调度、镜像分发的实现。

---

## 二、评分与验证管线（agent-2 报告）

### 2.1 七大验证体系

```
┌──────────────────────────────────────────────────────────────────┐
│                   newGZCTF 验证体系全景图                          │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  A. FlagChecker（后台 Channel Worker）                            │
│     └─ `Services/FlagChecker.cs` — 成熟，用于传统 CTF Flag 验证     │
│     └─ 调 GameInstanceRepository.VerifyAnswer()                   │
│                                                                  │
│  B. GameInstanceRepository.VerifyAnswer()                         │
│     └─ `Repositories/GameInstanceRepository.cs` — 成熟            │
│     └─ 检查 FlagContext、动态 Flag 模板                            │
│                                                                  │
│  C. SubmissionController.VerifySubmissionAsync()                  │
│     └─ `Controllers/SubmissionController.cs`                      │
│     └─ VerifyAutoExact：三层 fallback                             │
│        ① ScoringRule.ExpectedAnswerHash                          │
│        ② Stage.VerifyFlag()（Scenario 专属）                      │
│        ③ FlagContexts（传统 CTF 专属）                             │
│     └─ VerifyAutoRegex：从 VerificationConfig 读 Pattern          │
│                                                                  │
│  D. CheckpointVerificationService（后台 30s 轮询）                 │
│     └─ `Services/CheckpointVerificationService.cs`                │
│     └─ AutoCommand：SSH 远程执行命令匹配输出                        │
│     └─ AutoScript：永远返回 false ⚠️                               │
│                                                                  │
│  E. IRChallengeController.SubmitCheckpoint()                      │
│     └─ 手动提交 ManualAnswer 类型检查点                            │
│     └─ 更新 IRInstance.CheckpointResults JSON                     │
│     └─ ⚠️ 不创建 Submission 记录                                  │
│                                                                  │
│  F. ScenarioController.SubmitStageFlag()                          │
│     └─ 检查 Stage.FlagHash（SHA256）                              │
│     └─ 更新 ScenarioInstance.StageStatuses JSON                   │
│     └─ ⚠️ 不创建 Submission 记录                                  │
│                                                                  │
│  G. ExerciseInstanceRepository.VerifyAnswer()                     │
│     └─ 练习模式专用                                              │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### 2.2 关键 Bug

| Bug | 描述 | 位置 |
|---|---|---|
| **IR 分数不进入排行榜** | Checkpoint 完成更新 IRInstance JSON 但不写 Submission 表。LeaderboardService 只读 ScoringRules+Submissions，IR 分数永远为 0 | `IRChallengeController.cs:571-578` |
| **Double-decay 衰减** | `ApplyScoreDecay` 在两处重复：提交时减一次（SubmissionController）、统计总分时再减一次（ScoringService） | `SubmissionController.cs:524` + `ScoringService.cs:73` |
| **AutoScript 永远 false** | 两处 AutoScript 都是存根，第一处创建永远待审核的提交，第二处永远返回 false | `SubmissionController.cs:400`、`CheckpointVerificationService.cs:283` |
| **Scenario 绕过 ScoringRule** | `SubmitStageFlag` 直接改 StageStatuses JSON 不写 Submissions。选手可以走两条路提交导致双重计分 | `ScenarioController.cs:540-555` |
| **VerifyAutoExact 三层 fallback 脆弱** | 如果第一层 ExpectedAnswerHash 设错了但非空，第二三层永远不会执行 | `SubmissionController.cs:417-453` |

### 2.3 VerificationType vs VerificationMode 重叠

```
IRCheckpoint.VerificationType          ScoringRule.VerificationMode
─────────────────────────────          ────────────────────────────
AutoScript  = 0                        无
AutoCommand = 1                        无
ManualAnswer = 2                       无
无                                       AutoExact   = 0
无                                       AutoRegex   = 1
ManualReview = 3                        ManualReview = 3
```

两个枚举代表相同概念但值不兼容，操作完全不同的数据模型且无桥接代码。

---

## 三、API 表面分析（agent-3 报告）

### 3.1 完整端点表

| 方法 | 路由 | Controller | 认证 | 备注 |
|---|---|---|---|---|
| POST | /api/v1/ir-challenges | IRChallenge.Create | Admin | |
| GET | /api/v1/ir-challenges | IRChallenge.List | 无（公开） | |
| GET | /api/v1/ir-challenges/{id} | IRChallenge.Get | 无 | |
| PUT | /api/v1/ir-challenges/{id} | IRChallenge.Update | Admin | |
| DELETE | /api/v1/ir-challenges/{id} | IRChallenge.Delete | Admin | 返回 **204** |
| POST | /api/v1/ir-challenges/{id}/instances | IRChallenge.CreateInstance | User | |
| GET | /api/v1/ir-challenges/instances/{id} | IRChallenge.GetInstance | User | |
| POST | /api/v1/ir-challenges/instances/{id}/checkpoints/{cpId}/submit | IRChallenge.SubmitCheckpoint | User | |
| POST | /api/v1/ir-challenges/instances/{id}/reset | IRChallenge.ResetInstance | User | |
| POST | /api/v1/scenarios | Scenario.CreateScenario | Admin | |
| GET | /api/v1/scenarios | Scenario.ListScenarios | 无 | |
| GET | /api/v1/scenarios/{id} | Scenario.GetScenario | 无 | |
| PUT | /api/v1/scenarios/{id} | Scenario.UpdateScenario | Admin | 不更新 Stages |
| DELETE | /api/v1/scenarios/{id} | Scenario.DeleteScenario | Admin | 返回 **200** |
| POST | /api/v1/scenarios/{id}/publish | Scenario.PublishScenario | Admin | |
| POST | /api/v1/scenarios/{id}/instances | Scenario.CreateInstance | User | |
| GET | /api/v1/scenarios/instances/{id} | Scenario.GetInstanceStatus | User | |
| POST | /api/v1/scenarios/instances/{id}/stages/{stageId}/submit | Scenario.SubmitStageFlag | User | |
| POST | /api/v1/submissions | Submission.CreateSubmission | User | |
| GET | /api/v1/submissions | Submission.QuerySubmissions | User | |
| POST | /api/v1/submissions/upload | Submission.UploadWriteup | User | 50MB |
| GET | /api/v1/submissions/pending-review | Submission.GetPendingReviews | Admin | |
| POST | /api/v1/submissions/{id}/review | Submission.SubmitReview | Admin | |
| GET | /api/v1/image-templates | ImageTemplate.List | Authorize | 匿名返回 **非 ArrayResponse** |
| POST | /api/v1/image-templates | ImageTemplate.Upload | Admin | 50GB |
| GET | /api/v1/image-templates/{id} | ImageTemplate.GetById | Authorize | |
| DELETE | /api/v1/image-templates/{id} | ImageTemplate.Delete | Admin | 返回 **204** |
| ... | /api/[controller] | Game/Team/Account/Admin | 多种 | 传统 GZCTF |

### 3.2 路由风格冲突

```
/api/v1/ir-challenges              ← 新 RESTful
/api/v1/scenarios                  ← 新 RESTful
/api/v1/submissions                ← 新 RESTful
/api/v1/image-templates            ← 新 RESTful
/api/v1/time-slots                 ← 新 RESTful
/api/[controller]                  ← 传统 GZCTF（Game/Team/Account/Admin）
/api/Info                          ← 无版本前缀
/api/tokens                        ← 无版本前缀
[controller]/action                ← AssetsController 无 API 前缀
```

**GameController 路由 Bug** — 路由 `[HttpGet("Games/{id:int}/Captures")]` 挂在 `[Route("api/[controller]")]` 下产生 `/api/Game/Games/{id}/Captures`，"Games" 出现两次。

### 3.3 认证不一致

| Controller | 认证方式 |
|---|---|
| IRChallengeController | `[RequirePrivilege(Role.Admin)]`（泛型形式） |
| ScenarioController | `[RequireAdmin]`（派生类） |
| AdminController | `[RequireAdmin]`（Controller 级） |
| ImageTemplateController | `[Authorize]` + `[Authorize(Roles = "Admin")]`（混合） |

**新增端点（IR/Scenario/Submission）无速率限制** — Flag 提交端点无 `[EnableRateLimiting]`，缺乏暴力防范保护。

### 3.4 DELETE 状态码不一致

| 操作 | 返回码 |
|---|---|
| IRChallengeController.Delete | **204** No Content |
| ScenarioController.Delete | **200** OK |
| ImageTemplateController.Delete | **204** No Content |

### 3.5 缺失端点

- 用户无法删除自己的 IR/Scenario 实例
- 无按用户或按 challenge 列出实例的端点
- 无单独检查点/阶段的 CRUD（全部通过 bulk PUT）
- 无节点/分布式管理端点

### 3.6 死代码

- `ExerciseController` — 完全空的存根，只有 `// TODO: exercise mode support`
- `RequireAdminOrTokenAttribute` — 定义在 `PrivilegeAuthentication.cs` 但从未使用

---

## 四、前端 UI 层（agent-4 报告）

### 4.1 Swagger 客户端未覆盖新功能

`Api.ts`（Swagger 自动生成）包含 `ChallengeType.Scenario` / `ChallengeType.IRChallenge` 枚举值，但**没有任何新功能的请求/响应模型**。所有新页面使用裸 `fetch()`：

```typescript
// 无类型安全、无 SWR 缓存、无自动重验证
const res = await fetch(`/api/v1/ir-challenges/${challengeId}/instances`, ...)
```

**未覆盖在 Api.ts 中的端点：**
```
/api/v1/scenarios/*              (6 个端点)
/api/v1/ir-challenges/*          (9 个端点)
/api/v1/submissions/*            (5 个端点)
/api/v1/image-templates/*        (4 个端点)
/api/v1/time-slots/*             (2 个端点)
/hubs/scenario                    (SignalR)
```

### 4.2 死文件和重复文件

| 文件 | 状态 |
|---|---|
| `pages/admin/IRChallengeCreate.tsx` | **可能是死代码** — 子目录 `ir-challenges/new.tsx` 更完整 |
| `pages/admin/IRChallengeList.tsx` | **可能是死代码** — 同上 |
| `pages/admin/ScenarioCreate.tsx` | **可能是死代码** — 同上 |
| `pages/admin/ScenarioList.tsx` | **可能是死代码** — 同上 |
| `components/ir/GuacamoleDesktop.tsx` | **孤儿组件** — 编写了但从未被页面 import |
| `components/ir/ShellLogViewer.tsx` | **孤儿组件** — 同上 |
| `components/scenario/ScoringRuleEditor.tsx` | **孤儿组件** — 同上 |

### 4.3 类型定义

所有新功能 TypeScript 类型在组件内以内联 `interface` 或 `any` 定义，多个文件重复：
- `CheckpointData` 在 `IRChallengeCreate.tsx` 和 `ir-challenges/new.tsx` 重复定义
- `StageData` 在 `ScenarioCreate.tsx` 和 `scenarios/new.tsx` 重复定义
- `IRInstanceStatus` 中的 `accessDetails` 用 `{linux?: {...}; windows?: {...}}` 但后端实际返回原始 JSON 字符串

### 4.4 缺陷总结

| 问题 | 位置 | 严重度 |
|---|---|---|
| **XSS**: `dangerouslySetInnerHTML` 渲染用户提交内容 | `SubmissionReview.tsx:92` | **Security** |
| 新页面全用中文硬编码，忽略 i18n | 所有 IR/Scenario 页面 | P1 |
| 无空状态/骨架屏/错误边界 | IRList → 空白 Table | P1 |
| Silent `.catch(() => {})` 吞错误 | `scenarios/new.tsx` 等 | P1 |
| 硬编码常量重复（VerificationType、OS Type 等） | 至少 2-4 处重复 | P2 |
| RDP 通过外部链接打开而非嵌入 GuacamoleDesktop | `IRChallengePlayer.tsx:120` | P2 |

---

## 五、数据模型与数据库（agent-5 报告）

### 5.1 FK 关系缺口

| 位置 | 问题 | 影响 |
|---|---|---|
| `Container.GameInstanceId` / `Container.ExerciseInstanceId` | **无 FK 约束** — 数据库无引用完整性 | 可能指向不存在的行 |
| `FlagContext.ChallengeId` 和 `FlagContext.ExerciseId` | **无 OnDelete 级联** — 删除 GameChallenge 后 FlagContext 变成孤儿 | 数据残留 |
| `FlagContext` 双 FK | 无 CHECK 约束确保只能有一个非空 | 数据不一致 |
| `UserParticipation.Participation` | **是字段而非属性** — EF Core 导航属性需 `{ get; set; }`，这里用 `public Participation Participation = null!;`（字段） | 变更跟踪可能不工作 |

### 5.2 JSON-in-JSON 反模式（10 个字段）

| 实体 | 字段 | 内容 | 风险 |
|---|---|---|---|
| `ScenarioInstance` | `StageStatuses` (max 4096) | JSON Dict: stageId → status | 不可查询 |
| `ScenarioInstance` | `StageTimeline` (max 8192) | JSON 数组 | 不可查询 |
| `IRInstance` | `CheckpointResults` (unbounded) | JSON Dict | 不可查询 |
| `IRInstance` | `ShellLog` (unbounded) | JSON 数组 | **无长度限制** |
| `IRInstance` | `AccessDetails` | JSON 连接信息 | 敏感信息泄露 |
| `Stage` | `NetworkRules` | JSON 数组 | 不可查询 |
| `Stage` | `PrerequisiteStageIds` | JSON int 数组 | 不可查询 |
| `Stage` | `EnvironmentImageIds` | JSON int 数组 | 不可查询 |
| `ScoringRule` | `VerificationConfig` | JSON 配置 | 可接受 |
| `IRCheckpoint` | `VerificationConfig` | JSON 配置 | 可接受 |

**查询"已完成检查点 X 的实例"需要全表 `LIKE '%...%'` 扫描。**

### 5.3 并发保护缺口

| 有 `ConcurrencyToken` (xmin) | 缺少并发保护 |
|---|---|
| Challenge, GameInstance, ExerciseInstance, IRInstance | **Submission, FlagContext, Container, Game, Team, Participation, ScenarioInstance** |

Submission 无并发保护 — 同一队伍并发提交 Flag 可能竞争。

### 5.4 索引缺口

- **Submission.Status** 未索引 — 排行榜统计 Accepted 提交需扫描全表后内存过滤
- **Container.Status** 未索引 — 清理服务轮询扫描全表
- **GameEvents (GameId, Type, PublishTimeUtc)** 复合索引缺失

### 5.5 枚举存储不一致

| 枚举 | 存储 | 实体 |
|---|---|---|
| Role | **int** (4 bytes) | UserInfo |
| ParticipationStatus | **int** (4 bytes) | Participation |
| AnswerResult | **string** | Submission.Status |
| 其余 ~16 个 | **byte** (1 byte) | 各实体 |

### 5.6 Scenario/Stage 关系

`Stage.ScenarioId` 指向 `GameChallenge.Id`，`IRCheckpoint.ChallengeId` 也指向 `GameChallenge.Id`，但 Fluent API 使用了 `.WithMany()`（无反向导航属性）。这意味着从 `GameChallenge` 无法 `.Include(c => c.Stages)` 或 `.Include(c => c.Checkpoints)`，必须手动查询。

### 5.7 需要新增的实体

| 实体 | 用途 |
|---|---|
| `VMInstance` | VM 生命周期记录（平行于 Container） |
| `StageDependency` | 规范化 Stage 依赖关系（替代 PrerequisiteStageIds JSON） |
| `WorkerNode` / `DeploymentNode` | 分布式节点管理 |
| `ScenarioSubmission` | 扩展 Submission 带 StageId FK |

---

## 六、安全审计

| 等级 | 问题 | 位置 |
|---|---|---|
| **严重** | VNC 暴露在 0.0.0.0 无认证 | `VmManager.cs:323` |
| **严重** | Guacamole RDP 密码硬编码 "password" | `GuacamoleProxy.cs:78` |
| **严重** | VM 管理命令注入风险 — virsh/qemu-img 参数拼接 | `VmManager.cs:235-280` |
| **严重** | 前端 XSS — `dangerouslySetInnerHTML` | `SubmissionReview.tsx:92` |
| **中** | IRInstance 返回原始 AccessDetails（可能含敏感信息） | `IRChallengeController.cs:506-509` |
| **中** | 新增 Flag 提交端点无速率限制 | IR/Scenario/Submission Controller |
| **低** | try-catch 大量吞异常 | 多处反序列化代码 |

---

## 七、测试覆盖

### E2E（5 个文件）
```
tests/e2e/ir-challenge.spec.ts          ✓
tests/e2e/scenario-create.spec.ts       ✓
tests/e2e/scenario-play.spec.ts         ✓
tests/e2e/submission-scoring.spec.ts    ✓
tests/e2e/topology-editor.spec.ts       ✓
```
**缺失**: VM 镜像上传、Guacamole RDP 连接流、多阶段场景完整流

### 集成测试（17 个文件）
覆盖 GZCTF 原始功能较完整，**缺失**: IRChallengeController、ScenarioController、SubmissionController 的多类型提交测试

---

## 八、优先级矩阵（更新版）

```
P0（立即修复）                          P1（本周修复）
─────────────────                      ─────────────────
IR 分数不入排行榜                       无 Hyper-V 支持
Double-decay 衰减 Bug                  VNC 安全性（0.0.0.0 + 无认证）
AutoScript 永远返回 false               SubmitStageFlag 不写 Submission
Flag 端点无速率限制                     分布式节点架构设计
无接口抽象（VmManager）                  Api.ts 未覆盖新功能
Guacamole RDP 未接入 VmManager          死文件清理 + 孤儿组件处理
Container FK 无约束                     前端 XSS 修复
IRInstance 泄漏敏感信息                 10 个 JSON-in-JSON 字段迁移

P2（本月修复）                          P3（下阶段）
─────────────────                      ─────────────────
后端创建 Submission 记录（IR/Scenario）    Docker Compose 编排
前端类型定义集中化                        E2E 测试补全
i18n 覆盖新页面                          API 版本化统一
Submission 并发保护 + 索引               ExerciseController 实现
DELETE 状态码统一                        路由风格冲突
ScoringRule 与 IRCheckpoint 整合         新实体（VMInstance、WorkerNode 等）
```

---

## 附：深度分析文件清单

| 文件 | 来源 | 大小 |
|---|---|---|
| `docs/analysis-vm-infra.md` | agent-1 | ~12.8KB |
| `docs/analysis-scoring.md` | agent-2 | ~8KB |
| `docs/analysis-api.md` | agent-3 | ~23KB |
| `docs/analysis-frontend.md` | agent-4 | agent 内联报告 |
| `docs/analysis-datamodel.md` | agent-5 | agent 内联报告 |
| **`docs/codebase-analysis.md`** | **本文件（汇总）** | **完整版** |
