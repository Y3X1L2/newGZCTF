# Phase 9 TeamLab 组网独立代码审查 — 链路 4.1 Topology 保存/校验/发布

- 审查范围：审查规范 `docs/commercialization/phase-09-teamlab-networking-independent-code-review.md` 第 4.1 节
- 工作树：`D:/newgz/newGZCTF-main/`（分支 `codex/phase-09-teamlab-networking`）
- 审查方式：只读，实际打开规范中列出的 16 个文件 + 4 个相关辅助文件读取，行号在工作树当前状态下核对
- 输出位置：`docs/commercialization/reviews/phase-09-review-progress/chain-4.1-topology.md`

---

## 审查范围与覆盖

### 已完整阅读的代码文件（17 个）

| # | 文件 | 行数 | 角色 |
| --- | --- | --- | --- |
| 1 | `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs` | 168 | Open API v1 入口，所有变更经 operation framework |
| 2 | `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminTopologyController.cs` | 94 | Admin API，同步直调应用服务 |
| 3 | `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs` | 765 | 核心：Create/Update/Validate/Publish/Plan/List |
| 4 | `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyValidator.cs` | 32 | 顶层校验入口，分发到 structure/dependency/reachability |
| 5 | `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabDependencyGraphValidator.cs` | 78 | 依赖图 DFS 三态循环检测 |
| 6 | `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabReachabilityCompiler.cs` | 31 | 连接方向 → 可达性对集合 |
| 7 | `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabTopologyStructureValidator.cs` | 318 | key/CIDR/host offset/多网卡/默认路由/connection fail-closed |
| 8 | `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyV2Compiler.cs` | 53 | V2 → 执行模型，注入 implicit switch |
| 9 | `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyV1Normalizer.cs` | 102 | V1 → 执行模型，强制 Bidirectional + legacy DAG |
| 10 | `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs` | 116 | 发布：幂等检查 + canonical JSON + ContentHash + Version 分配 |
| 11 | `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseCodec.cs` | 310 | Normalize/Encode/Decode/ComputeContentHash（单一执行模型入口） |
| 12 | `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyExecutionModel.cs` | 94 | 统一执行模型 record 集合 |
| 13 | `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyContracts.cs` | 147 | V1 + 公共 contracts |
| 14 | `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyV2Contracts.cs` | 60 | V2-only contracts（Infrastructure/Dependency/Observation） |
| 15 | `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopology.cs` | 89 | EF entity：Topology/Network/Asset/Interface/Connection |
| 16 | `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopologyPrimitives.cs` | 13 | AssetKind/HealthCheckKind 枚举 |
| 17 | `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopologyRelease.cs` | 16 | EF entity：Release（不可变快照行） |

### 辅助阅读的关联文件（4 个）

| # | 文件 | 行数 | 用途 |
| --- | --- | --- | --- |
| 18 | `src/GZCTF/Modules/TeamLab/Domain/TeamLabInfrastructurePrimitives.cs` | 28 | ConnectionDirection/DependencyCondition/ObservationMode/InfrastructureKind |
| 19 | `src/GZCTF/Modules/TeamLab/Domain/TeamLabReleaseAssetArtifact.cs` | 38 | Scenario baked artifact 的 ArtifactDigest 固定 |
| 20 | `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabTopologyEntityConfigurations.cs` | 167 | EF 唯一索引：Release (TopologyId, Version)/(TopologyId, SourceRevision, ContentHash)/ApiOperationId |
| 21 | `src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs` | 298 | Plan hash 确定性验证 |
| 22 | `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationApplicationService.cs` | 242 | Open API operation framework subject 解析 |
| 23 | `src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs` | 171 | Bootstrap profile 兼容性校验 |

### 已验证的不变量与检查项

