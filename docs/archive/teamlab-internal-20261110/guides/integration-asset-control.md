# TeamLab 对接：单资产控制与服务访问

完整调用顺序见 [对接入口](integration.md)，认证与幂等要求见 [调用约定](integration-authentication.md)。本文说明实现契约，现场验收范围见 [当前状态](../../../current-state.md)。

### 3.7 控制单个资产

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
```

响应字段为 `allowed` 和 `reason`。`allowed` 为 `true` 时提交命令：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
Idempotency-Key: match-001-web-restart-001
Content-Type: application/json

{
  "generation": 1,
  "action": "restart",
  "reason": "平台管理员重启资产",
  "confirmed": false
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `generation` | int | 当前运行代次，最小值 1 |
| `action` | string | `start`、`stop`、`restart`、`rebuild`、`pause`、`resume` |
| `reason` | string | 操作原因，4 至 500 字符 |
| `confirmed` | bool | 重建等破坏性操作的确认值 |

受理响应为 `{ "ticketId": "<UUID>" }`。查询任务：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}
```

任务响应字段：`id`、`status`、`stage`、`errorCode`、`retryable`。

### 3.8 开放资产服务

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/service-access
Content-Type: application/json

{
  "protocol": "tcp",
  "internalPort": 8080,
  "publicPort": null,
  "networkKey": "office"
}
```

| 请求字段 | 类型 | 说明 |
| --- | --- | --- |
| `protocol` | string | `tcp` 或 `udp` |
| `internalPort` | int | 资产内部端口，1 至 65535 |
| `publicPort` | int / null | 服务器端口；空值由平台分配 |
| `networkKey` | string / null | 目标网段 key；多网卡资产填写此项 |

成功返回 `201 Created` 和 `OpenTeamLabServiceAccessModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | UUID | 映射 ID |
| `runtimeId` | UUID | 运行环境 ID |
| `generation` | int | 创建映射时的运行代次 |
| `assetId` | int | 资产 ID |
| `assetName` | string | 资产名称 |
| `networkKey` | string | 使用的网段 key |
| `protocol` | string | `tcp` 或 `udp` |
| `internalPort` | int | 资产内部端口 |
| `publicPort` | int | 服务器端口 |
| `endpoint` | string | 可交给访问方的地址 |
| `status` | string | 映射状态 |
| `createdAt` | uint64 | 创建时间，Unix 毫秒 |
| `revokedAt` | uint64 / null | 撤销时间，Unix 毫秒 |

```http
GET    /api/open/v1/teamlab/runtimes/{runtimeId}/service-access
DELETE /api/open/v1/teamlab/runtimes/{runtimeId}/service-access/{accessId}
```

查询返回 `OpenTeamLabServiceAccessModel[]`；撤销返回撤销后的同一模型。
