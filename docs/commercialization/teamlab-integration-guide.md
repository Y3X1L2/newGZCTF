# TeamLab 组网底座对接文档

TeamLab Open API 基础路径：

```text
/api/open/v1/teamlab
```

场景和资产的制作、导入方式见[组网场景制作与导入规范](../teamlab-authoring-guide.md)。

## 1. 接口地址与认证

### 1.1 认证

每次请求在请求头中携带 API Token：

```http
Authorization: Bearer <token>
```

Token 的权限由三部分组成：

| 配置 | 作用 |
| --- | --- |
| API Scope | 决定 Token 可以调用哪一类接口 |
| `teamlab-scope` 资源范围 | 决定 Token 可以发现和创建哪些控制范围下的资源 |
| Runtime Grant | 决定 Token 可以操作哪个运行环境或具体资产 |

创建运行环境的服务 Token 通常配置以下 Scope：

| Scope | 对应能力 |
| --- | --- |
| `teamlab.topologies:read` | 查询场景、发布版本和部署计划 |
| `teamlab.topologies:write` | 创建、修改、发布和归档场景 |
| `teamlab.runtimes:read` | 查询运行环境、资产、事件和授权 |
| `teamlab.runtimes:write` | 创建、变更、控制和销毁运行环境 |
| `teamlab.resource-pools:read` | 查询节点资源和镜像缓存 |
| `teamlab.device-packages:read` | 查询设备模板 |
| `teamlab.device-packages:write` | 登记、修改、启停和归档设备模板 |
| `teamlab.connectors:read` | 查询现场连接器和节点网卡 |
| `teamlab.connectors:write` | 登记、修改、占用和释放现场连接器 |
| `teamlab.remote-sessions:read` | 查询远程会话、文件目录和审计信息 |
| `teamlab.remote-sessions:write` | 创建会话、控制终端和修改文件 |
| `teamlab.link-policies:read` | 查询链路策略 |
| `teamlab.link-policies:write` | 应用和恢复链路策略 |
| `teamlab.traffic:read` | 查询流量记录和通信路径 |
| `teamlab.capture:read` | 查询和下载抓包 |
| `teamlab.capture:write` | 开始和停止抓包 |
| `images:read` | 查询镜像模板 |
| `images:write` | 导入镜像和配置远程访问 |

### 1.2 请求头

| 请求头 | 使用位置 | 内容 |
| --- | --- | --- |
| `Authorization` | 所有接口 | `Bearer <token>` |
| `Content-Type` | JSON 请求 | `application/json` |
| `Idempotency-Key` | 异步写接口 | 1 至 128 个 ASCII 字母、数字、`-`、`_` 或 `.` |

一次业务动作使用一个 `Idempotency-Key`。网络超时后，以同一个 key 重新发送原请求，服务端返回第一次受理的结果。请求内容发生变化时生成新的 key。

### 1.3 异步操作

创建、资产变更、生命周期控制、暂停、恢复、重置和销毁接口返回 `202 Accepted`，响应体为 `ApiOperationModel`。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | UUID | 操作 ID |
| `kind` | string | 操作类型 |
| `status` | `ApiOperationStatus` | 操作状态 |
| `stage` | string | 当前阶段 |
| `resourceType` | string / null | 目标资源类型 |
| `resourceId` | string / null | 目标资源 ID；创建运行环境成功后这里是 `runtimeId` |
| `deploymentQueueTicketId` | UUID / null | 对应的部署队列票据 |
| `currentProgress` | int64 | 已完成数量 |
| `totalProgress` | int64 | 总数量 |
| `attemptCount` | int | 执行次数 |
| `errorCode` | string / null | 失败错误码 |
| `errorDetail` | string / null | 失败详情 |
| `result` | object | 操作结果 |
| `createdAt` | uint64 | 创建时间，Unix 毫秒 |
| `startedAt` | uint64 / null | 开始时间，Unix 毫秒 |
| `updatedAt` | uint64 | 更新时间，Unix 毫秒 |
| `completedAt` | uint64 / null | 完成时间，Unix 毫秒 |

