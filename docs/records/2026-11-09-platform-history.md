# 历史负载与界面文案

状态：实现与本地验证完成；远端门禁和审查见 [PR #36](https://github.com/Y3X1L2/newGZCTF/pull/36)。负责人：Codex。

## 目标与基线

- 用户授权先合并 PR #35，再开发历史趋势、精简全平台多余说明，并审计开源复用；未授权新改动合并或部署。
- PR #35 于 2026-11-09 合入 main：`fcb14137b11ac03900334a7a15cbba00bdc8f278`。用户明确不将上一轮代理长连接审查项作为合并阻断。
- 新分支 `codex/platform-history-20261109`；主工作区 `D:\Work\newGZCTF`。
- 并行子任务独立 worktree：history-api（后端）、copy-cleanup（文案，Sol medium）、reuse-audit（审计，Sol high）；主任务负责前端趋势、集成与交付。

## 计划与进度

- [x] 合并 PR #35 并同步 main。
- [x] 复用持续写入的 WorkerNodeMetricSamples，实现历史查询；不新增采集器、表或迁移。
- [x] 概览和节点详情读取 1h / 24h / 7d 历史；使用现有 Recharts，删除浏览器会话累计与手写折线。
- [x] 清理多余开发者说明，保留操作风险、错误、单位和必要状态。
- [x] 完成[开源复用审计](2026-11-09-open-source-reuse-audit.md)及本地集成验证。
- [x] 推送并建立 PR #36，远端完整 Quality 与文档检查跟随最新提交运行。

## 验证与交付

- 文案子任务：40 文件；前端全门禁通过，469 项测试通过。
- 最终前端 `pnpm build` 通过：130 个测试文件、479 项测试，含 locale/lint/类型/架构/制品预算。日志 `D:\Work\newGZCTF-history-generated-build.log`。
- 安装隔离 .NET SDK `D:\Tools\dotnet`，启动本机 Docker 用于测试；未修改生产服务。
- 后端 Release build 通过；本机 PostgreSQL 16 / Redis 7 的新增历史接口集成测试 6/6 通过，覆盖三时间范围、空桶、异常读数过滤、历史保留、节点过滤与权限。日志 `D:\Work\newGZCTF-history-build.log`、`D:\Work\newGZCTF-history-tests.log`。
- 浏览器使用临时开发样本页验证历史组件：390/1366/1920/2560 宽度下日夜主题，无组件横向溢出；键盘切换范围有效；发现并修正采样点默认白色和时间刻度拥挤。图表关闭动画，提供可展开数据表。临时样本页已删除；这不是生产业务验收。
- 后端全量单元测试 1531/1531、OpenAPI 集成套件 12/12 通过；导出内部 v1 规范并使用仓库模板重新生成客户端。测试专用 controller 已排除出 API 文档。生成差异除新历史 DTO/方法、PR #35 的墓碑字段与类型位置变化外，均为当前规范的路由小写化；已核对 vNext 请求与缓存调用并重跑前端全门禁。历史 API 消费仍沿用 Runtime feature adapter 的运行时响应校验模式，未手改生成代码。
- 初次远端文档门禁发现 PR 复核理由不具体及文件尾空行，已补充逐模块依据并修正；未修改校验规则。
- 源码、测试与文档在本任务分支交付；新改动不合并、不部署。下一位接手先确认 PR #36 最新 CI 与差异，再决定合入或授权部署。开源监控和依赖维护后续项集中到 [backlog](../backlog.md)。
