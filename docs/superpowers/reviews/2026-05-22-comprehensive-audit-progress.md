# GZCTF 终极审计 — 进度与发现记录

> 审计原则: 逐行验证、白盒分析、不假设任何代码正确、不放过任何边界条件
> 审计方法: 读代码 + grep + 静态推理，不运行、不修改
> 审计完成时间: 2026-05-22

---

## 一、数据模型完整性审计

### 1.1 Challenge 基类字段审计

| 检查项 | 状态 | 发现 |
|--------|------|------|
| `Environment` 默认值 `EnvironmentType.None` | ✅ 正确 | `Challenge.cs:156` 默认值正确 |
| `ImageTemplateId` 为 nullable int | ⚠️ 潜在问题 | Docker 类型题目 `ImageTemplateId=null` 时，容器创建仍使用 `ContainerImage` 字段（`GameInstanceRepository.cs:119`），不会 NPE。但 VM 类型题目 `ImageTemplateId=null` 时，`GameController.cs:1253` 序列化 `templateId=null` 到 DeploymentTarget，Agent 端可能 NPE |
| `ContainerImage` 字段仍存在 | ✅ 正确 | `GameInstanceRepository.cs:172` 使用 `challenge.ContainerImage` 创建容器，Docker 类型回退到此字段 |
| `FlagTemplate` 字段仍存在 | ✅ 正确 | `Challenge.cs:108-128` 动态 Flag 生成逻辑完整，未被 FlagContext 重构破坏 |
| `ExerciseChallenge` 继承正确 | ✅ 正确 | `ExerciseChallenge.cs:3` 继承 Challenge，拥有 `Environment`/`ImageTemplateId` 字段 |

### 1.2 FlagContext 扩展字段审计

| 检查项 | 状态 | 发现 |
|--------|------|------|
| 8 个新字段 MaxLength | ✅ 正确 | `FlagContext.cs:17` Flag=127, `:32` Description=512, `:53` AttachmentHash=128, `:64` CustomName=64 |
| `OrderIndex` 默认 0 | ⚠️ P2 | 旧数据迁移后所有 Flag 的 OrderIndex=0，`OrderBy(f => f.OrderIndex)` 后顺序不确定（依赖插入顺序） |
| `AttachmentHash` SHA256 比对 | ✅ 正确 | `GameInstanceRepository.cs:340-342` 使用 `ToSHA256String()` 和 `OrdinalIgnoreCase` 比较 |
| `AnswerType` 默认值 `Flag` | ✅ 正确 | `FlagContext.cs:59` 默认 `AnswerType.Flag`，旧数据向后兼容 |
| `MaxAttempts=0` 无限制 | ✅ 正确 | `GameInstanceRepository.cs:319` `if (targetFlag.MaxAttempts > 0)` 条件正确 |

### 1.3 FirstSolve 主键变更审计

| 检查项 | 状态 | 发现 |
|--------|------|------|
| 复合主键包含 FlagId | ❌ **P0 严重** | `AppDbContext.cs:408` Fluent API `HasKey(e => new { e.ParticipationId, e.ChallengeId })` 覆盖了模型的 `[PrimaryKey(ParticipationId, ChallengeId, FlagId)]`，**PK 缺少 FlagId** |
| FlagId 为非 nullable int | ⚠️ P1 | 旧 FirstSolve 数据无 FlagId 列，迁移脚本需设置默认值 |
| GenScoreboard JOIN 包含 FlagId | ✅ 正确 | `GameRepository.cs:395` SolveSnapshot 包含 FlagId，JOIN 条件正确 |

### 1.4 Submission 模型审计

| 检查项 | 状态 | 发现 |
|--------|------|------|
| `FlagId` nullable int | ✅ 正确 | `Submission.cs:93` nullable，`GameInstanceRepository.cs:292` 有 null 回退逻辑 |
| `SubmissionType` 为 string | ✅ 正确 | `Submission.cs:54` `[MaxLength(32)] public string SubmissionType`，迁移已 ALTER COLUMN |
| `Status` 使用 HasConversion string | ✅ 正确 | `AppDbContext.cs:319` `HasConversion<string>()`，Snapshot 中为 `text` 类型 |
| `FlagContext` 导航属性 | ❌ **P1** | `Submission.FlagContext` 导航属性未在 Fluent API 中配置 `HasForeignKey(e => e.FlagId)`，EF Core 自动生成 `FlagContextId` 列（nullable），与 `FlagId` 脱节 |

### 1.5 已删除模型的残留引用

| 检查项 | 状态 | 发现 |
|--------|------|------|
| .cs 文件（排除 Migrations/） | ✅ 正确 | 仅 `DockerManager.cs` 引用 `DockerImageNotFoundException`（Docker SDK 异常，非模型引用） |
| .tsx/.ts 文件 | ⚠️ P3 | `services/scenarioHub.ts` 仍存在（死代码），`utils/screenDemoData.ts` 含 Scenario 类型 |
| AppDbContextModelSnapshot | ✅ 正确 | 无已删除模型 Entity 定义 |
| DI 注册 | ✅ 正确 | `ServicesExtension.cs` 无已删除服务 |

---

## 二、评分系统白盒审计

### 2.1 GenScoreboard 重写审计