查询操作：

```http
GET /api/open/v1/operations/{id}
```

`ApiOperationStatus`：

| 值 | 名称 | 含义 |
| --- | --- | --- |
| `0` | `Pending` | 等待处理 |
| `1` | `Running` | 正在执行 |
| `2` | `Succeeded` | 执行成功 |
| `3` | `Failed` | 执行失败 |

## 2. 资源标识

| 标识 | 来源 | 后续用途 |
| --- | --- | --- |
| `controlScopeId` | 控制范围接口 | 隔离不同业务域的场景和资源 |
| `topologyId` | 场景创建接口 | 编辑场景草稿 |
| `releaseId` | 场景发布接口 | 创建独立运行环境 |
| `runtimeId` | 创建环境 operation 的 `resourceId` | 查询和操作运行环境 |
| `externalReference` | 调用方创建环境时填写 | 保存队伍、项目或业务编号 |
| `generation` | 运行环境响应 | 单资产控制、文件操作；完整重置后递增 |
| `planRevision` | 运行环境响应 | 提交资产变更；每次变更成功后递增 |
| `assetId` | 资产列表响应 | 单资产控制、服务开放和远程会话 |
| `assetKey` | 场景资产定义 | 资产授权和运行中资产变更 |

同一个 `releaseId` 可以创建多个独立运行环境。调用方保存 `externalReference`、`runtimeId` 和创建 operation ID，即可把自己的业务对象与运行环境关联起来。

## 3. 标准调用流程

### 3.1 预热模板

活动开始前提交本次会使用的镜像模板：

```http
POST /api/open/v1/teamlab/preparations/templates
Content-Type: application/json

{
  "templateIds": [116, 117, 203]
}
```

请求模型 `PrepareTeamLabTemplatesModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `templateIds` | int[] | 已就绪的镜像模板 ID |

响应模型 `TeamLabTemplatePreparationResultModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `templateCount` | int | 本次处理的模板数 |
| `distributionCount` | int | 创建或复用的节点分发记录数 |

按发布版本准备和查询：

```http
POST   /api/open/v1/teamlab/preparations/releases/{releaseId}
GET    /api/open/v1/teamlab/preparations/releases/{releaseId}
DELETE /api/open/v1/teamlab/preparations/releases/{releaseId}
```

### 3.2 创建运行环境

```http
POST /api/open/v1/teamlab/runtimes
Idempotency-Key: match-001-team-017-create
Content-Type: application/json

{
  "releaseId": "<releaseId>",
  "externalReference": "match-001:team-017",
  "constraints": {
    "preferredRegion": null,
    "requiredCapabilities": []
  },
  "overlays": [
    {
      "assetKey": "web",
      "secrets": {
        "FLAG": "<该环境的实例值>"
      }
    }
  ]
}
```

请求模型 `CreateTeamLabRuntimeModel`：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `releaseId` | UUID | 是 | 场景发布版本 ID |
| `externalReference` | string / null | 否 | 调用方业务编号；列表接口可按该值查询 |
| `constraints` | object / null | 否 | 节点放置条件 |
| `constraints.preferredRegion` | string / null | 否 | 优先区域 |
| `constraints.requiredCapabilities` | string[] | 否 | 节点需要具备的能力 |
| `overlays` | array / null | 否 | 本次运行环境的资产机密 |
| `overlays[].assetKey` | string | 是 | 接收机密的资产 key |
| `overlays[].secrets` | object / null | 否 | 注入该资产的键值对 |

接口返回 `ApiOperationModel`。轮询 `/api/open/v1/operations/{id}`，状态为 `Succeeded` 后，从 `resourceId` 取得 `runtimeId`。

