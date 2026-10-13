# 异步 operation：受理、查询与恢复

operation 保存一个外部请求的受理与执行记录，不是业务资源本身。大部分异步写请求返回 202，调用方保存 operation ID，以 GET 查询进度；业务返回单资产部署 ticket 时，改查该票据，不能默认所有写响应都是 operation。

## 状态和查询

| ApiOperationStatus | JSON 数值 | 含义 |
| --- | --- | --- |
| Pending | 0 | 已保存，等待领取或延期重试 |
| Running | 1 | worker 持有租约执行 |
| Succeeded | 2 | operation 已完成 |
| Failed | 3 | operation 已失败 |

源码见 [ApiOperation](../../../../src/GZCTF/Modules/Audit/Domain/ApiOperation.cs)。没有 Cancelled 状态；stage 是业务 handler 的稳定阶段，currentProgress/totalProgress 是进度，resourceType/resourceId/result 用于读取结果，DeploymentQueueTicketId 关联原运行票据。成功结果仍须核对业务资源和实际运行条件。

`GET /api/open/v1/operations/{operationId}` 与 `GET /api/open/v1/operations?status=&kind=&limit=50&after=` 需要 operations:read。列表只返回当前 token 创建的 operation；单项只给当前 token 或明确 operation 资源 grant 可读。用户持有另一 token、创建者身份相同或角色较高，都不自动越过 token 授权。不可见返回 404。

limit 为 1–100，默认 50；status 必须是实际枚举，kind 最多 128 字符，after 为不透明时间/UUID 游标。客户端保留服务端 nextCursor，不解析或自造 cursor。格式错误使用稳定 operation_filter_invalid/operation_cursor_invalid。

## 幂等与持久化

Idempotency-Key 为 1–128 位 ASCII 字母、数字、-、_、.，唯一范围是 token、routeKey、key。相同请求摘要返回同一 operation；不同摘要返回 409 idempotency_conflict。断线后保留原 key 和请求，不能通过换 key 盲目创建重复副作用。

worker 从 PostgreSQL 领取可执行记录并续租，按 kind 解析注册 handler。失去租约或停止时不强行完成任务，租约到期后恢复；临时错误按有界重试处理，契约错误进入失败，延期可回 Pending。handler 自己负责业务副作用幂等，operation 租约不能替代资源互斥或部署队列。

附件上传是特例：AssetApplicationService 在一个事务内提交 Blob 和 Succeeded operation，HTTP 返回 201；这不创建待领取的后台上传。新增 handler 或结果 provider 的开发路线见 [开发地图](../development.md)。

## 安全和验收

错误记录 code、脱敏 detail 与关联标识，不记录内部秘密。外部 token、Flag、密码、Cookie、SSH/WireGuard 私钥、Registry 凭据和完整 user-data 不进日志、响应或快照。审计作为追踪事实，不能代替数据库业务实体或 Agent inventory 来恢复运行环境。

公共规则见 [API 标准](../../../architecture/external-api-standard.md)，故障排查见 [观测与审计恢复](../../../operations/observability-audit-recovery.md)。源码测试证明当前处理逻辑；多进程故障恢复和生产运行结论按 [当前状态](../../../current-state.md) 与对应任务记录签收。