| 检查项 | 状态 | 发现 |
|--------|------|------|
| SolveSnapshot 包含 FlagId | ✅ 正确 | `GameRepository.cs:684-689` record 包含 FlagId |
| FirstSolves 查询投影 FlagId | ✅ 正确 | `GameRepository.cs:395` `x.fs.FlagId` |
| allFlags 声明在事务块外 | ✅ 正确 | `GameRepository.cs:287` 在事务外声明，事务内赋值 |
| 按 (ChallengeId, FlagId) 分组 | ✅ 正确 | `GameRepository.cs:482-483` |
| FixedScore 跳过衰减 | ✅ 正确 | `GameRepository.cs:500-502` |
| 血牌分配 per-flag | ✅ 正确 | `GameRepository.cs:535-555` 每个 Flag 独立计算 1st/2nd/3rd |
| 团队总分 = 所有 Flag 得分之和 | ✅ 正确 | `GameRepository.cs:589` `scoreboardItem.Score += item.Score` |
| 动态衰减公式 | ✅ 正确 | `GameChallenge.cs:49-58` 公式正确 |
| Redis 缓存 7 天 | ✅ 正确 | `GameRepository.cs:166` |

### 2.2 VerifyAnswer 重写审计

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Flag 查找逻辑 | ✅ 正确 | `GameInstanceRepository.cs:292-305` FlagId 精确查找 + OrderIndex 回退 |
| AnswerType 路由 | ✅ 正确 | `GameInstanceRepository.cs:337-350` Flag/File/Custom 三种路由 |
| 尝试次数限制 | ⚠️ P2 | `GameInstanceRepository.cs:321-324` 统计**所有状态**的提交数（含 Accepted），而非仅非 Accepted。这意味着已正确回答的 Flag 也会消耗尝试次数 |
| pg_advisory_xact_lock | ✅ 正确 | `GameInstanceRepository.cs:367-370` 锁键包含 FlagId（通过 HashCode.Combine） |
| FirstSolve 创建 | ✅ 正确 | `GameInstanceRepository.cs:423-429` 四字段完整 |
| 重复检查包含 FlagId | ✅ 正确 | `GameInstanceRepository.cs:372-375` |
| CountBloodEligibleSolves 传入 flagId | ✅ 正确 | `GameInstanceRepository.cs:411` |

---

## 三、Flag 管理全链路审计

### 3.1 后端 CRUD

| 检查项 | 状态 | 发现 |
|--------|------|------|
| POST Flags 读取所有字段 | ✅ 正确 | `GameChallengeRepository.cs:19-32` 8 个扩展字段全部赋值 |
| PUT Flags 更新所有字段 | ✅ 正确 | `EditController.cs:983-991` 更新 8 个扩展字段 |
| DELETE Flags 级联影响 | ⚠️ P1 | `GameChallengeRepository.cs:99-121` 删除 Flag 时不检查 FirstSolve 引用，可能导致 FirstSolve.FlagId 指向已删除的 Flag |
| OrderIndex 批量添加冲突 | ⚠️ P3 | 无自动重排逻辑，管理员需手动确保 OrderIndex 不冲突 |

### 3.2 前端 Flag 编辑

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Flag 列表展示 | ✅ 正确 | 显示 ScoreMode、AnswerType、OrderIndex、CustomName |
| ScoreMode 下拉框 | ✅ 正确 | InheritDecay / FixedScore |
| FixedScore 仅 ScoreMode=FixedScore 时显示 | ❌ **P2** | `flags/index.tsx:118` FixedScore 输入框始终显示，未做条件隐藏 |
| AnswerType 下拉框 | ✅ 正确 | Flag / File / Custom |
| POST body 包含所有字段 | ✅ 正确 | `flags/index.tsx:68-77` |
| PUT 请求（编辑） | ❌ **P2** | 前端无编辑按钮，只能添加和（隐含的）删除，无法编辑已有 Flag |

### 3.3 玩家端 Flag 提交

| 检查项 | 状态 | 发现 |
|--------|------|------|
| 多 Flag 步骤条 | ⚠️ P2 | `ChallengeModal.tsx:277-294` 使用 `i + 1` 作为 FlagId（序号），而非实际 FlagContext.Id。提交时 `GameChallengeModal.tsx:153` 发送 `{ flagId: activeFlagId }`，但 activeFlagId 是序号（1,2,3...），不是数据库 FlagId |
| activeFlagId 传递 | ❌ **P1** | 上述问题导致 `GameController.cs:1037` 设置 `submission.FlagId = model.FlagId`（值为 1,2,3...），而 `VerifyAnswer` 用 `f.Id == submission.FlagId.Value` 查找，会匹配到 ID=1 的 Flag（而非第 1 个 Flag） |
| 单 Flag 不显示步骤条 | ✅ 正确 | `ChallengeModal.tsx:264` `hasMultiFlags = flags?.length > 1` |
| 提交后同步处理 | ✅ 正确 | `GameChallengeModal.tsx:166` 直接使用返回结果 |

---

## 四、提交与排行全链路审计

### 4.1 GameController.Submit 同步化

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Channel 已移除 | ✅ 正确 | 无 `Channel<Submission>` 引用 |
| FlagChecker 已删除 | ✅ 正确 | 无 FlagChecker 引用 |
| 提交后直接返回 | ✅ 正确 | `GameController.cs:1039` `Ok(new { submission.Id, Status, BloodType })` |
| 异常处理 rollback | ✅ 正确 | `GameController.cs:1046-1053` catch Exception → rollback |
| DbUpdateConcurrencyException retry | ✅ 正确 | `GameController.cs:1041-1045` 最多 3 次 |

### 4.2 前端提交流程

