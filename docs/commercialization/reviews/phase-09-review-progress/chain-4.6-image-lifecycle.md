# 链路 4.6 镜像导入/认证/分发/删除 审查结果

## 审查范围与覆盖

### 已读取文件清单

API 与应用服务：
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Api/OpenImagesController.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/ImageImportApplicationService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/ImageTemplateCertificationService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/ImageTemplateReferenceService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/BootstrapProfileApplicationService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/DockerImageReferencePolicy.cs`

Infrastructure：
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/VmQcow2ImageImportExecutor.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/VmImageCertificationProbeService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/DockerImageImportExecutor.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/OciArtifactRegistryClient.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/FileImageImportStagingStore.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageImportOperationHandler.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageImportStagingReconcileService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageTemplateArtifactCleaner.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageTemplateCertificationOperationHandler.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageTemplateDeletionReconcileService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/PreparedImageConformancePackageFactory.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/EfImageImportSubmissionStore.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/EfImageTemplateCatalog.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/BootstrapProfileArtifactService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/BootstrapProfileDistributionService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/BootstrapProfileOperationHandler.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageApiTokenResourceGrantPolicy.cs`

Domain：
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Domain/ImageImportJob.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Domain/ImageTemplateCapabilityCertification.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Domain/VmPreparedArtifact.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Domain/BootstrapProfile.cs`

主站 Fleet 与 Agent：
- `D:/newgz/newGZCTF-main/src/GZCTF/Services/Fleet/ImageDistributionService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Services/DockerImageRegistryService.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Models/Data/ImageTemplate.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF/Models/Data/ImageDistributionRecord.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/Vm/AgentOciArtifactUploader.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF.Agent/Services/ImageTransferSingleFlight.cs`
- `D:/newgz/newGZCTF-main/src/GZCTF.Agent/Controllers/ImageController.cs`

### 已验证的不变量清单

- #10 Managed 能力只来源于当前 digest 的受控认证（**被破坏**，见 Finding 4.6.1）
- #11 Opaque 模板不被在线改造，不伪造 guest 能力（已验证）
- #15 destroy 完成意味着所有节点和存储上的运行资源都已清理（已验证）
- #16 失败必须保留 correlation、阶段、节点、资产和稳定错误码（已验证）

## Findings 汇总

### Finding 4.6.1: External-evidence 认证可向 Bootstrap Profile 校验注入未受控能力

- 严重性: P1
- 精确文件和行号:
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs#L75-L100`（bootstrap 资产用 `CertifiedCapabilities` 校验所需能力）
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs#L104-L109`（`CertifiedCapabilities` 把所有 `Status==Certified` 的认证全部合并）
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageTemplateCertificationOperationHandler.cs#L67`（external-evidence 时 `probe` 为 null，`Status` 仍写为 `Certified`）
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageTemplateCertificationOperationHandler.cs#L82-L87`（external-evidence 路径 `PreparationContractVersion`/`GuestProtocolVersion` 为 null）
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Application/ImageTemplateCertificationService.cs#L137-L155`（`NormalizeCapabilities` 仅做 OS 类型与白名单检查，external-evidence 路径下用户可任意声明能力）
- 所属端到端链路: 4.6 Image Lifecycle
- 触发条件:
  1. 平台已存在一个 Managed VM 模板（已通过 controlled-probe 认证，具备 baseline 能力，如 `linux.cloud-init.nocloud.v1`/`network.virtio.v1`/`guest.supervisor.v1`/`image.vm.prepared.v1`）。
  2. 同一用户（拥有 `ImagesWrite` scope）针对该模板提交一次 `ProbeKind=external-evidence` 的认证请求，`Capabilities` 中包含 baseline 之外的任意白名单能力（例如 `windows.powershell.v1`、`bootstrap.firstboot.v1`），`EvidenceDigest` 只需是 64 位 hex 字符串，无任何实质校验。
  3. `ImageTemplateCertificationOperationHandler` 创建认证记录时，`probe` 为 null，`Status = Certified`，`PreparationContractVersion = null`、`GuestProtocolVersion = null`，但 `CapabilitiesJson` 已写入用户声明的能力集合。
  4. 拓扑发布时 `BootstrapProfileCompatibilityService.ValidateReleaseAsync` 走到 bootstrap 资产分支，调用 `CertifiedCapabilities`，该方法仅按 `ImageTemplateId`/`ImageHash`/`Status==Certified` 过滤，把 external-evidence 认证的能力也并入 `certified` 集合。
  5. `manifest.RequiredTemplateCapabilities` 校验通过，即便模板实际从未通过受控 probe 验证过这些额外能力。
