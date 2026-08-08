# 前端实现任务：网络区域工作台 + 观测前端（给前端实现 agent 的完整任务书）

仓库：D:\newgz\newGZCTF-main。前端目录：src/GZCTF/ClientApp（pnpm 项目）。这是实现任务，直接写代码；所有文本用简体中文；不要改后端代码。

## 第一步：读这些文件理解现状（必须）

1. `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/` 全部文件，重点：
   - `canvas/TeamLabCanvas.tsx` 与 `canvas/TeamLabCanvas.module.css`
   - `layout/autoLayoutTopology.ts`
   - `TeamLabDesignPage.tsx` 与 `TeamLabDesignPage.module.css`
   - `inspector/TeamLabInspector.tsx`、`inspector/AssetInspector.tsx`、`inspector/NetworkInterfacesEditor.tsx`
   - `state/`、`nodes/`、`edges/`、`validation/`、`palette/` 目录
2. `api/teamlabAdminApi.ts`、`api/teamlabContracts.ts`、`api/teamlabParsers.ts`（API 层模式：feature API adapter，不要直接访问生成 API 文件）
3. `runtimes/RuntimeRemoteAccessPanel.tsx`、`runtimes/useRuntimeLogs.ts`、`runtimes/RuntimeLogPanel.tsx`、`runtimes/RuntimeEventPanel.tsx`、`runtimes/teamlabRuntimeApi.ts`、`runtimes/useTrafficObservability.ts`、`runtimes/TrafficFlowPanel.tsx`
4. `api/teamlabImageCatalog.ts`（现有目录类组件模式）、`shared/TeamLabStatusBadge.tsx`
5. 编辑器现有测试：`canvas/TeamLabCanvas.test.tsx`（若有）、`layout/autoLayoutTopology.test.ts`（若有）——测试模式要模仿
6. `editor/TeamLabDesignPage.module.css` 与既有 CSS Module 风格

## 后端已就绪的能力（本任务的前端要消费它们）

- 服务目录 API（已上线）：
  - `GET /api/open/v1/teamlab/service-profiles?limit&after` → `{ items: [{ id, version, name, description, assetKinds, updatedAt }], nextCursor }`
  - `GET /api/open/v1/teamlab/service-profiles/{id}` → `{ id, version, name, description, assetKinds, parameters: [{ key, type, required, secret, defaultValue }], execution: { steps, healthChecks, maxReboots, phase }, status, documentationUrl, publishedAt }`
  - 需要浏览器管理员权限（平台管理员 token 或管理员会话）。参考现有 `teamlabImageCatalog.ts` 如何调 `/api/open/v1/...`。
- 事件 generation/stage 过滤（已上线）：
  - `GET /api/admin/teamlab/runtimes/{id}/events?after&limit&generation&stage`
- 批量 remote availability（已上线）：
  - `GET /api/admin/teamlab/runtimes/{id}/remote-access` → `[{ assetId, name, protocol, available, unavailableReason }]`（替代逐资产 `GET .../assets/{assetId}/remote-access` 扇出）

## 任务 A：网络区域工作台（计划 Task 8）

新建文件：
1. `editor/regions/NetworkRegionNode.tsx` + `editor/regions/NetworkRegionNode.module.css`：
   - 在 React Flow 画布上把每个 network key 渲染为一个"区域"节点（由 `editor.networks` 驱动，布局项 `{ x, y, width, height, collapsed }`，key 为 network key）
   - 区域是纯视觉容器：区域内的成员资产/交换机节点跟随区域；跨区域边从区域边界穿出
   - 区域支持点击选中、双击聚焦（fitView 到区域内所有子节点）、拖动调整大小（可选：若现有 React Flow 版本不方便做 resize，则至少支持拖拽移动区域并让内部节点跟随）
   - 折叠状态（collapsed）切换
