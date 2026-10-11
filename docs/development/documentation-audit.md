# 文档与协作审计结论

日期：2026-10-11。审计基线：`origin/main 03440c51`。范围是现行协作、开发进度与导航；不是全产品功能验收。归档仅列文件统计，没有通读正文。

## 判断与证据

现有模块化单体、独立 Agent、统一队列、任务分支/worktree、代码测试同提交、部署显式授权等原则合理。协作困难主要来自重复的事实源、任务记录缺少退场机制和没有明确的集成更新责任，继续增加“总交接”或“最新方案”会放大问题。

基线有 **291 份 docs Markdown：131 份非归档、160 份归档**。非归档中 63 份 handoff、6 份测试资料本来就是证据，并不需要新人全部阅读。`current-state.md` 为 365 行、约 84 KB；`current-handoff.md` 239 行，AI playbook 187 行。问题是入口和职责，不是所有专业文档都多余。

| 暴露的问题 | 本次核对到的事实 | 调整 |
| --- | --- | --- |
| 多个“当前”入口 | current-handoff 仍以 8/31 release 为主体；状态页同时保留 9 月与 10 月基线 | 删除重复入口；状态页按最新证据替换 |
| 同页事实互相冲突 | 状态页保留 134 与 150 条迁移、混合 Agent 与统一版本、已修复 SQL 仍被列为优先任务 | 发布、旧证据和未决事项分开；不从历史数量推断当前 |
| 必读清单不断加长 | AGENTS 要求概览，总交接再要求 playbook 等八项；架构/前端/部署文档缺少按影响范围选择的表述 | 三个入口后按任务选读 |
| 计划没有完成后退场 | 10/04 集成计划还写未合并，最新主线和发布记录已包含该实现；镜像计划还写待执行 | 删除被实现与最终证据覆盖的计划 |
| 小任务也制造文件 | playbook 要求所有任务创建记录，模板却允许短任务用提交说明 | 复杂任务一份记录兼容计划和交接；短任务用 PR |
| 并行隔离但共享状态无负责人 | 各 worktree 仍不断追加同一个状态页；旧记录中的授权、目录和节点旗标容易被继承 | 指定集成负责人、契约所有权和合入顺序；共享摘要合入时更新 |
| 文档声明的自动检查未落实 | 原导航要求链接/路由/类名检查；Quality 无 Markdown 链接门禁 | 增加轻量文件链接与状态长度检查，明确不检查 API/符号/锚点 |
| 专业说明和进度混放 | 2673 行使用/API 手册与运行事实契约放在 development；模块矩阵漏实际 Provisioning 模块 | 两份有效说明归 operations/modules；登记覆盖缺口 |
| 概览重复维护进度 | README、总纲、总交接还称练习待发布；旧 SSH.NET 风险沿用旧版本判断 | 概览改为稳定边界和状态引用；当前依赖 2026.0.0 的风险另核 |

源码核对包括：`ExerciseInstanceRepository` 的课程独立额度与自动驱逐开关、`TrainingContainerPolicy` 默认 3；TeamLab `/record` 删除和释放预留检查；vNext 路由注册、实际 Modules 目录、迁移文件与元数据、中央依赖版本、前端 build 命令、Quality 工作流。链接检查通过不能代替这些核对。

用户追加授权后，只读访问 `.27` 首页和 SSH：2026-10-11 北京时间 16:32–16:33，发布链接和 Main DLL 摘要与培训发布交接一致，回退链接保持；Main/Agent active，首页/Config 200，指标 200/Degraded。Main PID 已变为 1077899。本轮未查询数据库、读取健康详情、创建实例或连接其他 Worker；既有交接和本次现场证据分别标明。

## 清理清单

以下九份从现行树删除，Git 历史保留原文；没有删除业务源码、迁移、生成 API、运维脚本或发布证据。

| 删除的 docs/development 文件 | 理由与承接位置 |
| --- | --- |
| `current-handoff.md` | 与 AGENTS/current-state/导航重复，含已过时部署基线；当前事实与历史稳定基线记录承接 |
| `ai-development-playbook.md` | 重复 Git、读取和交接规则，含机器专属 SSH 假设；必要流程并入 AGENTS，提示词保留在本页下方 |
| `image-lifecycle-plan.md` | 按需准备、容量守卫、回收已落主线；边界提入 backlog，最终证据在 10/09 两批发布记录 |
| `image-lifecycle-rollout-plan.md` | 旧候选/checkpoint 已被第二批最终发布与清理记录替代 |
| `teamlab-admin-redesign.md` | 页面方案、日志索引和链路修复已集成；10/04 最终发布记录及现行制作/操作说明承接，未测范围保留 |
| `teamlab-admin-redesign-integration-plan.md` | 已完成的一次性分支合并顺序；10/04 集成与发布记录保留 |
| `teamlab-managed-network-plan.md` | 实现与限定验收已完成；operations 中制作和发布手册及 10/02 交接承接 |
| `teamlab-core-workflow-plan.md` | 已完成工程计划；运行事实契约译为简体中文并保留到 modules，10/06 集成/发布证据保留 |
| `teamlab-network-runtime-convergence.md` | 9/27 待部署进度不再当前；主线源码、后续受管网络说明及原 Git 版本可追溯 |

