# TeamLab：Webhook 与异步操作查询

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

### 11.3 Webhook

Webhook 用于把 TeamLab 事件推送到外部 HTTPS 端点。投递为至少一次，带 HMAC-SHA256 签名。端点必须可公网解析且为 HTTPS。

#### 创建 Webhook

```http
POST /api/open/v1/teamlab/webhooks
Authorization: Bearer <token>
Content-Type: application/json
Idempotency-Key: webhook-001
Scope: teamlab.topologies:write
```

请求体：

```json
{
  "controlScopeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "endpointUrl": "https://your-server.example.com/teamlab-events",
  "eventTypes": ["runtime.ready", "rollout.ready", "runtime.failed"],
  "enabled": true,
  "fromEventId": null
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `controlScopeId` | guid | 是 | 控制范围 |
| `endpointUrl` | string | 是 | HTTPS 公网可达端点，最长 2048 |
| `eventTypes` | string[] | 是 | 订阅事件类型，见下 |
| `enabled` | bool | 否 | 是否启用，默认 true |
| `fromEventId` | long | 否 | 起始事件游标 |

服务端已知事件类型（当前源码白名单）：

```text
deploy, reset, destroy, ready, pause, resume,
cleanup, fabric, bootstrap, network, route, probe,
infrastructure, access, remote-access,
capture, capture-expiry, capture-upload, capture-download,
observation, operation
```

传入白名单之外的值会返回 `422 webhook_event_type_invalid`。若目标环境的测试文档/线上 OpenAPI 出现点分事件名（如 `runtime.ready`），说明该部署包含归一化兼容层；实际对接以该环境 OpenAPI 描述为准。

响应 `202 Accepted` + operation；操作结果包含一次性 `signingSecret`，只返回一次。

#### 列出 Webhook

```http
GET /api/open/v1/teamlab/webhooks?scopeId={scopeId}&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `scopeId` | guid | 是 | 控制范围 |
| `limit` | int | 否 | 1-100 |
| `after` | string | 否 | 游标 |

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "controlScopeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "endpointUrl": "https://your-server.example.com/teamlab-events",
      "eventTypes": ["runtime.ready"],
      "active": true,
      "deliveryCursor": 1024,
      "consecutiveFailures": 0,
      "nextDeliveryAt": null,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "revokedAt": null,
      "recentFailures": []
    }
  ],
  "nextCursor": null
}
```

#### 获取 Webhook

```http
GET /api/open/v1/teamlab/webhooks/{webhookId}
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

#### 撤销 Webhook

```http
DELETE /api/open/v1/teamlab/webhooks/{webhookId}
Authorization: Bearer <token>
Idempotency-Key: webhook-revoke-001
Scope: teamlab.topologies:write
```

停止后续投递，已排队投递不回滚。响应 `202 Accepted` + operation。

#### 重放 Webhook

```http
POST /api/open/v1/teamlab/webhooks/{webhookId}/replay?fromEventId=100
Authorization: Bearer <token>
Idempotency-Key: webhook-replay-001
Scope: teamlab.topologies:write
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `fromEventId` | long | 否 | 起始事件 ID；不传从当前游标重放 |

响应 `202 Accepted` + operation。重放不推进投递游标，不创建新的业务操作。

### 11.4 查询异步操作

找回当前 token 发起的操作：

```http
GET /api/open/v1/operations?status={status}&kind={kind}&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: operations:read
```

`status` 使用数字枚举，`kind` 精确匹配；两者均可选。列表按 `createdAt`、`id` 倒序稳定分页，只返回当前 token 发起的 operation，不因 token 创建者相同或拥有其他资源授权而混入别的 token 历史。

```http
GET /api/open/v1/operations/{operationId}
Authorization: Bearer <token>
Scope: operations:read
```

响应模型：

```json
{
  "id": "019...",
  "kind": "teamlab.runtime.v1",
  "status": 1,
  "resourceType": "teamlab-runtime",
  "resourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "stage": "deploying",
  "currentProgress": 2,
  "totalProgress": 5,
  "errorCode": null,
  "errorDetail": null,
  "result": null,
  "createdAt": 1783641600000,
  "updatedAt": 1783641600000
}
```

`status` 是数字：Pending=0、Running=1、Succeeded=2、Failed=3；轮询直到 2 或 3。依据 [ApiOperationStatus](../../../../src/GZCTF/Modules/Audit/Domain/ApiOperation.cs)，断线恢复使用原 operation 与原幂等键。

除 `operations:read` scope 外，查询操作还需要具备对应操作的 resource grant（`operation:{operationId}` 或通配），或该操作本身属于当前 token 发起。单项读取不因为 token 的创建用户相同而授权。若无权限返回 404。

---

TeamLab 持久化 operation 的当前 kind 为 `teamlab.runtime.v1`；具体命令种类由对应 job 和 stage 区分，依据 [提交服务](../../../../src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationApplicationService.cs)。不要用未定义的 `teamlab.runtime.create` 过滤操作列表。