- schema v1/v2 单一执行模型入口：`TeamLabReleaseCodec.DecodeExecution`（L44-L53）统一分发到 V1 Normalizer / V2 Compiler，下游全部使用 `TeamLabExecutionTopology`。
- V1 normalizer 边界：V1 schema 拒绝 v2 字段；Direction 强制 Bidirectional；ViaNodeKey 强制 null；legacy DAG 按 DisplayOrder 分组（无环 by construction）。
- 发布快照不可变（canonical JSON 本体）：`TeamLabTopologyRelease` 无 update 路径；三个唯一索引 + ApiOperationId 去重；`DeleteAsync` 在存在 release 时拒绝删除 topology。
- Bootstrap Profile 引用固定：(ProfileId, Version) 在 `TeamLabBootstrapReferenceModel` 与 canonical JSON 中显式携带。
- Scenario artifact 引用固定：baked asset 通过 `TeamLabReleaseAssetArtifact.ArtifactDigest` 固定。
- 确定性 plan hash：`TeamLabAssetPlanner.Build` L96-L105 纯 SHA256 + `sha256:` 前缀，输入按 ordinal 排序。
- 失败关闭校验：entry network 恰好 1 个、primary interface 恰好 1 个、CIDR RFC1918 + 无重叠、host offset 保留位、connection router attached 校验、依赖图 DFS 循环检测、schema 版本非 1 非 2 fail closed。

---

## Findings 汇总

按严重性排序：2×P2 + 2×P3，无 P0/P1。

### Finding 4.1.1 — Image template 引用按 ID 而非 digest，破坏发布快照不可变性

- **严重性**：P2
- **精确文件与行号**：
  - `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyContracts.cs` L25-L41（`TeamLabTopologyAssetModel` 只携带 `int ImageTemplateId`，无 image hash/digest 字段）
  - `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseCodec.cs` L140-L157（`ToV1` 只写 `ImageTemplateId`）、L176-L197（`ToV2` 只写 `ImageTemplateId`）
  - `src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs` L36-L47（plan asset 仍只含 `ImageTemplateId`）
- **所属端到端链路**：4.1 Topology 保存、校验和发布
- **触发条件**：
  1. 用户发布 topology → release R1，此时 asset A 引用的 ImageTemplate T 的 ImageHash = `sha256:abc...`。
  2. ImageTemplate T 被重新认证或重新上传，ImageHash 变为 `sha256:def...`，Status 仍为 `Ready`。
  3. 用户基于 release R1 创建 runtime；runtime 解析 canonical JSON，按 `ImageTemplateId` 重新加载 ImageTemplate T，拿到当前 ImageHash `sha256:def...`。
- **实际影响**：同一 release R1 在 publish 时刻和 runtime 启动时刻可能使用不同的 image 内容；不可变快照实际可变。两条 runtime（基于同一 release）启动时间不同也会得到不同 image，破坏可重现性。
- **被破坏的不变量**：
  - 审查规范第 4.1 节"模板、Bootstrap Profile 和 Scenario artifact 引用是否固定到 digest/version"。
  - 审查规范第 5 节不变量 10："Managed 能力只来源于当前 digest 的受控认证"——发布快照未捕获 publish 时刻的 digest，runtime 使用的 digest 来自当前 DB 状态而非 release。
  - 审查规范第 4.1 节"发布快照是否完全不可变"。
- **根因**：canonical JSON 序列化时丢弃了 publish 时刻的 `ImageHash`，只保留数据库外键 `ImageTemplateId`。`TeamLabReleaseCodec.Normalize/ToV1/ToV2` 与 `TeamLabExecutionAsset` 均未携带 image digest。非 baked asset（普通 Docker/VM）在 `TeamLabReleaseAssetArtifact` 中也无 `ArtifactDigest` 字段。
- **最小且架构正确的修复方向**：
  1. 在 `TeamLabExecutionAsset` / `TeamLabTopologyAssetModel` 中增加 `ImageDigest` 字段（publish 时刻快照）。
  2. `TeamLabReleaseService.PublishAsync` L41-L49 在 Encode 之前查询每个 asset 引用的 ImageTemplate 的 `ImageHash` 与当前认证版本，写入 definition。
  3. `TeamLabReleaseCodec.ToV1/ToV2` 与 `Normalize` 保留 `ImageDigest` 字段。
  4. runtime 解析 canonical JSON 时优先使用 release 中的 `ImageDigest`，与 ImageTemplate 当前 `ImageHash` 校验，不一致时显式 fail（或按 release 中的 digest 拉取对应 image）。
  5. 与 scenario baked asset 的 `ArtifactDigest` 通路保持一致，避免双轨。
