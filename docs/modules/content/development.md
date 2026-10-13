# Content 开发地图

先读 [模块入口](README.md)。Content 的资产所有权与运行节点缓存分开；本页帮助找到实现，不把架构目标目录当现有目录。

## 链路与真实入口

| 要修改的行为 | 源码入口 | 稳定说明 |
| --- | --- | --- |
| Docker/VM 导入 | [OpenImagesController](../../../src/GZCTF/Modules/Content/Api/OpenImagesController.cs)、Application/ImageImportApplicationService.cs、Domain/ImageImportJob.cs；Infrastructure/ImageImportOperationHandler.cs 调用 DockerImageImportExecutor 或 VmQcow2ImageImportExecutor | [镜像生命周期](reference/image-lifecycle.md) |
| 目录、权限与引用删除 | [IImageTemplateCatalog](../../../src/GZCTF/Modules/Content/Application/IImageTemplateCatalog.cs)、ImageTemplateReferenceService；[公开引用接口](../../../src/GZCTF/Modules/Content/Contracts/IImageTemplateReferenceProvider.cs)；Infrastructure/EfImageTemplateCatalog.cs | [引用与删除](reference/image-lifecycle.md) |
| 来源迁移与认证 | Api/ImageSourceMigrationController.cs、Infrastructure/ImageSourceMigrationOperationHandler.cs、ImageTemplateCertificationOperationHandler.cs、VmImageCertificationProbeService.cs | [模板与缓存状态](reference/image-lifecycle.md) |
| 模板 OS/远程配置投影 | [IImageRuntimeAccessQuery](../../../src/GZCTF/Modules/Content/Application/IImageRuntimeAccessQuery.cs)、Infrastructure/EfImageRuntimeAccessQuery.cs；Contracts/ImageRuntimeAccessSummary.cs | [TeamLab 运行事实](../teamlab/reference/runtime-capabilities.md) |
| 启动配置与 OCI 制品 | Application/BootstrapProfileApplicationService.cs、Infrastructure/BootstrapProfileOperationHandler.cs、BootstrapProfileDistributionService.cs、OciArtifactRegistryClient.cs | [模块边界](../../architecture/module-boundary-map.md) |
| 附件上传与复用 | [OpenAssetsController](../../../src/GZCTF/Modules/Content/Api/OpenAssetsController.cs) → [AssetApplicationService](../../../src/GZCTF/Modules/Content/Application/AssetApplicationService.cs) → IBlobRepository | [附件资产](reference/attachments.md) |
| 选中节点准备及引用回收 | [ImageDistributionService](../../../src/GZCTF/Services/Fleet/ImageDistributionService.cs)、ImageDistributionCoordinator/Worker/ReconcileService；Agent/Services/AgentImageStorageBudget.cs、AgentImageDownloadWriter.cs、ImageTransferSingleFlight.cs | [节点缓存生命周期](reference/image-lifecycle.md) |

补充定位：模板与分发记录仍在 `src/GZCTF/Models/Data/ImageTemplate.cs` 和 `ImageDistributionRecord.cs`；Content 的 EF 配置在 `Modules/Content/Infrastructure/Persistence/`。Registry 适配有 `Services/DockerImageRegistryService.cs`，VM 文件存储有 `Services/Fleet/VmImageRegistryService.cs`、`VmArtifactStore.cs`。前端管理目录在 `ClientApp/src/vnext/features/admin/images/`，feature adapter 在 `features/admin/api/imageTemplateAdminApi.ts`。

## 跨模块边界

镜像全局主副本属于 Content，CTF/Exercise/Training/TeamLab 只拥有绑定或引用。新增消费者要提供 IImageTemplateReferenceProvider，删除检查不能直接从 Content 穿透业务表，也不能用 cascade 代替 asset_in_use。VM 已运行的 backing 文件和节点需求也要保护，失去某个 runtime 引用不能回收其他消费者的缓存。

远程配置秘密只用于受控服务端连接；query 向业务模块返回配置是否存在和能力信息，不提供用户名/凭据。Flag 和实例机密与模板制作分离；不得把管理员预览值写入正式实例。生成 API 类型不进入领域。

## 验证选择

| 改动 | 现有测试 |
| --- | --- |
| 来源、认证、远程能力 | `src/GZCTF.Test/UnitTests/Content/VmSourceMigrationTests.cs`、`ImageTemplateCertificationTests.cs`、`ImageRuntimeAccessQueryTests.cs`、`DockerImageReferencePolicyTests.cs` |
| 目录与引用 | `UnitTests/Models/ImageTemplateOwnershipTests.cs`、`ImageTemplateCatalogDeletionTests.cs`、`UnitTests/TeamLab/TeamLabImageTemplateReferenceTests.cs` |
| 导入与分发 | `UnitTests/Services/ImageImportStagingTests.cs`、`ImageImportDemandTests.cs`、`UnitTests/Fleet/ImageSelectedNodeDemandTests.cs`、`UnitTests/Runtime/ImageTransferSingleFlightLifecycleTests.cs` |
| PostgreSQL 删除/恢复 | `src/GZCTF.Integration.Test/Tests/Database/ImageTemplateDeletionQueryPostgresTests.cs`、`ImageRetirementPostgresTests.cs`、`ImagePreparationReclamationPostgresTests.cs`、`ImageSourceMigrationPostgresTests.cs` |
| 外部 HTTP | `Tests/Api/OpenImageApiTests.cs` 与 `ImageTemplateRemoteAccessTests.cs` |

涉及数据模型时新迁移、模型快照和读写路径一起变更；历史迁移不可改写。真实 Registry、冷缓存、磁盘容量与 backing 链验证独立记录；[历史回收发布](../../records/2026-10-09-image-lifecycle-second-rollout.md) 不证明自动 GC 已实现。现场入口见 [Registry 部署](../../operations/registry-deployment.md) 与 [节点部署](../../operations/node-deployment.md)，当前缺口见 [当前状态](../../current-state.md)。
