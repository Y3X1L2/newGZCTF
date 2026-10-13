# Content 开发地图

先读 [模块入口](README.md)。Content 的资产所有权与运行节点缓存分开；本页按现有代码目录定位实现。

## 链路与真实入口

| 要修改的行为 | 源码入口 | 稳定说明 |
| --- | --- | --- |
| Docker/VM 导入 | [镜像接口](../../../src/GZCTF/Modules/Content/Api/OpenImagesController.cs) 提交导入任务；Application/ 校验，Infrastructure/ 导入 worker 与执行器处理制品 | [镜像生命周期](reference/image-lifecycle.md) |
| 目录、权限与引用删除 | [镜像目录](../../../src/GZCTF/Modules/Content/Application/IImageTemplateCatalog.cs) 协调删除，[公开引用接口](../../../src/GZCTF/Modules/Content/Contracts/IImageTemplateReferenceProvider.cs) 收集消费者事实；EF 存储在 Infrastructure/ | [引用与删除](reference/image-lifecycle.md) |
| 来源迁移与认证 | Api/ 中的来源迁移入口提交 operation；Infrastructure/ 中的迁移、认证 handler 与 VM probe 执行验证 | [模板与缓存状态](reference/image-lifecycle.md) |
| 模板 OS/远程配置投影 | [运行配置 query](../../../src/GZCTF/Modules/Content/Application/IImageRuntimeAccessQuery.cs) 提供批量投影；EF 实现在 Infrastructure/，响应定义在 Contracts/ | [TeamLab 运行事实](../teamlab/reference/runtime-capabilities.md) |
| 启动配置与 OCI 制品 | Application/ 管理启动配置，Infrastructure/ 中的 operation handler、分发服务和 OCI Registry 客户端处理制品 | [模块边界](../../architecture/module-boundary-map.md) |
| 附件上传与复用 | [附件接口](../../../src/GZCTF/Modules/Content/Api/OpenAssetsController.cs) → [上传服务](../../../src/GZCTF/Modules/Content/Application/AssetApplicationService.cs) → Blob 存储 | [附件资产](reference/attachments.md) |
| 选中节点准备及引用回收 | [Fleet 分发服务](../../../src/GZCTF/Services/Fleet/ImageDistributionService.cs) 管理节点需求与回收；Agent/Services/ 中的下载和容量服务执行本机保护 | [节点缓存生命周期](reference/image-lifecycle.md) |

补充定位：模板与分发记录仍在 `src/GZCTF/Models/Data/ImageTemplate.cs` 和 `ImageDistributionRecord.cs`；Content 的 EF 配置在 `Modules/Content/Infrastructure/Persistence/`。Registry 适配有 `Services/DockerImageRegistryService.cs`，VM 文件存储有 `Services/Fleet/VmImageRegistryService.cs`、`VmArtifactStore.cs`。前端管理目录在 `ClientApp/src/vnext/features/admin/images/`，feature adapter 在 `features/admin/api/imageTemplateAdminApi.ts`。

## 跨模块边界

镜像全局主副本属于 Content，CTF/Exercise/Training/TeamLab 只拥有绑定或引用。新增消费者通过公开引用接口提供使用事实；删除服务收集各模块引用，有效引用存在时返回 asset_in_use。VM backing 文件、节点需求与每个 runtime 引用分别保护，节点缓存在全部有效需求释放后回收。

远程配置秘密只用于受控服务端连接；query 向业务模块返回配置是否存在和能力信息，秘密保留在服务端。Flag 和实例机密与模板制作分离；正式实例使用独立生成的机密值，预览值用于管理员预览。生成 API 类型用于协议层，领域维护自己的模型。

## 验证选择

| 改动 | 现有测试 |
| --- | --- |
| 来源、认证、远程能力 | `src/GZCTF.Test/UnitTests/Content/VmSourceMigrationTests.cs`、`ImageTemplateCertificationTests.cs`、`ImageRuntimeAccessQueryTests.cs`、`DockerImageReferencePolicyTests.cs` |
| 目录与引用 | `UnitTests/Models/ImageTemplateOwnershipTests.cs`、`ImageTemplateCatalogDeletionTests.cs`、`UnitTests/TeamLab/TeamLabImageTemplateReferenceTests.cs` |
| 导入与分发 | `UnitTests/Services/ImageImportStagingTests.cs`、`ImageImportDemandTests.cs`、`UnitTests/Fleet/ImageSelectedNodeDemandTests.cs`、`UnitTests/Runtime/ImageTransferSingleFlightLifecycleTests.cs` |
| PostgreSQL 删除/恢复 | `src/GZCTF.Integration.Test/Tests/Database/ImageTemplateDeletionQueryPostgresTests.cs`、`ImageRetirementPostgresTests.cs`、`ImagePreparationReclamationPostgresTests.cs`、`ImageSourceMigrationPostgresTests.cs` |
| 外部 HTTP | `Tests/Api/OpenImageApiTests.cs` 与 `ImageTemplateRemoteAccessTests.cs` |

涉及数据模型时新迁移、模型快照和读写路径一起变更；历史迁移保留原记录，通过新迁移前向升级。真实 Registry、冷缓存、磁盘容量与 backing 链验证独立记录；[历史回收发布](../../records/2026-10-09-image-lifecycle-second-rollout.md) 限定为当次手工回收证据，自动 GC 的实现和验收单独记录。现场入口见 [Registry 部署](../../operations/registry-deployment.md) 与 [节点部署](../../operations/node-deployment.md)，当前缺口见 [当前状态](../../current-state.md)。
