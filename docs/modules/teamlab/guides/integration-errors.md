# TeamLab 对接：错误码与恢复处理

完整调用顺序见 [对接入口](integration.md)，认证与幂等要求见 [调用约定](integration-authentication.md)。本文说明实现契约，现场验收范围见 [当前状态](../../../current-state.md)。

## 5. 错误响应

失败响应使用 `application/problem+json`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `type` | string / null | 问题类型 URI |
| `title` | string / null | 错误标题 |
| `status` | int / null | HTTP 状态码 |
| `detail` | string / null | 错误详情 |
| `instance` | string / null | 请求资源 |
| `code` | string | 稳定错误码，客户端据此选择处理动作 |
| `traceId` | string | 服务端请求追踪 ID |

| HTTP 状态 | 客户端处理 |
| --- | --- |
| `400` | 修正请求字段、查询参数或请求头 |
| `401` | 更新或重新签发 Token |
| `403` | 补充 API Scope 或 Runtime Grant |
| `404` | 更新资源 ID，或确认 Token 对该资源具有可见权限 |
| `409` | 读取最新运行状态或修订号，再生成下一次请求 |
| `422` | 根据 `code` 和 `detail` 修正拓扑、模板或能力参数 |
| `429` | 按 `Retry-After` 延迟请求；`RateLimit-*` 给出当前窗口信息 |
| `503` | 保留原 `Idempotency-Key`，延迟后重发原请求 |

常用错误码：

| `code` | 含义 | 处理动作 |
| --- | --- | --- |
| `topology_revision_conflict` | 场景草稿 revision 已变化 | 读取最新草稿后重新提交 |
| `topology_invalid` | 场景校验失败 | 按错误明细修正资产或网络 |
| `image_template_unavailable` | 镜像不存在、不可见或未就绪 | 查询镜像状态并完成导入或预热 |
| `address_pool_exhausted` | 地址池没有可分配网段 | 扩大地址池或释放已销毁环境占用 |
| `capability_unavailable` | 当前节点缺少所需能力 | 调整模板能力要求或节点配置 |
| `external_reference_conflict` | 业务编号已关联另一创建请求 | 查询原 operation 和 runtime |
| `runtime_plan_revision_conflict` | 资产计划已被其他请求更新 | 读取新 `planRevision` 后重新计算变更 |
| `runtime_operation_in_progress` | 运行环境正在执行互斥操作 | 等待当前 operation 进入终态 |
| `runtime_not_ready` | 当前运行状态不接受该操作 | 查询 `/status` 后选择对应操作 |
| `resume_blocked` | 原节点暂时无法恢复环境 | 查看运行事件和节点状态 |
| `runtime_grant_permission_invalid` | 授权中包含未知权限名 | 改用授权表中的权限名称 |
| `idempotency_conflict` | 同一个 key 对应了不同请求体 | 为新业务动作生成新的 key |
