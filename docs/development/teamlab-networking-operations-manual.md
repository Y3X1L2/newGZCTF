# TeamLab 组网模块使用手册与接口说明

## 1. 整体说明

TeamLab 组网模块用来创建、发布和运行一套多网段网络演练环境。环境由网段、交换机/路由器等基础设施、Docker 容器和虚拟机等资产组成。平台负责把设计好的拓扑发布成不可变版本，再按版本部署成互相隔离的运行时。

对外提供两类使用入口：

1. 管理后台界面与浏览器管理接口：给平台内管理员、教师使用，前缀为 `api/admin/teamlab`。
2. 开放 API：给外部系统、自动化脚本、比赛/培训模块使用，前缀为 `api/open/v1/teamlab`。

外部系统应只调用开放 API。管理 API 由浏览器会话鉴权，不承诺版本兼容；内部节点回传接口只允许节点身份调用。

### 1.1 核心对象关系

```text
ControlScope（控制范围）
  └── Topology（拓扑草稿，可编辑）
        └── Release（不可变发布版本）
              ├── Plan（部署计划预览）
              ├── Runtime（单个运行时实例）
              │     ├── Event（事件）
              │     ├── AccessGrant（WireGuard 访问授权）
              │     ├── TrafficFlow / TrafficPath（流量与路径）
              │     ├── Capture（抓包任务）
              │     ├── LinkPolicy（链路策略）
              │     └── RemoteSession（远程会话）
              └── Rollout（批量部署单，面向多目标）
                    └── RolloutTarget（每个部署目标对应一个 Runtime）
```

控制范围用于隔离资源归属。外部 token 必须被授予某个 `teamlab-scope` 或通配 `teamlab-scope:*` 才能操作对应资源。

### 1.2 异步操作约定

创建拓扑、发布版本、镜像准备、创建运行时、重置、暂停、恢复、销毁、访问授权、抓包、rollout 生命周期、webhook 管理等写操作，大多先返回 `202 Accepted` 和 `ApiOperationModel`，实际任务在后台执行。

调用方拿到 `operation.id` 后，轮询：

```http
GET /api/open/v1/operations/{operationId}
Authorization: Bearer <token>
```

操作状态一般包括 `queued`、`running`、`completed`、`failed`、`cancelled`（以实际返回为准）。操作完成后，`resourceUrl` 或返回内容中的 `resourceId` 指向创建出的资源。不要用固定 sleep 猜测完成时间。

### 1.3 幂等键

所有非天然幂等的开放写接口（创建、更新、删除、发布、准备、生命周期命令、抓包、webhook 等）必须携带请求头：

```http
Idempotency-Key: <1-128位，仅字母数字-_ .>
```

- 同一身份 + 同一路由 + 同一幂等键 + 同一请求体：返回原 operation，不重复创建。
- 同一幂等键 + 不同请求体：返回 `409 idempotency_conflict`。
- 请求结果未知时，用原幂等键查询原任务，不要换新键反复提交。

### 1.4 分页

列表接口统一使用 cursor 分页：

```text
?limit=50&after=<上一页返回的 NextCursor / Next>
```

- `limit` 默认 50，开放接口最大 100；管理接口部分默认 30，最大会被服务端截断。
- 没有下一页时，`nextCursor` / `Next` 为 `null`。

### 1.5 错误格式

错误响应使用 `application/problem+json`：

```json
{
  "type": "https://docs.yinyu.example/problems/asset-in-use",
  "title": "Image template is in use",
  "status": 409,
  "code": "asset_in_use",
  "detail": "The image template is referenced by an active TeamLab release.",
  "traceId": "00-...",
  "errors": {}
}
```

按稳定 `code` 判断错误类型，不要依赖 `title`/`detail` 文案。常见 HTTP 状态：

- `400`：请求或游标/筛选无效
- `401`：身份无效
- `403`：权限不足或资源不可见
- `404`：资源不存在或不可见
- `409`：版本冲突、状态冲突、幂等冲突、rollout 未排空等
- `422`：内容不符合要求
- `429`：限流
- `503`：依赖服务不可用

### 1.6 认证与权限 Scope

开放 API 使用 Bearer token：

```http
Authorization: Bearer <token>
Content-Type: application/json
```

与 TeamLab 相关的 scope 如下：

| Scope | 开放能力 |
| --- | --- |
| `teamlab.topologies:read` | 查能力、控制范围、拓扑、版本、rollout 读侧 |
| `teamlab.topologies:write` | 创建/更新/删除拓扑、发布、归档、rollout 写侧 |
| `teamlab.runtimes:read` | 查运行时、事件、镜像准备状态、下载访问配置 |
| `teamlab.runtimes:write` | 创建/重置/销毁/暂停/恢复运行时、访问授权、准备镜像 |
| `teamlab.traffic:read` | 查流量记录、路径 |
| `teamlab.capture:read` | 查/下载抓包 |
| `teamlab.capture:write` | 开始/停止抓包 |
| `teamlab.resource-pools:read` | 查资源池、节点缓存 |
| `teamlab.device-packages:read` | 查设备包 |
| `teamlab.connectors:read` | 查连接器 |
| `teamlab.connectors:write` | 占用/释放连接器 |
| `teamlab.link-policies:read` | 查链路策略 |
| `teamlab.link-policies:write` | 应用/恢复链路策略 |
| `teamlab.remote-sessions:read` | 查远程会话与可用性 |
| `teamlab.remote-sessions:write` | 创建/关闭远程会话 |

除 scope 外，token 还必须有对应资源的 `teamlab-scope` grant。管理员可用通配 `teamlab-scope:*`；普通 token 只能被授予具体 scope。scope 归档后只能读不能写。

### 1.7 枚举序列化说明

本平台开放 API 的 JSON 序列化默认不把枚举转字符串。请求与响应中的枚举字段，除单独标注字符串的自定义枚举外，都使用**数字值**。涉及到的关键枚举见本文末尾“枚举速查”。

---

## 2. 开放 API 总览

| 分组 | 前缀 |
| --- | --- |
| 能力与控制范围 | `/api/open/v1/teamlab/capabilities`、`/api/open/v1/teamlab/scopes` |
| 拓扑与发布 | `/api/open/v1/teamlab/topologies` |
| 镜像准备 | `/api/open/v1/teamlab/preparations` |
| Rollout 批量部署 | `/api/open/v1/teamlab/rollouts` |
| 运行时 | `/api/open/v1/teamlab/runtimes` |
| 流量与抓包 | `/api/open/v1/teamlab/runtimes/{runtimeId}/traffic`、`/captures` |
| 链路策略 | `/api/open/v1/teamlab/link-policies` |
| 连接器 | `/api/open/v1/teamlab/connectors` |
| 设备包 | `/api/open/v1/teamlab/device-packages` |
| 资源池 | `/api/open/v1/teamlab/resource-pools` |
| 远程会话 | `/api/open/v1/teamlab/runtimes/{runtimeId}/remote-access`、`/api/open/v1/teamlab/remote-sessions` |
| Webhook | `/api/open/v1/teamlab/webhooks` |
| 操作查询 | `/api/open/v1/operations` |