### 3.3 查询部署状态

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/status
```

响应模型 `OpenTeamLabRuntimeStatusModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | UUID | 运行环境 ID |
| `generation` | int | 当前代次 |
| `status` | `TeamLabRuntimeStatus` | 运行状态 |
| `stage` | string | 当前执行阶段 |
| `deploymentQueueTicketId` | UUID / null | 当前部署票据 |
| `queueStatus` | `DeploymentQueueTicketStatus` | 票据状态 |
| `queueStage` | string / null | 票据执行阶段 |
| `updatedAt` | uint64 / null | 更新时间，Unix 毫秒 |
| `assets` | object | 资产数量汇总 |
| `assets.total` | int | 资产总数 |
| `assets.pending` | int | 等待中的资产数 |
| `assets.running` | int | 运行中的资产数 |
| `assets.paused` | int | 已暂停的资产数 |
| `assets.stopped` | int | 已停止的资产数 |
| `assets.failed` | int | 失败的资产数 |

`TeamLabRuntimeStatus`：

| 值 | 名称 | 含义 |
| --- | --- | --- |
| `0` | `Pending` | 等待创建 |
| `1` | `Planning` | 正在生成执行计划 |
| `2` | `Scheduled` | 已完成节点调度 |
| `3` | `Deploying` | 正在部署资产和网络 |
| `4` | `Probing` | 正在检查资产就绪状态 |
| `5` | `Running` | 环境运行中 |
| `6` | `Failed` | 部署或运行失败 |
| `7` | `CleanupPending` | 等待清理 |
| `8` | `Paused` | 环境已暂停 |
| `9` | `Destroying` | 正在销毁 |
| `10` | `Destroyed` | 已销毁 |
| `11` | `Stopped` | 已停止 |

`DeploymentQueueTicketStatus`：

| 值 | 名称 |
| --- | --- |
| `0` | `Pending` |
| `1` | `Scheduling` |
| `2` | `Scheduled` |
| `3` | `Running` |
| `4` | `Succeeded` |
| `5` | `Failed` |
| `6` | `Cancelled` |

运行状态到达 `Running`、`Failed`、`Paused`、`Destroyed` 或 `Stopped` 后停止轮询。需要网络、分片和失败详情时读取：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}
```

完整运行环境响应 `OpenTeamLabRuntimeModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | UUID | 运行环境 ID |
| `releaseId` | UUID | 来源发布版本 |
| `generation` | int | 当前代次 |
| `planRevision` | int | 当前资产计划修订号 |
| `status` | `TeamLabRuntimeStatus` | 当前状态 |
| `stage` | string | 当前阶段 |
| `openForAccess` | bool | 环境访问是否开放 |
| `shards` | `OpenTeamLabRuntimeShardModel[]` | Worker 分片 |
| `networks` | `TeamLabRuntimeNetworkProjectionModel[]` | 已部署网段 |
| `assets` | `OpenTeamLabRuntimeAssetModel[]` | 运行资产 |
| `createdAt` | uint64 | 创建时间，Unix 毫秒 |
| `updatedAt` | uint64 / null | 更新时间，Unix 毫秒 |
| `failure` | `OpenTeamLabFailureModel` / null | 当前失败信息 |
| `currentOperationId` | UUID / null | 当前操作 ID |
| `deploymentQueueTicketId` | UUID / null | 当前部署票据 |
| `queueStatus` | `DeploymentQueueTicketStatus` | 票据状态 |
| `subStages` | `OpenTeamLabRuntimeSubStageModel[]` | 子阶段进度 |
| `controlScopeId` | UUID / null | 所属控制范围 |
| `releaseVersion` | int / null | 发布版本号 |
| `recoveryActions` | string[] | 可执行的恢复动作 |

| 嵌套模型 | 字段 |
| --- | --- |
| `OpenTeamLabRuntimeShardModel` | `id`、`status`、`networkKeys`、`assetKeys`、`failure` |
| `TeamLabRuntimeNetworkProjectionModel` | `key`、`name`、`cidr`、`gatewayIp` |
| `OpenTeamLabRuntimeAssetModel` | `id`、`key`、`name`、`kind`、`primaryIp`、`status`、`failure` |
| `OpenTeamLabFailureModel` | `code`、`stage`、`retryable`、`actions`、`resourceType`、`resourceId`、`detail` |
| `OpenTeamLabRuntimeSubStageModel` | `id`、`status`、`message` |

