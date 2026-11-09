# YINYU 当前开发状态

整理日期：2026-10-18。平台显示与交互候选已按用户授权部署到 `.27`，包括仪表盘、按钮反馈、队长账号删除和 TeamLab 布局；[PR #35](https://github.com/Y3X1L2/newGZCTF/pull/35) 仍为 Draft，明确不合入 main，等待人工验收。完整证据见[本轮记录](records/2026-10-18-platform-experience.md)。
本页只保留当前摘要，更新时替换旧事实；完整测试、部署过程和授权边界放在任务记录中。目标120行以内，超长时提炼摘要。

## 代码与部署基线

| 项目 | 最新已知事实 | 依据与时效 |
| --- | --- | --- |
| 开发主线 | `main`；新任务从实时 fetch 后的 `origin/main` 创建分支 | SHA 以 Git 为准，本页不追写每次文档提交 |
| 培训实例修复 | PR #24 合入 `60170a52`，PR #25 为后续部署记录；课程默认每用户 3 实例，公共练习和课程额度隔离 | [主线修复与发布](records/2026-10-11-training-runtime-mainline.md)、[课程生命周期](modules/training/reference/container-lifecycle.md) |
| 培训动态设置 / 练习分页 | PR #26 已合入 `b1812d15`，PR #27记录已部署；管理员可动态设置培训额度，练习/管理题库使用服务端分页 | Git 合入事实与[新任务记录](records/2026-10-11-training-quota-settings.md)、[分页说明](modules/exercise/reference/catalog-pagination.md)；10-11发布交接已确认API与页面分页验收；本轮未重新实测 |
| 主站发布 | `.27` 的 `/opt/gzctf/publish` 指向 `/srv/yinyu-data/releases/platform-experience-1c9941ba46be2d3235e8240a86b075cf2572eda8-20261018/publish`；Main 来源 `1c9941ba`、DLL摘要 `fe24f2e2…` | 本轮任务分支制品已推送但未合 main；后续文档提交不改变来源 |
| 前端 / Agent | 前端同源 `1c9941ba`，226文件校验；本机 Agent摘要仍 `87e011cf…`、PID未变，原5个执行包文件逐字节保留 | 10-18现场；本轮未升级Agent或子节点，无重启现有VM |
| 数据库 | 152条历史，head `20261017184129_AddUserDeletionTombstone`；新增可空 `AspNetUsers.DeletedAtUtc`，无默认值 | 新备份完整隔离恢复、同字节SQL升级和26表计数一致；生产schema与临时删除账号墓碑断言通过；历史来源漂移仍保留 |
| 应用回退 | previous为原 `logs-ui-966ca6081000035c6ac46b4289f44d13dfebf75d-main-927ceef8-20261016/publish`；旧release保留 | 10-18真实链接；未执行真实应用回退；回退保留新增可空列，旧版本缺少删除过滤能力，须暂停删除操作 |
| 备份 | `/srv/yinyu-data/backups/platform-experience-20261018-pre-20261017T184600Z`；双库和附件摘要通过 | 4分钟备份、13分47秒完整隔离恢复；同SQL迁移与计数核验通过，备份/证明保留 |
| 服务与页面 | Main / 本机 Agent active/running、NRestarts=0；首页、Config200；指标healthz200/Degraded，和切前相同 | 10-18短维护14.041秒；生产新版概览、拓扑已浏览器复验；本地日夜/4宽度/键盘/减少动效通过 |
| 节点与调度 | 3节点Online、1个Docker/2个Docker+KVM；`.31`原有4VM保持；504模板Ready、分发235 Ready/1 Failed | 10-18基础冒烟与课程Docker完整创建/入口200/Flag Accepted/停止清理通过；未重试既有失败分发，不代表全部网络与镜像已验收 |

`stable-20260908` 保留用于历史复现，现网身份以最新发布记录和现场回读为准。生产操作前重新核对现场、授权、候选身份、备份和回退；[10-11文档核验记录](records/2026-10-11-docs-collaboration-audit.md)与既有发布证据分别记录。

## 已有能力与验收边界

| 领域 | 当前实现与限定验收 | 接手入口 |
| --- | --- | --- |
| CTF / 理论 / 培训 / 练习 | 已有业务入口；10-18课程4题88真实Docker创建、入口HTTP200、重复Accepted、停止与空态通过；共享按钮等待Promise和实例后续操作状态 | [模块索引](modules/README.md)、[课程生命周期](modules/training/reference/container-lifecycle.md)、[本轮记录](records/2026-10-18-platform-experience.md) |
| TeamLab | 场景、发布、runtime、逐资产操作、受管网络、文件/会话、流量/抓包；10-18拓扑正交避障、双网卡分线、卡片类型标签和版本启动层级已发布，原四机场景浏览器复验 | [制作导入](modules/teamlab/guides/authoring.md)、[运行事实契约](modules/teamlab/reference/runtime-capabilities.md)；既有四机生命周期与删除验收仍见各自历史记录 |
| 镜像与运行底座 | 统一队列、按需节点准备、下载容量守卫、最后消费者缓存释放；历史来源迁移、模板退役和受控 Registry GC 已执行 | [第二批发布](records/2026-10-09-image-lifecycle-second-rollout.md)；自动 GC、冷缓存 TTL 不由该次手工回收证明 |
| 身份 / AWDP | 管理员删除队长会退队/交接并匿名保留历史，凭据和会话撤销；10-18临时队长真实删除、旧会话401、新登录401及SQL清理断言通过；未删除既有用户 | [Identity](modules/identity/development.md)；Portal SSO和AWDP仍需专门真实验收，见[Open API](operations/open-api-integration.md)、[AWDP手册](modules/awdp/guides/manual-acceptance.md) |
| 管理日志 | 列表显示操作者、动作、对象与结果；详情无二级折叠，正常事件不展示空错误；例行ACK/HTTP明细降噪，历史缺失身份/结果显示未知；真实请求记录User/admin/IP，后台标System | [日志改造](records/2026-10-14-logs-readability.md)、[现场验收](records/2026-10-16-logs-browser-acceptance.md)、[Audit](modules/audit/README.md)；正常/失败详情通过，Docker完整链路因题目镜像缺Ready模板未通过，无活动容器残留 |

正式前端路由以 `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx` 与 `app/shell/moduleRegistry.ts` 为准。运行配置地址、`Ready`、模板连接设置均不能代替真实网络和桌面登录验证。

## 未解决事项

技术缺口统一记录在 [后续事项](backlog.md)，本页只列重点：

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
