# TeamLab 外部控制面契约

本文说明外部平台如何只通过 `/api/open/v1` 管理 TeamLab 组网资源。浏览器管理端与外部调用使用同一业务底座；外部调用方不需要数据库、节点或 Agent 权限。

## 1. 认证与资源边界

- 使用 `Authorization: Bearer <token>`。
- Token 同时包含 API scope 和资源授权。API scope 决定可执行的操作类型，`teamlab-scope:<scope-id>` 决定可访问的控制范围。
- 管理员可签发 `teamlab-scope:*`；普通签发者只能授权仍存在且未归档的具体 scope。
- scope 归档后资源仍可读取和排空，但禁止创建、更新、发布、准备、部署和新增 webhook。
- 未授权资源统一按不存在处理，避免通过 `403` 枚举其他客户资源。

TeamLab 使用以下 API scope：

| Scope | 权限 |
| --- | --- |
| `teamlab.topologies:read` | 读取能力、拓扑、发布版本和服务目录 |
| `teamlab.topologies:write` | 创建、更新、校验、发布拓扑 |
| `teamlab.runtimes:read` | 读取镜像准备、rollout、runtime 和事件 |
| `teamlab.runtimes:write` | 准备镜像、管理 rollout/runtime 和访问授权 |
| `teamlab.traffic:read` | 查询流量与路径 |
| `teamlab.capture:read` | 查询和下载抓包 |
| `teamlab.capture:write` | 创建和停止抓包 |

## 2. 写操作与 Operation

所有异步写操作必须携带 `Idempotency-Key`，成功受理返回 `202` 和 operation：

```http
POST /api/open/v1/teamlab/rollouts/{id}/prepare
Authorization: Bearer ...
Idempotency-Key: customer-order-20260808-001
```

- 同一调用身份、路由、key 和请求体重复提交，返回同一个 operation。
- key 相同但请求体不同，返回 `409 idempotency_conflict`。
- 客户端断线后不得盲目重提，使用 `GET /api/open/v1/operations/{operationId}` 恢复进度。
- operation 返回稳定的 `status`、`stage`、进度、关联队列票据和安全错误信息。
- 并发操作由数据库唯一约束和资源级串行控制收敛；同一 runtime 不允许两个生命周期写操作同时执行。

## 3. 完整生命周期

1. 创建控制 scope，并为 Token 授予具体 scope。
2. 查询 `capabilities` 和 `service-profiles`。
3. 创建拓扑，保存执行定义与编辑器布局。
4. 校验拓扑；阻断项全部消除后发布不可变版本。
5. 查询或提交发布版本的镜像准备。
6. 创建 rollout，设置目标列表并执行 prepare。
7. 所有目标就绪后 open-access。
8. 运行期间查询 runtime、事件、流量、路径和抓包；按需 pause/resume 或重建单个失败目标。
9. 比赛结束先 close-access，再 drain；所有目标清理后 archive rollout。
10. 不再接受新工作时归档 scope。发布预热引用被释放，仍由活动 rollout/runtime 持有的镜像不会删除。

发布版本保存执行定义和画布布局两个快照。布局变化会产生新修订和新发布版本，但不会改变执行摘要；已有 runtime 永远使用创建时指定的发布版本。

## 4. 接口清单

### 控制范围与能力

- `GET /api/open/v1/teamlab/capabilities`
- `GET|POST /api/open/v1/teamlab/scopes`
- `GET /api/open/v1/teamlab/service-profiles`
- `GET /api/open/v1/teamlab/service-profiles/{profileId}`

### 拓扑与发布

- `GET|POST /api/open/v1/teamlab/topologies`
- `GET|PUT|DELETE /api/open/v1/teamlab/topologies/{topologyId}`
- `POST /api/open/v1/teamlab/topologies/{topologyId}/validate`
- `GET|POST /api/open/v1/teamlab/topologies/{topologyId}/releases`
- `GET /api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}`
- `POST /api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}/plan`

### 镜像准备

- `GET /api/open/v1/teamlab/preparations/releases/{releaseId}`
- `POST /api/open/v1/teamlab/preparations/releases/{releaseId}`

状态为 `planAvailable`、`preparing`、`readyToStart` 或 `blocked`。返回每个模板的适配节点数、就绪数、准备中数量和失败数量，不暴露节点地址。

