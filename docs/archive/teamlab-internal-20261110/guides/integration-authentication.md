# TeamLab 对接：身份、请求头、operation 与资源标识

完整调用顺序见 [对接入口](integration.md)，认证与幂等要求见 [调用约定](integration-authentication.md)。本文说明实现契约，现场验收范围见 [当前状态](../../../current-state.md)。

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
