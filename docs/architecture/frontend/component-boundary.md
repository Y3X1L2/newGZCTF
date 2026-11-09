# vNext 组件与请求边界

正式前端在 [src/vnext](../../../src/GZCTF/ClientApp/src/vnext)。先从功能目录进入；共用组件解决布局与交互，共用组件不决定业务权限。

```text
路由 / 页面 → feature controller 或 hook → feature panel → shared 展示组件
                         ↓
                feature API adapter → @Api → generated/Api.ts
```

## 当前目录

| 位置 | 职责 |
| --- | --- |
| [app/VNextApp.tsx](../../../src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx) | 注册路由与懒加载入口 |
| [app/shell](../../../src/GZCTF/ClientApp/src/vnext/app/shell) | 页面壳层和模块导航 |
| [features](../../../src/GZCTF/ClientApp/src/vnext/features) | 按功能组织页面、控制逻辑、adapter 和样式 |
| [shared](../../../src/GZCTF/ClientApp/src/vnext/shared) | 表单、菜单、表格分页、布局等可复用展示与交互 |
| [design](../../../src/GZCTF/ClientApp/src/vnext/design) | 主题、Token 与全局设计基础 |
| [api-client](../../../src/GZCTF/ClientApp/src/api-client) / [generated](../../../src/GZCTF/ClientApp/src/generated) | 稳定 API 门面与生成代码 |

## 修改时遵循的边界

- 页面读取路由参数、组织区域并展示加载/空态/失败；业务请求与 DTO 转换放到 feature adapter/controller，不能在大页面里同时维护整条链路。
- 通用展示组件接收数据和回调，不自己读取 CourseId/GameId、调用业务 API 或判断权限。最终授权由服务端执行。
- 跨功能引用公开的稳定接口；培训与赛事 feature 不互相导入。需要共享的基础展示提到 shared，不复制状态或请求逻辑。
- `.tsx` 仅使用 CSS Module；语义颜色与尺寸来自 [样式 Token](style-tokens.md)。画布几何值可例外传运行变量，不能借此恢复无作用域覆盖。
- vNext 不引入旧页面/组件/全局样式或 Mantine；页面组件不直接 import 生成 API。通过 feature adapter 使用 `@Api`，生成文件由现有工具更新。
- 缺少接口显示真实限制，异步写操作依据服务器状态回读；不能用固定等待或前端数字变化充当成功。

## 拆分与验证

共享 `ActionButton` 接收返回 Promise 的 `onClick`，请求期间显示等待标记、设置 `aria-busy` 并阻止重复点击；回调应返回实际 Promise，不用 `void` 丢弃它。服务器异步任务在请求返回后仍执行时，由所属 controller 按真实任务状态传入 `loading` 和可选 `loadingLabel`。表单提交按钮同样显式传 `loading`。按钮不推断业务成功，业务错误与最终结果仍由 feature 负责；未处理的请求异常显示通用重试提示，不暴露服务端详情。

按钮支持悬停、按压与键盘焦点反馈；减少动效时停止旋转与位移动画，保留等待语义和禁用状态。

同一组件承担多个独立请求、权限、弹窗与大型视觉区域时，按用户操作或业务链路拆 controller/panel；不要按行数机械拆出没有职责的文件。保留完整加载、失败、重试和取消路径。

架构检查由 [check-frontend-architecture.mjs](../../../src/GZCTF/ClientApp/scripts/check-frontend-architecture.mjs) 执行；它检查生成 API 入口、旧依赖、部分跨 feature 引用与样式边界，并不替代代码审查。完整门禁与视口要求见 [开发边界](development.md)。
