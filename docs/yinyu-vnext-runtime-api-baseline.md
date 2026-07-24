# YINYU vNext 运行时 API 基线

## 1. 对齐目标

本地 vNext 开发环境通过未跟踪的 `.env.local` 指向当前稳定服务端，仓库代码不包含测试服务器地址。API 对齐以实际运行时响应为准，GitHub 最新 `main` 仅用于识别后续契约变化。

当前服务端未开放 OpenAPI 文档，因此验收同时核对状态码、`Content-Type`、JSON 结构和业务字段，不能把 HTTP 200 直接视为接口成功。

## 2. 已核对契约

| 领域 | 当前稳定服务端 | 已知新版契约 | vNext 处理方式 |
| --- | --- | --- | --- |
| 镜像 | `GET /api/v1/image-templates`，页码分页 | 路径和主体结构兼容 | 类型化页模型与字段校验 |
| Registry | `GET /api/v1/image-templates/docker-registry` | 兼容 | 校验启用状态、地址、命名空间和上传上限 |
| 节点 | `GET /api/v1/nodes`，返回数组 | 兼容并可能增加字段 | 保留调度、容量、端口池和 TeamLab 能力字段 |
| 节点资源 | `GET /api/v1/nodes/{id}/resources`，页码分页 | 兼容 | 统一容器、VM、渗透和 TeamLab 资源模型 |
| 部署任务 | `GET /api/v1/deployment-targets`，页码分页 | `GET /api/v1/deployment-queue`，游标分页 | Adapter 自动识别并归一化两种契约 |
| 系统日志 | `GET /api/admin/logs`，数组响应，使用 `count/skip` | `items/nextCursor` 游标响应 | Adapter 归一化为统一日志页模型 |
| 传统实例 | `GET /api/admin/instances`，仅比赛容器 | 暂无完整全域实例列表 | 保留兼容接口，全域实例以节点资源接口为事实来源 |

## 3. 已确认的契约风险

1. 当前 Vite 代理访问不存在的 API 时，服务端可能返回 SPA HTML，状态码仍为 200。
2. 生成客户端中的 `/api/v1/deployment-queue` 与当前稳定服务端不一致。
3. 生成客户端把部分镜像、节点和队列响应声明为 `Blob`，不能直接提供页面所需的静态类型。
4. 日志接口在当前服务端和新版后端之间采用不同分页模型。
5. `/api/admin/instances` 不是全域运行实例视图，不能用于统计 VM、培训、渗透和 TeamLab 的全部资源。

## 4. 前端约束

- vNext 页面不得直接调用 `fetch` 或生成客户端的易变命名空间。
- 管理运维页面统一使用 `src/vnext/features/admin/api` 下的领域 Adapter。
- `runtimeJsonClient` 必须验证 JSON Content-Type；HTML 回退、空响应和结构漂移均作为契约错误处理。
- 部署队列路径兼容被集中在单一 Adapter 内，页面不感知服务端版本。
- 后续服务端升级后，只替换 Adapter 或删除兼容分支，不修改页面组件。