| 检查项 | 状态 | 发现 |
|--------|------|------|
| 同步处理结果 | ✅ 正确 | `GameChallengeModal.tsx:166` `checkDataFlag(res.data.id, res.data.status)` |
| 无轮询 | ✅ 正确 | 无 setInterval/useEffect 轮询 |
| Accepted 时销毁容器 | ✅ 正确 | `GameChallengeModal.tsx:194` |
| Wrong 时随机提示 | ✅ 正确 | `GameChallengeModal.tsx:200` |
| mutate 防止覆盖 | ✅ 正确 | `GameChallengeModal.tsx:159-162` |

### 4.3 排行榜

| 检查项 | 状态 | 发现 |
|--------|------|------|
| ScoreboardModel 包含 TotalFlags | ✅ 正确 | `ScoreboardModel.cs:360` |
| ChallengeItem 包含 FlagId | ✅ 正确 | `ScoreboardModel.cs:286` |
| 前端使用新字段 | ❌ **P2** | 前端排行榜未使用 `totalFlags` 字段展示多 Flag 完成进度 |

---

## 五、环境模板与镜像管理审计

### 5.1 后端 API

| 检查项 | 状态 | 发现 |
|--------|------|------|
| admin 端点 RequireAdmin | ✅ 正确 | Upload/Delete/RegisterDocker/ImportLocal 均有 `[RequireAdmin]` |
| register-docker RegistryAuth | ✅ 正确 | `ImageTemplateController.cs:147` `RegistryAuth` 字段存在 |
| upload RequestSizeLimit | ⚠️ P3 | `ImageTemplateController.cs:39` 50GB vs `:162` 60GB，两个端点限制不一致 |
| Delete 检查引用 | ✅ 正确 | `ImageTemplateController.cs:217-221` 检查 `Challenge.ImageTemplateId` |

### 5.2 ArchiveExtractor 安全审计

| 检查项 | 状态 | 发现 |
|--------|------|------|
| tar 命令注入 | ⚠️ P1 | `ArchiveExtractor.cs:51-53` `archivePath` 和 `extractDir` 使用双引号包裹，但 `archivePath` 由服务器生成（`Path.Combine(tempDir, "archive" + ext)`），`extractDir` 由 GUID 生成，**路径本身安全**。但 `originalFileName` 用于扩展名检测和模板命名，如果文件名含特殊字符可能影响 `Path.GetExtension` |
| qemu-img 命令注入 | ⚠️ P1 | `ArchiveExtractor.cs:74` `baseVmdk` 来自 `Directory.GetFiles` 扫描结果，路径安全 |
| VM 格式检测 | ✅ 正确 | `.vmx`+`.vmdk` → VMware, `.qcow2` → KVM |
| SHA256 async | ✅ 正确 | `ArchiveExtractor.cs:94-96` 使用 `ComputeHashAsync` |
| **.ova 格式未处理** | ❌ **P1** | `ArchiveExtractor.cs:65` 检测到 `.ova` 文件但无处理逻辑，不会转换 |

### 5.3 前端环境模板

| 检查项 | 状态 | 发现 |
|--------|------|------|
| data?.items 分页访问 | ✅ 正确 | `images/Index.tsx:171` `data?.items` |
| 注册 Docker Modal | ✅ 正确 | 字段验证 + loading 状态 |
| 上传压缩包 | ✅ 正确 | FormData + POST |
| 删除确认 | ✅ 正确 | confirm 对话框 |
| 本地导入 Modal | ✅ 正确 | 路径输入 |
| 状态 Badge | ✅ 正确 | Ready/Importing/Error |

---

## 六、节点与部署审计

### 6.1 后端 API

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Detail 脱敏 | ✅ 正确 | `NodesController.cs:59-64` 返回匿名 DTO，排除 AuthToken |
| Heartbeat 频率 | ⚠️ P3 | 无频率限制，恶意客户端可高频写入 DB |
| Register Admin 权限 | ✅ 正确 | `[RequireAdmin]` |
| SSH 命令注入 | ❌ **P0 严重** | `NodeDeployService.cs:95-99` `sshpass -p "{password}" ssh ... {user}@{host} "{safeCommand}"` — **password 和 host/user 直接拼接进命令行参数**，仅对 command 做了引号转义。`password` 含 `"` 或 `$` 等特殊字符时可注入。且 `sshpass` 在 Windows 不可用 |

### 6.2 前端节点管理

| 检查项 | 状态 | 发现 |
|--------|------|------|
| 15 秒轮询 | ✅ 正确 | `nodes/Index.tsx:77` |
| DeployButton 内联表单 | ✅ 正确 | 收集 IP/用户名/密码 |
| CleanupButton | ✅ 正确 | 获取列表 → DELETE 离线节点 |
| Detail 不展示 authToken | ✅ 正确 | 后端已脱敏 |
| 删除按钮 | ✅ 正确 | DELETE /api/v1/nodes/{id} |

---

## 七、部署队列审计

### 7.1 后端

| 检查项 | 状态 | 发现 |
|--------|------|------|
| GET 支持 status 过滤 | ✅ 正确 | `DeploymentTargetsController.cs:115-116` |
| GET 支持分页 | ✅ 正确 | page/pageSize 参数 |
| Authorize 权限 | ✅ 正确 | `[Authorize]` |
| DELETE 仅取消 Pending/Running | ✅ 正确 | `DeploymentTargetsController.cs:154` |
| RequireAdmin 权限 | ✅ 正确 | `[RequireAdmin]` |

### 7.2 前端

| 检查项 | 状态 | 发现 |
|--------|------|------|
| SWR 10 秒轮询 | ✅ 正确 | `queue/Index.tsx:25` |
| 状态筛选下拉框 | ✅ 正确 | |
| 取消按钮仅 Pending/Running | ✅ 正确 | `queue/Index.tsx:87` |
| 错误信息 Tooltip | ✅ 正确 | |
| 总记录数 | ✅ 正确 | |

