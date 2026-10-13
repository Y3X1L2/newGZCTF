# Audit 开发地图

先读 [模块入口](README.md)，异步状态、查询和恢复语义见 [operation 专题](reference/operations.md)。审计与业务状态分开，恢复运行资源查 Runtime 数据和 Agent inventory。

## 代码路线

| 行为 | 源码入口 |
| --- | --- |
| 创建/复用幂等请求 | [IdempotencyService](../../../src/GZCTF/Modules/Audit/Application/IdempotencyService.cs)、ExternalIdempotencyKey.cs；Infrastructure/Persistence/ApiOperationEntityConfiguration.cs 维护唯一约束 |
| 查询与更新 operation | [ApiOperationService](../../../src/GZCTF/Modules/Audit/Application/ApiOperationService.cs)、IApiOperationStore.cs；Api/OperationsController.cs；Contracts/ApiOperationModel.cs |
| 持久化领取和租约 | [EfApiOperationStore](../../../src/GZCTF/Modules/Audit/Infrastructure/EfApiOperationStore.cs)、Domain/ApiOperation.cs |
| worker 与按 kind 分发 | [ApiOperationWorker](../../../src/GZCTF/Modules/Audit/Infrastructure/ApiOperationWorker.cs)、Application/IApiOperationHandler.cs 与 IApiOperationResultProvider.cs；模块组合根注册 keyed handler |
| 外部请求审计 | [ExternalApiRequestAuditMiddleware](../../../src/GZCTF/Modules/Audit/Infrastructure/ExternalApiRequestAuditMiddleware.cs)、ExternalApiAuditWriter.cs；Domain/ExternalApiRequestAudit.cs |
| 管理写操作审计 | Infrastructure/AdminMutationAuditFilter.cs；Application/ExternalApiAuditContext.cs |
| 结构化运行事件 | [OperationalEventQueryService](../../../src/GZCTF/Modules/Audit/Application/OperationalEventQueryService.cs)、IOperationalEventWriter.cs、OperationalCorrelation.cs；Infrastructure/EfOperationalEventWriter.cs |
| 事件代码与 EF 事实 | Domain/OperationalEventCodes.cs、OperationalEvent.cs、OperationalAggregates.cs；Infrastructure/Persistence/ |

主站管理日志 UI 在 `src/GZCTF/ClientApp/src/vnext/features/admin/logs/`；TeamLab 会话证据由 TeamLabRemoteAuditService 管理，不能扩成命令审计而不更新安全和存储契约。通用事件分类见 [事件契约](../../architecture/event-taxonomy.md)。

## 新增一种异步操作

1. 在业务 Application 中完成权限、资源状态和请求规范化，使用 IdempotencyService。
2. 由业务模块实现 IApiOperationHandler，组合根按 Kind 注册；如果要返回业务结果，同时实现 IApiOperationResultProvider。
3. 通过 ApiOperationService 更新阶段、进度与资源关联。部署操作关联既有 DeploymentQueueTicket，禁止在 Audit 建第二队列。
4. 重试和恢复基于数据库租约及业务幂等，不使用不可恢复的裸 Task.Run；资源副作用应能在重入时收敛。
5. 测试同键复用、不同摘要冲突、token 隔离、失去租约与失败脱敏，文档同步对应业务专题。

worker 当前常量为并发批次 8、最大尝试 5、租约 30 秒、10 秒续租、1 秒轮询；这些是实现参数，不是对外 SLA。延期与重试可再次回到 Pending，handler 要保持幂等。未来调整时以源码和压测事实为准。

## 验证与现场边界

| 修改范围 | 测试定位 |
| --- | --- |
| 查询授权与游标 | `src/GZCTF.Test/UnitTests/Services/ApiOperationQueryTests.cs` |
| worker/租约/handler | `UnitTests/Services/ApiOperationWorkerTests.cs`；`src/GZCTF.Integration.Test/Tests/Api/ApiOperationWorkerDirectTests.cs`、`ApiOperationHandlerResolutionTests.cs` |
| 持久化与幂等 | `Tests/Api/ApiOperationPersistenceTests.cs`、`ApiOperationWorkerMinimalTests.cs` |
| 业务集成与请求审计 | `Tests/Api/OpenImageApiTests.cs`、`OpenTeamLabOperationsApiTests.cs`；`Base/ExternalApiAuditAssertions.cs` |

审计日志、请求摘要和 result 都不得保存 token、Flag、Cookie、密码、私钥、Registry 凭据或完整 user-data。测试快照同样脱敏。日志保留与故障排查走 [观测与审计恢复](../../operations/observability-audit-recovery.md)；数据库运维走 [治理手册](../../operations/database-governance-operations.md)。本次文档整理没有重测 worker 多进程故障或生产恢复，验收状态查 [当前状态](../../current-state.md)。