- **修复后的验证方式**：
  - 单元测试：发布 release 后修改 ImageTemplate 的 ImageHash（重新认证），DecodeExecution 应返回原 digest；runtime 启动应拒绝当前 digest 或使用原 digest 拉取镜像。
  - 集成测试：同一 release 在 ImageHash 变更前后启动，应得到字节级一致的容器/VM image。

### Finding 4.1.2 — Scenario overlay 内容 hash 使用 HMAC + 丢失前缀，破坏内容寻址确定性

- **严重性**：P2
- **精确文件与行号**：
  - `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs` L51-L55（augmentation 重新赋值 `contentHash` 时丢失 `sha256:` 前缀）
  - `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs` L91-L111（`ComputeScenarioInputDigest` 使用 `HMACSHA256(configService.GetXorKey())` 对 overlays 含 Secrets 做 HMAC）
- **所属端到端链路**：4.1 Topology 保存、校验和发布
- **触发条件**：
  1. 用户通过 Open API `POST /api/open/v1/teamlab/topologies/{id}/releases` 携带 `ScenarioOverlays` 发布，得到 ContentHash = H1。
  2. 平台执行 XOR key 轮换（`configService.GetXorKey()` 变化），或部署到另一环境。
  3. 用完全相同的 overlays 再次发布同一 topology 同一 revision：ContentHash = H2 ≠ H1。
- **实际影响**：
  - 跨环境 / 跨 key 轮换周期，相同输入产生不同 ContentHash，破坏 `TeamLabTopologyRelease` 的 (TopologyId, SourceRevision, ContentHash) 去重——同一 logical release 被写入两行。
  - ContentHash 不再是纯内容寻址指纹，跨环境对账无法进行。
  - L54-L55 重新赋值后 `contentHash` 形如 `abcdef...`（无 `sha256:` 前缀），与 `TeamLabReleaseCodec.ComputeContentHash` 返回的 `sha256:...` 格式不一致，下游存储/索引/日志混用两种格式。
- **被破坏的不变量**：
  - 审查规范第 4.1 节"相同输入是否产生确定性 hash 和计划"。
  - 审查规范第 5 节不变量 17（间接）："秘密不得进入……API projection"——HMAC 是为避免 secret 直接进 hash 而引入，但实现选择了平台 key 而非秘密自身的指纹，导致确定性丧失。
- **根因**：`ComputeScenarioInputDigest` 试图同时满足"secret 不直接进 hash"与"hash 可重现"两个目标，但选择 HMAC（依赖平台 XOR key）使 hash 依赖运行时配置而非纯输入内容。L54-L55 直接用 `Convert.ToHexStringLower(...)` 覆盖 `contentHash`，丢失了上一行 `ComputeContentHash` 返回的 `sha256:` 前缀。
- **最小且架构正确的修复方向**：
  1. 对 secret 单独计算 `SHA256(secret)`（或带固定 per-release salt 的 hash），将 secret 指纹而非 secret 原值参与内容 hash；指纹本身不可逆，且不依赖平台 key。
  2. 或：将 secret 字段排除在内容 hash 之外，单独持久化 secret 指纹用于运行时校验。
  3. 修正 L54-L55：augmentation 后保留 `sha256:` 前缀，例如 `contentHash = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(...))}"`。
  4. 与 `TeamLabAssetPlanner.Build` L96-L105 / `TeamLabReleaseCodec.ComputeContentHash` L55-L61 的 hash 格式保持一致。
- **修复后的验证方式**：
  - 单元测试：相同 overlays 在两个不同 `GetXorKey()` 返回值下应产生相同 ContentHash。
  - 单元测试：scenario-augmented ContentHash 必须以 `sha256:` 开头。
  - 集成测试：同一 release 跨环境（不同 XOR key）应能通过 (TopologyId, SourceRevision, ContentHash) 命中同一行。

### Finding 4.1.3 — Admin API Publish 绕过 operation framework，version 计数器 read-then-write 竞态