- 实际影响:
  - 攻击者可通过 external-evidence 认证绕过平台受控 probe，让 bootstrap profile 发布校验接受模板实际不具备的能力（如 `windows.powershell.v1`、`bootstrap.firstboot.v1`）。
  - 这些能力会驱动运行时实际行为（例如 bootstrap firstboot 步骤、PowerShell 步骤），最终在 Guest 内执行失败或触发未预期路径，导致 runtime 不可用或行为偏离设计契约。
  - 与 VM 资产分支（line 36-74，仅使用 `IsCurrentManagedCertification` 单条认证）形成两套事实，bootstrap 资产分支能力集被人为放大。
- 被破坏的不变量: #10（Managed 能力只来源于当前 digest 的受控认证）
- 根因:
  - `ImageTemplateCertificationOperationHandler` 在 external-evidence 路径仍把记录标为 `Status=Certified`，未与 controlled-probe 区分。
  - `BootstrapProfileCompatibilityService.CertifiedCapabilities` 把所有 `Status==Certified` 认证（含 external-evidence）合并，而 VM 资产分支仅使用 `IsCurrentManagedCertification`。两条校验路径使用了不同的能力集合来源，bootstrap 资产分支未对 probe kind / 协议版本做过滤。
  - 设计意图是“外部证据不能把 Opaque 提升为 Managed”，但实现层面未阻止 external-evidence 为已 Managed 模板追加未受控能力。
- 最小且架构正确的修复方向:
  - 在 `BootstrapProfileCompatibilityService.CertifiedCapabilities` 中仅纳入通过 `IsCurrentManagedCertification` 的认证（或显式过滤 `ProbeKind == "controlled-probe"` 且 `PreparationContractVersion == GuestControlProtocol.PreparationContractVersion` 且 `GuestProtocolVersion == GuestControlProtocol.SchemaVersion` 的认证）。
  - 或者：将 `ImageTemplateCertificationOperationHandler` 中 external-evidence 路径的 `Status` 改为非 `Certified` 的独立状态（如 `Attested`），并在 `CertifiedCapabilities` 与 `IsCurrentManagedCertification` 中统一排除该状态，使 external-evidence 仅作为审计证据存在，不参与能力校验。
- 修复后的验证方式:
  - 构造一个已 Managed 模板，提交 external-evidence 认证声明额外能力，再发布一个要求该额外能力的 bootstrap profile，校验应返回 `bootstrap_profile_incompatible` 409。
  - 单元测试覆盖 `CertifiedCapabilities` 仅返回受控认证能力集合。
  - 回归测试确保正常的受控认证 + bootstrap profile 发布流程仍可通过。

### Finding 4.6.2: VmQcow2 暂存文件未被 Staging Reconciler 保护，长导入可能被误删

- 严重性: P2
- 精确文件和行号:
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/ImageImportStagingReconcileService.cs#L19-L30`（`activePaths` 查询仅过滤 `SourceKind == ImageImportSourceKind.DockerArchive`）
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/FileImageImportStagingStore.cs#L124-L149`（`DeleteUnreferencedAsync` 删除 `_root` 下所有不在 `activePaths` 且超过 1 小时的文件，未区分 VmQcow2）
  - `D:/newgz/newGZCTF-main/src/GZCTF/Modules/Content/Infrastructure/VmQcow2ImageImportExecutor.cs#L39-L61`（导入执行器在 `staging.VerifyAsync` 与 `registry.PushFileAsync` 期间依赖 `job.StagedPath` 仍存在）