> 管理端接口见第 12 章；Agent 内部回传接口见第 13 章；异步操作查询见 11.4。

---

## 3. 能力与控制范围

### 3.1 查询平台能力

```http
GET /api/open/v1/teamlab/capabilities
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

响应模型：

```json
{
  "apiVersion": "1",
  "topologySchemaVersions": [2],
  "assetKinds": [0, 1],
  "networkModel": "ovn",
  "features": {
    "multiNode": true,
    "linuxVm": true,
    "windowsVm": true,
    "trafficFlows": true,
    "onDemandPcap": true,
    "editorLayout": true,
    "editorLayoutVersion": 1,
    "networkRegions": true,
    "rollouts": true,
    "pauseResume": true
  },
  "limits": {
    "networksPerTopology": 32,
    "assetsPerTopology": 128,
    "interfacesPerAsset": 8
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `apiVersion` | string | 当前 API 版本 |
| `topologySchemaVersions` | int[] | 支持的拓扑 schema 版本 |
| `assetKinds` | int[] | 支持的资产类型，0=Docker，1=VM |
| `networkModel` | string | 网络模型标识 |
| `features` | object | 能力开关 |
| `features.multiNode` | bool | 是否支持跨节点组网 |
| `features.linuxVm` | bool | 是否支持 Linux 虚拟机 |
| `features.windowsVm` | bool | 是否支持 Windows 虚拟机 |
| `features.trafficFlows` | bool | 是否采集流量 |
| `features.onDemandPcap` | bool | 是否支持按需抓包 |
| `features.editorLayout` | bool | 是否保存画布布局 |
| `features.editorLayoutVersion` | int | 画布布局版本 |
| `features.networkRegions` | bool | 是否支持网络区域 |
| `features.rollouts` | bool | 是否支持批量 rollout |
| `features.pauseResume` | bool | 是否支持暂停/恢复 |
| `limits` | object | 数量限制 |
| `limits.networksPerTopology` | int | 单拓扑最大网段数 |
| `limits.assetsPerTopology` | int | 单拓扑最大资产数 |
| `limits.interfacesPerAsset` | int | 单资产最大网卡数 |

### 3.2 控制范围

控制范围（ControlScope）用于把拓扑、release、runtime、连接器等资源划归某个外部业务边界。创建 scope 只有管理员创建的 token 可执行。

#### 列出当前 token 可见的控制范围

```http
GET /api/open/v1/teamlab/scopes
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

响应：

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "key": "customer-a",
    "displayName": "客户 A",
    "archived": false,
    "createdAt": "2026-08-19T10:00:00+00:00",
    "updatedAt": "2026-08-19T10:00:00+00:00"
  }
]
```

#### 创建控制范围

```http
POST /api/open/v1/teamlab/scopes
Authorization: Bearer <token>
Scope: teamlab.topologies:write
```

请求体：

```json
{
  "key": "customer-a",
  "displayName": "客户 A"
}
```

响应 `201 Created`：

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "key": "customer-a",
  "displayName": "客户 A",
  "archived": false,
  "createdAt": "2026-08-19T10:00:00+00:00",
  "updatedAt": "2026-08-19T10:00:00+00:00"
}
```

#### 归档控制范围

```http
POST /api/open/v1/teamlab/scopes/{scopeId}/archive
Authorization: Bearer <token>
Scope: teamlab.topologies:write
```

归档后 scope 只读，不再接受新写入；重复归档幂等。只有管理员 token 可归档。成功返回 `204 No Content`。

---

## 4. 拓扑与发布

拓扑是一份可编辑草稿。创建、更新返回异步操作。更新是整体替换，必须携带最新 `revision`。

### 4.1 拓扑定义字段

#### 创建/更新拓扑公共字段

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `name` | string | 是 | 场景名称 |
| `schemaVersion` | int | 是 | 当前固定为 2 |
| `networks` | array | 是 | 网段列表 |
| `assets` | array | 是 | 资产列表 |
| `connections` | array | 是 | 网段/路由连接列表 |
| `editor` | object | 否 | 画布布局（坐标、尺寸、折叠） |
| `infrastructure` | array | 否 | 交换机/路由器基础设施节点 |
| `dependencies` | array | 否 | 资产启动依赖 |
| `observation` | object | 否 | 观测策略 |
| `controlScopeId` | guid | 创建时可选 | 归属控制范围；管理员可用 |

更新模型额外要求 `revision` 为当前修订号；更新不允许改 `controlScopeId`。

#### 网段对象 `networks[]`

```json
{
  "key": "net-entry",
  "name": "入口网段",
  "addressPool": {
    "poolCidr": "10.80.1.0/24",
    "runtimePrefixLength": 24
  },
  "isEntry": true,
  "orderIndex": 0
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `key` | string | 是 | 唯一标识，用于资产/连接引用 |
| `name` | string | 是 | 显示名 |
| `addressPool.poolCidr` | string | 是 | 地址池 CIDR |
| `addressPool.runtimePrefixLength` | int | 是 | 每个运行时网段前缀长度 |
| `isEntry` | bool | 是 | 是否入口网段（选手接入点） |
| `orderIndex` | int | 否 | 排序，默认 0 |

#### 资产对象 `assets[]`

```json
{
  "key": "plc",
  "name": "PLC",
  "kind": 0,
  "imageTemplateId": 116,
  "resources": {
    "cpuUnits": 1,
    "memoryMiB": 256,
    "storageMiB": 256
  },
  "interfaces": [
    {
      "key": "plc-eth0",
      "networkKey": "net-entry",
      "hostOffset": 10,
      "primary": true,
      "orderIndex": 0
    }
  ],
  "exposePort": 502,
  "healthCheck": {
    "kind": 1,
    "port": 80
  },
  "orderIndex": 0,
  "endpointObservation": 2,
  "devicePackageId": null,
  "deviceParameters": null,
  "connectorId": null
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `key` | string | 是 | 资产唯一标识 |
| `name` | string | 是 | 资产名称 |
| `kind` | int | 是 | `0`=Docker，`1`=VM；Linux/Windows 由镜像模板决定 |
| `imageTemplateId` | int | 是 | 镜像模板 ID |
| `resources.cpuUnits` | int | 是 | CPU 份额 |
| `resources.memoryMiB` | int | 是 | 内存 MiB |
| `resources.storageMiB` | int | 是 | 存储 MiB |
| `interfaces[]` | array | 是 | 网卡列表 |
| `interfaces[].key` | string | 是 | 网卡标识 |
| `interfaces[].networkKey` | string | 是 | 所在网段 key |
| `interfaces[].hostOffset` | int | 是 | 主机位偏移，决定 IP 末位 |
| `interfaces[].primary` | bool | 是 | 是否主网卡 |
| `interfaces[].orderIndex` | int | 否 | 排序 |
| `exposePort` | int | 否 | 对外暴露端口 |
| `healthCheck.kind` | int | 否 | 健康检查类型，`0`=TCP，`1`=HTTP |
| `healthCheck.port` | int | 否 | 健康检查端口 |
| `orderIndex` | int | 否 | 排序 |
| `endpointObservation` | int | 否 | `0`=Disabled，`1`=Optional，`2`=Required |
| `devicePackageId` | int | 否 | 设备包 ID（虚实结合） |
| `deviceParameters` | object | 否 | 设备包参数，JSON 对象 |
| `connectorId` | guid | 否 | 关联现场连接器 |

#### 连接对象 `connections[]`

```json
{
  "key": "route-entry-core",
  "fromNetworkKey": "net-entry",
  "toNetworkKey": "net-core",
  "viaAssetKey": null,
  "viaNodeKey": null,
  "direction": 1
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `key` | string | 是 | 连接唯一标识 |
| `fromNetworkKey` | string | 是 | 起始网段 key |
| `toNetworkKey` | string | 是 | 目标网段 key |
| `viaAssetKey` | string | 否 | 经过的资产（如路由器资产 key） |
| `viaNodeKey` | string | 否 | 经过的基础设施节点 key |
| `direction` | int | 否 | `0`=FromTo（单向），`1`=Bidirectional（双向）；缺省时由实现决定 |

#### 基础设施对象 `infrastructure[]`

```json
{
  "key": "sw-core",
  "name": "核心交换机",
  "kind": 0,
  "networkKey": "net-core",
  "interfaces": [
    {
      "key": "sw-core-eth0",
      "networkKey": "net-core",
      "hostOffset": 1,
      "primary": true,
      "orderIndex": 0
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `key` | string | 是 | 基础设施标识 |
| `name` | string | 是 | 显示名 |
| `kind` | int | 是 | `0`=ManagedSwitch，`1`=ManagedRouter |
| `networkKey` | string | 否 | 所属网段（交换机通常必填） |
| `interfaces[]` | array | 是 | 网卡列表，结构与资产网卡相同 |

#### 依赖对象 `dependencies[]`

```json
{
  "assetKey": "scada",
  "dependsOnKey": "plc",
  "condition": 1
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `assetKey` | string | 是 | 后启动资产 |
| `dependsOnKey` | string | 是 | 先决资产 |
| `condition` | int | 是 | `0`=NetworkReady，`1`=GuestReady，`2`=ServiceReady，`3`=BootstrapCompleted |

#### 观测策略对象 `observation`

```json
{
  "flowMetadataEnabled": true,
  "onDemandPcapEnabled": true,
  "endpointObservation": 1
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `flowMetadataEnabled` | bool | 否 | 是否采集流量元数据，默认 true |
| `onDemandPcapEnabled` | bool | 否 | 是否允许按需抓包，默认 true |
| `endpointObservation` | int | 否 | 端点观测模式，`0`=Disabled，`1`=Optional，`2`=Required |

#### 编辑器布局对象 `editor`

```json
{
  "networks": {
    "net-entry": { "x": 100, "y": 100, "width": 240, "height": 160, "collapsed": false }
  },
  "assets": {
    "plc": { "x": 140, "y": 140, "width": 120, "height": 80, "collapsed": false }
  },
  "infrastructure": {
    "sw-core": { "x": 300, "y": 120, "width": 120, "height": 80, "collapsed": false }
  }
}
```

每个节点布局对象字段：`x`、`y` 必填，`width`、`height`、`collapsed` 可选。

### 4.2 创建拓扑

```http
POST /api/open/v1/teamlab/topologies
Authorization: Bearer <token>
Idempotency-Key: topo-create-001
Scope: teamlab.topologies:write
```

请求体为“拓扑定义公共字段”完整对象。示例见第 14 章。

响应 `202 Accepted`：

```json
{
  "id": "019...",
  "status": "queued",
  "resourceUrl": "/api/open/v1/operations/019..."
}
```

### 4.3 列出拓扑

```http
GET /api/open/v1/teamlab/topologies?limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

查询参数：

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `limit` | int | 否 | 1-100，默认 50 |
| `after` | string | 否 | 上一页游标 |

响应 `200 OK`：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "controlScopeId": null,
      "name": "two-net-demo",
      "revision": 3,
      "schemaVersion": 2,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "updatedAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "nextCursor": null
}
```

### 4.4 获取拓扑详情

```http
GET /api/open/v1/teamlab/topologies/{topologyId}
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

响应 `200 OK`：

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "controlScopeId": null,
  "revision": 3,
  "schemaVersion": 2,
  "definition": {
    "name": "two-net-demo",
    "networks": [],
    "assets": [],
    "connections": [],
    "infrastructure": [],
    "dependencies": [],
    "observation": null
  },
  "editor": {
    "networks": {},
    "assets": {},
    "infrastructure": {}
  },
  "createdAt": "2026-08-19T10:00:00+00:00",
  "updatedAt": "2026-08-19T10:00:00+00:00"
}
```

### 4.5 更新拓扑

```http
PUT /api/open/v1/teamlab/topologies/{topologyId}
Authorization: Bearer <token>
Idempotency-Key: topo-update-001
Scope: teamlab.topologies:write
```

请求体为“更新拓扑公共字段”，必须包含最新 `revision`。

### 4.6 删除拓扑

```http
DELETE /api/open/v1/teamlab/topologies/{topologyId}
Authorization: Bearer <token>
Idempotency-Key: topo-delete-001
Scope: teamlab.topologies:write
```

响应 `202 Accepted` + operation。

### 4.7 校验拓扑

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/validate
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

无请求体。响应 `200 OK`：

```json
{
  "valid": true,
  "issues": []
}
```

校验失败时：

```json
{
  "valid": false,
  "issues": [
    {
      "code": "network_no_entry",
      "path": "networks[0]",
      "message": "场景必须包含入口网段"
    }
  ]
}
```

### 4.8 克隆拓扑

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/clone
Authorization: Bearer <token>
Scope: teamlab.topologies:write
```

成功 `201 Created`，`Location` 指向新拓扑，响应为新拓扑详情。

### 4.9 发布不可变版本

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/releases
Authorization: Bearer <token>
Idempotency-Key: topo-release-001
Scope: teamlab.topologies:write
```

请求体：

```json
{
  "revision": 3
}
```

`revision` 必须与当前草稿修订一致。响应 `202 Accepted` + operation；操作完成后取得 `releaseId`。

### 4.10 列出版本

```http
GET /api/open/v1/teamlab/topologies/{topologyId}/releases?limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

响应 `200 OK`：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "topologyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "version": 1,
      "sourceRevision": 3,
      "schemaVersion": 2,
      "contentHash": "sha256...",
      "publishedAt": "2026-08-19T10:00:00+00:00",
      "editor": null,
      "archived": false,
      "publisherName": "admin"
    }
  ],
  "nextCursor": null
}
```

### 4.11 获取单个版本

```http
GET /api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