- **严重性**：P3
- **精确文件与行号**：
  - `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminTopologyController.cs` L68-L76（`Publish` 同步直调 `topologies.PublishAsync`，未提交到 operation framework）
  - `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs` L70-L85（`nextVersion = MAX(Version) + 1` 后 `Add + SaveChangesAsync`）
- **所属端到端链路**：4.1 Topology 保存、校验和发布
- **触发条件**（两个 owner + 时序）：
  - Owner A：通过 `POST /api/admin/teamlab/topologies/{id}/releases` 发起 Publish（无 Idempotency-Key，无 operation framework 串行化）。
  - Owner B：同一时刻通过同一 admin 端点发起 Publish（不同 scenario overlays 或相同 overlays 但不同请求）。
  - 时序：
    1. T1：A 执行 `MAX(Version)` 查询，得到 N。
    2. T2：B 执行 `MAX(Version)` 查询，得到 N。
    3. T3：A 计算 nextVersion = N+1，`Add` + `SaveChangesAsync`，插入 (TopologyId, Version=N+1) 成功。
    4. T4：B 计算 nextVersion = N+1，`Add` + `SaveChangesAsync`，违反 `uniq_TeamLabTopologyReleases_TopologyId_Version`，抛 `PostgresException`，EF 包装为 `DbUpdateException`，控制器返回 500。
- **实际影响**：
  - 无数据损坏（唯一索引兜底），但 B 收到 500 而非 409/200，UX 差。
  - 若 B 的 overlays 与 A 不同，B 永远无法成功发布（A 已占用 N+1，B 重试时读到 N+1，可继续），但首次请求被 500 中断，需要客户端重试逻辑。
  - Open API 路径不受影响：`TeamLabRuntimeOperationApplicationService.ResolveResource` L227-L240 对 `TopologyPublish` 返回 `("teamlab-topology", topologyId)`，operation framework 按 subject 串行化。
- **被破坏的不变量**：无直接不变量；属于 API 契约与稳定性问题。
- **根因**：Admin API 仍保留同步直调路径，未走 operation framework；`nextVersion` 是经典的 read-then-write，未使用乐观重试或 DB 序列。
- **最小且架构正确的修复方向**（任选其一）：
  1. 在 `TeamLabReleaseService.PublishAsync` L70-L85 捕获 `DbUpdateException`（Postgres unique violation 23505），重新读取 `MAX(Version)` 并重试插入（限制 1 次重试即可，因为 ContentHash 去重会让重复请求直接返回 existing）。
  2. 或：将 Admin API `Publish` 也改为通过 operation framework 提交（与 Open API 路径一致），统一 subject 串行化。
  3. 不建议引入 DB sequence（与 ContentHash 去重 + ApiOperationId 去重模型不冲突，但增加迁移成本）。
- **修复后的验证方式**：
  - 并发测试：两个 admin 同时 Publish 同一 topology（不同 overlays），都应得到 200/409 而非 500。
  - 单元测试：mock `SaveChangesAsync` 第一次抛 `DbUpdateException`（unique violation），第二次成功，验证重试逻辑。

### Finding 4.1.4 — ValidateAsync 不校验 bootstrap profile 兼容性，API 契约不一致

- **严重性**：P3
- **精确文件与行号**：
  - `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs` L285-L305（`ValidateAsync` 只调用 `validator.Validate` + `ValidateImageTemplatesAsync`，未调用 `bootstrapCompatibility.ValidateReleaseAsync`）
- **所属端到端链路**：4.1 Topology 保存、校验和发布
- **触发条件**：
  1. 用户构造一个 topology，引用一个 Status != Ready 或 Profile.Status != Active 的 Bootstrap Profile Version。
  2. 调用 `POST /api/admin/teamlab/topologies/{id}/validate` → 返回 `Valid=true`。
  3. 调用 `POST /api/admin/teamlab/topologies/{id}/releases` → `TeamLabReleaseService.PublishAsync` L47-L49 调用 `bootstrapCompatibility.ValidateReleaseAsync`，抛 `bootstrap_profile_incompatible`（409/422）。
