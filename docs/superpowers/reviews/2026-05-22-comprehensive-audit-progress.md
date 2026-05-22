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
