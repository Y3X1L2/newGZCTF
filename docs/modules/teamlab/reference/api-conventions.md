# TeamLab：认证、幂等、分页与异步操作

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

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

`ApiOperationModel.status` 使用整数枚举：`Pending=0`、`Running=1`、`Succeeded=2`、`Failed=3`，以 [当前枚举](../../../../src/GZCTF/Modules/Audit/Domain/ApiOperation.cs) 与 [生成 OpenAPI](../../../commercialization/openapi/open-v1.json) 为准。`stage` 表达执行阶段，部署票据与 runtime 还有各自状态，不能混为一套枚举，也不能把 `cancelled` 当作 ApiOperationStatus。

成功后根据响应中的 `resourceType`、`resourceId` 与 `result` 读取对应资源；结果内容依具体操作契约而定，不假设所有响应都有 `resourceUrl`。不要用固定 sleep 猜测完成时间。

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
| `teamlab.topologies:write` | 拓扑及 rollout 定义/目标等写侧，具体部署命令另查 runtimes:write |
| `teamlab.runtimes:read` | 查运行时、事件、镜像准备状态、下载访问配置 |
| `teamlab.runtimes:write` | runtime 生命周期、访问授权、准备镜像及 rollout 部署类命令 |
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

除 scope 外，token 还必须有对应资源的 `teamlab-scope` grant。管理员可用通配 `teamlab-scope:*`；普通 token 只能被授予具体 scope。scope 归档后可继续读取和排空，禁止新增创建、发布、准备和部署；按具体清理命令校验。Runtime 与资产操作还需对应 Runtime Grant，不能仅凭 API scope 访问。

### 1.7 枚举序列化说明

本平台开放 API 的 JSON 序列化默认不把枚举转字符串。请求与响应中的枚举字段，除单独标注字符串的自定义枚举外，都使用**数字值**。涉及到的关键枚举见[枚举速查](enums.md)。

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

> 管理端与 Agent 内部接口见 [专页](admin-internal-api.md)；异步操作查询见 [Webhook 与 operation](webhooks-operations.md)。

---