- **实际影响**：validate 接口的"Valid=true"承诺与 publish 时的实际校验结果不一致，用户无法通过 validate 提前发现问题。OpenAPI 描述（"Validates topology structure, addressing, connectivity, assets, and deployment constraints"）与实现不匹配。
- **被破坏的不变量**：API 契约一致性（无生产数据损坏风险）。
- **根因**：`ValidateAsync` 只复用了结构校验和 image template ready 校验，未复用 `TeamLabReleaseService.PublishAsync` 中的 `bootstrapCompatibility.ValidateReleaseAsync`。两者校验集合不一致。
- **最小且架构正确的修复方向**：
  1. 在 `ValidateAsync` L297 之前增加 `bootstrapCompatibility.ValidateReleaseAsync` 调用（需先将 `definition` 通过 `TeamLabReleaseCodec.Encode + DecodeExecution` 转为执行模型，与 `PublishAsync` L41-L49 保持一致）。
  2. 捕获 `TeamLabApiContractException` 转为 `TeamLabValidationResultModel`（与现有 `ValidateImageTemplatesAsync` 的 catch 模式一致）。
  3. 不要直接在 `ValidateAsync` 中重复 `PublishAsync` 的全部逻辑（避免双轨），可抽出共享的 `ValidateForReleaseAsync(definition, schemaVersion)` 方法供两者调用。
- **修复后的验证方式**：
  - 单元测试：构造引用不兼容 bootstrap profile 的 topology，调用 `ValidateAsync` 应返回 `Valid=false` 并携带 `bootstrap_profile_incompatible` issue。
  - 契约测试：`validate` 与 `publish` 的校验集合应一致（任何 publish 拒绝的输入，validate 也应拒绝）。

---

## 已检查但确认不是问题的高风险点

### 1. schema v1/v2 双版本是否保留两套执行模型 — 确认不是问题

- `TeamLabReleaseCodec.DecodeExecution`（L44-L53）是唯一执行模型入口：V1 → `TeamLabTopologyV1Normalizer.Normalize`，V2 → `TeamLabTopologyV2Compiler.Compile`，两者均产出 `TeamLabExecutionTopology`。
- 下游所有消费者（`TeamLabAssetPlanner.Build`、`BootstrapProfileCompatibilityService.ValidateReleaseAsync`、`TeamLabRuntimeOrchestrator` 等）只依赖 `TeamLabExecutionTopology`，不存在 V1/V2 双轨。
- `TeamLabTopologyV2Compiler.Compile`（L10-L46）复用 `TeamLabTopologyV1Normalizer.ToExecution` 做网络/资产映射，仅在此基础上注入 implicit switch 与 direction 默认值，进一步保证单一模型。

### 2. V1 normalizer 边界是否 fail closed — 确认不是问题

- `TeamLabTopologyStructureValidator` L27-L35：V1 schema 拒绝所有 v2 字段（infrastructure/dependencies/observation/Stateless/Bootstrap/EndpointObservation/ViaNodeKey/Direction），出现即报 `topology_v2_field_in_v1_schema`。
- `TeamLabTopologyV1Normalizer.Normalize` L10-L35：Direction 强制 `Bidirectional`；ViaNodeKey 强制 `null`；Observation 强制 `(true, true, Disabled)`。
- `BuildLegacyDependencies` L77-L101：按 DisplayOrder 分组，每组依赖前一组，无环 by construction；HealthCheckKind 决定 condition。
- `ManagedSwitchKey` L69-L75：长度 >63 时截断 + SHA256 后缀，确定性。

### 3. canonical JSON 本体是否不可变 — 确认不是问题

- `TeamLabTopologyRelease` 是 EF entity，仅有 `Add` 路径（`TeamLabReleaseService.PublishAsync` L84），无 `Update` 路径。
- 三个唯一索引（`TeamLabTopologyEntityConfigurations.cs` L131-L133）：`(TopologyId, Version)`、`(TopologyId, SourceRevision, ContentHash)`、`ApiOperationId` 防止重复发布。
- `TeamLabTopologyApplicationService.DeleteAsync` L254-L258：存在 release 时拒绝删除 topology（`release_immutable` 409）。
- `TeamLabTopologyReleaseEntityConfiguration` L129：`CanonicalJson` 列无 `HasComputed`/触发器，纯插入。
- 注：canonical JSON 不可变性本身没问题，但 asset 引用未固定到 digest（见 Finding 4.1.1），导致"内容可变"而非"行可变"。