- 所属端到端链路: 4.6 Image Lifecycle
- 触发条件:
  1. 用户上传一个较大的 qcow2 镜像（如 60-120GB），`SubmitVmQcow2Async` 完成流式 staging 后，异步 import job 进入 `Pending`/`Running`。
  2. 导入执行器需要将 qcow2 流式推送到 OCI Registry（`OciArtifactRegistryClient.PushFileAsync` 以 32MB 分块上传），网络抖动或 Registry 慢时整体耗时可超过 1 小时。
  3. `ImageImportStagingReconcileService` 每隔 15 分钟运行，`activePaths` 查询的 `where` 子句仅过滤 `SourceKind == ImageImportSourceKind.DockerArchive`，VmQcow2 job 的 `StagedPath` 不会被纳入保护集合。
  4. `FileImageImportStagingStore.DeleteUnreferencedAsync` 删除 `_root` 下所有不在 `activePaths` 且 `File.GetLastWriteTimeUtc(path) <= now - 1h` 的文件，包括正在使用的 VmQcow2 暂存文件。
  5. 后续 `staging.VerifyAsync` 或 `PushFileAsync` 因源文件被删除而失败，import job 进入 terminal failure。
- 实际影响:
  - 大型 qcow2 导入在 1 小时窗口外被后台 reconciler 误删暂存文件，导致 import 永久失败、需要重新上传（120GB 级别文件重传成本极高）。
  - 失败后 staging 已被删除，`ImageImportOperationHandler.OnTerminalFailureAsync` 的 `staging.DeleteAsync` 也无法找到文件，但 job 状态已 terminal，业务影响是用户必须重新发起完整上传。
  - 与设计要求“Registry 或对象存储中断时 operation 保留可恢复状态”冲突——此处是平台自身后台服务主动破坏了可恢复状态。
- 被破坏的不变量: #16（失败必须保留 correlation、阶段、节点、资产和稳定错误码）—暂存文件丢失导致 retry 无法恢复，只能重新上传
- 根因:
  - `ImageImportStagingReconciler.ReconcileAsync` 的 `where` 子句遗漏 `ImageImportSourceKind.VmQcow2`，可能是早期实现仅考虑 Docker archive 场景，后续增加 VmQcow2 时未同步更新 reconciler。
- 最小且架构正确的修复方向:
  - 在 `ImageImportStagingReconcileService.cs#L23` 的 `where` 子句中增加 `|| job.SourceKind == ImageImportSourceKind.VmQcow2`，使 VmQcow2 job 的 `StagedPath` 也被纳入 `activePaths` 保护集合。
  - 或者把过滤条件改为 `job.SourceKind != ImageImportSourceKind.DockerReference`（涵盖所有使用 staging 的 source kind）。
- 修复后的验证方式:
  - 构造一个 VmQcow2 import job 处于 `Running` 状态、`StagedPath` 指向一个真实文件，触发 `ReconcileAsync`，验证文件未被删除。
  - 验证 DockerArchive 路径未被回归。
  - 可选：验证超过 grace period 的孤儿 VmQcow2 文件仍能被清理（即 job 已 terminal 的情况下）。

## 安全边界验证