### Rollout

- `GET|POST /api/open/v1/teamlab/rollouts`
- `GET /api/open/v1/teamlab/rollouts/{rolloutId}`
- `GET|PUT /api/open/v1/teamlab/rollouts/{rolloutId}/targets`
- `POST .../{rolloutId}/prepare|open-access|close-access|drain|pause|resume|archive`
- `POST .../{rolloutId}/targets/{targetId}/rebuild|pause|resume|restart`

Rollout 只描述一批部署目标。单个目标失败不会删除其他已就绪目标；恢复必须由调用方显式选择重建目标、移除目标或排空 rollout。

### Runtime 与访问

- `POST /api/open/v1/teamlab/runtimes`
- `GET|DELETE /api/open/v1/teamlab/runtimes/{runtimeId}`
- `POST .../{runtimeId}/reset|pause|resume`
- `GET .../{runtimeId}/events`
- `POST .../{runtimeId}/access-grants`
- `GET .../{runtimeId}/access-grants/{grantId}/download`
- `DELETE .../{runtimeId}/access-grants/{grantId}`

Runtime 投影包含当前 operation、队列票据、队列状态、阶段、generation、scope、发布版本、分片/资产状态和恢复动作。暂停保留原节点、地址、网络和磁盘；恢复不会重新调度或重新下载镜像。

### 流量与抓包

- `GET .../{runtimeId}/traffic/flows`
- `GET .../{runtimeId}/traffic/paths`
- `GET .../{runtimeId}/traffic/paths/{pathId}`
- `POST .../{runtimeId}/captures`
- `GET .../{runtimeId}/captures/{captureId}`
- `POST .../{runtimeId}/captures/{captureId}/stop`
- `GET .../{runtimeId}/captures/{captureId}/download`

流量接口支持 cursor、关键字、协议、网段和路径分类条件；抓包有时间和大小上限，下载结果只包含已验证分片。

### Webhook

- `GET|POST /api/open/v1/teamlab/webhooks`
- `GET|DELETE /api/open/v1/teamlab/webhooks/{webhookId}`
- `POST /api/open/v1/teamlab/webhooks/{webhookId}/replay`

Webhook 只接受可公开解析且不指向内网、回环、链路本地或平台地址的 HTTPS endpoint。签名 secret 仅在创建结果中返回一次，数据库只保存加密值。

投递为至少一次语义，包含 `X-TeamLab-Event-Id`、时间戳和 HMAC-SHA256 签名。事件体包含事件 ID、类型、发生时间、scope、资源、资源版本、operation ID 和安全 URL。失败采用有上限的退避并保留有限失败记录；Webhook 失败不改变部署状态，重放不会创建新的业务 operation。

## 5. 分页、错误和恢复动作

- 列表使用不透明 `after` cursor；调用方不得解析或自行构造 cursor。
- runtime 事件可按 generation 和 stage 过滤；流量和路径支持各自的筛选字段。
- 失败统一返回 `application/problem+json`，客户端以稳定 `code` 分支，不解析中文 detail。
- 常见代码：`scope_archived`、`topology_revision_conflict`、`topology_invalid`、`service_profile_not_found`、`runtime_operation_in_progress`、`resume_blocked`、`rollout_not_drained`、`idempotency_conflict`。
- 恢复动作 ID 包括 `wait_for_node`、`wait_for_capacity`、`retry_image_preparation`、`retry_operation`、`retry_cleanup`、`rebuild_runtime` 和 `drain_runtime`。

## 6. 容量与清理保证

- Docker 与 KVM 能力独立判断，缺少 KVM 不影响 Docker 调度。
- runtime、rollout 和发布预热是同一镜像缓存记录上的独立引用；共享镜像不会重复下载。
- 发布预热引用为可续期的 24 小时窗口；rollout 排空、runtime 销毁和 scope 归档只释放各自拥有的引用。
- 最后一条引用释放后缓存进入后台清理；Registry 主副本不随比赛结束删除。
- drain 顺序固定为关闭访问、停止观测、销毁资产和网络、释放容量与镜像引用；重复 drain/destroy 必须收敛到相同终态。

完整字段、请求模型和示例以运行中的中文接口页 `/api-docs` 及唯一机器契约 `/openapi/open-v1.json` 为准。
