# TeamLab 对接：运行授权与资产计划变更

完整调用顺序见 [对接入口](integration.md)，认证与幂等要求见 [调用约定](integration-authentication.md)。本文说明实现契约，现场验收范围见 [当前状态](../../../current-state.md)。

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
