# YINYU 文档导航

本文是 `docs/` 的统一入口。文档数量较多时，先判断生命周期，再决定是否把内容当作当前事实。

## 1. 文档生命周期

| 类型 | 含义 | 使用规则 |
| --- | --- | --- |
| 现行规范 | 当前代码必须遵守的架构、API、数据和运维规则 | 可以作为实现与审查依据 |
| 当前状态 | 会随提交、部署和验收变化的短期事实 | 任务开始时必须重新核对 Git 与运行环境 |
| 实施记录 | 已完成阶段的计划、验收和问题修复证据 | 用于解释历史决策，不代表当前分支或服务器状态 |
| 历史归档 | 已废弃方案、旧审查和商业化重置前资料 | 只用于追溯，不得作为当前实现依据 |
| 已退役 | 已知错误或已被替代的操作入口 | 不得执行，按文档中的新入口跳转 |

文件名包含日期、`phase`、`plan`、`progress`、`acceptance` 或 `baseline` 时，默认先视为实施记录，除非本索引明确列为现行规范。

## 2. 开始阅读

| 目的 | 文档 |
| --- | --- |
| 协作和质量规则 | [`../AGENTS.md`](../AGENTS.md) |
| 当前代码、环境和缺口 | [`development/current-state.md`](development/current-state.md) |
| 产品与快速开始 | [`../README.md`](../README.md) |
| 商业化目标与阶段顺序 | [`platform-commercialization-master-plan.md`](platform-commercialization-master-plan.md) |
| 模块文档覆盖情况 | [`modules/README.md`](modules/README.md) |
| 平台能力与差异化 | [`yinyu-platform-capabilities-and-differentiation-20260813.md`](yinyu-platform-capabilities-and-differentiation-20260813.md) |
| 汇报与演示指南 | [`yinyu-platform-leadership-report-and-demo-guide-20260810.md`](yinyu-platform-leadership-report-and-demo-guide-20260810.md) |
| 跨会话交接 | [`development/task-handoff-template.md`](development/task-handoff-template.md) |

## 3. 现行架构与契约

- [`commercialization/domain-glossary.md`](commercialization/domain-glossary.md)：统一领域术语。
- [`commercialization/module-boundary-map.md`](commercialization/module-boundary-map.md)：模块所有权和允许依赖。
- [`commercialization/external-api-standard.md`](commercialization/external-api-standard.md)：外部 API、鉴权、错误和异步操作标准。
- [`commercialization/open-api-v1-guide.md`](commercialization/open-api-v1-guide.md)：开放 API 使用指南。
- [`commercialization/teamlab-api-foundation-contract.md`](commercialization/teamlab-api-foundation-contract.md)：TeamLab API 基础契约。
- [`commercialization/event-taxonomy.md`](commercialization/event-taxonomy.md)：运行事件和错误分类。
- [`commercialization/agent-capability-protocol.md`](commercialization/agent-capability-protocol.md)：主站与 Agent 能力协商。
- [`commercialization/database-index-and-lifecycle-audit.md`](commercialization/database-index-and-lifecycle-audit.md)：数据生命周期与索引基线。
- [`commercialization/cache-invalidation-map.md`](commercialization/cache-invalidation-map.md)：Redis 与缓存失效边界。

前端设计与交互规范仍作为产品实现依据，但不属于 README 的主叙事：

- [`yinyu-vnext-design-language-draft.md`](yinyu-vnext-design-language-draft.md)
- [`yinyu-vnext-page-interaction-api-spec.md`](yinyu-vnext-page-interaction-api-spec.md)
- [`yinyu-vnext-development-guardrails.md`](yinyu-vnext-development-guardrails.md)
- [`commercialization/frontend-component-boundary.md`](commercialization/frontend-component-boundary.md)
- [`commercialization/frontend-style-token-contract.md`](commercialization/frontend-style-token-contract.md)
- [`yinyu-vnext-phase34-alignment.md`](yinyu-vnext-phase34-alignment.md)

## 4. 运维与验收

