# TeamLab：链路策略与恢复

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 8. 链路策略

链路策略在运行时链路上实际执行损伤或访问控制。所有策略都作用于某个运行时网段（可选指定资产），字段 `kind` 使用字符串枚举（请求体里 `kind` 是字符串），`parameters` 是随 kind 变化的 JSON 对象。

### 8.1 支持的类型与参数

| `kind` | 用途 | 参数 |
| --- | --- | --- |
| `packet-loss` | 丢包 | `lossPercent` 0-100 |
| `latency` | 延迟 | `delayMillis` 1-10000 |
| `jitter` | 抖动 | `jitterMillis` 0-5000 |
| `duplication` | 重复包 | `duplicatePercent` 0-100 |
| `bandwidth-limit` | 限速 | `rateMbps` 0.1-100000，可选 `burstKilobytes` 0-2097152 |
| `link-break` | 断连 | 无参数，传 `{}` |
| `access-rule` | 访问控制 | 见下 |
| `nat` | DNAT/SNAT | 见下 |

> 提示：链路策略参数在不同版本/部署中可能存在兼容别名。本表列出的是当前源码校验器直接支持的规范字段；测试文档中出现的 `percent`、`delayMs`、`port` 若在目标环境实测可用，说明该环境保留兼容解析。实际对接时以目标环境 OpenAPI JSON 中 `ApplyTeamLabLinkPolicyModel` 的描述和实测响应为准。

`access-rule` 参数：

| 参数 | 必填 | 取值/范围 |
| --- | --- | --- |
| `direction` | 是 | `inbound`、`outbound`、`both` |
| `action` | 是 | `allow`、`deny` |
| `protocol` | 否 | `tcp`、`udp`、`icmp`、`any`，默认 `any` |
| `sourceCidr` | 否 | CIDR，如 `10.0.0.0/8` |
| `destinationCidr` | 否 | CIDR |
| `priority` | 否 | 0-1000 整数 |

`nat` 参数：

- `mode` 必填，`snat` 或 `dnat`。
- `snat`：必填 `translatedAddress`（IP 地址）。
- `dnat`：
  - 必填 `externalPort`（1-65535）、`internalAddress`（IP 地址）；
  - 可选 `externalAddress`（IP）、`internalPort`（1-65535）。

### 8.2 应用链路策略

```http
POST /api/open/v1/teamlab/link-policies
Authorization: Bearer <token>
Content-Type: application/json
Scope: teamlab.link-policies:write
```

请求体：

```json
{
  "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "networkKey": "net-entry",
  "assetKey": null,
  "kind": "packet-loss",
  "parameters": {
    "lossPercent": 40
  },
  "recoverAt": null
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `runtimeId` | guid | 是 | 运行时 ID |
| `networkKey` | string | 是 | 目标网段 key |
| `assetKey` | string | 否 | 目标资产 key；为空表示整网段 |
| `kind` | string | 是 | 策略类型 |
| `parameters` | object | 是 | 策略参数 |
| `recoverAt` | datetime | 否 | 自动恢复时间 |

同参数重复应用幂等；不同参数需要先 `recover`。成功 `201 Created`，响应：

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "networkKey": "net-entry",
  "assetKey": null,
  "kind": "packet-loss",
  "parameters": {
    "lossPercent": 40
  },
  "status": "active",
  "recoverAt": null,
  "appliedAt": "2026-08-19T10:00:00+00:00",
  "recoveredAt": null,
  "recoverOrigin": "none",
  "lastError": null
}
```

### 8.3 列出运行时链路策略

```http
GET /api/open/v1/teamlab/link-policies?runtimeId={runtimeId}&status=active&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.link-policies:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `runtimeId` | guid | 是 | 运行时 ID |
| `status` | string | 否 | `active`、`recovered`、`failed`；默认不传返回未恢复策略 |
| `limit` | int | 否 | 默认 50 |
| `after` | string | 否 | 游标 |

### 8.4 恢复链路策略

```http
POST /api/open/v1/teamlab/link-policies/{policyId}/recover
Authorization: Bearer <token>
Scope: teamlab.link-policies:write
```

已恢复的策略幂等返回。成功 `200 OK`，响应同 8.2。

---
