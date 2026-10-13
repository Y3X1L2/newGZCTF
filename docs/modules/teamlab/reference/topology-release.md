# TeamLab：拓扑草稿、校验、发布与计划

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

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

#### 观测策略对象 `observation`

```json
{
  "flowMetadataEnabled": true,
  "onDemandPcapEnabled": true
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `flowMetadataEnabled` | bool | 否 | 是否采集流量元数据，默认 true |
| `onDemandPcapEnabled` | bool | 否 | 是否允许按需抓包，默认 true |

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

请求体为“拓扑定义公共字段”完整对象。示例见 [端到端调用](walkthrough.md)。

响应 `202 Accepted`：

```json
{
  "id": "019...",
  "kind": "teamlab.runtime.v1",
  "status": 0,
  "stage": "pending"
}
```

以上为 ApiOperationModel 的部分字段，operation.id 用于查询；没有通用 resourceUrl 字段。完整模型见 [ApiOperationModel](../../../../src/GZCTF/Modules/Audit/Contracts/ApiOperationModel.cs)。

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