响应结构与列表项一致，并包含不可变 `editor`（若发布时保存）。

### 4.12 生成部署计划

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}/plan
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

无请求体。响应 `200 OK`：

```json
{
  "topologyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "networks": [
    {
      "key": "net-entry",
      "name": "入口网段",
      "candidateCidr": "10.80.1.0/24",
      "isEntry": true
    }
  ],
  "assets": [
    {
      "key": "plc",
      "name": "PLC",
      "kind": 0,
      "imageTemplateId": 116,
      "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 256 },
      "interfaces": [
        { "key": "plc-eth0", "networkKey": "net-entry", "hostOffset": 10, "primary": true }
      ]
    }
  ],
  "shards": [
    {
      "key": "shard-1",
      "networkKeys": ["net-entry"],
      "assetKeys": ["plc"],
      "dockerSlots": 1,
      "vmSlots": 0,
      "infrastructureKeys": ["sw-core"]
    }
  ],
  "crossShardConnections": 1,
  "requiredCapabilities": ["docker", "teamlab.ovs-ovn.v1"],
  "warnings": [],
  "planHash": "abc...",
  "managedInfrastructureCount": 1,
  "observationPointEstimate": 4
}
```

### 4.13 归档版本

```http
POST /api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}/archive
Authorization: Bearer <token>
Scope: teamlab.topologies:write
```

