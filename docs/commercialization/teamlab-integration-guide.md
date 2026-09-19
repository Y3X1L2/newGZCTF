# TeamLab 组网底座对接文档

本文供调用 TeamLab 的平台服务使用。TeamLab 提供场景版本、独立运行环境、资产执行、网络、访问入口、远程运维和运行授权。队伍、比赛阶段、商店、计分、Flag、Checker 和环境发放由调用方管理。

基础路径：`/api/open/v1/teamlab`

机器契约：`/openapi/open-v1.json`

## 1. 调用约定

### 1.1 认证

所有 Open API 使用 Bearer Token：

```http
Authorization: Bearer <token>
```

JSON 请求使用：

```http
Content-Type: application/json
```

API Token 同时受三层权限约束：

1. API scope 决定可以调用哪类接口。
2. `teamlab-scope` 资源授权决定可以发现和管理哪些控制范围。
3. Runtime Grant 决定可以操作哪个运行环境或具体资产。

任一层不满足都会拒绝请求。API Token 不继承创建者的管理员身份。

### 1.2 API scope

| Scope | 用途 |
| --- | --- |
| `teamlab.topologies:read` | 查询能力、拓扑、发布版本和部署计划 |
| `teamlab.topologies:write` | 创建、更新、删除和发布拓扑 |
| `teamlab.runtimes:read` | 查询运行环境、状态、资产、事件和授权 |
| `teamlab.runtimes:write` | 创建、热更新、控制、重置和销毁运行环境 |
| `teamlab.resource-pools:read` | 查询节点资源池和镜像缓存 |
| `teamlab.device-packages:read` | 查询设备模板 |
| `teamlab.device-packages:write` | 登记、修改、启停和归档设备模板 |
| `teamlab.connectors:read` | 查询现场连接器及节点网卡 |
| `teamlab.connectors:write` | 登记、修改、占用和释放现场连接器 |
| `teamlab.link-policies:read` | 查询链路策略 |
| `teamlab.link-policies:write` | 应用和恢复链路策略 |
| `teamlab.remote-sessions:read` | 查询远程会话、文件和审计证据 |
| `teamlab.remote-sessions:write` | 创建或结束会话，修改资产文件 |
| `teamlab.traffic:read` | 查询流量和路径 |
| `teamlab.capture:read` | 查询、下载抓包 |
| `teamlab.capture:write` | 开始和停止抓包 |
| `images:read` | 查询镜像模板 |
| `images:write` | 导入镜像并配置远程访问 |

调用方通常使用一个具备拓扑和运行管理权限的服务 Token；面向用户的 Token 应按实际需要收窄 scope 和 Runtime Grant。

### 1.3 幂等与异步操作

创建拓扑、更新拓扑、发布、创建运行环境、资产编排、生命周期操作、暂停、恢复、重置和销毁等异步写接口要求：

```http
Idempotency-Key: <1-128 个 ASCII 字母、数字、-、_ 或 .>
```

同一次业务动作必须复用同一个 key。请求体变化后仍使用原 key，会返回 `409 idempotency_conflict`。

异步接口返回 `202 Accepted` 和 `ApiOperationModel`。使用返回的 `id` 查询：

```http
GET /api/open/v1/operations/{operationId}
Authorization: Bearer <token>
```

`status`：`0`=Pending，`1`=Running，`2`=Succeeded，`3`=Failed。失败时读取 `errorCode`、`errorDetail` 和 `stage`，不要从展示文案推断错误类型。

同步接口包括 Runtime Grant 全量替换、模板批量预热、服务开放和撤销。它们直接返回处理结果，不需要轮询 operation。

### 1.4 分页与时间

- 列表默认 `limit=50`，多数接口上限为 100。
- `after` 或 `cursor` 是不透明游标，只能原样回传。
- 时间字段为 ISO 8601 字符串或 OpenAPI 标注的 Unix 时间值，按生成客户端类型读取，不自行猜测单位。

