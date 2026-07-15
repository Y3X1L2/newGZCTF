# YINYU vNext Phase 3/4 对齐说明

> 基线提交：`b45eb9b`（Phase 4 完成）
>
> 关联规格：[设计语言初稿](./yinyu-vnext-design-language-draft.md) / [页面交互与 API 规格](./yinyu-vnext-page-interaction-api-spec.md)

## 1. 对设计方案的影响

Phase 3/4 不改变 `Folded Domain / 折域` 的视觉方向，但会改变 TeamLab、渗透、日志、队列和流量页面的数据模型与交互语义。vNext Demo 和正式前端必须遵守本说明，不再兼容旧 Penetration topology DTO 或 offset 深分页。

## 2. TeamLab 契约

- TeamLab 独立拥有 topology draft、immutable release、runtime、generation、access grant、event、traffic flow 和 capture。
- Penetration 只保留比赛 binding、objective、submission、scoreboard 和 reset policy，通过 adapter 调用 TeamLab。
- topology、release、runtime、grant 和 capture 的外部标识均使用 GUID；页面不得依赖数据库整数 ID。
- runtime create/reset/destroy 是异步操作。界面提交后进入 operation 状态，不以首次 HTTP 成功代表部署完成。
- reset 保留 runtime public ID、递增 generation，并撤销旧 access grant；页面历史记录必须标明 generation。
- WireGuard access grant 是一次性、可撤销资源；下载按钮应显示有效期和撤销状态。

### 2.1 管理接口

```text
GET    /api/admin/teamlab/capabilities
GET    /api/admin/teamlab/topologies
POST   /api/admin/teamlab/topologies
GET    /api/admin/teamlab/topologies/{topologyId}
PUT    /api/admin/teamlab/topologies/{topologyId}
DELETE /api/admin/teamlab/topologies/{topologyId}
POST   /api/admin/teamlab/topologies/{topologyId}/validate
POST   /api/admin/teamlab/topologies/{topologyId}/releases
GET    /api/admin/teamlab/topologies/{topologyId}/releases
POST   /api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/plan
GET    /api/admin/teamlab/runtimes/{runtimeId}
GET    /api/admin/teamlab/runtimes/{runtimeId}/events
GET    /api/admin/teamlab/runtimes/{runtimeId}/traffic/flows
POST   /api/admin/teamlab/runtimes/{runtimeId}/captures
```

### 2.2 Open API

外部平台使用 `/api/open/v1/teamlab`，与管理端共用 application contract、operation、deployment queue 和 runtime facts。Demo 的 API Adapter 必须允许管理端和 Open API 共享领域类型，但不能共享鉴权实现。

## 3. Penetration 契约

选手工作区使用：

```text
GET  /api/pentest/games/{gameId}/workspace
POST /api/pentest/games/{gameId}/access-grants
GET  /api/pentest/games/{gameId}/access-grants/{grantId}/download
POST /api/pentest/games/{gameId}/submit
POST /api/pentest/games/{gameId}/reset
GET  /api/pentest/games/{gameId}/scoreboard
```

管理端只管理比赛与 TeamLab 的 binding、objectives、release activation 和队伍 runtime，不重新编辑一套平行拓扑。

## 4. Phase 4 查询契约

- 日志、部署队列、Submission 历史和 TeamLab flow 使用稳定时间游标。
- 响应返回 `items` 与 `nextCursor`；界面使用“加载更多”或虚拟列表，不显示无法随机跳转的伪页码。
- 筛选条件变化时清空游标链并重新获取首屏。
- 非法游标返回 `invalid_cursor`，前端清空游标后重试一次，并保留当前筛选条件。
- PostgreSQL 是事实来源；前端缓存只能降低重复请求，不能把 Hub 或本地状态当作唯一事实。

## 5. Demo 数据边界

Demo 使用与真实接口同形的 mock contracts：

- `TopologySummary`, `TopologyRelease`, `RuntimeSummary`, `RuntimeEvent`。
- `CursorPage<T> = { items: T[]; nextCursor: string | null }`。
- `AsyncOperation = { id, status, progress, resourceId, errorCode }`。
- `VITE_DATA_MODE=mock|live` 控制 Adapter；页面组件不得判断数据来源。

## 6. 验收重点

1. TeamLab 页面不再出现旧 Penetration topology DTO 字段。
2. runtime reset 后页面保持同一 runtime 路由并切换 generation。
3. operation 未完成时不提前显示“部署成功”。
4. 日志、队列和流量列表不使用 offset 页码。
5. 断线重连后重新请求 REST snapshot，再恢复实时增量。
6. 所有 GUID 路由在复制、刷新和直接打开时可恢复页面状态。