### 3.4 查询资产

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets?cursor=&limit=50&status=
```

| 查询参数 | 类型 | 说明 |
| --- | --- | --- |
| `cursor` | string / null | 上一页返回的 `nextCursor` |
| `limit` | int | 单页数量 |
| `status` | `TeamLabRuntimeStatus` / null | 按运行状态筛选 |

响应模型 `OpenTeamLabRuntimeAssetPageModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `items` | `OpenTeamLabRuntimeAssetModel[]` | 当前页资产 |
| `nextCursor` | string / null | 下一页游标；为空表示已到最后一页 |

资产类型 `TeamLabAssetKind`：`0` 为 `Docker`，`1` 为 `Vm`。

### 3.5 配置运行环境授权

读取当前授权：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/grants
```

提交完整授权集合：

```http
PUT /api/open/v1/teamlab/runtimes/{runtimeId}/grants
Content-Type: application/json

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

`PUT` 用请求中的 `grants` 替换该运行环境的现有授权；传入空数组会清空 Runtime Grant。

请求项 `TeamLabRuntimeGrantWriteModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `subjectType` | string | `user` 或 `apiToken` |
| `subjectId` | UUID | 用户 ID 或 API Token ID |
| `assetKey` | string / null | 空值表示整个运行环境；填写后只授权该资产 |
| `permissions` | string[] | 权限列表 |

| 权限 | 可调用的能力 |
| --- | --- |
| `StateRead` | 状态和资产摘要 |
| `MetadataRead` | 完整环境、事件、流量和抓包元数据 |
| `RemoteSessionOperate` | 终端、SSH、RDP 和 VNC 会话 |
| `FileTransfer` | 目录、上传、下载、移动和删除 |
| `AssetOperate` | 启动、停止、重启、暂停、恢复和重建资产 |
| `AssetCompose` | 新增、替换和移除资产 |
| `ServiceAccessManage` | 创建和撤销服务开放 |
| `RuntimeManage` | 暂停、恢复、重置、销毁和授权管理 |

`GET` 和 `PUT` 都返回 `TeamLabRuntimeGrantModel[]`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | int64 | 授权记录 ID |
| `subjectType` | string | 主体类型 |
| `subjectId` | UUID | 主体 ID |
| `subjectName` | string | 用户名或 Token 名称 |
| `assetKey` | string / null | 资产范围 |
| `permissions` | string[] | 已授予权限 |
| `updatedAt` | uint64 | 更新时间，Unix 毫秒 |

### 3.6 运行中增删替换资产

先从 `GET /runtimes/{runtimeId}` 读取 `planRevision`，再提交本轮资产变更：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/asset-changes
Idempotency-Key: match-001-team-017-assets-005
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
      "exposePort": null,
      "healthCheck": { "kind": 0, "port": 22 },
      "orderIndex": 20,
      "devicePackageId": null,
      "deviceParameters": null,
      "connectorId": null
    }
  ],
  "replace": [],
  "remove": [],
  "overlays": []
}
```

