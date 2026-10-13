# YINYU 当前开发状态

整理日期：2026-10-11。本轮源码基线：`origin/main 9bc3db66`。生产事实引用10月11日已提交的发布记录，本轮没有重新连接服务器。
本页只保留当前摘要，更新时替换旧事实；完整测试、部署过程和授权边界放在任务记录中。最多 120 行。

## 代码与部署基线

| 项目 | 最新已知事实 | 依据与时效 |
| --- | --- | --- |
| 开发主线 | `main`；新任务从实时 fetch 后的 `origin/main` 创建分支 | SHA 以 Git 为准，本页不追写每次文档提交 |
| 培训实例修复 | PR #24 合入 `60170a52`，PR #25 为后续部署记录；课程默认每用户 3 实例，公共练习和课程额度隔离 | [主线修复与发布](handoffs/2026-10-11-training-runtime-mainline.md)、[课程生命周期](../modules/training-container-lifecycle.md) |
| 培训动态设置 / 练习分页 | PR #26 已合入 `b1812d15`，PR #27记录已部署；管理员可动态设置培训额度，练习/管理题库使用服务端分页 | Git 合入事实与[新任务记录](handoffs/2026-10-11-training-quota-settings.md)、[分页说明](../modules/exercise-catalog-pagination.md)；10-11发布交接已确认API与页面分页验收；本轮未重新实测 |
| 主站发布 | `.27` 的 `/opt/gzctf/publish` 指向 `/srv/yinyu-data/releases/training-settings-06930b095cb0511d31bf06d8971477fa71f0aeb8-20261011/publish`；Main DLL摘要前缀 `0d3ea1e2…` | [10-11 17:32发布记录](handoffs/2026-10-11-training-quota-settings.md)；本轮未重新核验文件 |
| 前端 / Agent | 前端随 `06930b09` 发布；三节点 Agent 最近记录为 `87e011cf…` | 10-10/10-11 发布交接；本轮未重验节点文件与进程 |
| 数据库 | 最近记录 150 条历史，head `20261001145237_AddTeamLabManagedGuestNetwork`；培训修复无迁移 | 10-11 发布交接；本轮未查询生产数据库 |
| 应用回退 | `/opt/gzctf/publish.previous` 指向 `/srv/yinyu-data/releases/training-runtime-quota-60170a5266f684581f21e95013687b0e7367515f-20261011/publish` | 10-11培训设置发布交接；真实应用回退演练未完成 |
| 备份 | `/srv/yinyu-data/backups/training-settings-06930b095cb0511d31bf06d8971477fa71f0aeb8-20261011-pre`；两份新 dump 完整隔离恢复通过 | 10-11 发布交接；本轮未重新读取或恢复备份 |
| 服务与页面 | Main / 本机 Agent active/running、NRestarts=0；首页、Config 200；指标 healthz 200/Degraded | 10-11 17:32发布交接；当前现场状态须按需重验 |
| 节点与调度 | 最近交接三节点 Online；`.27/.31` Docker/KVM，`.30` Docker；10-10 记录恢复三节点调度开关 | 有能力或调度开关不代表 `.27` 来宾网络故障已修复；本轮未重验节点库存 |

`stable-20260908` 是固定历史标签，不能代表现网。生产操作前重新核对现场、授权、候选身份、备份和回退；[本轮只读核验范围](handoffs/2026-10-11-docs-collaboration-audit.md)与既有发布证据分别记录。

## 已有能力与验收边界

| 领域 | 当前实现与限定验收 | 接手入口 |
| --- | --- | --- |
| CTF / 理论 / 培训 / 练习 | 已有业务入口；练习核心生产测试已有用户确认；10-11 课程三容器、独立端口与停止重建验收通过 | [模块索引](../modules/README.md)、[课程生命周期](../modules/training-container-lifecycle.md) |
| TeamLab | 场景、发布、runtime、逐资产操作、受管网络、文件/会话、流量/抓包；10-09 两套四机生命周期限定验收，10-10 无引用场景和已销毁记录删除验收 | [制作导入](../teamlab-authoring-guide.md)、[运行事实契约](../modules/teamlab-runtime-capability-contract.md)、[记录删除发布](handoffs/2026-10-10-teamlab-delete-rollout.md) |
| 镜像与运行底座 | 统一队列、按需节点准备、下载容量守卫、最后消费者缓存释放；历史来源迁移、模板退役和受控 Registry GC 已执行 | [第二批发布](handoffs/2026-10-09-image-lifecycle-second-rollout.md)；自动 GC、冷缓存 TTL 不由该次手工回收证明 |
| 身份 / AWDP | 本地登录、Portal SSO 接口与 AWDP 操作已存在；跨系统 SSO 和真实攻防仍需目标环境验收 | [Open API](../commercialization/open-api-v1-guide.md)、[AWDP 人工验收](../yinyu-awdp-manual-acceptance.md) |

正式前端路由以 `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx` 与 `app/shell/moduleRegistry.ts` 为准。运行配置地址、`Ready`、模板连接设置均不能代替真实网络和桌面登录验证。

## 未解决事项

技术缺口统一记录在 [后续事项](backlog.md)，本页只列重点：

- `.27` 来宾网络回读根因、运行失败诊断粒度、CPU 单位与执行 vCPU 语义、磁盘硬配额。
- 迁移来源漂移、指标并发、既有健康降级；历史问题须重新定位，不能套用旧迁移数量或旧依赖版本。
- 镜像持久导入入口收口、自动 Blob GC、冷缓存策略和运行增长预算。
- 多 Worker 故障恢复、完整跨节点/物理设备、长期监督与规模验收；普通 Windows 冷缓存及桌面链路、AWDP/SSO、运营与真实回退验收。

已不再作为待办：9 月 `teamlab-flow` SQL 42601 修复及三节点旧混合版本。9 月后的实际修复与验收不代表所有故障场景闭环。

## 协作与任务入口

1. [AGENTS.md](../../AGENTS.md)：规则、Git 隔离、验证和发布边界。
2. [文档导航](../README.md)：按任务选择契约、模块或操作手册。
3. [任务记录索引](handoffs/README.md)：本任务接手和最新发布证据。
4. [文档审计结论与清理清单](documentation-audit.md)：本次治理依据和后续审查范围。

源码、测试、生产验收分开记录；无关小任务不修改本页。当前状态由集成或发布负责人在合入时更新，禁止向页首不断追加历史流水。