## 2. 资源关系

```text
Control Scope
  └─ Topology 草稿
       └─ Release 不可变版本
            └─ Runtime 独立运行环境
                 ├─ Runtime Asset
                 ├─ Runtime Grant
                 ├─ Service Access
                 ├─ Remote Session
                 ├─ Link Policy
                 └─ Capture / Traffic
```

| 标识 | 用途 |
| --- | --- |
| `controlScopeId` | 调用方的资源隔离边界 |
| `topologyId` | 可编辑场景草稿 UUID |
| `releaseId` | 不可变场景版本 UUID |
| `runtimeId` | 独立运行环境 UUID |
| `externalReference` | 调用方自己的业务编号，TeamLab 不解析其含义 |
| `generation` | 完整 reset 后递增；普通资产热更新不变 |
| `planRevision` | 当前资产计划修订；热更新成功后递增 |
| `assetId` | 当前运行环境中的资产数字 ID |
| `assetKey` | 场景内稳定资产标识，用于授权和资产变更 |

同一个 `releaseId` 可以创建多个彼此隔离的运行环境。调用方应保存 `externalReference`、`runtimeId` 和创建 operation ID，不能用队伍名或页面标题反查现场资源。

## 3. 标准对接流程

### 3.1 准备控制范围和场景版本

控制范围用于隔离不同调用方或业务域。管理员 Token 可以创建：

```http
POST /api/open/v1/teamlab/scopes
Authorization: Bearer <token>
Content-Type: application/json

{
  "key": "league-2026",
  "displayName": "数字攻防联赛 2026"
}
```

场景制作、镜像和设备模板导入见 [`../teamlab-authoring-guide.md`](../teamlab-authoring-guide.md)。运行环境只能引用已经发布的 `releaseId`。

### 3.2 预热模板

活动开始前可把已 Ready 的镜像模板分发到具备对应能力的节点：

```http
POST /api/open/v1/teamlab/preparations/templates
Authorization: Bearer <token>
Content-Type: application/json

{
  "templateIds": [116, 117, 203]
}
```

响应中的 `templateCount` 是请求模板数，`distributionCount` 是创建或复用的节点分发记录数。预热不创建运行环境。

也可以按发布版本管理准备状态：

```http
POST   /api/open/v1/teamlab/preparations/releases/{releaseId}
GET    /api/open/v1/teamlab/preparations/releases/{releaseId}
DELETE /api/open/v1/teamlab/preparations/releases/{releaseId}
```

`POST` 和 `DELETE` 需要 `Idempotency-Key`。

### 3.3 创建独立运行环境

```http
POST /api/open/v1/teamlab/runtimes
Authorization: Bearer <token>
Idempotency-Key: runtime-team-017-20260919-01
Content-Type: application/json

{
  "releaseId": "<releaseId>",
  "externalReference": "match-2026-001:team-017",
  "constraints": {
    "preferredRegion": null,
    "requiredCapabilities": []
  },
  "overlays": [
    {
      "assetKey": "web",
      "secrets": {
        "FLAG": "<实例隔离值>"
      }
    }
  ]
}
```

`externalReference` 只用于调用方关联业务。TeamLab 不识别队伍、场次或比赛阶段。

`overlays[].secrets` 用于实例机密，不会写入发布版本。不要把机密放进拓扑、日志、operation 元数据或普通环境变量接口。

创建成功后的读取顺序：

1. 轮询 operation，直到成功或失败。
2. 从 operation 的 `resourceId` 取得 `runtimeId`。
3. 轮询 `GET /runtimes/{runtimeId}/status`。
4. 进入终态后停止轮询；需要完整信息时再读取 `GET /runtimes/{runtimeId}`。