- [`operations/vnext-maintenance-window-rollout.md`](operations/vnext-maintenance-window-rollout.md)：正式发布和回滚唯一主入口。
- [`commercialization/runbooks/runtime-scheduling-and-recovery.md`](commercialization/runbooks/runtime-scheduling-and-recovery.md)：运行队列、调度和恢复。
- [`commercialization/runbooks/observability-audit-recovery.md`](commercialization/runbooks/observability-audit-recovery.md)：日志、事件和恢复排障。
- [`commercialization/runbooks/redis-deployment-and-recovery.md`](commercialization/runbooks/redis-deployment-and-recovery.md)：Redis 部署与恢复。
- [`commercialization/runbooks/database-governance-operations.md`](commercialization/runbooks/database-governance-operations.md)：数据库治理运维。
- [`commercialization/runbooks/teamlab-foundation-acceptance.md`](commercialization/runbooks/teamlab-foundation-acceptance.md)：TeamLab 基础验收。
- [`node-deployment/README.md`](node-deployment/README.md)：WorkerNode 准备。
- [`registry-server/README.md`](registry-server/README.md)：内网 Registry。
- [`operations/windows-vm-quick-deployment-guide.md`](operations/windows-vm-quick-deployment-guide.md)：Windows VM 简明交付。
- [`operations/windows-vm-deployment-guide.md`](operations/windows-vm-deployment-guide.md)：Windows VM 原理与故障排查。
- [`yinyu-awdp-manual-acceptance.md`](yinyu-awdp-manual-acceptance.md)：AWDP 人工业务验收。

`deploy/production.md` 和 `deploy/agent-node.md` 是保留旧链接的退役跳转页，不是操作指南。

## 5. 实施记录

以下内容有审计价值，但不得单独用于判断当前状态：

- `commercialization/phase-00-*` 至 `phase-07-*`：商业化阶段实施方案和验收记录。
- `commercialization/benchmarks/`：阶段基准和待补现场数据。
- `yinyu-vnext-phase*.md`、`yinyu-vnext-phase5*.md`：已完成前端切片的计划和进度。
- `yinyu-vnext-*-acceptance*.md`、`yinyu-vnext-*-baseline*.md`：特定日期的验收或环境快照。
- `dynamic-flag-*.md`、`training-dynamic-flag-*.md`：具体缺陷的根因、修复和回归证据。
- `platform-commercialization-audit-progress.md`：Phase 0-7 的历史实施流水，不是当前状态页。

## 6. 历史归档

`archive/pre-commercial-reset-20260709/` 保存商业化重置前的计划、审查、旧产品模型和交接材料。归档内容可能包含已经删除的模块、旧路径、旧分支和失效部署方式。

除非任务是考证历史决策，否则不要检索或引用归档内容。归档文档不应重新移动到现行目录。

## 7. 维护规则

- 新增长期规则时更新现行规范；不要把规则只写在聊天或阶段计划里。
- 项目提交、部署或缺口发生变化时更新 `development/current-state.md`。
- 大任务结束后把临时计划标记为实施记录，不继续在其中维护“当前状态”。
- 被新文档替代的危险操作指南应改为退役跳转页；有审计价值的原文通过 Git 历史保留。
- 新模块进入稳定主线前，必须在 `modules/README.md` 中补齐负责人、边界、API、数据、测试和运维文档入口。

## 8. 本次审计结论

- README 已收敛为产品能力、运行架构、启动、构建、部署和文档入口，不再承担前端专项规范。
- `deploy/production.md` 和 `deploy/agent-node.md` 的旧操作内容存在误操作风险，现已退役为跳转页。
- `archive/` 中的重置前资料，以及带日期、Phase、Plan、Progress、Acceptance、Baseline 的文件，属于历史证据而非当前事实；本轮不批量删除，避免丢失审计依据。
- WorkerNode 和 Registry 文档已移除特定 PVE、NFS 和旧测试网段假设，改为基于当前 Agent、调度和镜像分发契约的通用指南。
- 仍缺少完整模块说明的领域及补充优先级见 [`modules/README.md`](modules/README.md)。其中 AWDP、Identity、Theory、Training、CTF/Content、Exercise 和 Penetration 已有实现但说明仍然分散。
- 当前没有发现必须立即物理删除的现行文档。后续可在不影响链接的专项提交中，把已冻结的实施记录批量移动到 `archive/`。
