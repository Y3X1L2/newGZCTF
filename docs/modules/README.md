# 模块文档覆盖矩阵

本表对照 `src/GZCTF/Modules` 的实际模块，说明新开发者可以直接使用的现行文档，以及仍需要补充的模块说明。归档目录中的阶段记录不计入当前覆盖。

## 当前覆盖

| 模块 | 当前入口文档 | 覆盖情况 | 仍需补充 |
| --- | --- | --- | --- |
| Audit | `commercialization/event-taxonomy.md`、`commercialization/runbooks/observability-audit-recovery.md` | 较完整 | 管理端审计权限和保留策略的独立模块页 |
| Awdp | `yinyu-awdp-manual-acceptance.md`、`yinyu-vnext-page-interaction-api-spec.md` | 功能验收可用 | 状态机、Checker/Flag/补丁 API、计分和运维总览 |
| Content | `commercialization/module-boundary-map.md`、`commercialization/open-api-v1-guide.md`、镜像/附件运维文档 | 边界和接口较完整 | 题目资产、附件、镜像绑定、引用检查和删除生命周期总览 |
| Ctf | `commercialization/open-api-v1-guide.md`、vNext 页面/API 规格 | 赛事入口和接口可查 | Flag、Submission、Scoreboard 数据流和出题操作规范 |
| Exercise | vNext 页面/API 规格、总纲、Open API | 已实现且入口清晰 | 生产验收、内容运营、来源导入和实例生命周期总览 |
| Identity | 总纲、Open API、认证页面/API 规格 | 入口和边界可查 | Portal SSO、角色权限、账号生命周期和故障排查手册 |
| Penetration | `teamlab-api-foundation-contract.md`、`teamlab-networking-feature-guide.md`、页面/API 规格 | 边界和组网契约可查 | objective/submission/score/reset 的独立玩法说明 |
| Runtime | Agent 能力协议、调度/恢复手册、节点部署文档 | 较完整 | 面向业务模块的 command/query 速查表 |
| TeamLab | TeamLab 契约、功能说明、基础验收手册、页面/API 规格 | 较完整 | 双 Worker 故障、长期流量和规模验收手册 |
| Theory | 总纲、页面/API 规格、Open API | 业务入口可查 | JSON 出题格式、答卷快照、判分和重试策略总览 |
| Training | 总纲、页面/API 规格、Windows/实例运维文档 | 业务入口可查 | 课程权限、教师/学员、资源绑定和实例生命周期总览 |

Agent 不在 `Modules` 目录，但执行面文档覆盖较好：

- `commercialization/agent-capability-protocol.md`
- `node-deployment/README.md`
- `commercialization/runbooks/runtime-scheduling-and-recovery.md`
- `operations/windows-vm-quick-deployment-guide.md`

## 补文档顺序

1. AWDP：状态机复杂且真实流程需要人工验收。
2. Identity：认证链路跨系统，需固定 token、账号映射、改名和故障边界。
3. Theory 与 Training：业务已投入使用，规则需要从页面和契约集中整理。
4. Ctf 与 Content：补齐 Flag、附件、镜像、引用和删除的统一资产视图。
5. Penetration：提炼当前最小玩法契约，避免依赖历史大方案。
6. Exercise：补生产部署、内容运营和规模验收记录。

## 模块文档最低结构

每个新模块说明至少包含：

1. 目标与非目标；
2. 实体、事实源和状态机；
3. 角色与权限；
4. Command、Query、HTTP API 和异步 operation；
5. 幂等、缓存、日志、审计和数据保留；
6. 跨模块依赖与禁止依赖；
7. 单元、集成、浏览器和真实环境验收；
8. 部署、回滚与常见故障。

模块文档只描述当前实现或明确标记的未来能力。历史方案、阶段计划和一次性修复记录统一放入 `docs/archive/implementation-records/`。