---

## 八、数据库迁移完整性审计

### 8.1 迁移文件检查

| 检查项 | 状态 | 发现 |
|--------|------|------|
| FixSubmissionTypeColumn Up | ✅ 正确 | ALTER COLUMN SubmissionType TYPE text |
| FixSubmissionTypeColumn Down | ✅ 正确 | 回滚为 smallint |
| Snapshot 无已删除模型 | ✅ 正确 | |
| Submission.SubmissionType 为 string | ✅ 正确 | `character varying(32)` |
| FirstSolve 主键包含 FlagId | ❌ **P0** | `AppDbContextModelSnapshot.cs:457` `HasKey("ParticipationId", "ChallengeId")` — **缺少 FlagId** |
| FlagContext 包含 8 个新字段 | ✅ 正确 | OrderIndex, ScoreMode, FixedScore, MaxAttempts, AnswerType, Description, CustomName, AttachmentHash |
| Submission/FirstSolve 有 FlagContextId 列 | ❌ **P1** | EF Core 自动生成的冗余 FK 列，与 FlagId 脱节 |

### 8.2 迁移一致性

| 检查项 | 状态 | 发现 |
|--------|------|------|
| dotnet ef migrations list | ⚠️ | 无法在当前环境运行（需 SDK） |
| 新列存在 | ✅ | Snapshot 确认所有新列已定义 |
| 已删除表 | ⚠️ | TimeSlots 表可能仍存在于 DB（迁移未 DROP TABLE） |

---

## 九、ConfigureWarnings 抑制源审计

| 位置 | 文件 | 状态 |
|------|------|------|
| AddDbContext | `DatabaseExtension.cs:22-23` | ✅ 存在 |
| AddEntityConfiguration | `DatabaseExtension.cs:38-39` | ✅ 存在 |
| CreateAppDbContext | `EntityConfigurationProvider.cs:75` | ✅ 存在 |
| using Diagnostics | 两个文件 | ✅ 存在 |

**注意**: 3 处 `ConfigureWarnings(w => w.Ignore(PendingModelChangesWarning))` 仍然存在，这意味着 EF Core 不会检测模型与 Snapshot 的不一致。这正是 FirstSolve PK 错误未被发现的原因。

---

## 十、前端路由与页面完整性审计

### 10.1 管理导航

| 检查项 | 状态 | 发现 |
|--------|------|------|
| pages 数组 9 项 | ✅ 正确 | 仪表盘/比赛/团队/用户/环境模板/节点/部署队列/日志/设置 |
| 无 Scenario/IR/Docker 路由 | ✅ 正确 | |
| 无 SubmissionReview 路由 | ✅ 正确 | |

### 10.2 页面存在性

| 检查项 | 状态 | 发现 |
|--------|------|------|
| 已删除页面目录 | ✅ | scenarios/, ir-challenges/, DockerImages, Instances, SubmissionReview, ScenarioPlayer, IRChallengePlayer 均不存在 |
| 残留文件 | ⚠️ P3 | `services/scenarioHub.ts` 仍存在（死代码） |
| 残留引用 | ⚠️ P3 | `utils/screenDemoData.ts` 含 `DemoScenario` 类型，`components/screen/useScreenData.ts` 和 `components/ctf-screen/useCTFScreenData.ts` 引用 `useDemoScreenData` |

### 10.3 前端类型与后端同步

| 检查项 | 状态 | 发现 |
|--------|------|------|
| ChallengeType 4 个值 | ✅ 正确 | StaticAttachment/StaticContainer/DynamicAttachment/DynamicContainer |
| ChallengeCategory 无 Scenario/IR | ✅ 正确 | |
| EnvironmentType 枚举 | ✅ 正确 | None/Docker/WindowsVM |
| FlagScoreMode 枚举 | ✅ 正确 | InheritDecay/FixedScore |
| AnswerType 枚举 | ✅ 正确 | Flag/File/Custom |
| FlagSubmitModel 包含 flagId | ❌ **P1** | `Api.ts:2059-2065` `FlagSubmitModel` 接口**缺少 `flagId` 字段**，但前端代码 `GameChallengeModal.tsx:153` 实际发送了 `flagId`（因 `@ts-nocheck` 未报错） |

---

## 十一、安全审计

### 11.1 命令注入

| 检查项 | 状态 | 发现 |
|--------|------|------|
| ArchiveExtractor tar/qemu-img | ✅ 低风险 | 路径由服务器生成，非用户直接输入 |
| NodeDeployService sshpass | ❌ **P0** | password/host/user 直接拼入命令行参数，可注入。且 sshpass 在 Windows 不可用 |

### 11.2 认证与授权

| 检查项 | 状态 | 发现 |
|--------|------|------|
| 无 Authorize(Roles=Admin) 残留 | ✅ 正确 | 全部使用 RequireAdmin |
| Authorize 用于只读端点 | ✅ 正确 | |
| NodesController.Detail 不返回 AuthToken | ✅ 正确 | |

### 11.3 信息泄露

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Flag 在响应中遮蔽 | ✅ 正确 | 玩家端不返回明文 Flag |
| 异常消息 | ⚠️ P2 | `LocalImageImporter.cs:123` `Description = $"Imported from: {localPath}"` 泄露服务器本地路径 |
| ImageTemplate GetById 返回完整实体 | ⚠️ P2 | `ImageTemplateController.cs:101-105` 返回完整 template 对象，可能包含 `RegistryAuth` 等敏感字段 |