轻量状态接口：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/status
```

运行状态：`0`=Pending、`1`=Planning、`2`=Scheduled、`3`=Deploying、`4`=Probing、`5`=Running、`6`=Failed、`7`=CleanupPending、`8`=Paused、`9`=Destroying、`10`=Destroyed、`11`=Stopped。

资产分页：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets?cursor=&limit=50&status=
```

### 3.4 配置运行授权

读取和替换授权：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/grants
PUT /api/open/v1/teamlab/runtimes/{runtimeId}/grants
```

`PUT` 提交完整授权集合，会替换该运行环境现有 Runtime Grant：

```json
{
  "grants": [
    {
      "subjectType": "user",
      "subjectId": "<userId>",
      "assetKey": null,
      "permissions": ["StateRead", "MetadataRead", "RemoteSessionOperate"]
    },
    {
      "subjectType": "apiToken",
      "subjectId": "<apiTokenId>",
      "assetKey": "web",
      "permissions": ["StateRead", "AssetOperate"]
    }
  ]
}
```

`subjectType` 只接受 `user` 和 `apiToken`。`assetKey: null` 表示整个运行环境；填写资产 key 时只对该资产生效。

| Permission | 允许的操作 |
| --- | --- |
| `StateRead` | 查看运行状态和资产摘要 |
| `MetadataRead` | 查看完整环境信息、事件、流量和抓包元数据 |
| `RemoteSessionOperate` | 创建和关闭终端、SSH、RDP、VNC 会话 |
| `FileTransfer` | 浏览、上传、下载、移动和删除文件 |
| `AssetOperate` | 启动、停止、重启、暂停、恢复和重建资产 |
| `AssetCompose` | 新增、替换和移除资产 |
| `ServiceAccessManage` | 创建和撤销服务开放 |
| `RuntimeManage` | 重置、销毁、暂停、恢复和管理授权 |

远程会话与文件权限分开。只授予 `RemoteSessionOperate` 时，用户可以使用终端，但不能上传或下载文件。

## 4. 运行中资产编排

资产热更新只允许把资产接入运行环境已有网段，不修改网段、路由和基础设施。

先读取完整运行环境，取得当前 `planRevision`；然后一次提交本轮全部变更：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/asset-changes
Authorization: Bearer <token>
Idempotency-Key: runtime-team-017-assets-20260919-01
Content-Type: application/json

{
  "expectedPlanRevision": 4,
  "add": [
    {
      "key": "scanner",
      "name": "扫描节点",
      "kind": 0,
      "imageTemplateId": 203,
      "resources": { "cpuUnits": 2, "memoryMiB": 1024, "storageMiB": 2048 },
      "interfaces": [
        { "key": "eth0", "networkKey": "office", "hostOffset": 35, "primary": true, "orderIndex": 0 }
      ],
      "healthCheck": { "kind": 0, "port": 22 },
      "orderIndex": 20
    }
  ],
  "replace": [],
  "remove": [],
  "overlays": []
}
```

规则：

- `add` 使用新资产 key。
- `replace` 提交完整资产定义，并使用要替换的资产 key。
- `remove` 填资产 key 数组。
- 新增和替换只能引用已 Ready 的镜像模板或设备模板。
- 网卡只能引用运行环境已有 `networkKey`。
- 成功后 `generation` 不变，`planRevision` 加一。
- `409 runtime_plan_revision_conflict` 表示已有其他变更成功；重新读取运行环境后重新计算本次请求。

将整个运行环境更新到另一发布版本时使用：

```http
GET  /api/open/v1/teamlab/runtimes/{runtimeId}/updates/preview?releaseId={releaseId}
POST /api/open/v1/teamlab/runtimes/{runtimeId}/updates
```

该入口会把版本差异转换为同一资产变更执行链。需要修改网络结构时使用完整 reset，不要用 `asset-changes` 绕过限制。

## 5. 单资产控制