### 4. Bootstrap Profile 引用是否固定到 version — 确认不是问题

- `TeamLabBootstrapReferenceModel`（`TeamLabTopologyV2Contracts.cs` L17-L20）显式携带 `Guid ProfileId, int Version`。
- `TeamLabReleaseCodec.ToV2` L185-L188 将 `(ProfileId, Version, Parameters)` 写入 canonical JSON。
- `BootstrapProfileCompatibilityService.ValidateReleaseAsync` L22-L27 按 `(ProfileId, Status=Ready, Profile.Status=Active)` 查询，固定到具体 version。
- 引用按 version 固定，不依赖 current version 指针。

### 5. Scenario baked artifact 引用是否固定到 digest — 确认不是问题

- `TeamLabReleaseAssetArtifact`（Domain 文件）：`SourceImageTemplateId` + `ScenarioImageTemplateId` + `ArtifactDigest` + `EvidenceDigest`。
- `TeamLabScenarioBakeService.BuildIdentity`（参考）：`SHA256($"{releaseHash}:{assetKey}:{source.Id}:{source.ImageHash}:{source.VmNetworkMode}")`，baked identity 包含 `source.ImageHash`。
- baked asset 通过 `ArtifactDigest` 固定到具体制品内容。
- 注：此通路仅覆盖 `BakeAtPublish=true` 的 asset；非 baked asset（普通 Docker/VM）走 ImageTemplateId 通路，见 Finding 4.1.1。

### 6. Plan hash 是否确定性 — 确认不是问题

- `TeamLabAssetPlanner.Build` L96-L105：`hashPayload` 包含 `topologyId, releaseId, networks, assets, shards, crossShardConnections, capabilities`，全部按 ordinal 排序后 `JsonSerializer.SerializeToUtf8Bytes`，再 `SHA256.HashData`，加 `sha256:` 前缀。
- `JsonSerializerOptions` 使用 `JsonSerializerDefaults.Web`（确定性枚举字符串转换）。
- `shards` 排序：L64-L76 先按 `IsEntry` 降序，再按 Node.Name/Id 升序，最后按 shard index 命名 `shard-{index+1}`，确定性。
- `networks`/`assets` 排序：L28-L47 按 Key ordinal 排序。
- 确定性 hash 通过。

### 7. fail-closed 校验项 — 确认不是问题

`TeamLabTopologyStructureValidator` 全部 fail closed：

- L18-L25：schemaVersion 非 1 非 2 直接 fail。
- L46-L47：要求恰好 1 个 entry network（多了/少了都 fail）。
- L49-L70：CIDR 校验（IPv4、prefix 1-30、RFC1918、runtime prefix > pool prefix 且 <=29、无重叠）。
- L218-L220：要求恰好 1 个 primary interface。
- L237-L244：host offset 校验（managedRouter 最小 1，普通 asset 最小 3，上限 hostCapacity-2 保留 WireGuard server + broadcast）。
- L154-L206 `ValidateConnection`：hasAsset XOR hasNode 必须恰好一个；viaNode 必须是 ManagedRouter；viaAsset 必须 RoutingEnabled；router 必须同时 attached 到 FromNetwork 和 ToNetwork。
- L310-L317：Key 正则 `^[a-z][a-z0-9-]{0,62}$`、环境变量 key 正则 `^[A-Z_][A-Z0-9_]{0,63}$`、bootstrap 参数 key 正则 `^[a-z][a-zA-Z0-9_.-]{0,62}$`。
- `TeamLabDependencyGraphValidator`：标准 DFS 三态循环检测，按 ordinal 排序遍历，检查 asset 存在、自引用、重复边。
- `TeamLabReachabilityCompiler.Compile`：FromTo 只单向，Bidirectional 双向；`CompileRouting` 始终双向（路由表语义）。

