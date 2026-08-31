# YINYU 文档导航

更新时间：2026-08-16

本文是 `docs/` 的唯一现行入口。开发、部署和交接先从本页选择文档，不在归档目录中搜索“最新方案”。

## 1. 必读顺序

1. [`../AGENTS.md`](../AGENTS.md)：长期协作、架构、质量和部署规则。
2. [`development/current-state.md`](development/current-state.md)：当前代码事实、已知缺口和任务起点。
3. [`../README.md`](../README.md)：产品能力、技术栈和本地启动。
4. [`platform-commercialization-master-plan.md`](platform-commercialization-master-plan.md)：现行架构、产品边界和建设顺序。
5. 与任务直接相关的模块、API 或运维文档。

## 2. 架构与开发规范

| 主题 | 现行文档 |
| --- | --- |
| 领域术语 | [`commercialization/domain-glossary.md`](commercialization/domain-glossary.md) |
| 模块所有权和依赖 | [`commercialization/module-boundary-map.md`](commercialization/module-boundary-map.md) |
| 外部 API 统一规则 | [`commercialization/external-api-standard.md`](commercialization/external-api-standard.md) |
| Agent 能力协商 | [`commercialization/agent-capability-protocol.md`](commercialization/agent-capability-protocol.md) |
| 运行事件分类 | [`commercialization/event-taxonomy.md`](commercialization/event-taxonomy.md) |
| 缓存失效 | [`commercialization/cache-invalidation-map.md`](commercialization/cache-invalidation-map.md) |
| 数据库索引与生命周期 | [`commercialization/database-index-and-lifecycle-audit.md`](commercialization/database-index-and-lifecycle-audit.md) |
| 模块文档覆盖 | [`modules/README.md`](modules/README.md) |

## 3. 前端规范

| 主题 | 现行文档 |
| --- | --- |
| 设计语言 | [`yinyu-vnext-design-language-draft.md`](yinyu-vnext-design-language-draft.md) |
| 页面、交互和 API 映射 | [`yinyu-vnext-page-interaction-api-spec.md`](yinyu-vnext-page-interaction-api-spec.md) |
| 开发边界 | [`yinyu-vnext-development-guardrails.md`](yinyu-vnext-development-guardrails.md) |
| 组件边界 | [`commercialization/frontend-component-boundary.md`](commercialization/frontend-component-boundary.md) |
| 样式 Token | [`commercialization/frontend-style-token-contract.md`](commercialization/frontend-style-token-contract.md) |
| 待补契约和验收 | [`yinyu-vnext-deferred-contract-gaps.md`](yinyu-vnext-deferred-contract-gaps.md) |

正式前端位于 `src/GZCTF/ClientApp/src/vnext`。设计文档定义体验和边界，真实路由与 API 仍以当前源码和 OpenAPI 为准。

## 4. 功能与接口说明

| 主题 | 现行文档 |
| --- | --- |
| Open API 调用 | [`commercialization/open-api-v1-guide.md`](commercialization/open-api-v1-guide.md) |
| OpenAPI 快照 | [`commercialization/openapi/open-v1.json`](commercialization/openapi/open-v1.json) |
| TeamLab API 基础契约 | [`commercialization/teamlab-api-foundation-contract.md`](commercialization/teamlab-api-foundation-contract.md) |
| TeamLab 外部控制面 | [`commercialization/teamlab-external-control-plane-contract.md`](commercialization/teamlab-external-control-plane-contract.md) |
| TeamLab 功能说明 | [`commercialization/teamlab-networking-feature-guide.md`](commercialization/teamlab-networking-feature-guide.md) |
| 平台能力与差异化 | [`yinyu-platform-capabilities-and-differentiation-20260813.md`](yinyu-platform-capabilities-and-differentiation-20260813.md) |
| 汇报与演示提纲 | [`yinyu-platform-leadership-report-and-demo-guide-20260810.md`](yinyu-platform-leadership-report-and-demo-guide-20260810.md) |

`open-v1.json` 是生成制品，不手工翻译或修改。接口变化后重新生成并审查 breaking/additive 差异。

## 5. 运维与验收

| 场景 | 现行文档 |
| --- | --- |
| 生产发布与回滚 | [`operations/vnext-maintenance-window-rollout.md`](operations/vnext-maintenance-window-rollout.md) |
| 公网入口和 ACK | [`operations/public-gateway-port-map-ack.md`](operations/public-gateway-port-map-ack.md) |
| WorkerNode | [`node-deployment/README.md`](node-deployment/README.md) |
| 镜像 Registry | [`registry-server/README.md`](registry-server/README.md) |
| Windows VM 速查 | [`operations/windows-vm-quick-deployment-guide.md`](operations/windows-vm-quick-deployment-guide.md) |
| Windows VM 完整说明 | [`operations/windows-vm-deployment-guide.md`](operations/windows-vm-deployment-guide.md) |
| 运行调度与恢复 | [`commercialization/runbooks/runtime-scheduling-and-recovery.md`](commercialization/runbooks/runtime-scheduling-and-recovery.md) |
| 日志、审计与恢复 | [`commercialization/runbooks/observability-audit-recovery.md`](commercialization/runbooks/observability-audit-recovery.md) |
| Redis 部署与恢复 | [`commercialization/runbooks/redis-deployment-and-recovery.md`](commercialization/runbooks/redis-deployment-and-recovery.md) |
| 数据库治理 | [`commercialization/runbooks/database-governance-operations.md`](commercialization/runbooks/database-governance-operations.md) |
| TeamLab 基础验收 | [`commercialization/runbooks/teamlab-foundation-acceptance.md`](commercialization/runbooks/teamlab-foundation-acceptance.md) |
| AWDP 人工验收 | [`yinyu-awdp-manual-acceptance.md`](yinyu-awdp-manual-acceptance.md) |

## 6. 交接与状态维护

- 当前事实只写入 [`development/current-state.md`](development/current-state.md)。
- 新 AI 或新成员接手项目先阅读 [`development/current-handoff.md`](development/current-handoff.md)。
- 并行开发和 AI 会话规范见 [`development/ai-development-playbook.md`](development/ai-development-playbook.md)。
- 跨人员或跨会话任务使用 [`development/task-handoff-template.md`](development/task-handoff-template.md)。
- 具体任务记录集中在 [`development/handoffs/`](development/handoffs/)。
- 并行开发、合并和任务分支清理遵守 `AGENTS.md` 的 Worktree 规则；任务完成后应保留提交和交接记录，不保留无主 worktree。
- 大任务计划完成后移入 `archive/implementation-records/`，不继续在原计划中维护当前状态。
- 生产地址、发布 SHA、备份和冒烟结果只有在现场重新验证后才能写入当前状态。

## 7. 归档规则

- `archive/pre-commercial-reset-20260709/`：商业化重置前的旧方案和交接资料。
- `archive/implementation-records/`：已完成的 Phase、计划、审查、基线、验收和一次性修复记录。
- 归档内容保留原始语言和历史路径以便审计，不参与当前开发决策，也不要求修复其中的旧链接。
- 需要追溯已删除文档时使用 Git 历史，不在现行目录保留危险的跳转页。

## 8. 文档维护要求

1. 现行说明使用简体中文；代码标识、命令、API 路径和标准协议名称保留原文。
2. 新增功能时同步更新模块说明、API、运维、测试和当前状态，不只增加阶段计划。
3. 文档中的“已完成”必须有源码、迁移、自动测试或真实运行证据。
4. 相对链接、路由、类名和配置键在提交前自动检查。
5. 归档文档不得重新加入 `AGENTS.md` 或本页的必读清单。