请求模型 `ChangeTeamLabRuntimeAssetsModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `expectedPlanRevision` | int | 本次变更依据的计划修订号 |
| `add` | `TeamLabTopologyAssetModel[]` / null | 新增资产的完整定义 |
| `replace` | `TeamLabTopologyAssetModel[]` / null | 替换资产的完整定义；`key` 对应现有资产 |
| `remove` | string[] / null | 移除的资产 key |
| `overlays` | `TeamLabRuntimeOverlayModel[]` / null | 新增或替换资产的机密键值对 |

`TeamLabTopologyAssetModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `key` | string | 运行环境内稳定且唯一的资产 key |
| `name` | string | 资产名称 |
| `kind` | `TeamLabAssetKind` | `0` Docker，`1` VM |
| `imageTemplateId` | int | 镜像模板 ID |
| `resources` | object | CPU、内存和磁盘规格 |
| `resources.cpuUnits` | int | CPU 核数 |
| `resources.memoryMiB` | int | 内存，MiB |
| `resources.storageMiB` | int | 磁盘，MiB |
| `interfaces` | `TeamLabTopologyInterfaceModel[]` | 网卡列表 |
| `exposePort` | int / null | 场景默认开放端口 |
| `healthCheck` | object / null | TCP 或 HTTP 就绪检查 |
| `orderIndex` | int | 画布和列表顺序 |
| `devicePackageId` | int / null | 设备模板 ID |
| `deviceParameters` | object / null | 设备模板参数 |
| `connectorId` | UUID / null | 现场连接器 ID |

`TeamLabTopologyInterfaceModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `key` | string | 网卡 key |
| `networkKey` | string | 运行环境中已有的网段 key |
| `hostOffset` | int | 网段内主机地址偏移 |
| `primary` | bool | 是否为主网卡 |
| `orderIndex` | int | 网卡顺序 |

健康检查模型：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `kind` | `TeamLabHealthCheckKind` | `0` TCP，`1` HTTP |
| `port` | int | 检查端口 |

接口返回 `ApiOperationModel`。操作成功后 `generation` 保持不变，`planRevision` 增加 1。收到 `409 runtime_plan_revision_conflict` 时，重新读取运行环境，以新的 `planRevision` 计算并提交下一次变更。

### 3.7 控制单个资产

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
```

响应字段为 `allowed` 和 `reason`。`allowed` 为 `true` 时提交命令：

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
Idempotency-Key: match-001-web-restart-001
Content-Type: application/json