归档后版本只读，不能再创建新运行时；已有运行时不中断；重复归档幂等。成功 `204 No Content`。

---

## 5. 镜像准备

镜像准备接口围绕“某个 release 在可调度节点上是否已就绪”。

### 5.1 查询镜像准备状态

```http
GET /api/open/v1/teamlab/preparations/releases/{releaseId}
Authorization: Bearer <token>
Scope: teamlab.runtimes:read
```

响应 `200 OK`：

```json
{
  "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "state": "readyToStart",
  "planAvailable": true,
  "readyToStart": true,
  "blockers": [],
  "images": [
    {
      "templateId": 116,
      "templateName": "modbus-slave",
      "imageType": "Docker",
      "eligibleNodeCount": 2,
      "readyNodeCount": 2,
      "preparingNodeCount": 0,
      "failedNodeCount": 0,
      "failure": null
    }
  ]
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `releaseId` | guid | 发布版本 |
| `state` | string | `planAvailable` / `preparing` / `readyToStart` / `blocked` |
| `planAvailable` | bool | 计划是否可用 |
| `readyToStart` | bool | 是否可开始部署 |
| `blockers` | string[] | 阻断原因列表 |
| `images[]` | array | 每个镜像模板的就绪统计 |
| `images[].templateId` | int | 镜像模板 ID |
| `images[].templateName` | string | 模板名称 |
| `images[].imageType` | string | 镜像类型 |
| `images[].eligibleNodeCount` | int | 符合条件节点数 |
| `images[].readyNodeCount` | int | 已就绪节点数 |
| `images[].preparingNodeCount` | int | 准备中节点数 |
| `images[].failedNodeCount` | int | 失败节点数 |
| `images[].failure` | object | 失败详情或 null |

### 5.2 提交镜像准备

```http
POST /api/open/v1/teamlab/preparations/releases/{releaseId}
Authorization: Bearer <token>
Idempotency-Key: prep-001
Scope: teamlab.runtimes:write
```

无请求体。响应 `202 Accepted` + operation。幂等，适合发布后显式准备或失败后重试。

### 5.3 释放镜像准备引用

```http
DELETE /api/open/v1/teamlab/preparations/releases/{releaseId}
Authorization: Bearer <token>
Idempotency-Key: prep-release-001
Scope: teamlab.runtimes:write
```

无请求体。响应 `202 Accepted` + operation。幂等，停止继续保留准备状态。

---

## 6. Rollout 批量部署

Rollout 管理一组目标（targets），每个目标最终对应一个 runtime，适合“为 N 支队伍部署同一版本”。

### 6.1 列出 Rollout

```http
GET /api/open/v1/teamlab/rollouts?scopeId={scopeId}&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `scopeId` | guid | 是 | 控制范围 ID |
| `limit` | int | 否 | 1-100，默认 50 |
| `after` | string | 否 | 游标 |