- qcow2 流式校验: 已确认。`FileImageImportStagingStore.StageAsync` 使用 `IncrementalHash.CreateHash(HashAlgorithmName.SHA256)` 在流式写入文件的同时增量计算 digest（`FileImageImportStagingStore.cs#L49-L75`），不会把整文件读入内存。`OciArtifactRegistryClient.PushFileAsync` 在推送前用 `ComputeSha256Async` 流式复算 digest 并与 `expectedSha256` 比对（`OciArtifactRegistryClient.cs#L70-L73`），并分块（32MB）上传。`VmQcow2ImageImportExecutor.ImportAsync` 在执行前还会调用 `staging.VerifyAsync` 重新流式校验（`VmQcow2ImageImportExecutor.cs#L39`）。
- OCI repo/tag/digest 不可变 + 路径注入: 已确认。repository 全部由 `SHA256.HashData(...)[..24]`、`Guid:N`、`sha256:digest` 等不可变 hash 拼接（`VmQcow2ImageImportExecutor.cs#L41-L47`、`DockerImageImportExecutor.cs#L34-L38`、`BootstrapProfileArtifactService.cs#L124-L136`）。tag 是 digest 或 version 数字字符串。`OciArtifactRegistryClient.NormalizeTag` 显式拒绝 `/` 与空白（`OciArtifactRegistryClient.cs#L199-L205`），`NormalizeDigest` 强制 64 位 hex（`OciArtifactRegistryClient.cs#L207-L214`）。`DockerImageRegistryService.NormalizeRepository` 用正则 `^[a-z0-9]+(?:[._/-][a-z0-9]+)*$` 严格校验（`DockerImageRegistryService.cs#L26-L27, L623-L635`），`NormalizeTag` 用 `^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$`（`DockerImageRegistryService.cs#L29, L805-L812`）。
- 路径穿越防护: 已确认。`FileImageImportStagingStore.ResolveManagedPath` 用 `Path.GetFullPath` + 前缀比对确保 staging 路径不逃逸（`FileImageImportStagingStore.cs#L151-L170`）。`BootstrapProfileArtifactService.ResolveStagedPath` 同样做前缀校验（`BootstrapProfileArtifactService.cs#L150-L161`）。`BootstrapProfileApplicationService.ValidateArtifactPath` 拒绝根路径、空段、`.` 与 `..`（`BootstrapProfileApplicationService.cs#L356-L361`）。Agent 端 `ImageController.DownloadVmImageCoreAsync` 用 `request.TemplateId.Value.ToString()` 或 `request.Hash` 作为 `fileStem`，`NormalizeSha256` 强制 64 位 hex，不存在穿越风险。
- shell/参数注入防护: 已确认。`DockerImageRegistryService.RunDockerAsync` 通过 `ProcessExecution.RunAsync` 以参数数组传递（`DockerImageRegistryService.cs#L830-L849`），不经过 shell。`ConfigureLocalInsecureRegistriesAsync` 的 bash 脚本通过 `ShellQuote` 单引号转义 registry 地址（`DockerImageRegistryService.cs#L559, L867`）。`BuildEnsureRegistryScript` 中的 `port` 已经 `Math.Clamp(port, 1, 65535)` 为 int（`DockerImageRegistryService.cs#L429`）。`NormalizeShellArgument` 仅替换 `\r\n`，不构成 shell 转义，但因 `ProcessExecution.RunAsync` 使用参数数组而非 shell，不构成注入。

## 不变量验证

- #10 Managed 仅来自当前 digest 受控认证: **被破坏**。详见 Finding 4.6.1。`ImageTemplateCertificationOperationHandler` 仅在 `probe is { Success: true }` 时才将 `template.VmRuntimeMode = VmRuntimeMode.Managed`（`ImageTemplateCertificationOperationHandler.cs#L97-L105`），external-evidence 不直接提升 Managed，这一点是正确的。但 `BootstrapProfileCompatibilityService.CertifiedCapabilities` 把 external-evidence 认证的能力也并入 bootstrap 资产校验，违反“Managed 能力只来源于当前 digest 的受控认证”。
- #11 Opaque 不被在线改造: 已确认。`ImageTemplateCertificationOperationHandler` 只在 `probe is { Success: true }` 时更新 `template.VmRuntimeMode = Managed` 与 `PreparedArtifact.Status = Ready`（`ImageTemplateCertificationOperationHandler.cs#L97-L105`），没有把 Opaque 在线改造为 Managed 的路径。`VmImageCertificationProbeService.ProbeAsync` 显式拒绝 Scenario 模板与未 Ready 的 PreparedArtifact（`VmImageCertificationProbeService.cs#L41-L46`），且要求 `template.ImageHash == prepared.ArtifactDigest`，确保认证基于当前 digest。
- #15 destroy 完整清理: 已确认。`ImageTemplateArtifactCleaner.CleanupAsync` 顺序为：先 `distribution.CleanupTemplateForDeletionAsync`（清理节点分发缓存）、再 `dockerRegistry.DeleteManagedImageAsync` 或 `vmRegistry.DeleteArtifactAsync`（清理 Registry 制品）、再 `storage.DeleteImageAsync`（清理对象存储）（`ImageTemplateArtifactCleaner.cs#L15-L27`）。`ImageDistributionService.CleanupRecordAsync` 对 VM 镜像先检查 `HasActiveVmUsingTemplateAsync`，若仍有运行中 VM 则保持 `CleanupPending` 不删除（`ImageDistributionService.cs#L892-L905`），保证运行资源未清理完不会标记完成。`EfImageTemplateCatalog.CompleteDeletionAsync` 在 cleanup 失败时保留 `Deleting` 状态与 `ErrorMessage`，由 `ImageTemplateDeletionReconcileService` 每分钟重试（`EfImageTemplateCatalog.cs#L94-L110`、`ImageTemplateDeletionReconcileService.cs#L13-L43`）。
- #16 失败保留 correlation/错误码: 已确认。`ImageDistributionService.ProcessClaimedAsync` 在失败时写入 `LastErrorCode`、`ErrorCategory`、`Retryable`、`LastCorrelationId = record.Id`、`ErrorMessage`（`ImageDistributionService.cs#L547-L575`），并通过 `AppendImageEvent` 写入 operational event。`ImageFailure` 映射稳定错误码（`ImageDistributionService.cs#L1092-L1110`）。`ImageTemplateCertificationOperationHandler` 在 probe 失败时把 `ErrorCode`/`ErrorDetail` 写入认证记录（`ImageTemplateCertificationOperationHandler.cs#L67-L91`），并 throw `ApiOperationTerminalException` 携带稳定错误码。

