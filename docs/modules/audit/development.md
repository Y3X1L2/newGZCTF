# Audit 开发地图

先读 [模块入口](README.md)，异步状态、查询和恢复语义见 [operation 专题](reference/operations.md)。审计与业务状态分开，恢复运行资源查 Runtime 数据和 Agent inventory。

## 代码路线

| 行为 | 源码入口 |
| --- | --- |
| 创建/复用幂等请求 | [幂等服务](../../../src/GZCTF/Modules/Audit/Application/IdempotencyService.cs) 校验请求；Infrastructure/Persistence/ 中的 operation 配置维护唯一约束 |
| 查询与更新 operation | [操作服务](../../../src/GZCTF/Modules/Audit/Application/ApiOperationService.cs) 调用存储接口；Api/ 为查询入口，Contracts/ 为响应模型 |
| 持久化领取和租约 | [EF 操作存储](../../../src/GZCTF/Modules/Audit/Infrastructure/EfApiOperationStore.cs) 与 Domain/ApiOperation.cs |
| worker 与按 kind 分发 | [后台 worker](../../../src/GZCTF/Modules/Audit/Infrastructure/ApiOperationWorker.cs) 解析模块组合根注册的 handler；Application/ 定义执行与结果接口 |
| 外部请求审计 | [请求审计中间件](../../../src/GZCTF/Modules/Audit/Infrastructure/ExternalApiRequestAuditMiddleware.cs) 调用写入服务，Domain/ 保存请求审计事实 |
| 管理写操作审计 | Infrastructure/ 中的管理变更 filter 与 Application/ 中的请求关联上下文 |
| 结构化运行事件 | [事件查询](../../../src/GZCTF/Modules/Audit/Application/OperationalEventQueryService.cs) 与同目录写入、关联接口；EF 写入实现在 Infrastructure/ |
| 事件代码与 EF 事实 | Domain/ 管理事件代码、实体和聚合；Infrastructure/Persistence/ 管理映射 |

主站管理日志 UI 在 `src/GZCTF/ClientApp/src/vnext/features/admin/logs/`；TeamLab 会话证据由 TeamLabRemoteAuditService 管理，扩展到命令审计时同步更新安全与存储契约。通用事件分类见 [事件契约](../../architecture/event-taxonomy.md)。

## 新增一种异步操作

1. 在业务 Application 中完成权限、资源状态和请求规范化，使用 IdempotencyService。
2. 由业务模块实现 IApiOperationHandler，组合根按 Kind 注册；如果要返回业务结果，同时实现 IApiOperationResultProvider。
3. 通过 ApiOperationService 更新阶段、进度与资源关联。部署操作关联既有 DeploymentQueueTicket，沿用统一部署队列。
4. 后台执行通过持久化 worker 领取任务；重试和恢复基于数据库租约及业务幂等，资源副作用在重入时收敛。
5. 测试同键复用、不同摘要冲突、token 隔离、失去租约与失败脱敏，文档同步对应业务专题。

worker 当前常量为并发批次 8、最大尝试 5、租约 30 秒、10 秒续租、1 秒轮询；这些参数用于 worker 运行配置，对外 SLA 由环境验收与服务约定确定。延期与重试可再次回到 Pending，handler 要保持幂等。未来调整时以源码和压测事实为准。

## 验证与现场边界

| 修改范围 | 测试定位 |
| --- | --- |
| 查询授权与游标 | `src/GZCTF.Test/UnitTests/Services/ApiOperationQueryTests.cs` |
| worker/租约/handler | `UnitTests/Services/ApiOperationWorkerTests.cs`；`src/GZCTF.Integration.Test/Tests/Api/ApiOperationWorkerDirectTests.cs`、`ApiOperationHandlerResolutionTests.cs` |
| 持久化与幂等 | `Tests/Api/ApiOperationPersistenceTests.cs`、`ApiOperationWorkerMinimalTests.cs` |
| 业务集成与请求审计 | `Tests/Api/OpenImageApiTests.cs`、`OpenTeamLabOperationsApiTests.cs`；`Base/ExternalApiAuditAssertions.cs` |

审计日志、请求摘要和 result 都不得保存 token、Flag、Cookie、密码、私钥、Registry 凭据或完整 user-data。测试快照同样脱敏。日志保留与故障排查走 [观测与审计恢复](../../operations/observability-audit-recovery.md)；数据库运维走 [治理手册](../../operations/database-governance-operations.md)。本次文档整理保留既有现场证据；worker 多进程故障和生产恢复的验收状态见 [当前状态](../../current-state.md)。
