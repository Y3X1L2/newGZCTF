# TeamLab：批量部署与目标生命周期

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

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