查询资产是否可执行单资产命令：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
```

提交命令：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
Authorization: Bearer <token>
Idempotency-Key: asset-web-restart-20260919-01
Content-Type: application/json

{
  "generation": 1,
  "action": "restart",
  "reason": "平台侧人工重启",
  "confirmed": false
}
```

`action` 支持 `start`、`stop`、`restart`、`rebuild`、`pause`、`resume`。响应返回原 `DeploymentQueueTicket` 的 `ticketId`：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}
```

## 6. 服务开放

把资产内部服务开放为无需 WireGuard 的服务器地址：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/service-access
Authorization: Bearer <token>
Content-Type: application/json

{
  "protocol": "tcp",
  "internalPort": 8080,
  "publicPort": null,
  "networkKey": "office"
}
```

- `protocol` 只接受 `tcp` 或 `udp`。
- `publicPort: null` 由平台自动分配。
- 指定 `publicPort` 时，端口冲突会返回错误。
- 多网卡资产应明确提供 `networkKey`。
- 成功响应的 `endpoint` 是调用方可以分发的地址。

查询和撤销：

```http
GET    /api/open/v1/teamlab/runtimes/{runtimeId}/service-access
DELETE /api/open/v1/teamlab/runtimes/{runtimeId}/service-access/{accessId}
```

运行环境销毁时会清理所属映射和端口租约。

## 7. 远程运维

先查询资产支持的协议：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/remote-access
```

创建会话：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-sessions
Authorization: Bearer <token>
Idempotency-Key: session-web-20260919-01
Content-Type: application/json

{
  "reason": "赛事平台发起运维会话",
  "vncConsole": false
}
```

VM 使用 SSH 或 RDP；`vncConsole: true` 请求 libvirt 图形控制台。Docker 使用容器终端。创建为异步 operation，完成后保存 `sessionId`。

```http
GET    /api/open/v1/teamlab/remote-sessions/{sessionId}
POST   /api/open/v1/teamlab/remote-sessions/{sessionId}/connect
GET    /api/open/v1/teamlab/remote-sessions/{sessionId}/terminal
DELETE /api/open/v1/teamlab/remote-sessions/{sessionId}
```

`connect` 返回短时一次性 URL，适用于 VM 图形或代理连接。容器终端是 WebSocket，连接时继续携带 Bearer 身份。会话使用完毕后主动 `DELETE`，释放临时通道。

### 7.1 文件操作

文件接口都要求当前 `generation`：

```http
GET    /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/files?generation=1&path=/tmp
GET    /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/files/download?generation=1&path=/tmp/a.bin
POST   /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/files/upload?generation=1&path=/tmp/a.bin&overwrite=false&confirmed=false
POST   /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/files/directories
POST   /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/files/move
DELETE /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/files?generation=1&path=/tmp/a.bin&recursive=false&confirmed=true
```

上传请求体是 `application/octet-stream`，必须提供 `Content-Length`。覆盖、递归删除等破坏性动作需要显式 `confirmed=true`。VM 文件操作依赖镜像模板的 SSH 运维配置；VNC 可用不代表 SFTP 可用。

## 8. 运行状态、链路和流量