### 11.4 并发与竞态

| 检查项 | 状态 | 发现 |
|--------|------|------|
| VerifyAnswer pg_advisory_xact_lock | ✅ 正确 | 锁键包含 FlagId |
| Submit retry 循环 | ✅ 正确 | 最多 3 次 DbUpdateConcurrencyException |

---

## 十二、已知架构级缺失（记录）

1. **Agent 程序未实现** — 心跳端点存在但无人调用，节点 CPU/内存指标永远为默认值
2. **QueueManager 只分配不执行** — 需要后台服务执行实际的容器/VM 部署
3. **FleetManager 未被业务代码调用** — 调度器已实现但未接入
4. **VM 生命周期不完整** — VmInstance 模型创建了但没有 Running/Destroyed 状态同步
5. **TimeSlots 表仍在 DB 中** — 模型已删除但迁移未 DROP TABLE
6. **sshpass 在 Windows 不可用** — NodeDeployService 完全无法在 Windows 上运行
7. **GamePhase 残留** — `GamePhase.cs` 中 `IREnabled`/`ScenarioEnabled` 字段仍存在，前端 `Phases.tsx` 仍展示 IR/Scenario 列
8. **前端 `scenarioHub.ts` 死代码** — 引用已删除的 Scenario/IR 功能

---

## 发现汇总（按严重级别排序）

### P0 — 导致崩溃/数据丢失/安全漏洞

| 编号 | 文件:行号 | 描述 |
|------|-----------|------|
| P0-1 | `AppDbContext.cs:408` | FirstSolve 复合主键缺少 FlagId，Fluent API `HasKey(ParticipationId, ChallengeId)` 覆盖了模型注解 `[PrimaryKey(ParticipationId, ChallengeId, FlagId)]`。多 Flag 场景下同一 Participation+Challenge 只能有一条 FirstSolve 记录，后续 Flag 的 FirstSolve 会因 PK 冲突插入失败 |
| P0-2 | `NodeDeployService.cs:95-99` | SSH 命令注入：password/host/user 直接拼入命令行参数，且 sshpass 在 Windows 不可用 |

### P1 — 功能不可用或数据错误

| 编号 | 文件:行号 | 描述 |
|------|-----------|------|
| P1-1 | `AppDbContext.cs` | Submission/FirstSolve 的 `FlagContext` 导航属性未配置 `HasForeignKey(e => e.FlagId)`，EF Core 自动生成冗余 `FlagContextId` 列，导航属性与 FlagId 脱节 |
| P1-2 | `ChallengeModal.tsx:279-288` | 多 Flag 步骤条使用序号（1,2,3...）作为 flagId 提交，而非实际 FlagContext.Id，导致 VerifyAnswer 查找错误的 Flag |
| P1-3 | `GameChallengeRepository.cs:99-121` | 删除 Flag 时不检查 FirstSolve 引用，可能导致 FirstSolve.FlagId 悬空引用 |
| P1-4 | `Api.ts:2059-2065` | FlagSubmitModel 接口缺少 `flagId` 字段，前后端类型不同步 |
| P1-5 | `ArchiveExtractor.cs:65` | 检测到 .ova 文件但无处理/转换逻辑 |

### P2 — 可用但有缺陷

| 编号 | 文件:行号 | 描述 |
|------|-----------|------|
| P2-1 | `FlagContext.cs:27` | OrderIndex 默认 0，旧数据迁移后排序不确定 |
| P2-2 | `GameInstanceRepository.cs:321-324` | MaxAttempts 统计包含 Accepted 状态的提交，已正确回答的 Flag 也消耗尝试次数 |
| P2-3 | `flags/index.tsx:118` | FixedScore 输入框始终显示，未做 ScoreMode 条件隐藏 |
| P2-4 | `flags/index.tsx` | 前端无 Flag 编辑功能，只能添加和删除 |
| P2-5 | 前端排行榜 | 未使用 totalFlags 字段展示多 Flag 完成进度 |
| P2-6 | `LocalImageImporter.cs:123` | Description 泄露服务器本地路径 |
| P2-7 | `ImageTemplateController.cs:101-105` | GetById 返回完整实体，可能包含 RegistryAuth |

### P3 — 代码卫生

| 编号 | 文件:行号 | 描述 |
|------|-----------|------|
| P3-1 | `services/scenarioHub.ts` | 死代码，引用已删除的 Scenario/IR 功能 |
| P3-2 | `utils/screenDemoData.ts` | 含 DemoScenario 类型，引用已删除功能 |
| P3-3 | `Phases.tsx:20-21` | 仍展示 IR/Scenario 列 |
| P3-4 | `GamePhase.cs:16-17` | IREnabled/ScenarioEnabled 字段残留 |
| P3-5 | `ImageTemplateController.cs:39,162` | 两个上传端点的 RequestSizeLimit 不一致（50GB vs 60GB） |
| P3-6 | `NodesController.cs:78-93` | Heartbeat 端点无频率限制 |
| P3-7 | Flag OrderIndex 无自动重排逻辑 |

---

## 二次深度核验记录（2026-05-22）

> 方法：逐条读取源码，交叉验证模型/Fluent API/Snapshot/前端类型，确认无误判

### 核验结论汇总