## 并发与恢复验证

- 相同 digest 安全复用: 已确认。`OciArtifactRegistryClient.PushFileAsync` 在推送前先 `ExistsAsync` 检查，若已存在则直接返回现有 reference（`OciArtifactRegistryClient.cs#L74`）。`VmQcow2ImageImportExecutor.ImportAsync` 把 tag 设为 digest（`VmQcow2ImageImportExecutor.cs#L47`），相同 digest 自然命中同一 tag。`BootstrapProfileArtifactService.PublishAsync` 同样先 `ExistsAsync`（`BootstrapProfileArtifactService.cs#L105`）。`EfImageImportSubmissionStore.SubmitAsync` 通过 `(ApiTokenId, RouteKey, IdempotencyKey)` 唯一约束与 `RequestHash` 比对实现幂等复用（`EfImageImportSubmissionStore.cs#L48-L58`）。
- 相同模板多引用不重复传输: 已确认。`ImageDistributionService.QueueTemplateOnNodeAsync` 通过 `(ImageTemplateId, WorkerNodeId)` 唯一约束（Postgres 端用 `ON CONFLICT DO NOTHING`，`ImageDistributionService.cs#L359-L373`）确保同一模板在同一节点只有一条记录。`AddReferenceAsync` 用 `(DistributionRecordId, Kind, ResourceId)` 唯一约束（Postgres 端 `ON CONFLICT DO NOTHING`，`ImageDistributionService.cs#L1013-L1023`）追加引用计数，不重复创建记录。`ImageDistributionRecord.Status == Ready && ImageHash 匹配` 时直接复用，不重新触发传输（`ImageDistributionService.cs#L396-L404`）。Agent 端 `ImageTransferSingleFlight` 用 `Lazy<Task>` 对同一 key 合并并发请求（`ImageTransferSingleFlight.cs#L7-L23`），`ImageController.DownloadVmImageCoreAsync` 在文件已存在且 digest 匹配时直接返回（`ImageController.cs#L96-L104`）。
- 分发 claim/reference count/运行中实例保护并发安全: 已确认。`ImageDistributionService.AcquireDistributionLockAsync` 用 `pg_advisory_xact_lock(hashtextextended(...))` 对 `(templateId, nodeId)` 加事务级 advisory lock（`ImageDistributionService.cs#L1042-L1050`）。`QueueTemplateOnNodeAsync` 在事务内加锁后查询/创建记录与引用（`ImageDistributionService.cs#L350-L435`）。`ProcessClaimedAsync` 通过 `record.ClaimOwner == claimOwner` 严格校验 claim 持有者（`ImageDistributionService.cs#L445`）。`QueueCleanup` 在 record 处于 `Pulling` 且 claim 未过期时跳过（`ImageDistributionService.cs#L777-L779`），避免与活动传输冲突。`HasActiveVmUsingTemplateAsync` 检查 `VmInstances` 与 `TeamLabRuntimeAssets`（`ImageDistributionService.cs#L957-L972`），运行中实例存在时保持 `CleanupPending`。
- 模板删除先写意图再清理可恢复: 已确认。`EfImageTemplateCatalog.MarkDeletingAsync` 在 Serializable 事务内先校验引用，再把 `template.Status = Deleting` 持久化（`EfImageTemplateCatalog.cs#L42-L82`）。`CompleteDeletionAsync` 在 `Status == Deleting` 时执行实际清理，失败时保留 `Deleting` 状态与 `ErrorMessage`（`EfImageTemplateCatalog.cs#L84-L110`）。`ImageTemplateDeletionReconcileService` 每分钟扫描 `Status == Deleting` 的模板并重试 `CompleteDeletionAsync`（`ImageTemplateDeletionReconcileService.cs#L13-L43`）。服务重启后从 PostgreSQL 事实恢复，无内存状态依赖。
- 无半删除（Registry/节点缓存/prepared artifact/DB 元数据）: 已确认（除 Finding 4.6.2 中 VmQcow2 staging 文件可能在导入期间被误删这一非删除链路问题外，删除链路本身无半删除）。Registry 删除：`OciArtifactRegistryClient.DeleteAsync` 先 HEAD 获取 manifest digest，再 DELETE manifest，404 视为成功（`OciArtifactRegistryClient.cs#L109-L128`），幂等。节点缓存删除：`ImageDistributionService.CleanupRecordAsync` 调用 `agentClient.DeleteVmImageAsync`/`DeleteDockerImageAsync`，Agent 端 `ImageController.DeleteVmImage` 按 templateId 与 hash 解析所有可能的缓存路径并逐个删除（`ImageController.cs#L176-L191, L400-L411`），幂等。PreparedArtifact 删除：`EfImageTemplateCatalog.CompleteDeletionAsync` 在 cleanup 成功后 `context.VmPreparedArtifacts.Remove(preparedArtifact)` 与 `context.ImageTemplates.Remove(template)` 在同一 `SaveChangesAsync` 内提交（`EfImageTemplateCatalog.cs#L97-L101`），DB 层原子。Registry/节点缓存清理失败时模板不删除，保留 `Deleting` 状态由 reconciler 重试，重试时各清理步骤幂等。

