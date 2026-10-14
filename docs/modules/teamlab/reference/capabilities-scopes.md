# TeamLab：能力查询与控制范围

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

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