事件与状态检查：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/events?after=&limit=50&generation=&stage=
GET /api/open/v1/teamlab/runtimes/{runtimeId}/device-health
GET /api/open/v1/teamlab/runtimes/{runtimeId}/status-check
```

`status-check` 只返回当前数据库事实和现场资产的差异。修复动作仍调用单资产控制接口。

链路策略：

```http
POST /api/open/v1/teamlab/link-policies
GET  /api/open/v1/teamlab/link-policies?runtimeId={runtimeId}&status=active
POST /api/open/v1/teamlab/link-policies/{policyId}/recover
```

`kind` 支持 `access-rule`、`bandwidth-limit`、`latency`、`jitter`、`packet-loss`、`duplication` 和 `link-break`。服务端字段以 OpenAPI 中 `ApplyTeamLabLinkPolicyModel` 为准。公网服务映射使用 Service Access，不通过链路策略配置 NAT。

流量与抓包：

```http
GET  /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/flows
GET  /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths
GET  /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
POST /api/open/v1/teamlab/runtimes/{runtimeId}/captures
GET  /api/open/v1/teamlab/runtimes/{runtimeId}/captures
GET  /api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}
POST /api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/stop
GET  /api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/download
```

抓包请求：

```json
{
  "scope": "network",
  "networkKey": "office",
  "maxSeconds": 60,
  "maxBytes": 104857600,
  "expiresInSeconds": 3600
}
```

跨节点抓包按节点拆成多个分段。下载返回 tar 流，包含 manifest 和已验证的分段文件。

## 9. 暂停、恢复、重置和销毁

```http
POST   /api/open/v1/teamlab/runtimes/{runtimeId}/pause
POST   /api/open/v1/teamlab/runtimes/{runtimeId}/resume
POST   /api/open/v1/teamlab/runtimes/{runtimeId}/reset
DELETE /api/open/v1/teamlab/runtimes/{runtimeId}
```

以上接口需要 `Idempotency-Key`。

- 暂停保留运行身份、网络、磁盘和容量预留。
- 恢复只在原节点执行，不重新调度；原节点不可用会返回 `resume_blocked`。
- reset 清理当前代并重新部署，`generation` 递增，旧会话和访问授权失效。
- destroy 清理资产、网络、服务映射、抓包、访问授权、端口和容量租约。

销毁成功以 operation 成功且运行状态为 Destroyed 为准。调用方不要在收到 `202` 后立即删除自己的关联记录，应保留 `runtimeId` 和 operation ID 直到终态。

## 10. 可选 Rollout

Rollout 是一组同版本运行目标的通用编排器。调用方需要统一准备、开放、关闭和清理一批独立运行环境时可以使用；按业务逐个创建独立 Runtime 时不需要 Rollout。

Rollout 不保存队伍、对手、比赛阶段或发放规则。`targets` 只是一组外部引用和运行目标快照。

```text
GET    /api/open/v1/teamlab/rollouts
POST   /api/open/v1/teamlab/rollouts
GET    /api/open/v1/teamlab/rollouts/{rolloutId}
GET    /api/open/v1/teamlab/rollouts/{rolloutId}/targets
PUT    /api/open/v1/teamlab/rollouts/{rolloutId}/targets
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/prepare
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/open-access
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/close-access
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/pause
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/resume
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/drain
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/archive
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/pause
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/resume
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/restart
POST   /api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/rebuild
```

`PUT /targets` 替换期望目标快照，不会立即删除已移除目标。结束时先 `close-access`，再 `drain`，全部清理完成后 `archive`。

## 11. Webhook

Webhook 可接收 operation 和运行资源事件：

```http
GET    /api/open/v1/teamlab/webhooks
POST   /api/open/v1/teamlab/webhooks
GET    /api/open/v1/teamlab/webhooks/{webhookId}
DELETE /api/open/v1/teamlab/webhooks/{webhookId}
POST   /api/open/v1/teamlab/webhooks/{webhookId}/replay
```

签名 secret 只在创建响应中返回一次。投递是至少一次语义，调用方必须按事件 ID 去重。Webhook 只用于通知；部署事实仍以 operation 和运行状态查询为准。

## 12. 错误处理

失败响应使用 `application/problem+json`。客户端按稳定 `code` 分支，不解析 `detail` 文案。

| HTTP | 处理方式 |
| --- | --- |
| `400` | 修正格式、查询参数或请求头 |
| `401` | Token 缺失、无效或已撤销；重新认证 |
| `403` | 缺少 API scope、Control Scope 或 Runtime Grant；补授权 |
| `404` | 资源不存在或对当前 Token 不可见；不要猜测内部 ID |
| `409` | 状态冲突、修订冲突或幂等冲突；先读取最新事实 |
| `422` | 请求语义不符合拓扑、模板或能力约束；修正请求后再提交 |
| `429` | 按 `Retry-After` 等待；同时读取 `RateLimit-*` 响应头 |
| `503` | 限流或协调基础设施不可用；使用相同幂等 key 延迟重试 |

常用错误码：

| Code | 含义 |
| --- | --- |
| `topology_revision_conflict` | 草稿 revision 已变化 |
| `topology_invalid` | 拓扑校验失败 |
| `image_template_unavailable` | 镜像不存在、不可见或未 Ready |
| `address_pool_exhausted` | 网段地址池没有可分配子网 |
| `capability_unavailable` | 当前节点不满足 Docker、KVM 或网络能力 |
| `external_reference_conflict` | 同一属主的业务编号已用于另一请求 |
| `runtime_plan_revision_conflict` | 热更新基于的 `planRevision` 已过期 |
| `runtime_operation_in_progress` | 该运行环境已有互斥操作正在执行 |
| `runtime_not_ready` | 当前状态不允许执行请求 |
| `resume_blocked` | 原节点当前无法恢复暂停环境 |
| `runtime_grant_permission_invalid` | Runtime Grant 包含未知权限 |
| `idempotency_conflict` | 同一幂等 key 对应了不同请求 |

重试规则：

- 网络超时但不知道服务端是否受理时，使用原 `Idempotency-Key` 重试。
- operation 已失败时，先看错误码和运行事件；只有错误明确可重试时才重新提交。
- revision 或 `planRevision` 冲突时，读取最新资源并重新计算变更。
- `422` 不做自动重试。

## 13. 接口目录

### 13.1 场景与模板

```text
GET    /capabilities
GET    /scopes
POST   /scopes
POST   /scopes/{scopeId}/archive
GET    /device-packages
POST   /device-packages
GET    /device-packages/{packageId}
PUT    /device-packages/{packageId}
POST   /device-packages/{packageId}/enable
POST   /device-packages/{packageId}/disable
POST   /device-packages/{packageId}/archive
GET    /topologies
POST   /topologies
GET    /topologies/{topologyId}
PUT    /topologies/{topologyId}
DELETE /topologies/{topologyId}
POST   /topologies/{topologyId}/clone
POST   /topologies/{topologyId}/validate
GET    /topologies/{topologyId}/releases
POST   /topologies/{topologyId}/releases
GET    /topologies/{topologyId}/releases/{releaseId}
POST   /topologies/{topologyId}/releases/{releaseId}/plan
POST   /topologies/{topologyId}/releases/{releaseId}/archive
POST   /preparations/templates
GET    /preparations/releases/{releaseId}
POST   /preparations/releases/{releaseId}
DELETE /preparations/releases/{releaseId}
```

### 13.2 运行环境

```text
GET    /runtimes
POST   /runtimes
GET    /runtimes/{runtimeId}
DELETE /runtimes/{runtimeId}
GET    /runtimes/{runtimeId}/status
GET    /runtimes/{runtimeId}/assets
GET    /runtimes/{runtimeId}/events
POST   /runtimes/{runtimeId}/pause
POST   /runtimes/{runtimeId}/resume
POST   /runtimes/{runtimeId}/reset
GET    /runtimes/{runtimeId}/updates/preview
POST   /runtimes/{runtimeId}/updates
POST   /runtimes/{runtimeId}/asset-changes
GET    /runtimes/{runtimeId}/device-health
GET    /runtimes/{runtimeId}/status-check
POST   /runtimes/{runtimeId}/protocol-events
GET    /runtimes/{runtimeId}/grants
PUT    /runtimes/{runtimeId}/grants
```

`protocol-events` 接收设备程序已经生成的事件。TeamLab 不解析设备协议，也不从网络流量中推导协议事件。

### 13.3 访问与运维

```text
GET    /runtimes/{runtimeId}/access-grants
POST   /runtimes/{runtimeId}/access-grants
DELETE /runtimes/{runtimeId}/access-grants/{grantId}
GET    /runtimes/{runtimeId}/access-grants/{grantId}/download
GET    /runtimes/{runtimeId}/service-access
POST   /runtimes/{runtimeId}/assets/{assetId}/service-access
DELETE /runtimes/{runtimeId}/service-access/{accessId}
GET    /runtimes/{runtimeId}/assets/{assetId}/control
POST   /runtimes/{runtimeId}/assets/{assetId}/control
GET    /runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}
GET    /remote-sessions
GET    /runtimes/{runtimeId}/remote-access
POST   /runtimes/{runtimeId}/assets/{assetId}/remote-sessions
GET    /remote-sessions/{sessionId}
DELETE /remote-sessions/{sessionId}
POST   /remote-sessions/{sessionId}/connect
GET    /remote-sessions/{sessionId}/terminal
POST   /remote-sessions/{sessionId}/audit
GET    /remote-sessions/{sessionId}/audit
GET    /remote-sessions/{sessionId}/audit/evidence/{evidenceId}/download
GET    /runtimes/{runtimeId}/assets/{assetId}/files
GET    /runtimes/{runtimeId}/assets/{assetId}/files/download
POST   /runtimes/{runtimeId}/assets/{assetId}/files/upload
POST   /runtimes/{runtimeId}/assets/{assetId}/files/directories
POST   /runtimes/{runtimeId}/assets/{assetId}/files/move
DELETE /runtimes/{runtimeId}/assets/{assetId}/files
```

### 13.4 网络、观测和现场接入

```text
GET    /link-policies
POST   /link-policies
POST   /link-policies/{policyId}/recover
GET    /runtimes/{runtimeId}/traffic/flows
GET    /runtimes/{runtimeId}/traffic/paths
GET    /runtimes/{runtimeId}/traffic/paths/{pathId}
GET    /runtimes/{runtimeId}/captures
POST   /runtimes/{runtimeId}/captures
GET    /runtimes/{runtimeId}/captures/{captureId}
POST   /runtimes/{runtimeId}/captures/{captureId}/stop
GET    /runtimes/{runtimeId}/captures/{captureId}/download
GET    /connectors
POST   /connectors
GET    /connectors/{connectorId}
PUT    /connectors/{connectorId}
POST   /connectors/{connectorId}/archive
GET    /connectors/{connectorId}/health
POST   /connectors/{connectorId}/leases
POST   /connectors/{connectorId}/leases/release
GET    /connectors/nodes/{nodeId}/interfaces
GET    /resource-pools
GET    /resource-pools/node-cache
```

### 13.5 批量编排与通知

```text
GET    /rollouts
POST   /rollouts
GET    /rollouts/{rolloutId}
GET    /rollouts/{rolloutId}/targets
PUT    /rollouts/{rolloutId}/targets
POST   /rollouts/{rolloutId}/prepare
POST   /rollouts/{rolloutId}/open-access
POST   /rollouts/{rolloutId}/close-access
POST   /rollouts/{rolloutId}/pause
POST   /rollouts/{rolloutId}/resume
POST   /rollouts/{rolloutId}/drain
POST   /rollouts/{rolloutId}/archive
POST   /rollouts/{rolloutId}/targets/{targetId}/pause
POST   /rollouts/{rolloutId}/targets/{targetId}/resume
POST   /rollouts/{rolloutId}/targets/{targetId}/restart
POST   /rollouts/{rolloutId}/targets/{targetId}/rebuild
GET    /webhooks
POST   /webhooks
GET    /webhooks/{webhookId}
DELETE /webhooks/{webhookId}
POST   /webhooks/{webhookId}/replay
```

完整请求模型、枚举和响应字段以当前 [`open-v1.json`](openapi/open-v1.json) 为准。生成客户端时固定使用这份文件，不从示例反推类型。