响应 `200 OK`：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "status": "active",
      "preparationRequested": true,
      "desiredAccessOpen": false,
      "drainRequested": false,
      "pauseRequested": false,
      "counts": {
        "total": 2,
        "pending": 0,
        "provisioning": 1,
        "ready": 1,
        "accessOpen": 0,
        "failed": 0,
        "draining": 0,
        "destroyed": 0,
        "paused": 0
      },
      "preparedAt": null,
      "accessOpenedAt": null,
      "drainingAt": null,
      "completedAt": null,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "updatedAt": "2026-08-19T10:00:00+00:00",
      "error": null,
      "controlScopeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "adapterKind": "external",
      "externalReference": "match-20260819",
      "revision": 0
    }
  ],
  "nextCursor": null
}
```

### 6.2 创建 Rollout

```http
POST /api/open/v1/teamlab/rollouts
Authorization: Bearer <token>
Idempotency-Key: rollout-001
Scope: teamlab.topologies:write
```

请求体：

```json
{
  "controlScopeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "releaseId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "externalReference": "match-20260819",
  "targets": [
    {
      "externalSubject": "team-01",
      "displayName": "红队一号"
    },
    {
      "externalSubject": "team-02",
      "displayName": "红队二号"
    }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `controlScopeId` | guid | 是 | 控制范围 |
| `releaseId` | guid | 是 | 发布版本 |
| `externalReference` | string | 是 | 外部关联编号 |
| `targets[].externalSubject` | string | 是 | 外部主体标识（队伍/用户） |
| `targets[].displayName` | string | 是 | 展示名 |

响应 `202 Accepted` + operation，轮询取得 `rolloutId`。

### 6.3 获取 Rollout

```http
GET /api/open/v1/teamlab/rollouts/{rolloutId}
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

响应同 6.1 中单项结构。

### 6.4 列出 Rollout Targets

```http
GET /api/open/v1/teamlab/rollouts/{rolloutId}/targets?limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "externalSubject": "team-01",
      "displayName": "红队一号",
      "runtimeId": null,
      "status": "pending",
      "operationId": null,
      "runtimeStatus": null,
      "runtimeStage": null,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "updatedAt": "2026-08-19T10:00:00+00:00",
      "error": null
    }
  ],
  "nextCursor": null
}
```

### 6.5 替换 Rollout Targets

```http
PUT /api/open/v1/teamlab/rollouts/{rolloutId}/targets
Authorization: Bearer <token>
Idempotency-Key: rollout-targets-001
Scope: teamlab.topologies:write
```

请求体：

```json
{
  "targets": [
    {
      "externalSubject": "team-01",
      "displayName": "红队一号"
    }
  ]
}
```

被移除的 target 在显式清理前保持不变。

### 6.6 生命周期命令

以下命令均返回 `202 Accepted` + operation。

| 操作 | 方法与路径 | 所需 scope | 说明 |
| --- | --- | --- | --- |
| 准备 | `POST /rollouts/{rolloutId}/prepare` | topologies:write | 启动镜像准备与 target 供给协调 |
| 开放访问 | `POST /rollouts/{rolloutId}/open-access` | runtimes:write | 所有期望 targets 就绪后才开放 |
| 关闭访问 | `POST /rollouts/{rolloutId}/close-access` | runtimes:write | 关闭玩家访问，不销毁 |
| 排空 | `POST /rollouts/{rolloutId}/drain` | runtimes:write | 关闭访问并按有界批次销毁所有 target |
| 暂停协调 | `POST /rollouts/{rolloutId}/pause` | topologies:write | 暂停协调，已提交目标不变 |
| 恢复协调 | `POST /rollouts/{rolloutId}/resume` | topologies:write | 恢复协调 |
| 归档 | `POST /rollouts/{rolloutId}/archive` | topologies:write | 归档已完全清理的 rollout |

每个命令必须携带 `Idempotency-Key`。

### 6.7 单 Target 命令

| 操作 | 方法与路径 | 所需 scope | 说明 |
| --- | --- | --- | --- |
| 重建 | `POST /rollouts/{rolloutId}/targets/{targetId}/rebuild` | runtimes:write | 重建失败 target |
| 暂停 | `POST /rollouts/{rolloutId}/targets/{targetId}/pause` | runtimes:write | 暂停单 target |
| 恢复 | `POST /rollouts/{rolloutId}/targets/{targetId}/resume` | runtimes:write | 恢复单 target |
| 重启 | `POST /rollouts/{rolloutId}/targets/{targetId}/restart` | runtimes:write | 按原版本重启单 target |

均返回 `202 Accepted` + operation。

---

## 7. 运行时与访问授权

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
  "executionModel": 1,
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

## 9. 连接器与设备包

### 9.0 本地在研执行协议（2026-09-08，未发布）

设备包目录返回 `id`（公共 UUID）和 `bindingId`（拓扑现行数字绑定值）。编辑器使用 `bindingId` 保存绑定，选择设备包时按制品摘要匹配同类型镜像，并提高资源配置至设备包最低要求；没有匹配镜像时明确提示先导入镜像。目录中的 `memoryMib` 与 Unix 毫秒时间在 feature adapter 内统一转换，不在页面里猜测字段名或时间格式。

发布和运行规划均校验设备包制品摘要、镜像类型、最低资源和参数 schema。schema 只允许包内引用。常见字符串、数字、整数、布尔值和枚举提供参数表单；复杂声明和导入的无效 JSON 保留高级编辑入口，不静默丢弃草稿。

设备包的程序必须实现以下参数消费协议，不能把参数已传输等同于设备业务已执行：

- Docker：读取环境变量 `GZCTF_DEVICE_PARAMETERS`，内容是经校验的 JSON 对象。
- VM：读取 QEMU fw_cfg 的 `opt/org.gzctf/device-parameters`。Linux 支持相应驱动时可从 `/sys/firmware/qemu_fw_cfg/by_name/opt/org.gzctf/device-parameters/raw` 读取；其他来宾需设备程序自行适配。
- 参数仅用于公开的设备配置，不填写密码或令牌。参数和设备包身份进入不可变执行快照，重建复用该快照。
- 声明的 TCP/HTTP 健康检查进入现有启动就绪检查；目前尚不能把 `intervalSeconds` 或 `protocolEventTypes` 的目录声明视作持续健康监督或协议事件采集已实现。

本地已验证真实 libvirt 接受 fw_cfg 配置并转换为 QEMU 参数；尚未完成真实设备程序在 Docker/VM 中读取参数、执行业务和持续健康/协议事件验收。连接器的真实 provider 生命周期仍待补，目录与租约不代表已经接通物理设备。

### 9.0.1 运行检查与 NAT 修复边界

运行详情“资产运维”新增“运行状态检查”：主动读取节点 inventory，展示资产期望状态、实际状态、缺失、身份冲突和节点不可查询状态。检查不写数据库、不回收资源；可处理的差异需要确认，提交前重新检查，再进入原资产操作队列，结果仍在资产任务区查看。网络/连接器差异与旧代残留的显式清理尚未覆盖。

NAT 执行改为按 runtime、代次、网络摘要和网关端口定位 OVN 路由器，使用 V2 的 `OvnNorthboundEndpoint`；禁止采用“第一个路由器”或写死的测试节点。DNAT 端口入口通过 OVN VIP load balancer 实现，避免同端口映射错误地开放整个地址。映射携带原网络归属，并纳入执行计划的清理流程。

链路策略新增 nullable `Generation`。旧行保持未知代次，不能自动套用当前代；需要按原执行记录核实。定时恢复和销毁后的策略清理必须实际请求执行端，失败保留可见状态和重试资格，不再只更新数据库为“已恢复”。完整的端口分配、并发冲突管理、可达探测与服务入口产品链路仍待完成。真实 OVN NB 验证只证明控制面规则与精确清理，不证明实际网络流量可达。

连接器和设备包用于把现场真实/半实物设备接入网络场景。外部系统只读写目录与租约，不接触设备接入地址。

### 9.1 设备包

#### 列出设备包

```http
GET /api/open/v1/teamlab/device-packages?name=&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.device-packages:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `name` | string | 否 | 按名称过滤 |
| `limit` | int | 否 | 默认 50 |
| `after` | string | 否 | 游标 |

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "modbus-slave",
      "displayName": "MODBUS 从站模拟器",
      "version": "1.0.0",
      "artifactKind": "oci-image",
      "artifactReference": "10.0.7.118:5000/labs/modbus-slave:v1",
      "digest": "sha256:...",
      "description": "MODBUS TCP 从站",
      "supportedAssetKinds": ["docker"],
      "cpuMillis": 1000,
      "memoryMib": 256,
      "storageGib": 1,
      "ports": [
        {
          "name": "modbus",
          "port": 502,
          "protocol": "tcp"
        }
      ],
      "parameterSchema": null,
      "healthDeclaration": null,
      "protocolEventTypes": ["modbus.read", "modbus.write"],
      "enabled": true,
      "archived": false,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "updatedAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "next": null
}
```

注意列表分页字段是 `next`（不是 `nextCursor`）。

#### 获取设备包

```http
GET /api/open/v1/teamlab/device-packages/{packageId}
Authorization: Bearer <token>
Scope: teamlab.device-packages:read
```

响应结构同上单项。

### 9.2 连接器

#### 列出连接器

```http
GET /api/open/v1/teamlab/connectors?scopeId={scopeId}&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.connectors:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `scopeId` | guid | 否 | 控制范围过滤；不传返回平台级+已授权范围 |
| `limit` | int | 否 | 默认 50 |
| `after` | string | 否 | 游标 |

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "sim-plc",
      "displayName": "模拟 PLC",
      "kind": "managed-nic",
      "controlScopeId": null,
      "supportsSharedUse": false,
      "capacity": 1,
      "occupiedSlots": 1,
      "activeLeases": [
        {
          "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "connectorId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "slot": 1,
          "acquiredAt": "2026-08-19T10:00:00+00:00",
          "releasedAt": null,
          "releaseReason": "none"
        }
      ],
      "health": "healthy",
      "healthObservedAt": "2026-08-19T10:00:00+00:00",
      "description": null,
      "archived": false,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "updatedAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "next": null
}
```

`kind` 取值：`managed-nic`、`vlan`、`segment`、`serial`、`usb-gateway`、`dedicated-network`。
`health` 取值：`unknown`、`healthy`、`degraded`、`unreachable`。
`releaseReason` 取值：`none`、`manual-release`、`runtime-destroyed`、`admin-revoked`、`node-lost`。

#### 获取连接器

```http
GET /api/open/v1/teamlab/connectors/{connectorId}?scopeId={scopeId}
Authorization: Bearer <token>
Scope: teamlab.connectors:read
```

#### 占用连接器

```http
POST /api/open/v1/teamlab/connectors/{connectorId}/leases
Authorization: Bearer <token>
Content-Type: application/json
Scope: teamlab.connectors:write
```

请求体：

```json
{
  "runtimeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

独占连接器同一时间只属于一个运行时；重复申请幂等返回。成功 `201 Created`，响应为租约对象。

#### 释放连接器

```http
POST /api/open/v1/teamlab/connectors/{connectorId}/leases/release
Authorization: Bearer <token>
Content-Type: application/json
Scope: teamlab.connectors:write
```

请求体同上。重复释放幂等。成功 `200 OK`，响应为租约对象。

---

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
| `confidence` | string | 否 | `packet-exact`、`process-correlated`、`temporally-related` |

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

## 11. 资源池、远程会话与 Webhook

### 11.1 资源池

#### 查询资源池快照

```http
GET /api/open/v1/teamlab/resource-pools
Authorization: Bearer <token>
Scope: teamlab.resource-pools:read
```

响应 `200 OK`：

```json
{
  "computeNodes": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "node-118",
      "status": "online",
      "schedulable": true,
      "dockerCapable": true,
      "kvmCapable": true,
      "teamLabNetworkEnabled": true,
      "fabricStatus": "healthy",
      "currentContainers": 2,
      "maxContainers": 50,
      "currentVms": 1,
      "maxVms": 20,
      "cpuLoadPercent": 23.5,
      "memoryLoadPercent": 41.2,
      "agentVersion": "1.2.3",
      "lastHeartbeat": "2026-08-19T10:00:00+00:00",
      "metricObservedAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "templates": [
    {
      "id": 116,
      "name": "modbus-slave",
      "osType": "Linux",
      "imageType": "Docker",
      "status": "Ready",
      "fileSizeBytes": 104857600,
      "digest": "sha256:...",
      "supportsInstanceCredentials": false,
      "uploadedAt": "2026-08-19T10:00:00+00:00"
    }
  ]
}
```

#### 查询节点制品缓存

```http
GET /api/open/v1/teamlab/resource-pools/node-cache?limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.resource-pools:read
```

响应：

```json
{
  "items": [
    {
      "templateId": 116,
      "nodeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "imageHash": "sha256:...",
      "status": "ready",
      "operation": "distribute",
      "stage": "completed",
      "attemptCount": 1,
      "activeReferenceCount": 2,
      "lastErrorCode": null,
      "progressUpdatedAt": "2026-08-19T10:00:00+00:00"
    }
  ],
  "next": null
}
```

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

### 11.3 Webhook

Webhook 用于把 TeamLab 事件推送到外部 HTTPS 端点。投递为至少一次，带 HMAC-SHA256 签名。端点必须可公网解析且为 HTTPS。

#### 创建 Webhook

```http
POST /api/open/v1/teamlab/webhooks
Authorization: Bearer <token>
Content-Type: application/json
Idempotency-Key: webhook-001
Scope: teamlab.topologies:write
```

请求体：

```json
{
  "controlScopeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "endpointUrl": "https://your-server.example.com/teamlab-events",
  "eventTypes": ["runtime.ready", "rollout.ready", "runtime.failed"],
  "enabled": true,
  "fromEventId": null
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `controlScopeId` | guid | 是 | 控制范围 |
| `endpointUrl` | string | 是 | HTTPS 公网可达端点，最长 2048 |
| `eventTypes` | string[] | 是 | 订阅事件类型，见下 |
| `enabled` | bool | 否 | 是否启用，默认 true |
| `fromEventId` | long | 否 | 起始事件游标 |

服务端已知事件类型（当前源码白名单）：

```text
deploy, reset, destroy, ready, pause, resume,
cleanup, fabric, bootstrap, network, route, probe,
infrastructure, access, remote-access,
capture, capture-expiry, capture-upload, capture-download,
observation, sensor-authentication, operation
```

传入白名单之外的值会返回 `422 webhook_event_type_invalid`。若目标环境的测试文档/线上 OpenAPI 出现点分事件名（如 `runtime.ready`），说明该部署包含归一化兼容层；实际对接以该环境 OpenAPI 描述为准。

响应 `202 Accepted` + operation；操作结果包含一次性 `signingSecret`，只返回一次。

#### 列出 Webhook

```http
GET /api/open/v1/teamlab/webhooks?scopeId={scopeId}&limit=50&after=<cursor>
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `scopeId` | guid | 是 | 控制范围 |
| `limit` | int | 否 | 1-100 |
| `after` | string | 否 | 游标 |

响应：

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "controlScopeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "endpointUrl": "https://your-server.example.com/teamlab-events",
      "eventTypes": ["runtime.ready"],
      "active": true,
      "deliveryCursor": 1024,
      "consecutiveFailures": 0,
      "nextDeliveryAt": null,
      "createdAt": "2026-08-19T10:00:00+00:00",
      "revokedAt": null,
      "recentFailures": []
    }
  ],
  "nextCursor": null
}
```

#### 获取 Webhook

```http
GET /api/open/v1/teamlab/webhooks/{webhookId}
Authorization: Bearer <token>
Scope: teamlab.topologies:read
```

#### 撤销 Webhook

```http
DELETE /api/open/v1/teamlab/webhooks/{webhookId}
Authorization: Bearer <token>
Idempotency-Key: webhook-revoke-001
Scope: teamlab.topologies:write
```

停止后续投递，已排队投递不回滚。响应 `202 Accepted` + operation。

#### 重放 Webhook

```http
POST /api/open/v1/teamlab/webhooks/{webhookId}/replay?fromEventId=100
Authorization: Bearer <token>
Idempotency-Key: webhook-replay-001
Scope: teamlab.topologies:write
```

| 查询参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `fromEventId` | long | 否 | 起始事件 ID；不传从当前游标重放 |

响应 `202 Accepted` + operation。重放不推进投递游标，不创建新的业务操作。

### 11.4 查询异步操作

```http
GET /api/open/v1/operations/{operationId}
Authorization: Bearer <token>
Scope: operations:read
```

响应模型：

```json
{
  "id": "019...",
  "kind": "teamlab.runtime.create",
  "status": "running",
  "resourceType": "teamlab-runtime",
  "resourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "stage": "deploying",
  "currentProgress": 2,
  "totalProgress": 5,
  "errorCode": null,
  "errorDetail": null,
  "result": null,
  "createdAt": 1783641600000,
  "updatedAt": 1783641600000
}
```

`status` 字段以实际返回为准（如 `queued`、`running`、`completed`、`failed`）。轮询直到终态；断线恢复也使用该接口，不要重新提交写操作。

除 `operations:read` scope 外，查询操作还需要具备对应操作的 resource grant（`operation:{operationId}` 或通配），或该操作本身属于当前 token 发起且平台按创建者授权。若无权限返回 404。

---

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

## 14. 端到端操作示例

以下示例按“创建场景 → 校验 → 发布 → 创建运行时 → 访问 → 链路策略 → 抓包 → 销毁”的完整顺序，可直接用 curl 调用。假设：

- Base URL：`https://your-platform.example.com/api/open/v1`
- Token：`$GZCTF_TOKEN`
- 每个写操作使用独立 `Idempotency-Key`。

