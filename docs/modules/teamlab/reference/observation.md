# TeamLab：流量证据、路径与抓包

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 10. 流量、路径与抓包

### 10.1 列出流量记录

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/flows?limit=50&after=<cursor>&query=&protocol=&networkKey=&port=
Authorization: Bearer <token>
Scope: teamlab.traffic:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `after` | string | 否 | 游标 |
| `limit` | int | 否 | 1-100，默认 50 |
| `query` | string | 否 | 关键字搜索 |
| `protocol` | string | 否 | 协议过滤，如 `tcp` |
| `networkKey` | string | 否 | 网段 key 过滤 |
| `port` | int | 否 | 端口过滤，1-65535 |

响应：

```json
{
  "items": [
    {
      "cursor": "abc",
      "shardId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "networkKey": "net-entry",
      "sourceIp": "10.80.1.20",
      "sourcePort": 50000,
      "destinationIp": "10.80.1.10",
      "destinationPort": 502,
      "protocol": "tcp",
      "bytes": 1200,
      "packets": 330,
      "firstSeen": "2026-08-19T10:00:00+00:00",
      "lastSeen": "2026-08-19T10:00:00+00:00"
    }
  ],
  "nextCursor": null,
  "completeness": {
    "complete": true,
    "droppedRecords": 0
  }
}
```

### 10.2 列出流量路径

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths?limit=50&after=<cursor>&query=&protocol=&confidence=
Authorization: Bearer <token>
Scope: teamlab.traffic:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `after` | string | 否 | 游标 |
| `limit` | int | 否 | 1-100，默认 50 |
| `query` | string | 否 | 关键字搜索 |
| `protocol` | string | 否 | 协议过滤 |
| `confidence` | string | 否 | `packet-exact` |

响应：

```json
{
  "items": [
    {
      "cursor": "abc",
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "confidence": "packet-exact",
      "sourceIp": "10.80.1.20",
      "sourcePort": 50000,
      "destinationIp": "10.80.1.10",
      "destinationPort": 502,
      "protocol": "tcp",
      "startedAt": "2026-08-19T10:00:00+00:00",
      "endedAt": "2026-08-19T10:00:00+00:00",
      "hopCount": 3
    }
  ],
  "nextCursor": null,
  "completeness": {
    "complete": true,
    "droppedRecords": 0
  }
}
```

### 10.3 获取流量路径详情

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
Authorization: Bearer <token>
Scope: teamlab.traffic:read
```

响应包含 `hops`：

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "confidence": "packet-exact",
  "sourceIp": "10.80.1.20",
  "sourcePort": 50000,
  "destinationIp": "10.80.1.10",
  "destinationPort": 502,
  "protocol": "tcp",
  "startedAt": "2026-08-19T10:00:00+00:00",
  "endedAt": "2026-08-19T10:00:00+00:00",
  "hops": [
    {
      "ordinal": 1,
      "observedAt": "2026-08-19T10:00:00+00:00",
      "evidenceKind": 0,
      "observationPointKind": 3,
      "shardId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "networkKey": "net-entry",
      "infrastructureKey": null,
      "assetKey": "scada",
      "direction": "observed",
      "sourceIp": "10.80.1.20",
      "sourcePort": 50000,
      "destinationIp": "10.80.1.10",
      "destinationPort": 502,
      "protocol": "tcp"
    }
  ]
}
```

`evidenceKind` 与 `observationPointKind` 使用数字枚举（见文末）。

### 10.4 开始抓包

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/captures
Authorization: Bearer <token>
Content-Type: application/json
Idempotency-Key: capture-001
Scope: teamlab.capture:write
```

请求体：

```json
{
  "scope": "network",
  "networkKey": "net-entry",
  "maxSeconds": 60,
  "maxBytes": 536870912,
  "expiresInSeconds": 3600
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `scope` | string | 是 | 抓包范围，见下 |
| `networkKey` | string | 条件 | 仅 `scope=network` 时使用 |
| `maxSeconds` | int | 是 | 最长抓包秒数，1-86400 |
| `maxBytes` | long | 是 | 最大字节数，1024-10GiB |
| `expiresInSeconds` | int | 是 | 保留秒数，60-604800 |

`scope` 支持四种值：

| 值 | 含义 | 附加定位 |
| --- | --- | --- |
| `runtime` | 整个运行时全部观测点 | 无 |
| `network` | 指定网段 | 请求体 `networkKey` 必填 |
| `path:{pathId}` | 指定流量路径 | `pathId` 为流量路径 GUID |
| `asset:{assetKey}` | 指定资产 | `assetKey` 为资产 key（小写字母/数字/连字符，最长 63） |

响应 `202 Accepted` + operation；轮询后取得 `captureId`。

### 10.5 列出抓包任务

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/captures?limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.capture:read
```

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "status": 3,
      "scope": "network",
      "networkKey": "net-entry",
      "maxBytes": 536870912,
      "maxSeconds": 60,
      "capturedBytes": 1048576,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "startedAt": "2026-08-19T10:00:00+00:00",
      "completedAt": "2026-08-19T10:00:01+00:00",
      "expiresAt": "2026-08-19T11:00:00+00:00",
      "segments": [
        {
          "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "status": 3,
          "observationPointId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "observationPointKind": 3,
          "networkKey": "net-entry",
          "infrastructureKey": null,
          "assetKey": null,
          "capturedBytes": 1048576,
          "uploadedBytes": 1048576,
          "sha256": "abc...",
          "error": null
        }
      ],
      "failure": null
    }
  ],
  "next": null
}
```

抓包状态 `status`：`0`=Pending，`1`=Running，`2`=Stopping，`3`=Completed，`4`=Failed，`5`=Expired，`6`=CleanupPending。

### 10.6 获取抓包状态

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}
Authorization: Bearer <token>
Scope: teamlab.capture:read
```

### 10.7 停止抓包

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/stop
Authorization: Bearer <token>
Idempotency-Key: capture-stop-001
Scope: teamlab.capture:write
```

响应 `202 Accepted` + operation。

### 10.8 下载抓包文件

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/download
Authorization: Bearer <token>
Scope: teamlab.capture:read
```

成功返回 `application/x-tar` 归档文件（内含 PCAP 分段）。抓包完成后再下载。

---