{
  "generation": 1,
  "action": "restart",
  "reason": "平台管理员重启资产",
  "confirmed": false
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `generation` | int | 当前运行代次，最小值 1 |
| `action` | string | `start`、`stop`、`restart`、`rebuild`、`pause`、`resume` |
| `reason` | string | 操作原因，4 至 500 字符 |
| `confirmed` | bool | 重建等破坏性操作的确认值 |

受理响应为 `{ "ticketId": "<UUID>" }`。查询任务：

```http
GET /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}
```

任务响应字段：`id`、`status`、`stage`、`errorCode`、`retryable`。

### 3.8 开放资产服务

```http
POST /api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/service-access
Content-Type: application/json

{
  "protocol": "tcp",
  "internalPort": 8080,
  "publicPort": null,
  "networkKey": "office"
}
```

| 请求字段 | 类型 | 说明 |
| --- | --- | --- |
| `protocol` | string | `tcp` 或 `udp` |
| `internalPort` | int | 资产内部端口，1 至 65535 |
| `publicPort` | int / null | 服务器端口；空值由平台分配 |
| `networkKey` | string / null | 目标网段 key；多网卡资产填写此项 |

成功返回 `201 Created` 和 `OpenTeamLabServiceAccessModel`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | UUID | 映射 ID |
| `runtimeId` | UUID | 运行环境 ID |
| `generation` | int | 创建映射时的运行代次 |
| `assetId` | int | 资产 ID |
| `assetName` | string | 资产名称 |
| `networkKey` | string | 使用的网段 key |
| `protocol` | string | `tcp` 或 `udp` |
| `internalPort` | int | 资产内部端口 |
| `publicPort` | int | 服务器端口 |
| `endpoint` | string | 可交给访问方的地址 |
| `status` | string | 映射状态 |
| `createdAt` | uint64 | 创建时间，Unix 毫秒 |
| `revokedAt` | uint64 / null | 撤销时间，Unix 毫秒 |

```http
GET    /api/open/v1/teamlab/runtimes/{runtimeId}/service-access
DELETE /api/open/v1/teamlab/runtimes/{runtimeId}/service-access/{accessId}
```

查询返回 `OpenTeamLabServiceAccessModel[]`；撤销返回撤销后的同一模型。

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

### 3.11 暂停、恢复、重置和销毁

| 动作 | 接口 | 请求体 |
| --- | --- | --- |
| 暂停 | `POST /runtimes/{runtimeId}/pause` | 无 |
| 恢复 | `POST /runtimes/{runtimeId}/resume` | 无 |
| 重置 | `POST /runtimes/{runtimeId}/reset` | `ResetTeamLabRuntimeModel` |
| 销毁 | `DELETE /runtimes/{runtimeId}` | 无 |

四个接口都携带 `Idempotency-Key`，并返回 `ApiOperationModel`。

重置请求：

```json
{
  "overlays": [
    {
      "assetKey": "web",
      "secrets": {
        "FLAG": "<新代次实例值>"
      }
    }
  ],
  "releaseId": null
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `overlays` | `TeamLabRuntimeOverlayModel[]` / null | 新代次使用的资产机密 |
| `releaseId` | UUID / null | 空值沿用当前来源版本；填写后按指定发布版本重置 |

暂停保留运行身份、网络、磁盘和容量记录。恢复在原节点继续运行。重置清理当前代并重新部署，`generation` 增加 1。销毁操作成功且运行状态为 `Destroyed` 后，该环境的资产、网络、服务映射、抓包和容量租约均已进入清理终态。

## 4. 查询和辅助能力

### 4.1 运行事件与状态检查

| 接口 | 返回内容 |
| --- | --- |
| `GET /runtimes/{runtimeId}/events?after=&limit=50&generation=&stage=` | 分页运行事件 |
| `GET /runtimes/{runtimeId}/device-health` | 各资产最近一次设备健康结果 |
| `GET /runtimes/{runtimeId}/status-check` | 数据库运行事实与现场资产状态的差异 |

运行失败时，先读取 operation 的 `errorCode`、`errorDetail` 和 `stage`，再读取运行事件。状态检查返回差异；恢复动作调用单资产控制、重置或销毁接口。

### 4.2 链路策略

```http
GET  /api/open/v1/teamlab/link-policies?runtimeId={runtimeId}&status=active
POST /api/open/v1/teamlab/link-policies
POST /api/open/v1/teamlab/link-policies/{policyId}/recover
```

策略类型：`access-rule`、`bandwidth-limit`、`latency`、`jitter`、`packet-loss`、`duplication`、`link-break`。应用接口返回策略记录；恢复接口把对应网卡恢复到应用前状态。

### 4.3 流量和抓包

| 能力 | 接口 |
| --- | --- |
| 流量记录 | `GET /runtimes/{runtimeId}/traffic/flows` |
| 通信路径列表 | `GET /runtimes/{runtimeId}/traffic/paths` |
| 通信路径详情 | `GET /runtimes/{runtimeId}/traffic/paths/{pathId}` |
| 开始抓包 | `POST /runtimes/{runtimeId}/captures` |
| 抓包列表 | `GET /runtimes/{runtimeId}/captures` |
| 抓包状态 | `GET /runtimes/{runtimeId}/captures/{captureId}` |
| 停止抓包 | `POST /runtimes/{runtimeId}/captures/{captureId}/stop` |
| 下载抓包 | `GET /runtimes/{runtimeId}/captures/{captureId}/download` |

常用抓包请求：

```json
{
  "scope": "network",
  "networkKey": "office",
  "maxSeconds": 60,
  "maxBytes": 104857600,
  "expiresInSeconds": 3600
}
```

跨节点抓包会生成多个分段。下载接口返回 tar 流，包含 manifest 和各节点分段文件。

### 4.4 WireGuard 访问

| 动作 | 接口 |
| --- | --- |
| 创建访问授权 | `POST /runtimes/{runtimeId}/access-grants` |
| 查询授权 | `GET /runtimes/{runtimeId}/access-grants` |
| 下载配置 | `GET /runtimes/{runtimeId}/access-grants/{grantId}/download` |
| 撤销授权 | `DELETE /runtimes/{runtimeId}/access-grants/{grantId}` |

服务开放用于发布一个具体 TCP/UDP 服务；WireGuard 访问授权用于让运维人员进入运行环境网段。

### 4.5 现场连接器

| 动作 | 接口 |
| --- | --- |
| 查询连接器 | `GET /connectors`、`GET /connectors/{connectorId}` |
| 登记和修改 | `POST /connectors`、`PUT /connectors/{connectorId}` |
| 读取节点网卡 | `GET /connectors/nodes/{nodeId}/interfaces` |
| 查询现场状态 | `GET /connectors/{connectorId}/health` |
| 占用和释放 | `POST /connectors/{connectorId}/leases`、`POST /connectors/{connectorId}/leases/release` |
| 归档 | `POST /connectors/{connectorId}/archive` |

### 4.6 批量编排和通知

Rollout 用于对一组独立运行环境统一执行准备、开放、暂停、恢复和清理：

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
```

Webhook 接收 operation 和运行资源事件：

```text
GET    /webhooks
POST   /webhooks
GET    /webhooks/{webhookId}
DELETE /webhooks/{webhookId}
POST   /webhooks/{webhookId}/replay
```

创建 Webhook 时返回签名 secret。接收方按事件 ID 去重，收到事件后使用 operation 或运行状态接口读取最新状态。

## 5. 错误响应

失败响应使用 `application/problem+json`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `type` | string / null | 问题类型 URI |
| `title` | string / null | 错误标题 |
| `status` | int / null | HTTP 状态码 |
| `detail` | string / null | 错误详情 |
| `instance` | string / null | 请求资源 |
| `code` | string | 稳定错误码，客户端据此选择处理动作 |
| `traceId` | string | 服务端请求追踪 ID |

| HTTP 状态 | 客户端处理 |
| --- | --- |
| `400` | 修正请求字段、查询参数或请求头 |
| `401` | 更新或重新签发 Token |
| `403` | 补充 API Scope 或 Runtime Grant |
| `404` | 更新资源 ID，或确认 Token 对该资源具有可见权限 |
| `409` | 读取最新运行状态或修订号，再生成下一次请求 |
| `422` | 根据 `code` 和 `detail` 修正拓扑、模板或能力参数 |
| `429` | 按 `Retry-After` 延迟请求；`RateLimit-*` 给出当前窗口信息 |
| `503` | 保留原 `Idempotency-Key`，延迟后重发原请求 |

常用错误码：

| `code` | 含义 | 处理动作 |
| --- | --- | --- |
| `topology_revision_conflict` | 场景草稿 revision 已变化 | 读取最新草稿后重新提交 |
| `topology_invalid` | 场景校验失败 | 按错误明细修正资产或网络 |
| `image_template_unavailable` | 镜像不存在、不可见或未就绪 | 查询镜像状态并完成导入或预热 |
| `address_pool_exhausted` | 地址池没有可分配网段 | 扩大地址池或释放已销毁环境占用 |
| `capability_unavailable` | 当前节点缺少所需能力 | 调整模板能力要求或节点配置 |
| `external_reference_conflict` | 业务编号已关联另一创建请求 | 查询原 operation 和 runtime |
| `runtime_plan_revision_conflict` | 资产计划已被其他请求更新 | 读取新 `planRevision` 后重新计算变更 |
| `runtime_operation_in_progress` | 运行环境正在执行互斥操作 | 等待当前 operation 进入终态 |
| `runtime_not_ready` | 当前运行状态不接受该操作 | 查询 `/status` 后选择对应操作 |
| `resume_blocked` | 原节点暂时无法恢复环境 | 查看运行事件和节点状态 |
| `runtime_grant_permission_invalid` | 授权中包含未知权限名 | 改用授权表中的权限名称 |
| `idempotency_conflict` | 同一个 key 对应了不同请求体 | 为新业务动作生成新的 key |
