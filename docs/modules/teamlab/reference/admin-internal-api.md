# TeamLab：管理端与 Agent 内部回传

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 12. 管理端接口

管理端接口由浏览器会话鉴权，路由前缀 `api/admin/teamlab`。只有 Teacher/Admin 角色可访问；管理接口不要求也不接受 API token。以下列出常用接口；管理接口以实际页面调用为准。

### 12.1 场景管理

| 方法与路径 | 说明 |
| --- | --- |
| `GET /api/admin/teamlab/capabilities` | 查能力 |
| `GET /api/admin/teamlab/topologies` | 场景列表 |
| `POST /api/admin/teamlab/topologies` | 创建场景 |
| `GET /api/admin/teamlab/topologies/{topologyId}` | 获取场景 |
| `PUT /api/admin/teamlab/topologies/{topologyId}` | 更新场景 |
| `DELETE /api/admin/teamlab/topologies/{topologyId}` | 删除场景 |
| `POST /api/admin/teamlab/topologies/{topologyId}/validate` | 校验 |
| `POST /api/admin/teamlab/topologies/{topologyId}/releases` | 发布 |
| `GET /api/admin/teamlab/topologies/{topologyId}/releases` | 版本列表 |
| `POST /api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/plan` | 生成计划 |
| `GET /api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/readiness` | 查询发布就绪状态 |
| `POST /api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/images/prepare` | 发起镜像准备 |

场景列表查询参数：`search`、`owner`、`ownerId`、`status`、`after`、`limit`（默认 30）。创建/更新请求体与开放 API 的拓扑字段一致，但管理接口不需要 `Idempotency-Key`。场景创建同步返回 `201 Created`，更新返回 `200 OK`，删除返回 `204 No Content`。发布请求体 `{ "revision": n }`，发布成功同步返回 `TeamLabReleaseModel`（`200 OK`），不是异步 operation。

### 12.2 试运行与运行时管理

| 方法与路径 | 说明 |
| --- | --- |
| `GET /api/admin/teamlab/runtimes` | 运行时列表 |
| `POST /api/admin/teamlab/runtimes/trials` | 创建试运行 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}` | 获取运行时 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/logs` | 查询日志 |
| `POST /api/admin/teamlab/runtimes/{runtimeId}/reset` | 重置 |
| `DELETE /api/admin/teamlab/runtimes/{runtimeId}` | 销毁 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/events` | 查询事件 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/link-policies` | 链路策略列表 |
| `POST /api/admin/teamlab/runtimes/{runtimeId}/link-policies` | 应用链路策略 |
| `POST /api/admin/teamlab/runtimes/{runtimeId}/link-policies/{policyId}/recover` | 恢复链路策略 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/traffic/flows` | 流量记录 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/traffic/paths` | 流量路径 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}` | 路径详情 |
| `POST /api/admin/teamlab/runtimes/{runtimeId}/access-grants` | 创建 WireGuard 授权 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/access-grants` | 授权列表 |
| `DELETE /api/admin/teamlab/runtimes/{runtimeId}/access-grants/{grantId}` | 撤销授权 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/access-grants/{grantId}/download` | 下载授权 |
| `POST /api/admin/teamlab/runtimes/{runtimeId}/captures` | 开始抓包 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/captures` | 抓包列表 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}` | 抓包状态 |
| `POST /api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}/stop` | 停止抓包 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}/download` | 下载抓包 |

试运行创建请求体：

```json
{
  "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "constraints": null,
  "overlays": [],
  "externalReference": "trial-01"
}
```

试运行创建返回 `202 Accepted`，响应体为 `TeamLabRuntimeProjectionModel`，不是 ApiOperation。管理端运行时列表查询参数：`topologyId`、`after`、`limit`（默认 30）。事件查询：`after`（默认 0）、`limit`（默认 100，服务端截断 200）、`generation`、`stage`。

管理端 `GET /runtimes/{id}/events` 返回 `IReadOnlyList<TeamLabRuntimeEventModel>` 数组（不是分页对象）；开放 API 的事件接口返回分页对象。

管理端运行时接口的返回语义与开放 API 不同：

- `POST /runtimes/{id}/reset`、`DELETE /runtimes/{id}` 返回 `202 Accepted` + `TeamLabRuntimeProjectionModel`。
- `POST /runtimes/{id}/access-grants` 返回 `201 Created` + `TeamLabAccessGrantModel`。
- `POST /runtimes/{id}/captures` 返回 `201 Created` + `TeamLabCaptureModel`。
- `POST /runtimes/{id}/captures/{captureId}/stop` 返回 `200 OK` + `TeamLabCaptureModel`。
- 授权下载、抓包下载与开放 API 相同，返回文件流。

链路策略、流量、日志、事件等管理接口的请求/响应模型与开放 API 对应操作一致，仅鉴权方式不同。