2. `editor/help/teamLabFieldHelp.ts`：共享中文帮助元数据（键→{标题, 说明}），覆盖：主机偏移（host offset）、网卡顺序（interface order）、发布时烘焙（publish-time baking）、端点观测（endpoint observation）、服务注入（service injection）、健康检查（health checks）、网络区域（network regions）。只给有实质说明的字段配帮助；行业缩写保留但要配中文解释。
3. `editor/help/FieldHelpButton.tsx`：小问号按钮 + Popover/Tooltip 展示帮助；无帮助内容的字段不渲染按钮。

修改文件：
4. `editor/layout/autoLayoutTopology.ts`：确定性分步自动布局：先放网络区域（按拓扑序），再在区域中心放交换机，成员资产围绕交换机摆放，路由器放在相连区域之间，跨区域边走区域边界；手动设置的区域尺寸在能容纳子节点时保留，放不下时才扩张。
5. `editor/canvas/TeamLabCanvas.tsx` + `TeamLabCanvas.module.css`：
   - 空白画布拖拽产生多选框（box selection），保留节点直接拖拽；Space 或中键拖拽平移
   - 区域点击选中、双击聚焦；全拓扑 fit；当前区域 fit
   - 多选摘要：网络成员关系、跨网络连接数、请求资源汇总（CPU/内存），**不做浏览器端调度**
   - 调色板（palette）与属性检查器（inspector）独立垂直滚动；画布 pan/zoom 独立；body 不做滚动容器
6. `editor/inspector/ServiceProfilePicker.tsx`：用服务目录数据的搜索/选择控件，展示用途、支持的资产类型、公开参数（key/type/required/secret/defaultValue）、执行阶段、文档链接；普通模式下不暴露裸 Profile ID 输入框；选择结果只写 profile reference + 公开参数（写回拓扑 asset 的 bootstrap 引用模型，先读明白现有 bootstrap 引用字段再写）
7. `editor/inspector/TeamLabInspector.tsx`、`editor/inspector/AssetInspector.tsx`、`editor/inspector/NetworkInterfacesEditor.tsx`、`TeamLabDesignPage.tsx`、`TeamLabDesignPage.module.css`：接入区域节点/帮助按钮/服务目录选择器；检查器区域独立滚动

约束：
- 布局是纯演示层：**任何布局改动不得改变发布执行摘要（digest）**——只写 `editor` 视图字段
- 遵循 `Route -> feature controller/hook -> feature panel -> foundation component` 依赖方向；页面不直接访问生成 API 文件
- 支持日间/夜间、键盘操作、`prefers-reduced-motion`
- 在 390/1366/1920/2560 宽度检查重叠与横向滚动

## 任务 B：观测前端（计划 Task 7 前端部分）

8. `runtimes/RuntimeRemoteAccessPanel.tsx` + `api/teamlabRemoteAccessApi.ts`：**移除逐资产 Promise.all 扇出**，改用批量端点 `GET /api/admin/teamlab/runtimes/{id}/remote-access`；保留每资产检查中/错误状态（单资产不可用不拖垮整批）
9. `runtimes/useRuntimeLogs.ts`/`RuntimeLogPanel.tsx`/`RuntimeEventPanel.tsx`：事件面板加 generation 下拉与 stage 筛选（接后端参数）；日志面板保留 cursor 分页；从失败资产/阶段到对应证据视图（事件 tab 预过滤）的链接

## 测试与门禁（必须做）

- 为 `editor/layout/autoLayoutTopology.ts` 写单元测试（相同输入→相同输出；区域+交换机+成员+路由器的确定性）；为区域渲染/多选/平移写 `editor/canvas/TeamLabCanvas.test.tsx`（若有现成测试文件则扩展）
- 运行：`pnpm validate:locales && pnpm lint:check && pnpm check && pnpm check:architecture && pnpm test && pnpm build`（在 src/GZCTF/ClientApp 下，pnpm 已装）。全部通过才算完成；若有失败要修复到通过
- 不做大而无当的重构；改动最小化到任务要求

## 输出

完成后报告：新增/修改文件清单、每个任务的完成情况、测试与门禁结果（贴关键输出）、遗留问题（若有）。
