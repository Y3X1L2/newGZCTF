# TeamLab：WireGuard 授权与配置下载

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

### 7.6 创建 WireGuard 访问授权

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/access-grants
Authorization: Bearer <token>
Idempotency-Key: grant-001
Scope: teamlab.runtimes:write
```

请求体：

```json
{
  "type": "WireGuard"
}
```

只支持 `WireGuard`。响应 `202 Accepted` + operation；操作完成后取得授权 `grantId`。

#### 列出仍可管理的访问授权

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/access-grants
Authorization: Bearer <token>
Scope: teamlab.runtimes:read
```

服务先校验 runtime 的控制范围授权，只返回当前 generation 中尚未撤销的授权元数据。响应不包含客户端/服务端私钥、配置正文、下载 token、token hash、受保护字段或一次性下载 URL。

### 7.7 下载访问配置

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/access-grants/{grantId}/download?token={oneTimeToken}
Authorization: Bearer <token>
Scope: teamlab.runtimes:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `token` | string | 是 | 一次性下载 token |

成功返回 `application/x-wireguard-profile` 文件。token 只可使用一次。

### 7.8 撤销访问授权

```http
DELETE /api/open/v1/teamlab/runtimes/{runtimeId}/access-grants/{grantId}
Authorization: Bearer <token>
Idempotency-Key: grant-revoke-001
Scope: teamlab.runtimes:write
```

响应 `202 Accepted` + operation。

---
