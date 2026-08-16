# 模块文档覆盖矩阵

本表对照 `src/GZCTF/Modules` 的实际模块，判断是否存在可供新开发者直接接手的现行说明。“被总纲提到”不等于拥有完整模块文档。

## 覆盖情况

| 模块 | 现有主要文档 | 覆盖评价 | 仍缺少 |
| --- | --- | --- | --- |
| Audit | `event-taxonomy.md`、观测 runbook、Phase 7 | 较完整 | 管理端审计权限和保留策略的简明模块页 |
| Awdp | AWDP 人工验收、总纲、前端实施记录 | 部分 | **当前架构、状态机、Checker/Flag/补丁 API、计分和运维指南** |
| Content | 模块边界、开放 API、镜像/附件相关阶段文档 | 部分 | 题目资产、附件、镜像绑定、引用检查和删除生命周期总览 |
| Ctf | 开放题目 API、总纲、页面/API 规格 | 部分 | 普通比赛生命周期、Flag/Submission/Scoreboard 数据流和出题操作规范 |
| Exercise | 总纲、开放 API、练习前端与验收记录 | 部分 | **产品范围、实体/API、来源导入、提交/进度、附件/Flag/实例生命周期的现行模块总览** |
| Identity | Phase 1、API token、认证实施记录 | 部分 | **本地认证、Portal SSO、角色权限、账号生命周期和故障排查的现行说明** |
| Penetration | TeamLab 契约、Phase 3、页面/API 规格 | 部分 | **玩法边界、objective/submission/score/reset 与 TeamLab binding 的独立模块说明** |
| Runtime | Phase 5-7、调度/恢复 runbook、Agent protocol | 较完整 | 对业务模块开放的运行 command/query 速查表 |
| TeamLab | Phase 3、API contract、acceptance runbook、vNext 管理端 | 较完整 | 产品工作流总览、双 Worker 故障、长期流量和规模验收仍需补齐 |
| Theory | 总纲、数据库治理、页面/API 规格、前端实施记录 | 部分 | **理论题 JSON 格式、题库/试卷/答卷快照、判分和重试策略的现行模块说明** |
| Training | 总纲、页面/API 规格、动态 Flag 缺陷记录 | 部分 | **课程权限、教师/学员流程、章节完成、资源/题目/理论绑定和实例生命周期总览** |

Agent 不在 `Modules` 目录，但执行面文档覆盖较好：

- `commercialization/agent-capability-protocol.md`
- `node-deployment/README.md`
- `commercialization/runbooks/runtime-scheduling-and-recovery.md`
- Windows VM 系列文档

## 优先补充顺序

1. **AWDP 模块说明**：功能已上线且状态机复杂，当前只有测试步骤，维护成本最高。
2. **Identity 与 Portal SSO**：认证链路跨系统，必须把 token、账号映射、改名行为和故障边界固定下来。
3. **Theory 与 Training**：业务已投入使用，但规则散落在前端计划、数据库阶段和缺陷记录中。
4. **CTF 与 Content**：补齐 Flag、附件、镜像、引用和删除的统一资产视图。
5. **Penetration**：从历史大方案中提炼当前仍存在的最小玩法契约。
6. **Exercise**：从现有实现提炼独立模块说明，并补齐生产部署、内容运营和规模验收记录。

## 模块文档最低结构

每个新模块说明至少包含：

1. 目标与非目标。
2. 所有实体和事实源。
3. 角色与权限。
4. Command、Query、HTTP API 和异步 operation。
5. 关键状态机与幂等规则。
6. 跨模块依赖与禁止依赖。
7. 缓存、日志、审计和数据保留。
8. 单元、集成、浏览器和真实环境验收。
9. 部署、回滚与常见故障。

模块文档只描述当前实现或已经批准的目标。历史方案和未来设想必须明确标记，不能混写成现有能力。