### 14.1 查能力和控制范围

```bash
curl -s "$BASE/teamlab/capabilities" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s "$BASE/teamlab/scopes" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.2 创建拓扑

```bash
curl -s -X POST "$BASE/teamlab/topologies" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-topology-001" \
  -d '{
    "name": "two-net-demo",
    "schemaVersion": 2,
    "networks": [
      { "key": "net-entry", "name": "入口网段", "addressPool": { "poolCidr": "10.80.1.0/24", "runtimePrefixLength": 24 }, "isEntry": true, "orderIndex": 0 },
      { "key": "net-core", "name": "核心网段", "addressPool": { "poolCidr": "10.80.2.0/24", "runtimePrefixLength": 24 }, "isEntry": false, "orderIndex": 1 }
    ],
    "assets": [
      { "key": "plc", "name": "PLC", "kind": 0, "imageTemplateId": 116, "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 256 },
        "interfaces": [ { "key": "plc-eth0", "networkKey": "net-entry", "hostOffset": 10, "primary": true, "orderIndex": 0 } ],
        "exposePort": 502, "endpointObservation": 2 },
      { "key": "scada", "name": "SCADA", "kind": 0, "imageTemplateId": 117, "resources": { "cpuUnits": 1, "memoryMiB": 256, "storageMiB": 256 },
        "interfaces": [ { "key": "scada-eth0", "networkKey": "net-core", "hostOffset": 20, "primary": true, "orderIndex": 0 } ] }
    ],
    "connections": [
      { "key": "route-entry-core", "fromNetworkKey": "net-entry", "toNetworkKey": "net-core", "direction": 1 }
    ]
  }'
