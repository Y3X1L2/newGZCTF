# TeamLab：资源池与节点缓存

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

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
