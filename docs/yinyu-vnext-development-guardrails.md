# YINYU vNext 开发边界

本文是当前正式前端的开发约束，适用于 `src/GZCTF/ClientApp/src/vnext` 下的新增功能、重构和缺陷修复。历史 Demo、阶段计划和归档材料不能作为实现依据。

## 1. 依据与优先级

出现实现分歧时按以下顺序判断：

1. 当前运行行为、真实请求和浏览器证据；
2. 当前 `main` 源码、OpenAPI 和自动化测试；
3. `AGENTS.md` 与 `docs/development/current-state.md`；
4. [页面交互与 API 规格](./yinyu-vnext-page-interaction-api-spec.md)；
5. [前端组件边界](./commercialization/frontend-component-boundary.md)；
6. [前端样式 Token 契约](./commercialization/frontend-style-token-contract.md)；
7. [前端设计语言规范](./yinyu-vnext-design-language-draft.md)。

`docs/archive/`、废弃原型目录和历史页面只用于追溯，不能直接复制回正式依赖图。

## 2. 代码边界

依赖方向固定为：

```text
Route -> feature controller/hook -> feature panel -> foundation component
                         |
                         +-> feature API adapter -> generated API client
```

- 路由只负责参数解析、权限入口和页面装配。
- 请求、DTO 规范化、兼容处理和错误映射放入所属 feature 的 API adapter。
- 复杂业务状态进入 controller、hook 或 reducer，不与大段 JSX 混写。
- foundation 组件只接收展示数据和回调，不读取路由参数、不发业务请求、不判断业务权限。
- 页面不得直接访问生成 API 文件；生成代码由 OpenAPI 重新生成，不手工修改。
- 跨 feature 复用应提取稳定领域能力，禁止通过互相导入页面组件形成隐式耦合。

## 3. 视觉与布局

- 使用 vNext 壳层、语义 Token、CSS Module、既有 Lucide 图标和统一交互组件。
- 不新增无作用域全局选择器，不恢复 `YinyuRefinement.css`、`YinyuTheme.css` 等覆盖链。
- 业务状态通过文本、图标和语义色共同表达，不能只依赖颜色。
- 页面只有明确的主滚动容器；抽屉、弹窗和工作台独立控制滚动，不能制造页面级横向滚动条。
- 固定格式区域使用稳定网格、宽度约束和 `aspect-ratio`，避免异步内容导致布局抽动。
- 日间与夜间主题均使用语义 Token，不在页面里硬编码主题分支颜色。
- 动效解释层级和状态变化；支持 `prefers-reduced-motion`，不以持续装饰动画干扰操作。

## 4. 数据与交互

- 只展示服务端真实事实，不伪造统计、实例、通知、活动、权限或成功结果。
- 每个读取页面必须有加载、空、错误和可重试状态；刷新时保留可用旧数据的场景应明确标记正在刷新。
- 写操作必须防止重复提交；异步任务展示服务端状态，不使用固定延迟推断成功。
- URL 中只保留可分享、可恢复的页面状态。筛选和标签切换应规范化无效参数，不能让旧查询参数阻断导航。
- 表单值不能从已卸载事件对象中读取；先复制原始值，再更新 React 状态或执行异步操作。
- 权限由服务端最终裁决。前端可隐藏无权限入口，但不能把隐藏按钮当作授权边界。
- 接口缺失时显示真实限制或待建设状态，不加载旧页面，不拼接假的成功路径。

## 5. 当前路由原则

正式路由以 `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx` 为准。已注册的认证、赛事、练习、培训、个人、通用管理、赛事管理、AWDP 和 TeamLab 页面均属于当前 vNext，不再使用“首批只实现首页和赛事列表”的阶段假设。

未注册路由进入统一待建设页。新增一级模块时必须同时更新：

- 路由与懒加载边界；
- `moduleRegistry.ts` 导航元数据；
- feature adapter 和权限入口；
- 国际化文本；
- 页面/API 规格及必要的模块文档。

## 6. 验收门禁

每个完整垂直切片至少验证：

- 真实 API 的加载、空、错误、重试和写入回读；
- 390、1366、1920、2560 像素宽度无重叠、横向滚动和布局抽动；
- 日间、夜间、键盘操作和 reduced-motion；
- 刷新、深链接、浏览器前进后退和无效查询参数；
- 权限拒绝、并发提交和异步任务失败；
- strict TypeScript、架构检查、测试和生产构建。

前端完整门禁：

```powershell
cd src/GZCTF/ClientApp
pnpm validate:locales
pnpm lint:check
pnpm check
pnpm check:architecture
pnpm test
pnpm build
```

真实 Docker、VM、TeamLab、AWDP、镜像和公网入口仍需在授权基础设施上单独验收，不能仅凭 mock 或构建通过判定完成。
