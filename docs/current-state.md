# YINYU 当前开发状态

整理日期为 2026-11-11。北京时间 17:27，PR #44 的组网前端已部署至主站 `.27`，真实场景的拓扑、资产和键盘定位通过。主站后端仍是 PR #42，`.40` 的 Netlab 执行面未更新。详见[组网布局发布](records/2026-11-11-teamlab-page-layout.md#生产发布)。
本页只保留当前摘要，更新时替换旧事实；完整测试、部署过程和授权边界放在任务记录中。目标120行以内，超长时提炼摘要。

## 代码与部署基线

| 项目 | 最新已知事实 | 依据与时效 |
| --- | --- | --- |
| Netlab 接入 | 原 release-v2 四机场景在独立 YINYU 项目，底座仍为 v1.2.2；11-11 回读为两台运行、两台停止，发布未操作虚拟机 | [接入上线](records/2026-11-10-netlab-rollout.md)、[最新场景回读](records/2026-11-11-teamlab-page-layout.md#生产发布) |
| 开发主线 | `main`；新任务从实时 fetch 后的 `origin/main` 创建分支 | SHA 以 Git 为准，本页不追写每次文档提交 |
| 培训实例修复 | PR #24 合入 `60170a52`，PR #25 为后续部署记录；课程默认每用户 3 实例，公共练习和课程额度隔离 | [主线修复与发布](records/2026-10-11-training-runtime-mainline.md)、[课程生命周期](modules/training/reference/container-lifecycle.md) |
| 培训动态设置 / 练习分页 | PR #26 已合入 `b1812d15`，PR #27记录已部署；管理员可动态设置培训额度，练习/管理题库使用服务端分页 | Git 合入事实与[新任务记录](records/2026-10-11-training-quota-settings.md)、[分页说明](modules/exercise/reference/catalog-pagination.md)；10-11发布交接已确认API与页面分页验收；本轮未重新实测 |
| 主站发布 | 前端 `f800a987`（PR #44，272 个文件），后端沿用 `8d9efc7c`（PR #42），Agent 未更新或重启 | [发布身份](records/2026-11-11-teamlab-page-layout.md#生产发布) |
| 数据库 | 153 条迁移，头为 `20261110070457_IntegrateNetlabSceneBindings`；本次无迁移 | [现场核验](records/2026-11-11-teamlab-page-layout.md#生产发布) |
| 应用回退 | 上一版 `netlab-8d9efc7c…/publish` 保留，回退脚本已准备，未实际回退 | [备份与回退](records/2026-11-11-teamlab-page-layout.md#备份与回退) |
| 备份 | 11-11 双库、附件、加密配置和旧发布包通过校验；双库实际恢复演练见 11-10 记录 | [最新备份](records/2026-11-11-teamlab-page-layout.md#备份与回退)、[恢复演练](records/2026-11-10-netlab-rollout.md) |
| 服务与页面 | 首页、配置接口、静态资源和附件通过；健康检查保持既有 Degraded；真实场景拓扑和资产列表通过，流量解析仍见 NETLAB-02 | [验收边界](records/2026-11-11-teamlab-page-layout.md#生产发布) |
| 节点与调度 | 3 节点在线，活跃队列为 0；534 模板 Ready，分发 240 Ready / 1 既有 Failed | [现场核验](records/2026-11-11-teamlab-page-layout.md#生产发布)；Docker 创建、HTTP 入口和销毁见[11-09 验收](records/2026-11-09-history-rollout.md) |

`stable-20260908` 保留用于历史复现，现网身份以最新发布记录和现场回读为准。生产操作前重新核对现场、授权、候选身份、备份和回退；[10-11文档核验记录](records/2026-10-11-docs-collaboration-audit.md)与既有发布证据分别记录。

## 已有能力与验收边界

| 领域 | 当前实现与限定验收 | 接手入口 |
| --- | --- | --- |
| 历史负载 | 概览与节点详情读取后台持续保存的负载记录，支持 1 小时 / 24 小时 / 7 天；关闭页面不影响采集 | [Runtime](modules/runtime/development.md)、[生产验证](records/2026-11-09-history-rollout.md) |
| CTF / 理论 / 培训 / 练习 | 已有业务入口；11-09 课程4题88 Docker 创建、入口 HTTP 200、销毁与空态通过；Flag 提交最近实测仍为 10-18 | [模块索引](modules/README.md)、[课程生命周期](modules/training/reference/container-lifecycle.md)、[课程业务验收](records/2026-11-09-history-rollout.md) |
| TeamLab | 正式接入独立 Netlab，原 release-v2 四机场景及版本保留；真实启停和来宾代理响应通过；流量解析异常，VM 动态 Flag 文件注入依赖未部署补丁 | [接入说明](modules/teamlab/netlab-integration.md)、[上线记录](records/2026-11-10-netlab-rollout.md) |
| 镜像与运行底座 | 统一队列、按需节点准备、下载容量守卫、最后消费者缓存释放；历史来源迁移、模板退役和受控 Registry GC 已执行 | [第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)；自动 GC、冷缓存 TTL 不由该次手工回收证明 |
| 身份 / AWDP | 管理员删除队长会退队/交接并匿名保留历史，凭据和会话撤销；10-18临时队长真实删除、旧会话401、新登录401及SQL清理断言通过；未删除既有用户 | [Identity](modules/identity/development.md)；Portal SSO和AWDP仍需专门真实验收，见[Open API](operations/open-api-integration.md)、[AWDP手册](modules/awdp/guides/manual-acceptance.md) |
| 管理日志 | 列表显示操作者、动作、对象与结果；详情无二级折叠，正常事件不展示空错误；例行ACK/HTTP明细降噪，历史缺失身份/结果显示未知；真实请求记录User/admin/IP，后台标System | [日志改造](records/2026-10-14-logs-readability.md)、[现场验收](records/2026-10-16-logs-browser-acceptance.md)、[Audit](modules/audit/README.md)；正常/失败详情通过，Docker完整链路因题目镜像缺Ready模板未通过，无活动容器残留 |

正式前端路由以 `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx` 与 `app/shell/moduleRegistry.ts` 为准。运行配置地址、`Ready`、模板连接设置均不能代替真实网络和桌面登录验证。

## 未解决事项

技术缺口统一记录在 [后续事项](backlog.md)，本页只列重点：

- Netlab 流量报文解析错误、VM 初始化文件补丁及比赛真实业务验收，见 NETLAB-01 / NETLAB-02。
- `.27` 来宾网络回读根因、运行失败诊断粒度、CPU 单位与执行 vCPU 语义、磁盘硬配额。
- 迁移来源漂移、指标并发、既有健康降级；历史问题须重新定位，不能套用旧迁移数量或旧依赖版本。
- 镜像持久导入入口收口、自动 Blob GC、冷缓存策略和运行增长预算。
- 多 Worker 故障恢复、完整跨节点/物理设备、长期监督与规模验收；普通 Windows 冷缓存及桌面链路、AWDP/SSO、运营与真实回退验收。

已不再作为待办：9 月 `teamlab-flow` SQL 42601 修复及三节点旧混合版本。9 月后的实际修复与验收不代表所有故障场景闭环。

## 协作与任务入口

已合入：[模块文档与自动维护](records/2026-10-14-documentation-implementation.md)，[PR #28](https://github.com/Y3X1L2/newGZCTF/pull/28) 于2026-10-14北京时间08:36合并为 `fe35746e`。11个功能入口、专题与自动检查已进入主线，主线Documentation/Quality和手动巡检均通过；定期巡检配置已启用，每周一北京时间10:23运行。

1. [AGENTS.md](../AGENTS.md)：规则、Git 隔离、验证和发布边界。
2. [文档导航](README.md)：按任务选择契约、模块或操作手册。
3. [任务记录索引](records/README.md)：本任务接手和最新发布证据。
4. [文档审计结论与清理清单](records/2026-10-11-documentation-design.md)：本次治理依据和后续审查范围。

源码、测试、生产验收分开记录；无关小任务不修改本页。当前状态由集成或发布负责人在合入或授权发布时更新，未合并候选必须明确标注，禁止向页首不断追加历史流水。
