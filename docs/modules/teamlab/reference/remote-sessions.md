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

账号注销时，TeamLab 通过 Identity 的 `IUserAccountRevocationParticipant` 参与撤销：先在注销事务内将该账号的活跃会话标为 `ending` 并使其到期，提交后取消容器终端、删除节点中继和 Guacamole 临时资源。清理失败保持可重试状态，由现有远程会话维护任务继续处理；后台也检查已禁用账号，覆盖提交后进程中断的情况。创建预留、会话激活和连接均回读账号可用性，避免注销期间仍在准备的会话重新变为可用。其他账号的会话与已结束审计记录保留。

数据库撤销与真实通道关闭分别确认。节点或 Guacamole 不可达时不能将 `ending` 视为已断开，真实 PTY、RDP、SSH 与 VNC 通道关闭仍需在授权基础设施上验收。

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