```

响应 `202` 后解析 `operation.id`：

```bash
OPERATION_ID=<从响应读取>
curl -s "$BASE/operations/$OPERATION_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

操作完成后取得 `topologyId`。

### 14.3 校验并发布

```bash
curl -s -X POST "$BASE/teamlab/topologies/$TOPOLOGY_ID/validate" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s -X POST "$BASE/teamlab/topologies/$TOPOLOGY_ID/releases" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-release-001" \
  -d '{ "revision": 1 }'
```

轮询 operation 后取得 `releaseId`。

### 14.4 镜像准备并创建运行时

```bash
curl -s -X POST "$BASE/teamlab/preparations/releases/$RELEASE_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: demo-prep-001"

curl -s -X POST "$BASE/teamlab/runtimes" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-runtime-001" \
  -d '{
    "releaseId": "'"$RELEASE_ID"'",
    "externalReference": "demo-instance"
  }'
```

轮询取得 `runtimeId`。

### 14.5 查询运行时、事件、流量

```bash
curl -s "$BASE/teamlab/runtimes/$RUNTIME_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s "$BASE/teamlab/runtimes/$RUNTIME_ID/events" \
  -H "Authorization: Bearer $GZCTF_TOKEN"

curl -s "$BASE/teamlab/runtimes/$RUNTIME_ID/traffic/flows" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.6 创建 WireGuard 授权并下载

```bash
curl -s -X POST "$BASE/teamlab/runtimes/$RUNTIME_ID/access-grants" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-grant-001" \
  -d '{ "type": "WireGuard" }'
```

轮询后取得 `grantId` 和一次性下载 `token`，然后：

```bash
curl -s -o wg.conf "$BASE/teamlab/runtimes/$RUNTIME_ID/access-grants/$GRANT_ID/download?token=$TOKEN" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.7 应用链路策略

```bash
curl -s -X POST "$BASE/teamlab/link-policies" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "runtimeId": "'"$RUNTIME_ID"'",
    "networkKey": "net-entry",
    "kind": "packet-loss",
    "parameters": { "lossPercent": 40 }
  }'
```

### 14.8 抓包并下载

```bash
curl -s -X POST "$BASE/teamlab/runtimes/$RUNTIME_ID/captures" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-capture-001" \
  -d '{
    "scope": "network",
    "networkKey": "net-entry",
    "maxSeconds": 60,
    "maxBytes": 536870912,
    "expiresInSeconds": 3600
  }'
```

轮询后取得 `captureId`，完成后下载：

```bash
curl -s -o capture.tar "$BASE/teamlab/runtimes/$RUNTIME_ID/captures/$CAPTURE_ID/download" \
  -H "Authorization: Bearer $GZCTF_TOKEN"
```

### 14.9 销毁运行时

```bash
curl -s -X DELETE "$BASE/teamlab/runtimes/$RUNTIME_ID" \
  -H "Authorization: Bearer $GZCTF_TOKEN" \
  -H "Idempotency-Key: demo-destroy-001"
```

---

## 15. 枚举与常量速查

HTTP JSON 中未标注字符串的枚举使用数字值。以下汇总本文用到的枚举。

### 15.1 资产与拓扑

| 枚举 | 数值 | 含义 |
| --- | --- | --- |
| `TeamLabAssetKind` | 0 | Docker |
|  | 1 | VM |
| `TeamLabHealthCheckKind` | 0 | TCP |
|  | 1 | HTTP |
| `TeamLabInfrastructureKind` | 0 | ManagedSwitch |
|  | 1 | ManagedRouter |
| `TeamLabConnectionDirection` | 0 | FromTo |
|  | 1 | Bidirectional |
| `TeamLabDependencyCondition` | 0 | NetworkReady |
|  | 1 | GuestReady |
|  | 2 | ServiceReady |
|  | 3 | BootstrapCompleted |
| `TeamLabEndpointObservationMode` | 0 | Disabled |
|  | 1 | Optional |
|  | 2 | Required |
| `TeamLabExecutionModel` | 0 | V1 |
|  | 1 | V2 |

### 15.2 运行时

| 枚举 | 数值 | 含义 |
| --- | --- | --- |
| `TeamLabRuntimeStatus` | 0 | Pending |
|  | 1 | Planning |
|  | 2 | Scheduled |
|  | 3 | Deploying |
|  | 4 | Probing |
|  | 5 | Running |
|  | 6 | Failed |
|  | 7 | CleanupPending |
|  | 8 | Paused |
|  | 9 | Destroying |
|  | 10 | Destroyed |
| `TeamLabEventLevel` | 0 | Info |
|  | 1 | Success |
|  | 2 | Warning |
|  | 3 | Error |
| `TeamLabTrafficCaptureStatus` | 0 | Pending |
|  | 1 | Running |
|  | 2 | Stopping |
|  | 3 | Completed |
|  | 4 | Failed |
|  | 5 | Expired |
|  | 6 | CleanupPending |

### 15.3 观测与流量

| 枚举 | 数值 | 含义 |
| --- | --- | --- |
| `TeamLabObservationPointKind` | 0 | NetworkBridge |
|  | 1 | RouterFragment |
|  | 2 | FabricUplink |
|  | 3 | WorkloadEndpoint |
| `TeamLabTrafficEvidenceKind` | 0 | Packet |
|  | 1 | EndpointProcess |
| `TeamLabPathConfidence` | 0 | PacketExact |
|  | 1 | ProcessCorrelated |
|  | 2 | TemporallyRelated |