| 编号 | 原判定 | 二次核验 | 变化 |
|------|--------|----------|------|
| P0-1 | FirstSolve PK 缺少 FlagId | ✅ **确认** | 无变化。Snapshot 第 457 行 `b.HasKey("ParticipationId", "ChallengeId")` 确认只有 2 字段 PK |
| P0-2 | SSH 命令注入 | ✅ **确认** | 无变化。第 99 行 password/user/host 直接拼入参数 |
| P1-1 | FlagContext 导航属性 FK 缺失 | ✅ **确认** | 无变化。Snapshot 第 448 行 `b.Property<int?>("FlagContextId")` 确认自动生成冗余 FK |
| P1-2 | 多 Flag 步骤条用序号作 flagId | ✅ **确认，根因更深** | ⚠️ **升级描述**：后端 `ChallengeDetailModel` 完全没有 `flags` 字段，玩家端 API 不返回 flags 数据。前端步骤条是死代码，多 Flag 题目对玩家完全不可用。全链路缺失：①后端 Model 无 flags ②后端 GetInstance 不加载 Challenge.Flags ③前端永远拿不到 flags ④步骤条永不显示 ⑤flagId 永远不发送 |
| P1-3 | 删除 Flag 不检查 FirstSolve | ✅ **确认** | 无变化。第 109 行直接 Remove + SaveAsync |
| P1-4 | 前端 FlagSubmitModel 缺 flagId | ✅ **确认** | 无变化。Api.ts:2059-2065 只有 `flag: string`，后端有 `FlagId` |
| P1-5 | .ova 格式未处理 | ✅ **确认，比原描述更严重** | ⚠️ **升级描述**：①直接上传 .ova 必然失败（switch 走 `_ => false`）②如果 .ova 在 zip 内部被检测到但无转换逻辑，第 99 行 `new FileInfo(qcow2Path).Length` 会抛 FileNotFoundException |
| P2-1 | OrderIndex 默认 0 | ✅ **确认** | 无变化 |
| P2-2 | MaxAttempts 统计含 Accepted | ✅ **确认，比原描述更严重** | ⚠️ **升级描述**：当前提交在 VerifyAnswer 之前已入库（GameController 先 AddSubmission+Commit 再调 VerifyAnswer），CountAsync 包含当前提交。**MaxAttempts=3 实际只有 2 次有效尝试，MaxAttempts=1 完全不可用** |
| P2-3 | FixedScore 始终显示 | ✅ **确认** | 无变化。第 118 行无条件渲染 |
| P2-4 | 无 Flag 编辑功能 | ✅ **确认** | 无变化。且也无删除按钮 |
| P2-5 | 排行榜未用 totalFlags | ✅ **确认** | 无变化。前端无 totalFlags/solvedFlags 引用 |
| P2-6 | LocalImageImporter 泄露路径 | ✅ **确认** | 无变化。第 122 行 `Description = $"Imported from: {localPath}"` |
| P2-7 | GetById 返回完整实体 | ✅ **确认** | 无变化。第 101-105 行 `Ok(template)` 含 RegistryAuth |
| P3-1~P3-7 | 代码卫生问题 | ✅ **全部确认** | 无变化 |

### 二次核验新发现

| 编号 | 描述 |
|------|------|
| NEW-1 | `ChallengeDetailModel.FromInstance()` 不填充 flags，且 `GetInstance()` 不 Include `Challenge.Flags`，多 Flag 数据获取全链路缺失 |
| NEW-2 | `GameController.Submit` 先 `AddSubmission` + `CommitAsync` 再调 `VerifyAnswer`，当前提交已入库并被 CountAsync 计入，导致 MaxAttempts 偏差 |
| NEW-3 | `ArchiveExtractor` 第 65 行 `hasOva` 检测后无处理分支，如果只有 .ova 没有 vmx/vmdk/qcow2，第 99 行 `FileInfo(qcow2Path).Length` 抛异常 |

### 误判排除

无。所有原始发现均经源码交叉验证确认，无假阳性。

---

## 三、节点/模板/调度/容器启动深度审计（2026-05-22）

> 审计范围：节点注册、状态检测、模板注册、VM上传/格式转换/注册/分发、节点智能调度、多人容器启动

### 1. 节点注册全链路

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Register 端点权限 | ✅ | `[RequireAdmin]` |
| NodeDeployService 流程 | ❌ **P0** | 第 27-37 行：先创建 WorkerNode 入库（Status=Unknown），再 SSH 探测能力。SSH 失败时节点留在 DB 中 Status=Error，但**不回滚已创建的节点记录**。重复注册会创建多个 Error 节点 |
| SSH 命令注入（已修复） | ✅ | 第 93-104 行：已添加 SafeHostPattern/SafeUserPattern 白名单验证，第 116 行 password 通过环境变量 `SSHPASS` 传递，不再拼入命令行参数 |
| sshpass Windows 不可用 | ❌ **P1** | 第 109 行 `FileName = "sshpass"`，Windows 无此程序 |
| NodeRegisterRequest 死代码 | ⚠️ P3 | `NodesController.cs:174` 定义了 `NodeRegisterRequest`（简单注册，无 SSH），但无端点使用它 |
| Deregister 不删除节点 | ⚠️ P2 | 第 72-76 行：仅设 Status=Offline，不从 DB 删除。节点永远留在列表中 |
| AuthToken 生成方式 | ✅ | 第 31 行 `Convert.ToBase64String(Guid.NewGuid().ToByteArray())`，22 字符随机 token |
| 注册不验证 AuthToken 唯一性 | ⚠️ P3 | WorkerNode 无 AuthToken 唯一索引，理论上可能碰撞（概率极低） |

