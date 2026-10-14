# TeamLab：运行时创建、状态、事件与生命周期

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 7. 运行时与访问授权

### 7.0 找回可管理的运行时

```http
GET /api/open/v1/teamlab/runtimes?controlScopeId={scopeId}&externalReference={reference}&status={status}&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.runtimes:read
```

列表只返回当前 token 通过 `teamlab-scope` grant 可读的运行时。同一用户创建的其他 token 不会扩大可见范围。`controlScopeId`、`externalReference`（精确匹配）和数字枚举 `status` 均为可选筛选；`limit` 为 1-100，默认 50。按 `createdAt`、`id` 倒序稳定分页，`nextCursor` 为空时没有下一页。

### 7.1 创建运行时

```http
POST /api/open/v1/teamlab/runtimes
Authorization: Bearer <token>
Idempotency-Key: runtime-001
Scope: teamlab.runtimes:write
```

请求体：

```json
{
  "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "externalReference": "debug-instance-01",
  "constraints": {
    "preferredRegion": "华东",
    "requiredCapabilities": ["docker"]
  },
  "overlays": [
    {
      "assetKey": "plc",
      "secrets": {
        "PLC_PASSWORD": "changeme"
      }
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `releaseId` | guid | 是 | 发布版本 ID |
| `externalReference` | string | 否 | 外部关联编号 |
| `constraints.preferredRegion` | string | 否 | 首选区域 |
| `constraints.requiredCapabilities` | string[] | 否 | 必需能力列表 |
| `overlays[].assetKey` | string | 条件 | 覆盖资产 key |
| `overlays[].secrets` | object | 否 | 资产敏感参数（不写日志） |

响应 `202 Accepted` + operation，轮询得到 `runtimeId`。

### 7.2 获取运行时

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}
Authorization: Bearer <token>
Scope: teamlab.runtimes:read
```

响应 `200 OK`：

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "generation": 1,
  "status": 5,
  "stage": "running",
  "openForAccess": false,
  "shards": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "status": 5,
      "networkKeys": ["net-entry"],
      "assetKeys": ["plc"],
      "failure": null
    }
  ],
  "networks": [
    {
      "key": "net-entry",
      "name": "入口网段",
      "cidr": "10.80.1.0/24",
      "gatewayIp": "10.80.1.1"
    }
  ],
  "assets": [
    {
      "key": "plc",
      "name": "PLC",
      "kind": 0,
      "primaryIp": "10.80.1.10",
      "status": 5,
      "failure": null
    }
  ],
  "createdAt": "2026-08-19T10:00:00+00:00",
  "updatedAt": "2026-08-19T10:00:00+00:00",
  "failure": null,
  "currentOperationId": null,
  "deploymentQueueTicketId": null,
  "queueStatus": null,
  "subStages": null,
  "controlScopeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "releaseVersion": 1,
  "recoveryActions": null
}
```

### 7.3 运行时生命周期命令

| 操作 | 方法 | 所需 scope | 说明 |
| --- | --- | --- | --- |
| 重置 | `POST /runtimes/{runtimeId}/reset` | runtimes:write | 清理旧代并重新部署 |
| 销毁 | `DELETE /runtimes/{runtimeId}` | runtimes:write | 清理全部资源 |
| 暂停 | `POST /runtimes/{runtimeId}/pause` | runtimes:write | 保留身份/网络/磁盘/容量 |
| 恢复 | `POST /runtimes/{runtimeId}/resume` | runtimes:write | 原节点恢复 |

重置请求体：

```json
{
  "overlays": [],
  "releaseId": null
}
```

`releaseId` 为空表示继续用当前版本；也可指定新版本。所有生命周期命令必须携带 `Idempotency-Key`，返回 `202 Accepted` + operation。

由 rollout 管理的运行时，这些直连生命周期接口会返回 `409 runtime_managed_by_rollout`，应改用 rollout API。

### 7.4 列出运行时事件

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/events?after=<cursor>&limit=50&generation=1&stage=running
Authorization: Bearer <token>
Scope: teamlab.runtimes:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `after` | string | 否 | 游标 |
| `limit` | int | 否 | 1-100，默认 50 |
| `generation` | int | 否 | 运行代次过滤 |
| `stage` | string | 否 | 阶段过滤（如 `deploying`、`running`、`protocol`） |

响应 `200 OK`：

```json
{
  "items": [
    {
      "cursor": 1001,
      "generation": 1,
      "stage": "running",
      "level": 0,
      "message": "runtime running",
      "objectType": "runtime",
      "objectId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "createdAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "nextCursor": null
}
```

`level`：`0`=Info，`1`=Success，`2`=Warning，`3`=Error。

### 7.5 上报协议事件

设备/传感器模拟器把去敏后的协议事件写入运行时事件流。

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/protocol-events
Authorization: Bearer <token>
Scope: teamlab.runtimes:write
```

请求体：

```json
{
  "type": "modbus.read",
  "source": "plc",
  "occurredAt": "2026-08-19T10:00:00+00:00",
  "parameters": {
    "register": "40001",
    "value": "1"
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `type` | string | 是 | 事件类型 |
| `source` | string | 是 | 来源标识 |
| `occurredAt` | datetime | 否 | 发生时间，默认当前 |
| `parameters` | object | 否 | 参数；平台只记录参数名与计数，不记录原始值 |

响应 `200 OK`：

```json
{
  "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "stage": "protocol",
  "type": "modbus.read",
  "source": "plc"
}
```
