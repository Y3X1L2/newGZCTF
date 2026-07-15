# YINYU vNext 开发边界

本文件约束本轮正式前端重构。出现实现分歧时，按以下优先级执行：

1. `yinyu-vnext-design-language-draft.md`
2. `yinyu-vnext-page-interaction-api-spec.md`
3. `yinyu-vnext-phase34-alignment.md`
4. `docs/commercialization` 中已经落地的后端与前端契约
5. `D:/Work/newGZCTF-vnext-demo/src/GZCTF/ClientAppVNext` 的视觉与动效表现

Demo 只用于确认布局比例、视觉层级、主题和动效，不复制 mock 数据、固定 ID、路由或 API 适配器。

## 实现规则

- 已重构页面必须使用新的 DOM、CSS Module 和语义 Token。
- 只复用业务 API、生成类型、权限判断、国际化和经过确认的无视觉业务逻辑。
- 不复用旧页面布局组件、旧视觉 class 或历史全局覆盖来拼装新页面。
- 已重构页面不得依赖 `YinyuDesignLab.css`、`YinyuTheme.css`、`YinyuRefinement.css` 中的页面级选择器。
- 新入口不加载旧页面或旧壳层。未重构路由进入统一的正式待建设状态，不回退到历史页面。
- 每批只迁移少量完整路由，完成真实数据、交互、响应式、日夜主题和回归验证后再扩大范围。
- 新的通用组件必须是无业务 API 的展示组件；业务请求和状态机进入对应 feature。
- 页面不得伪造统计、实例、通知、活动或用户数据。接口缺失时显示正式空态。

## 首批范围

1. vNext Token、主题和路由壳层。
2. 首页 `/`。
3. 赛事列表 `/games`。

首批不实现 CTF、培训、管理和 TeamLab 页面。它们在各自垂直切片开始前只显示统一待建设状态，旧源码不进入新路由依赖图。

## 每页退出门槛

- 真实 API 数据及错误、加载、空状态完整。
- 390、1366、1920、2560 宽度无重叠、无页面级横向滚动。
- 日间与夜间主题均符合语义色规则。
- `prefers-reduced-motion` 下停止持续动画。
- 页面切换不引发布局抽动。
- strict TypeScript、架构检查和生产构建通过。
- 重构页面静态扫描不命中旧视觉 class 和旧页面级 CSS。