### 2. 节点状态检测与心跳

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Heartbeat 端点 | ✅ | `[Authorize]` + `EnableRateLimiting(Query)` |
| Heartbeat 不验证 AuthToken | ❌ **P0** | 第 82-94 行：仅 `[Authorize]`（任何登录用户），不验证请求者是否是该节点的 Agent。**任何登录用户可伪造任意节点的心跳数据**（CPU/内存/容器数等） |
| FleetHealthCheckService | ✅ | 30 秒检查一次，120 秒无心跳标记 Offline |
| 心跳数据无校验 | ❌ **P1** | HeartbeatRequest 的 CpuLoad/MemoryLoad 无范围校验，可提交负数或 >1 的值 |
| 心跳不更新 UsedPorts/TotalPorts | ⚠️ P2 | HeartbeatRequest 有 UsedPorts 但无 TotalPorts，PortCapacityTracker 永远无数据 |

### 3. 模板注册（Docker 镜像 + VM 磁盘）

| 检查项 | 状态 | 发现 |
|--------|------|------|
| RegisterDocker | ✅ | 创建 ImageTemplate(ImageType=Docker)，不实际拉取镜像 |
| Docker 注册不验证镜像存在 | ❌ **P1** | `ImageTemplateController.cs:148-162` 仅创建 DB 记录，不验证 RegistryUrl 是否可达或镜像是否存在。运行时容器创建才会发现镜像不存在 |
| Docker 注册不拉取镜像 | ❌ **P1** | ContainerOrchestrator.PullImageFromRegistryAsync 存在但**从未被调用**。Docker 镜像注册后不会预拉取到任何节点 |
| ImportFromLocal | ✅ | 复制文件 + SHA256 + 注册模板 |
| LocalImportRequest 无路径校验 | ❌ **P0** | `ImageTemplateController.cs:120` `request.LocalPath` 无任何校验。可提交 `../../etc/passwd` 等路径，虽然 `LocalImageImporter` 只读取不返回内容，但可探测服务器文件是否存在（FileNotFoundException vs InvalidOperationException） |
| Upload（直接上传磁盘） | ✅ | 验证扩展名 + 大小 + 保存 + virsh pool-refresh |
| Upload 不计算 SHA256 | ⚠️ P2 | `ImageStorage.SaveImageAsync` 不计算 hash，ImageTemplate.ImageHash 为 null。无法做完整性校验 |
| Upload 默认 OSType=Windows | ⚠️ P2 | `ImageStorage.cs:130` 硬编码 `OSType = OSType.Windows`，Linux 镜像上传后也标记为 Windows |

### 4. VM 上传/格式转换/注册全链路

| 检查项 | 状态 | 发现 |
|--------|------|------|
| UploadArchive 端点 | ✅ | 验证扩展名 + 保存临时文件 + 调用 ArchiveExtractor |
| ArchiveExtractor .zip/.tar.gz/.tar.xz | ✅ | 解压 + 格式检测 + qemu-img 转换 + SHA256 + 注册 |
| ArchiveExtractor .ova 未处理 | ❌ **P1** | 直接上传 .ova 走 `_ => false`；zip 内 .ova 检测到但无转换逻辑，会抛 FileNotFoundException |
| ArchiveExtractor 清理临时文件 | ✅ | `finally { Directory.Delete(tempDir, true) }` |
| qemu-img/virsh 命令注入 | ✅ | 路径由服务器生成（GUID），非用户输入 |
| ArchiveExtractor 不处理嵌套目录 | ⚠️ P2 | `Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories)` 搜索所有子目录，但只取第一个匹配的 vmdk/qcow2。嵌套目录中可能有多个镜像，取哪个不确定 |
| 格式转换后不删除原始 vmdk | ⚠️ P3 | 转换为 qcow2 后原始 vmdk 仍留在磁盘上，占用空间 |

### 5. 镜像分发服务

| 检查项 | 状态 | 发现 |
|--------|------|------|
| ImageDistributionService | ❌ **P0** | `DistributeToCapableNodesAsync` 创建 DeploymentTarget，但**从未被任何代码调用**。镜像注册后不会自动分发到节点 |
| Payload 序列化 localPath | ⚠️ P2 | 第 25 行 `localPath = template.LocalFilePath`，泄露服务器本地路径到 DeploymentTarget.Payload |
| 仅分发给 KVM 节点 | ✅ | 第 16 行 `n.Capabilities & NodeCapability.Kvm`，Docker 镜像不分发 |
| 无分发状态跟踪 | ⚠️ P2 | 创建 DeploymentTarget 后不跟踪分发结果 |

### 6. 节点智能调度

| 检查项 | 状态 | 发现 |
|--------|------|------|
| WeightedScheduler | ✅ | 评分公式合理：CPU(1000) + 内存(500) + 容器容量(200) + VM容量(200) |
| 最低分阈值 200 | ⚠️ P2 | 第 25 行 `best.Score < 200` 返回 null。所有节点 CPU>80% 且 内存>80% 且 容器>90% 时拒绝调度，但无排队提示 |
| FleetManager | ❌ **P0** | 注册到 DI 但**从未被任何 Controller/Repository/Service 调用**。整个调度系统是死代码 |
| QueueManager | ❌ **P0** | 构造函数启动 `ProcessQueueAsync` 无限循环，但 `EnqueueAsync` **从未被调用**。队列永远为空，循环空转等待信号量 |
| AutoTransferService | ❌ **P1** | 注册到 DI 但**从未被调用** |
| PortCapacityTracker | ❌ **P1** | 注册到 DI 但**从未被调用**（UpdateCapacity 从未触发），所有端口容量数据为空 |
| Docker 容器不经过调度 | ❌ **P0** | `GameInstanceRepository.CreateContainer` 直接调 `IContainerManager.CreateContainerAsync`，不经过 FleetManager/WeightedScheduler。**所有 Docker 容器都在主服务器创建，不分配到远程节点** |
| VM 容器不经过调度 | ❌ **P0** | `GameController.CreateContainer` 第 1246-1260 行创建 DeploymentTarget(TargetNodeId=Guid.Empty)，不调 FleetManager.TryScheduleAsync。TargetNodeId 为空 GUID，Agent 无法处理 |