### 12.3 远程运维管理

| 方法与路径 | 说明 |
| --- | --- |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-access` | 单资产远程可用性 |
| `GET /api/admin/teamlab/runtimes/{runtimeId}/remote-access` | 批量远程可用性 |
| `POST /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-sessions` | 创建远程会话 |
| `GET /api/admin/teamlab/remote-sessions/{sessionId}` | 查询会话 |
| `GET /api/admin/teamlab/remote-sessions/{sessionId}/connect` | 获取连接地址 |
| `DELETE /api/admin/teamlab/remote-sessions/{sessionId}` | 结束会话 |
| `GET /api/admin/teamlab/remote-sessions/{sessionId}/terminal` | WebSocket 终端代理 |

创建会话请求体：`{ "reason": "运维" }`。

### 12.4 设备包、连接器、资源池管理（仅 Admin）

| 方法与路径 | 说明 |
| --- | --- |
| `GET /api/admin/teamlab/device-packages` | 设备包列表 |
| `GET /api/admin/teamlab/device-packages/{packageId}` | 设备包详情 |
| `POST /api/admin/teamlab/device-packages` | 注册设备包 |
| `POST /api/admin/teamlab/device-packages/{packageId}/enable` | 启用 |
| `POST /api/admin/teamlab/device-packages/{packageId}/disable` | 停用 |
| `POST /api/admin/teamlab/device-packages/{packageId}/archive` | 归档 |
| `GET /api/admin/teamlab/connectors` | 连接器列表 |
| `POST /api/admin/teamlab/connectors` | 注册连接器 |
| `POST /api/admin/teamlab/connectors/{connectorId}/health` | 上报健康 |
| `POST /api/admin/teamlab/connectors/{connectorId}/leases/revoke` | 强制撤销租约 |
| `POST /api/admin/teamlab/connectors/{connectorId}/archive` | 归档连接器 |
| `GET /api/admin/teamlab/resource-pools` | 资源池 |
| `GET /api/admin/teamlab/resource-pools/node-cache` | 节点缓存 |
| `GET /api/admin/teamlab/scopes` | 控制范围列表 |
| `POST /api/admin/teamlab/scopes/{scopeId}/archive` | 归档控制范围 |

注册设备包请求体字段：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `name` | string | 是 | 唯一名 |
| `displayName` | string | 是 | 显示名 |
| `version` | string | 是 | 版本 |
| `artifactKind` | string | 是 | `oci-image` 或 `vm-image` |
| `artifactReference` | string | 是 | 制品引用 |
| `digest` | string | 否 | 摘要 |
| `description` | string | 否 | 描述 |
| `supportedAssetKinds` | string[] | 否 | 支持的资产类型 |
| `cpuMillis` | int | 是 | CPU 毫核 |
| `memoryMib` | int | 是 | 内存 MiB |
| `storageGib` | int | 是 | 存储 GiB |
| `ports[]` | array | 否 | 端口声明 |
| `parameterSchema` | object | 否 | 参数 JSON Schema |
| `healthDeclaration` | object | 否 | 健康声明 |
| `protocolEventTypes` | string[] | 否 | 协议事件类型 |

注册连接器请求体字段：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `name` | string | 是 | 唯一名 |
| `displayName` | string | 是 | 显示名 |
| `kind` | string | 是 | `managed-nic`/`vlan`/`segment`/`serial`/`usb-gateway`/`dedicated-network` |
| `controlScopeId` | guid | 否 | 控制范围 |
| `supportsSharedUse` | bool | 是 | 是否支持共享 |
| `capacity` | int | 是 | 容量 |
| `attachmentReference` | string | 否 | 接入引用（不暴露给外部） |
| `description` | string | 否 | 描述 |

健康上报请求体：`{ "health": "healthy" }`，取值为 `unknown`/`healthy`/`degraded`/`unreachable`。

### 12.5 控制范围管理（仅 Admin）

- `GET /api/admin/teamlab/scopes`：列出全部控制范围。
- `POST /api/admin/teamlab/scopes/{scopeId}/archive`：归档。

---

## 13. Agent 内部回传接口

Agent 回传接口只供平台内部节点调用，不对外部系统开放，也不承诺稳定。路由前缀 `api/internal/teamlab/captures`。

### 13.1 上传抓包分段

```http
PUT /api/internal/teamlab/captures/{captureId}/segments/{segmentId}
Authorization: Bearer <node-token>
X-GZCTF-Worker-Node: <worker-node-guid>
X-Content-SHA256: <sha256-of-body>
Content-Type: application/vnd.tcpdump.pcap
```

请求体为 PCAP 原始字节流。

响应：

- `200 OK`：`{ "segmentId": "...", "uploaded": true, "alreadyExists": false }`
- `400`：内容不合法
- `401`：节点身份无效
- `404`：抓包/分段不存在
- `409`：冲突

---