### 8. Topology Update 的事务与 lease 保护 — 确认不是问题

- `UpdateCoreAsync` L166-L241：`BeginTransactionAsync` 包裹 `ExecuteUpdateAsync` + `ExecuteDeleteAsync` + `RemoveRange` + `SaveChangesAsync` + `AddDefinitionChildren` + `SaveChangesAsync` + `CommitAsync`。
- L186-L204：删除网络前检查 `TeamLabNetworkLeases` 是否引用，引用则抛 `topology_network_in_use` 409。
- L170-L184：revision 冲突时 `ExecuteUpdateAsync` 返回 0，抛 `topology_revision_conflict` 409（乐观锁）。
- L206-L211：connections/interfaces/assets 全量删除后重建（避免孤儿）。
- L213-L231：networks 按 key 复用（保留 lease 引用稳定性）。

### 9. Schema 版本变更是否安全 — 确认不是问题

- `UpdateCoreAsync` L172 允许 `SchemaVersion` 变更（V1 ↔ V2）。
- 变更时全量重建 assets/connections/interfaces（L206-L211），避免 V1/V2 字段混合。
- 旧 release 保留原 schema（`TeamLabTopologyRelease.SchemaVersion` 独立存储），`DecodeExecution` 按 release 自身 schema 解析，不受 topology 当前 schema 影响。
- V1 → V2 升级：V1 字段在 V2 中有等价表达（Direction=Bidirectional, ViaNodeKey=null, Observation=Disabled）。
- V2 → V1 降级：V2 字段（infrastructure/dependencies/observation）在 V1 中被丢弃，`ToV1` L136-L157 显式丢弃，符合 V1 语义。

---

## 链路覆盖结论

针对审查规范第 4.1 节 5 项检查的覆盖结论：

| # | 检查项 | 结论 | 备注 |
| --- | --- | --- | --- |
| 1 | schema v1/v2 是否只有一个执行模型 | ✅ 通过 | `DecodeExecution` 单一入口，下游统一使用 `TeamLabExecutionTopology` |
| 2 | key/CIDR/host offset/多网卡/默认路由/连接方向/依赖 DAG 是否 fail closed | ✅ 通过 | `TeamLabTopologyStructureValidator` + `TeamLabDependencyGraphValidator` + `TeamLabReachabilityCompiler` 全部 fail closed |
| 3 | 发布快照是否完全不可变 | ⚠️ 部分通过 | canonical JSON 行本体不可变；但 asset 引用未固定到 digest（Finding 4.1.1），导致"内容可变" |
| 4 | 模板/Bootstrap Profile/Scenario artifact 引用是否固定到 digest/version | ⚠️ 部分通过 | Bootstrap Profile ✓（ProfileId+Version）；Scenario baked artifact ✓（ArtifactDigest）；Image template ✗（仅 ImageTemplateId，Finding 4.1.1） |
| 5 | 相同输入是否产生确定性 hash 和计划 | ⚠️ 部分通过 | Plan hash ✓（纯 SHA256）；base ContentHash ✓（纯 SHA256）；scenario-augmented ContentHash ✗（HMAC + 丢失 `sha256:` 前缀，Finding 4.1.2） |

### 链路 4.1 整体结论

- **findings 数量**：4 个（2×P2 + 2×P3，无 P0/P1）
- **关键风险**：发布快照的"内容不可变性"在 Image template 通路存在缺口（Finding 4.1.1），scenario overlay 通路存在确定性 hash 缺口（Finding 4.1.2）。两者均不破坏数据库一致性（唯一索引兜底），但破坏"发布即冻结"的商业承诺。
- **次要风险**：Admin API Publish 并发 UX（Finding 4.1.3）与 validate/publish 契约不一致（Finding 4.1.4），均为局部质量问题。
- **是否阻断生产准入**：链路 4.1 单独不引入 P0/P1，不直接导致 BLOCKED；但 Finding 4.1.1 / 4.1.2 属于 P2，需在正式发布前关闭以达成 CONDITIONAL → APPROVED。
- **链路覆盖状态**：Reviewed（所有 5 项检查均实际打开代码验证，无 Not Reviewed 项）。