## 已检查但确认不是问题的高风险点

- **`ImageDistributionService.ProcessClaimedAsync` 的 finally 块在 caller token 取消时用 `CancellationToken.None` 保存 claim 释放**（`ImageDistributionService.cs#L576-L584`）：这是正确设计，确保 claim 不会因 caller 取消而泄漏，DB 写入用 `CancellationToken.None` 是有意为之。
- **`ImageTransferSingleFlight.RunAsync` 用 `CancellationToken.None` 执行 operation，仅用 `waiterToken` 等待**（`ImageTransferSingleFlight.cs#L12-L13`）：这是 correct 的 single-flight 设计，operation 继续执行以服务其他并发 waiter，finally 块在 completed 后清理 entry。
- **`OciArtifactRegistryClient` 使用 HTTP 而非 HTTPS**（`OciArtifactRegistryClient.cs#L124, L136, L194`）：内部 Registry 通信，符合 `DockerRegistrySettings.FixedAddress` 的内部定位，且 `DockerImageReferencePolicy` 已禁止外部 registry 使用私有地址。
- **`ImageDistributionService` 静态 `VmArtifactRecoveryGates` 字典**（`ImageDistributionService.cs#L24-L25`）：用于对同一 Registry artifact 的恢复操作加 `SemaphoreSlim` 串行化，key 是 `{registry}/{repository}:{tag}`，不会无限增长（artifact 数量有限），且 `EnsureVmArtifactAvailableAsync` 在 finally 中 `gate.Release()`（`ImageDistributionService.cs#L636-L638`）。
- **`VmImageCertificationProbeService.ProbeAsync` 的 conformance VM 清理**（`VmImageCertificationProbeService.cs#L235-L252`）：finally 块用 `CancellationToken.None` 调用 `DestroyVmAsync`，确保临时 conformance VM 一定被清理，即使 probe 被 cancel。清理失败仅 log error 不 rethrow，符合“best-effort cleanup”设计。
- **`ImageTemplateCertificationOperationHandler` 在 external-evidence 路径不提升 Managed**（`ImageTemplateCertificationOperationHandler.cs#L97-L105`）：`if (probe is { Success: true })` 保证只有 controlled-probe 成功才写 `VmRuntimeMode = Managed`，external-evidence（probe is null）不提升，符合设计。
- **`IsCurrentManagedCertification` 严格校验协议版本**（`BootstrapProfileCompatibilityService.cs#L111-L129`）：external-evidence 认证因 `PreparationContractVersion == null` 与 `GuestProtocolVersion == null` 无法通过，VM 资产分支不会被 external-evidence 污染。
- **`ImageDistributionService.CleanupTemplateForDeletionAsync` 在 `removeOnSuccess: false` 下保留记录**（`ImageDistributionService.cs#L920-L930`）：清理成功后 record 保持 `CleanupPending`，由 `CleanupUnreferencedAsync` 后续清理，避免在 `CompleteDeletionAsync` 失败时丢失清理事实。
- **`DockerImageReferencePolicy.ValidateAsync` 的 SSRF 防护**（`DockerImageReferencePolicy.cs#L83-L118`）：完整覆盖 IPv4/IPv6 私有段、loopback、link-local、CGNAT、ULA、multi-cast，registry 必须是平台内部 registry 或公网 registry。
- **`BootstrapProfileOperationHandler.DeleteAsync` 先写 `Deleting` 再清理**（`BootstrapProfileOperationHandler.cs#L202-L213`）：`profile.Status = BootstrapProfileStatus.Deleting` 持久化后再逐版本清理 distribution 与 Registry，最后写 `Deleted`，失败时保留 `Deleting` 状态。