### 7. 比赛中多人启动容器全链路

| 检查项 | 状态 | 发现 |
|--------|------|------|
| Docker 容器创建流程 | ✅ | GetInstance → 检查限制 → CreateContainer → Docker API |
| 容器数量限制 | ✅ | `game.ContainerCountLimit` + AutoDestroyOnLimitReached |
| 容器操作频率限制 | ✅ | `IsContainerOperationTooFrequent` |
| Flag 注入容器 | ✅ | `ContainerConfig.Flag = gameInstance.FlagContext?.Flag` |
| 容器生命周期 | ✅ | Create → Running → Destroy，有 ExpectStopAt |
| Docker 容器始终在本地创建 | ❌ **P0** | `IContainerManager` 是 `DockerManager`，连接本地 Docker daemon。**无远程节点 Docker 创建能力** |
| VM 创建后无状态同步 | ❌ **P0** | `GameController.cs:1232-1263` 创建 VmInstance(Status=Creating) + DeploymentTarget，但无后台服务轮询 VM 状态。VmInstance 永远停留在 Creating 状态 |
| VM 创建不等待完成 | ❌ **P1** | 立即返回 `{ status: "Creating" }`，前端无法知道 VM 何时就绪 |
| VM 无销毁入口 | ❌ **P1** | 无 API 端点销毁 VM 实例。VmInstance 永远不会被标记为 Destroyed |
| 多人并发创建容器 | ✅ | pg_advisory_lock 防止同一 Participation 的竞态 |
| 容器创建失败不清理 GameInstance | ⚠️ P2 | `CreateContainer` 返回 null 时 GameInstance 仍存在（无 Container），下次请求会重新尝试创建 |

### 发现汇总（本轮新增）

#### P0 — 导致功能完全不可用

| 编号 | 文件:行号 | 描述 |
|------|-----------|------|
| NP0-1 | `NodesController.cs:82-94` | Heartbeat 端点仅 `[Authorize]`，不验证请求者是否是该节点的 Agent。任何登录用户可伪造任意节点的心跳数据 |
| NP0-2 | `ImageTemplateController.cs:120` | LocalImportRequest.LocalPath 无路径校验，可探测服务器任意文件是否存在 |
| NP0-3 | `ImageDistributionService.cs:14-30` | 镜像分发服务从未被调用，注册后不会自动分发到节点 |
| NP0-4 | FleetManager/WeightedScheduler/QueueManager | 整个调度系统注册到 DI 但从未被业务代码调用，是死代码 |
| NP0-5 | `GameInstanceRepository.cs:167` | Docker 容器直接调本地 DockerManager，不经过节点调度，所有容器在主服务器创建 |
| NP0-6 | `GameController.cs:1248` | VM DeploymentTarget.TargetNodeId=Guid.Empty，不经过调度，Agent 无法处理 |
| NP0-7 | `GameController.cs:1232-1263` | VM 创建后无后台服务同步状态，VmInstance 永远停留在 Creating |

#### P1 — 功能不可用或数据错误

| 编号 | 文件:行号 | 描述 |
|------|-----------|------|
| NP1-1 | `NodeDeployService.cs:109` | sshpass 在 Windows 不可用，节点一键部署功能在 Windows 上完全无法使用 |
| NP1-2 | `NodesController.cs:183-189` | HeartbeatRequest 无数据范围校验，可提交 CpuLoad=-1 或 999 |
| NP1-3 | `ImageTemplateController.cs:148-162` | Docker 注册不验证镜像是否存在，运行时才发现 |
| NP1-4 | ContainerOrchestrator | PullImageFromRegistryAsync 从未被调用，Docker 镜像不会预拉取 |
| NP1-5 | AutoTransferService/PortCapacityTracker | 注册到 DI 但从未被调用 |
| NP1-6 | VM 创建 | 无销毁入口，VmInstance 永远不会被标记为 Destroyed |
| NP1-7 | VM 创建 | 不等待完成，前端无法知道 VM 何时就绪 |

#### P2 — 可用但有缺陷

| 编号 | 描述 |
|------|------|
| NP2-1 | NodeDeployService SSH 失败不回滚已创建的节点记录 |
| NP2-2 | Deregister 不删除节点，仅设 Offline |
| NP2-3 | HeartbeatRequest 无 TotalPorts，PortCapacityTracker 永远无数据 |
| NP2-4 | ImageStorage.SaveImageAsync 不计算 SHA256 |
| NP2-5 | ImageStorage 默认 OSType=Windows |
| NP2-6 | ArchiveExtractor 嵌套目录中多个镜像时取哪个不确定 |
| NP2-7 | ImageDistributionService Payload 泄露服务器路径 |
| NP2-8 | 容器创建失败不清理 GameInstance |

#### P3 — 代码卫生

| 编号 | 描述 |
|------|------|
| NP3-1 | NodeRegisterRequest 死代码 |
| NP3-2 | WorkerNode.AuthToken 无唯一索引 |
| NP3-3 | ArchiveExtractor 转换后不删除原始 vmdk |
