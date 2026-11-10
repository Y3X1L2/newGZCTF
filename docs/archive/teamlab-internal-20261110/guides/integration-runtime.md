# TeamLab 对接：镜像准备、创建、状态与资产查询

完整调用顺序见 [对接入口](integration.md)，认证与幂等要求见 [调用约定](integration-authentication.md)。本文说明实现契约，现场验收范围见 [当前状态](../../../current-state.md)。

## 3. 标准调用流程

### 3.1 可选镜像预热与创建条件

普通发布和创建运行环境按需下载：先选执行节点，再准备这些节点缺少的镜像。活动开始前若希望提前下载，可以显式提交本次会使用的镜像模板；当前预热接口会向所有适配节点排队，因此不是创建环境的必需步骤：

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

`GET` 返回的发布版本汇总 `state` 当前为 `onDemand`、`readyToStart` 或 `blocked`。`onDemand` 表示部分模板没有就绪缓存，仍允许创建且 `readyToStart=true`；`readyToStart` 表示每个模板至少有一个适配节点缓存，也不保证本次最终选定节点都已有副本。`blocked` 表示源模板状态、冻结摘要或节点能力不满足，`readyToStart=false`。旧的 `planAvailable`、`preparing` 状态不再由当前聚合实现返回。

`planAvailable` 仅说明存在适配节点能力，CPU、内存和磁盘的实际预留仍在调度阶段完成。不要把无关节点的缓存失败当作本次创建失败；本次选定节点的下载或容量错误会进入实际运行任务。`DELETE` 只撤销发布版本预热需求，仍受实例、其他引用和实际 backing 文件使用保护的节点缓存会保留。

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
