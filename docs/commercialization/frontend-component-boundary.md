# 前端组件边界契约

## 1. 分层模型

前端依赖方向固定为：

```text
Route Page
  -> Feature Controller / Hook
  -> Feature Panel
  -> Foundation / Presentation Component
  -> Mantine / browser primitive

Feature Controller / Hook
  -> Module API Adapter
  -> Stable API Facade
  -> Generated API Client
```

反向依赖禁止。展示组件不能引用页面、业务 API 或权限服务。

## 2. 目录职责

| 目录 | 职责 | 禁止内容 |
| --- | --- | --- |
| `pages` | 路由参数、权限门禁、页面级编排 | 大段领域转换、私有全局 CSS、直接 fetch 拼接 |
| `components/foundation` | 页面容器、表格外壳、空态、状态和响应式承载 | 业务 API、GameId/CourseId 等领域概念 |
| `components/<feature>` | 可复用业务面板和交互 | 路由注册、跨模块状态穿透 |
| `hooks/<feature>` | 请求组合、缓存 key、页面状态机 | JSX 和视觉 class |
| `Api` / `utils/*Api` | 模块 API 适配和 DTO 映射 | 页面布局、通知文案 |
| `api-client` | 稳定 API 门面和全局请求策略 | 业务模块特例 |
| `generated` | 机器生成代码 | 手工修改 |
| `styles/foundation` | Token、基础布局、可访问性和响应式规则 | 页面 ID、业务路由特例 |
| `styles/components` | 单个组件的 CSS Module | 无归属的全站覆盖 |

## 3. 页面文件规则

页面文件负责：

1. 读取路由参数。
2. 执行顶层权限和不存在状态判断。
3. 调用 controller hook。
4. 组合页面区块。
5. 提供页面标题和返回路径。

页面文件不得新增：

- 视觉用途的 inline `style`；仅允许运行时几何值、CSS 自定义属性和第三方画布坐标。
- 无命名空间的全局 class。
- 直接访问生成 API 文件路径。
- 超过一个业务域的数据转换。
- 依赖视口宽度后直接拒绝渲染的逻辑。

建议目标：普通路由文件不超过 350 行，复杂编排路由不超过 600 行。超过目标必须在 Phase 文档中记录原因和拆分计划。

## 4. Controller Hook 规则

Controller Hook 负责：

- SWR key 和刷新策略。
- 加载、错误、空态和权限状态。
- 表单 draft 和提交状态机。
- 乐观更新与失效范围。
- DTO 到 view model 的适配。

Hook 返回语义化操作，例如 `approveStudent`、`publishCourse`、`deployRuntime`，而不是把底层 `fetch` 或 axios response 暴露给组件。

## 5. 公共组件规则

公共组件必须：

- 接受语义化属性并支持 `className`。
- 不假设具体业务实体。
- 在 360 像素宽度下保持可操作。
- 支持键盘焦点和可读的空态/错误态。
- 使用 Token，不定义新的裸颜色、阴影和动效常量。

首批公共组件：

| 组件 | 用途 |
| --- | --- |
| `PageShell` | 统一页面宽度、间距、背景层和横向溢出 |
| `PageHeader` | 标题、说明、状态和操作区 |
| `Surface` | 统一面板层级和密度 |
| `DataToolbar` | 筛选、搜索、刷新和主操作 |
| `ResponsiveTable` | 容器内滚动、稳定最小宽度和移动降级 |
| `EmptyState` | 空数据、无权限和未配置状态 |
| `StatusBadge` | 统一 success/warning/danger/neutral/info 语义 |
| `MetricGrid` | 稳定指标布局，不因文本变化跳动 |

## 6. 三个高风险页面的目标边界

### 课程详情

```text
CourseDetailPage
  CourseHeader
  CourseNavigation
  IntroPanel / ChapterPanel / ResourcePanel
  StudentPanel / TeacherPanel
  EnvironmentPanel / ChallengePanel / TheoryPanel / HomeworkPanel
  CourseDialogs
  StudentLearningDrawer
```

课程面板共享 `CourseDetailController`，但彼此不读取对方内部状态。

### TeamLab/Penetration

```text
PenetrationAdminPage
  usePenetrationBuilder
  TopologyCanvas
  TopologyPropertyPanel
  ConnectivityPlanPanel
  RuntimeOperationsPanel
  RuntimeObservability
```

拓扑转换和默认蓝图必须是无 React 状态的纯函数，可独立单测。

### 节点管理

```text
NodesPage
  useNodesController
  NodeSummary
  NodeFilters
  NodeGrid
  AddNodeModal
  NodeResourcePanel
```

资源分页和筛选属于资源面板；节点列表不持有资源详情内部状态。

## 7. 迁移兼容规则

- 现有 `YinyuUI` 在迁移期可继续使用，但新的通用承载能力进入 `components/foundation`。
- 历史 `yy-*` class 不要求一次删除；被迁移组件不得再新增跨页面选择器。
- 每次迁移必须先保持 DOM 行为和 API 调用一致，再清理样式。
- 不允许为完成拆分复制同一份状态或请求逻辑。
