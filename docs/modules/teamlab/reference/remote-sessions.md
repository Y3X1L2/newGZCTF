# TeamLab：远程会话与访问

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

### 11.2 远程会话

#### 查询运行时全部资产远程可用性

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/remote-access
Authorization: Bearer <token>
Scope: teamlab.remote-sessions:read
```

响应：

```json
[
  {
    "assetId": 1,
    "assetName": "plc",
    "protocol": "containerTerminal",
    "available": true,
    "unavailableReason": null
  },
  {
    "assetId": 2,
    "assetName": "win-vm",
    "protocol": "rdp",
    "available": true,
    "unavailableReason": null
  }
]
```

`protocol` 取值为 `containerTerminal`、`ssh`、`rdp`（字符串，因为该枚举自定义序列化）。

#### 创建远程会话

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-sessions
Authorization: Bearer <token>
Content-Type: application/json
Scope: teamlab.remote-sessions:write
```

请求体：

```json
{
  "reason": "故障排查"
}
```

成功 `201 Created`，响应：

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "assetId": 1,
  "assetName": "plc",
  "protocol": "containerTerminal",
  "status": "ready",
  "reason": "故障排查",
  "createdAt": "2026-08-19T10:00:00+00:00",
  "expiresAt": "2026-08-19T10:30:00+00:00",
  "connectedAt": null,
  "endedAt": null,
  "endReason": null
}
```

会话状态：`creating`、`ready`、`connected`、`ending`、`ended`、`failed`（字符串，因为该枚举自定义序列化）。

#### 查询远程会话

```http
GET /api/open/v1/teamlab/remote-sessions/{sessionId}
Authorization: Bearer <token>
Scope: teamlab.remote-sessions:read
```

响应同上单项。

#### 关闭远程会话

```http
DELETE /api/open/v1/teamlab/remote-sessions/{sessionId}
Authorization: Bearer <token>
Scope: teamlab.remote-sessions:write
```

重复关闭幂等。成功 `200 OK`，返回关闭后的会话。