## 链路覆盖结论

- qcow2 流式校验: 已确认。`FileImageImportStagingStore.StageAsync` 增量 SHA256、`staging.VerifyAsync` 流式复算、`OciArtifactRegistryClient.PushFileAsync` 分块上传前再次流式校验，三重校验且全程不将整文件读入内存。
- OCI 不可变 + 路径注入: 已确认。repository 由 hash 拼接，tag 为 digest 或 version，`NormalizeTag`/`NormalizeRepository`/`NormalizeDigest` 严格校验，无路径注入风险。
- digest 复用与多引用: 已确认。`ExistsAsync` 短路、`(templateId, nodeId)` 唯一约束、`(recordId, kind, resourceId)` 唯一约束、Agent 端 single-flight 与缓存复用，多引用不重复传输。
- 认证 probe 受控不污染: 部分确认。controlled-probe 失败时只写认证记录 `Failed` 并 throw，不修改 `template.VmRuntimeMode`（`ImageTemplateCertificationOperationHandler.cs#L93-L96`），conformance VM 在 finally 必销。但 external-evidence 认证被标为 `Status=Certified` 并被 `CertifiedCapabilities` 纳入，污染 bootstrap 资产校验（Finding 4.6.1）。
- external-evidence 无 Managed 提升路径: 部分确认。`VmRuntimeMode` 提升路径严格限于 controlled-probe 成功，external-evidence 不直接提升 Managed。但 external-evidence 通过 `CertifiedCapabilities` 间接注入未受控能力，违反 #10 不变量（Finding 4.6.1）。
- 分发并发安全: 已确认。advisory lock + claim owner 校验 + reference count 唯一约束 + single-flight，覆盖并发矩阵中“两个队伍同时部署”“同 runtime 重复 Create”“Agent 响应丢失重放”等场景。
- 删除可恢复 + 无半删除: 已确认。先写 `Deleting` 意图、各清理步骤幂等、失败保留 `Deleting` + `ErrorMessage`、reconciler 每分钟重试、DB 元数据与 Registry/节点缓存在同一事务或有序步骤中清理。唯一非删除链路的暂存文件问题见 Finding 4.6.2。
