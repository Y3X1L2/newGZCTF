# YINYU 文档导航

维护日期：2026-10-11。本页是现行文档的唯一导航，按任务选读；历史记录不进入必读清单。

## 接手只读三个入口

1. [AGENTS.md](../AGENTS.md)：长期协作、代码边界与质量门禁。
2. [当前状态](development/current-state.md)：代码 / 发布基线与重点缺口。
3. 本页：选择任务相关文档；继续已有任务再读指定 handoff。

第一次安装项目读 [根 README](../README.md)；调整产品范围或架构读 [平台总纲](platform-commercialization-master-plan.md)。普通修复不必反复通读这两份概览。

## 文档结构与职责

| 层次 | 位置 | 写什么 / 谁更新 |
| --- | --- | --- |
| 规则 | 根 AGENTS.md | 长期边界与流程；规则变更经审查 |
| 当前事实 | development/current-state.md | 120 行以内基线和索引；集成 / 发布负责人替换旧事实 |
| 未决事项 | development/backlog.md | 一个事项一条，接手与关闭有证据；相关任务负责人更新 |
| 现行说明 | modules/、commercialization/、operations/ 及本页列出的专项说明 | 模块、契约、使用与运维；改实现的人同步维护 |
| 任务证据 | development/handoffs/、development/test-reports/ | 一任务一记录，计划也写在该记录；短任务用 PR 说明，测试报告按提交和环境限定 |
| 历史 | archive/ 与 Git 历史 | 过期审计和唯一历史证据；不作为当前操作指令 |

保留现有有效路径，避免为统一目录名制造大规模链接变更。新使用/运维手册归 operations/，新模块说明归 modules/，不再放入 development/ 充当进度计划。

## 按任务选择

| 任务 | 阅读入口 |
| --- | --- |
| 后端架构 / 跨模块 | [模块边界](commercialization/module-boundary-map.md)、[术语](commercialization/domain-glossary.md)、[模块索引](modules/README.md) |
| 外部 API / Agent | [API 标准](commercialization/external-api-standard.md)、[调用指南](commercialization/open-api-v1-guide.md)、[Agent 能力协议](commercialization/agent-capability-protocol.md) |
| 数据 / 缓存 / 审计 | [索引生命周期](commercialization/database-index-and-lifecycle-audit.md)、[缓存失效](commercialization/cache-invalidation-map.md)、[事件分类](commercialization/event-taxonomy.md) |
| 前端 | [开发边界](yinyu-vnext-development-guardrails.md)、[组件边界](commercialization/frontend-component-boundary.md)、[Token](commercialization/frontend-style-token-contract.md)；设计时补读 [设计语言](yinyu-vnext-design-language-draft.md) 与 [页面/API 规格](yinyu-vnext-page-interaction-api-spec.md) |
| TeamLab 制作 / 调用 | [制作导入](teamlab-authoring-guide.md)、[平台对接](commercialization/teamlab-integration-guide.md)、[基础契约](commercialization/teamlab-api-foundation-contract.md)、[外部控制面](commercialization/teamlab-external-control-plane-contract.md)、[运行事实](modules/teamlab-runtime-capability-contract.md) |
| TeamLab 执行 / 排障 | [组网最佳实践](development/teamlab-networking-best-practices.md)、[操作与接口手册](operations/teamlab-networking-operations-manual.md)、[功能说明](commercialization/teamlab-networking-feature-guide.md)、[执行面手册](commercialization/runbooks/teamlab-high-performance-execution.md) |
| TeamLab 受管网络 | [制作要求](operations/teamlab-managed-network-authoring.md)、[发布验收](operations/teamlab-managed-network-rollout.md)、[Agent 网桥恢复](operations/agent-guest-network-recovery.md) |
| 培训实例 | [课程容器生命周期](modules/training-container-lifecycle.md) |
| 生产发布 | [维护窗口与回滚](operations/vnext-maintenance-window-rollout.md)、[公网入口 ACK](operations/public-gateway-port-map-ack.md)，再核现场与当前发布记录 |
| 节点 / 镜像 / Windows | [WorkerNode](node-deployment/README.md)、[Registry](registry-server/README.md)、[Windows 速查](operations/windows-vm-quick-deployment-guide.md)、[完整说明](operations/windows-vm-deployment-guide.md) |
| 运行 / 数据恢复 | [调度恢复](commercialization/runbooks/runtime-scheduling-and-recovery.md)、[审计恢复](commercialization/runbooks/observability-audit-recovery.md)、[Redis](commercialization/runbooks/redis-deployment-and-recovery.md)、[数据库治理](commercialization/runbooks/database-governance-operations.md) |
| 实机验收 | [TeamLab 基础](commercialization/runbooks/teamlab-foundation-acceptance.md)、[AWDP 人工验收](yinyu-awdp-manual-acceptance.md) |
| 后续任务 / 交接 | [后续事项](development/backlog.md)、[任务索引](development/handoffs/README.md)、[短交接模板](development/task-handoff-template.md) |
| 汇报 / 演示 | [能力说明（2026-08-13）](yinyu-platform-capabilities-and-differentiation-20260813.md)、[演示提纲（2026-08-10）](yinyu-platform-leadership-report-and-demo-guide-20260810.md)；仅作表达材料，能力状态需重核 |

[OpenAPI 快照](commercialization/openapi/open-v1.json)是生成制品，接口变更后重新生成并审查兼容差异，禁止手改。路由、配置键和源码符号以最新主线为准，链接检查不能证明它们仍符合实现。

## 文档生命周期

- 规则、当前事实、未决事项、实施过程分开；相同信息只维护一个权威位置，其他页引用。
- 复杂任务用一个 handoff 同时承载计划、决定、进度和最终证据；不另建 plan/progress/current-handoff 三套文件。
- 完成时把稳定行为写回现行模块/契约/手册，把未完成事项写入 backlog；任务记录封存，只有事实纠错才更新。
- 已被现行说明与最终记录替代的计划直接删除，Git 可追溯；含唯一决策、失败证据或未决问题的材料转历史，先提取接手事项。
- 不按日期自动删契约、迁移、运维脚本或仍承担回退证明的记录；归档不全量阅读、不批量恢复旧方案。
- 当前发布与回退证据保留，历史测试数字只说明当次提交；旧授权不能继承到新任务。

原设计与六项顾虑的审计、推荐模块结构、读者分层、自动维护方案，以及第一轮清理清单见 [文档体系方案](development/documentation-audit.md)。推荐目标尚未全量迁移；本页是当前过渡入口。

## 提交前检查

```powershell
git diff --check
python scripts/validation/check-documentation.py
```

检查现行 Markdown 本地文件链接与状态页长度；不联网检查外链、不检查锚点/API/类名，不读归档正文。删除文档还需全仓搜索旧路径，确认脚本、测试和入口没有继续依赖它。该轻量门禁在 Documentation 工作流运行；业务代码改变时仍按 AGENTS.md 执行对应门禁。