以下五份含独有旧审计、决定、验收或性能边界，移至 `archive/implementation-records/documentation-audit-20261011/` 并标为历史，不能因日期旧就删除其中未决要求：

- `teamlab-high-performance-execution-progress.md`：旧 V2 进度；可复用 k6 检查移入现行执行面 runbook。
- `teamlab-ankelu-gap-audit-20260907.md`：竞品能力差距与当时审计，后续变更已使状态过期。
- `teamlab-commercial-closure-plan.md`：能力地图混有大量本地测试流水；仍需重核的会话恢复、监督与实机能力由 backlog 接续。
- `teamlab-api-product-separation-plan.md`：P0–P3 部分实现与独立产品设想混放；现行接口以契约为准。独立产品拆分仍是待论证目标，不自动变成获批架构或已完成能力。
- `teamlab-competition-simulation-test-plan.md`：两小时测试已经执行；原计划保留作历史，结果与未覆盖项在 9/18 测试汇总。

两份有效说明改位置：`development/teamlab-networking-operations-manual.md` → `operations/`；`development/teamlab-core-capability-contract.md` → `modules/teamlab-runtime-capability-contract.md`。现行 Markdown 链接同步改正，历史归档内部旧路径保留。

清理后非归档 Markdown 为 **120 份**（含本次审计、任务记录与 backlog），归档 165 份。减少的是强制上下文和重复计划；既有 160 份 archive 本轮没有批量删除。回看原文使用 `git show 03440c51:docs/development/<原文件名>`，不恢复旧入口继续维护。

## 简明协作方案

| 环节 | 责任与产出 |
| --- | --- |
| 开始 | 任务负责人读 AGENTS、当前状态、导航和相关契约；fetch、核权限/起点，独立分支与 worktree |
| 拆分 | 需要并行时先定集成负责人、模块/文件所有权、冻结契约和合入顺序；同一共享契约不由多个任务猜字段 |
| 开发 | 实现、必要测试、稳定说明同分支；复杂任务一份 handoff，阶段覆盖更新；短任务 PR 说明即可 |
| 审查 | 核实际差异与最新主线，区分代码、软件门禁、实机结果；作者先自审，合入由指定审查者确认，不机械增加 AI 会话 |
| 集成 | 有授权后正常合入；集成负责人替换状态摘要、核 backlog；未合入分支的能力不写成主线能力 |
| 发布 | 明确授权、可识别制品、新鲜备份与恢复、独立 release/原子切换、真实冒烟；记录实际各组件身份 |
| 收尾 | 封存主记录；唯一历史证据保留，冗余计划删除；合并、推送及交接确认后清理 worktree/分支 |

共享文件的所有权是当前任务安排，不是永久锁：发现冲突先同步负责人和最新主线，审查解决后重跑受影响门禁。任务之间用 SHA/PR/记录传递证据，聊天用于讨论，不能代替交接。新任务不继承旧记录的一次性服务器或清理授权。

可复用的新任务提示词：

```text
这是 newGZCTF 的独立任务：<目标与验收>。
先读 AGENTS.md、docs/development/current-state.md 和 docs/README.md，
再只读相关契约；继续任务时读指定 handoff。不要通读 archive。
fetch 最新 origin/main，创建独立 codex/<task> 分支与 worktree。
核源码、接口与必要现场证据，闭环实现/测试/现行说明。
复杂任务只维护一份 handoff；小任务用 PR 说明。
完成后提交推送，报告测试、合入、部署、遗留问题；部署须明确授权。
```

## 本轮不宜继续批量删除的范围

- 旧长 TeamLab 操作/API 手册、前端页面规格和多个专项契约可能重复，但仍有实现和操作消费者；按模块对照 OpenAPI 和调用路径后再合并。已登记 DOC-01。
- 原商业能力地图的远程会话重启/清理、H02 长期监督、物理端点和多 Worker 未决要求，需逐条核最新实现；本轮归档不等于关闭。已登记 DOC-02。
- 已完成 handoff、测试报告与旧 archive 只按发布/回退/故障追溯需要定向处理。先明确证据被谁承接，不能按日期或“已完成”全删。

本轮本地验证与交付身份见 [任务记录](handoffs/2026-10-11-docs-collaboration-audit.md)。后续执行遵循 AGENTS；本审计不是第二份长期协作规则。
