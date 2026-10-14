# TeamLab 对接：远程会话与文件操作

完整调用顺序见 [对接入口](integration.md)，认证与幂等要求见 [调用约定](integration-authentication.md)。本文说明实现契约，现场验收范围见 [当前状态](../../../current-state.md)。

### 3.9 创建远程会话

查询资产当前可用的远程协议：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/remote-access
```

响应项：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `assetId` | int | 资产 ID |
| `assetName` | string | 资产名称 |
| `protocol` | `TeamLabRemoteProtocol` / null | 当前可用协议 |
| `available` | bool | 是否可创建会话 |
| `unavailableReason` | string / null | 不可用原因 |

`TeamLabRemoteProtocol`：

| 值 | 名称 | 用途 |
| --- | --- | --- |
| `1` | `ContainerTerminal` | Docker 容器终端 |
| `2` | `Ssh` | Linux VM SSH |
| `3` | `Rdp` | Windows VM RDP |
| `4` | `Vnc` | libvirt 图形控制台 |

创建会话：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-sessions
Idempotency-Key: match-001-web-session-001
Content-Type: application/json

{
  "reason": "平台用户进入资产运维",
  "vncConsole": false
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `reason` | string | 会话用途，4 至 500 字符 |
| `vncConsole` | bool | VM 为 `true` 时创建 VNC 控制台会话 |

接口返回 `ApiOperationModel`。操作成功后，从 operation 的 `resourceId` 取得 `sessionId`，然后查询：

```http
GET /api/open/v1/teamlab/remote-sessions/{sessionId}
```

`OpenTeamLabRemoteSessionModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | UUID | 会话 ID |
| `runtimeId` | UUID | 运行环境 ID |
| `assetId` | int | 资产 ID |
| `assetName` | string | 资产名称 |
| `protocol` | `TeamLabRemoteProtocol` | 连接协议 |
| `status` | `TeamLabRemoteSessionStatus` | 会话状态 |
| `reason` | string | 创建原因 |
| `createdAt` | uint64 | 创建时间，Unix 毫秒 |
| `expiresAt` | uint64 | 过期时间，Unix 毫秒 |
| `connectedAt` | uint64 / null | 首次连接时间 |
| `endedAt` | uint64 / null | 结束时间 |
| `endReason` | string / null | 结束原因 |

`TeamLabRemoteSessionStatus`：

| 值 | 名称 |
| --- | --- |
| `1` | `Creating` |
| `2` | `Ready` |
| `3` | `Connected` |
| `4` | `Ending` |
| `5` | `Ended` |
| `6` | `Failed` |

VM 会话在 `Ready` 后获取一次性连接地址：

```http
POST /api/open/v1/teamlab/remote-sessions/{sessionId}/connect
```

响应字段为 `url` 和 `expiresAt`。容器终端连接 WebSocket：

```http
GET /api/open/v1/teamlab/remote-sessions/{sessionId}/terminal
Connection: Upgrade
Upgrade: websocket
Authorization: Bearer <token>
```

WebSocket 二进制消息承载 PTY 字节；文本 JSON 消息支持 `resize`、`input` 和 `signal`。关闭会话：

```http
DELETE /api/open/v1/teamlab/remote-sessions/{sessionId}
Idempotency-Key: match-001-web-session-001-end
```

### 3.10 文件操作

文件接口使用当前 `generation`。Docker 通过容器文件通道执行，VM 通过镜像模板中配置的 SSH/SFTP 连接执行。

| 操作 | 请求 |
| --- | --- |
| 列目录 | `GET /runtimes/{runtimeId}/assets/{assetId}/files?generation=1&path=/tmp` |
| 下载 | `GET /runtimes/{runtimeId}/assets/{assetId}/files/download?generation=1&path=/tmp/a.bin` |
| 上传 | `POST /runtimes/{runtimeId}/assets/{assetId}/files/upload?Generation=1&Path=/tmp/a.bin&Overwrite=false&Confirmed=false` |
| 新建目录 | `POST /runtimes/{runtimeId}/assets/{assetId}/files/directories` |
| 移动或重命名 | `POST /runtimes/{runtimeId}/assets/{assetId}/files/move` |
| 删除 | `DELETE /runtimes/{runtimeId}/assets/{assetId}/files?generation=1&path=/tmp/a.bin&recursive=false&confirmed=true` |

上传请求体直接发送文件二进制流，请求头使用 `Content-Type: application/octet-stream` 和实际 `Content-Length`。上传成功、创建目录、移动和删除均返回 `204 No Content`。

列目录响应：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `items` | array | 目录中的直接子项 |
| `items[].name` | string | 文件或目录名 |
| `items[].kind` | string | 条目类型 |
| `items[].size` | int64 | 文件大小；目录按服务端返回值读取 |

新建目录请求：

```json
{
  "generation": 1,
  "path": "/tmp/results"
}
```

移动或重命名请求：

```json
{
  "generation": 1,
  "sourcePath": "/tmp/a.bin",
  "destinationPath": "/tmp/results/a.bin",
  "overwrite": false,
  "confirmed": false
}
```