> 注意：`confidence` 查询参数使用字符串 `packet-exact`、`process-correlated`、`temporally-related`；响应里 `confidence` 字段同样是字符串。

### 15.4 远程会话（字符串序列化）

| 枚举 | 序列化值 |
| --- | --- |
| `TeamLabRemoteProtocol` | `containerTerminal`、`ssh`、`rdp`、`vnc` |
| `TeamLabRemoteSessionStatus` | `creating`、`ready`、`connected`、`ending`、`ended`、`failed` |

### 15.5 字符串枚举（请求/响应直接使用字符串）

| 类型 | 取值 |
| --- | --- |
| 链路策略 `kind` | `access-rule`、`nat`、`bandwidth-limit`、`latency`、`jitter`、`packet-loss`、`duplication`、`link-break` |
| 链路策略 `status` | `active`、`recovered`、`failed` |
| 连接器 `kind` | `managed-nic`、`vlan`、`segment`、`serial`、`usb-gateway`、`dedicated-network` |
| 连接器 `health` | `unknown`、`healthy`、`degraded`、`unreachable` |
| 租约 `releaseReason` | `none`、`manual-release`、`runtime-destroyed`、`admin-revoked`、`node-lost` |
| 设备包 `artifactKind` | `oci-image`、`vm-image` |
| 镜像准备 `state` | `planAvailable`、`preparing`、`readyToStart`、`blocked` |

### 15.6 常见稳定错误码

| code | 含义 |
| --- | --- |
| `idempotency_conflict` | 幂等键重复但请求体不同 |
| `topology_revision_conflict` | 修订号过期 |
| `topology_invalid` | 校验未通过 |
| `scope_not_found` | 控制范围不可见/不存在 |
| `scope_archived` | 控制范围已归档，不能写 |
| `runtime_operation_in_progress` | 已有生命周期操作在跑 |
| `runtime_managed_by_rollout` | rollout 管理中的运行时不可直连操作 |
| `resume_blocked` | 恢复被阻止 |
| `rollout_not_drained` | 未排空就归档 |
| `rollout_target_not_found` | target 不存在 |
| `rollout_target_not_ready` | target 尚无运行中 runtime |
| `link_policy_parameters_invalid` | 链路策略参数不合法 |
| `traffic_filter_invalid` | 流量筛选参数不合法 |
| `connector_health_invalid` | 连接器健康值无效 |
| `device_package_parameter_schema_invalid` | 设备包参数 schema 不合法 |

---

## 16. 在研资产文件管理

当前分支新增，尚未发布生产。运行详情“资产运维”中的“资产文件管理”支持容器和 VM SFTP。先选择资产并读取目录，再上传、下载或删除。单文件上限 8 MiB，单目录最多 1000 项；超过上限返回明确错误。覆盖同名文件和删除必须确认；只删除普通文件或空目录，不递归删除。

上传使用目标目录下的平台保留暂存目录，写完后原子替换。异常返回时清理本次暂存；进程终止留下的一小时以上暂存，在下次读取该目录或上传时回收，近期暂存保留。保留目录不作为用户文件开放，普通文件操作不能进入它。

管理 API：`POST /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/files`。

请求字段：`generation` 为当前代次，`operation` 为 `list/upload/download/delete`，`path` 为容器内绝对路径；上传携带 Base64 `content`，覆盖携带 `overwrite=true` 和 `confirmed=true`，删除携带 `confirmed=true`。响应的 `entries` 返回名称、类型、字节数；下载的 `content` 是 Base64。浏览器由 feature adapter 转成下载文件，接口不对外承诺 Open API v1 兼容。

只允许拥有资产运维权限的用户操作运行中的当前代资产。Agent 验证容器标签、代次和进程身份，通过 Linux openat2 相对于容器根目录解析；拒绝符号链接、特殊文件、`..` 和跨挂载访问，包括宿主 bind mount、Docker volume、`/proc` 等。这里不是宿主文件管理器，不支持直接编辑挂载卷。审计记录操作者、资产、代次、操作和路径，不记录文件内容。

容器文件 Agent 需要 Linux 5.6+、可见的宿主 PID/cgroup 以及访问目标进程根目录的权限。直接部署在节点上的 Agent 使用节点 `/proc`；容器化模拟 Agent 需要 host PID/cgroup。缺少能力时明确报错。

VM 文件管理使用镜像模板中已启用的 SSH 运维账号，支持密码或未加密 SSH 私钥。Agent 复查虚拟机 UUID、代次与地址，并使用管理网络连接 SSH 服务；文件权限由该 SSH 账号决定。首次使用先探测并登记 SSH 主机身份，后续连接必须匹配。若在平台外重装虚拟机或更新 SSH 密钥，具有 LifecycleManage 权限的用户可确认“重新登记 SSH 身份”，不必重建磁盘；对应请求为 `operation=reset-ssh-identity`、`path=/`、`confirmed=true`。普通 SFTP 需要来宾 SSH 服务和管理网络，VNC 控制台不受此限制。

## 17. 在研单资产生命周期

运行详情“单资产生命周期”提供启动、停止、重启、重建、暂停和恢复。需要 LifecycleManage 权限，远程终端权限不自动授予生命周期权限。接口会复查当前代次、原节点及原生身份；V1、缺少不可变执行计划或受 rollout 托管的资产显示限制，不猜测执行资源。

- `GET /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control`：当前权限和能力。
- `POST /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control`：请求字段 `generation/action/reason/confirmed`，action 为 `start/stop/restart/rebuild/pause/resume`。返回 202 和原部署队列的 `ticketId`。
- `GET .../control/{ticketId}`：真实任务状态、阶段、错误码和重试资格。
- `POST .../control/{ticketId}/retry`：仅对可重试失败继续未完成阶段，返回新票据关联。

停止保留容器可写层和 VM 磁盘；暂停保留执行现场；重建替换目标资产资源及可写层，其他资产和场景网络不重建。VM 停止/重启会强制断电，确认框明确提示未保存数据风险。目标资产远程会话先结束，文件传输与生命周期协调执行。后台对账尊重停止/暂停意图；不要用全量 apply 自动拉起主动停止的资产。

任务完成表示该资产执行动作和实际电源状态达到目标，不代替真实来宾业务健康或完整跨节点组网验收。

## 18. 会话证据与 VNC 验收边界

会话结束并完成清理后，可在会话“操作审计”生成和下载 JSON 生命周期证据，包含会话/运行时/资产/操作者、时间、结束结果和关联标识；不包含命令、终端输出或屏幕录制。下载重新授权并校验 SHA-256，保留期和配额见商业能力清单。

VNC 使用平台管理的 libvirt 图形控制台，不填写来宾账号，不依赖 VM 内网络。实例必须有匹配 UUID/代次的活动域和 loopback VNC 监听。当前本地已验证无网卡的 TCG 域通过 Agent/Guacamole 收到图形数据并正确关闭；硬件 KVM、完整 VM 管理网络和浏览器视觉验收分别记录。

（完）
