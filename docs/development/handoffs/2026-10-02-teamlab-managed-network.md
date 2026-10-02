# TeamLab 自动网络配置交接

## 目标与基线

让平台声明的网络要求自动对应本次网卡，保留DHCP、支持VM逐网卡静态IPv4/DNS/网关/路由；镜像只准备一次驱动和管理组件，不为每道题重复写网卡修复任务。用户授权开发、子代理分工、本地验证和发布准备；部署前必须再次请求批准。

任务worktree：`C:/Users/Cloud/.codex/worktrees/teamlab-windows-network-review/newGZCTF`，分支 `codex/teamlab-managed-network`。从 `origin/main bc3599aa` 起步并正常合入已部署的 `codex/teamlab-windows-network`。2026-10-02完成整合，源码验证截止 `8959fad8`；最终文档提交和制品身份从分支HEAD及仓库外release manifest核对。不合并main，保留所有工作区和子代理分支。

## 已实现

- `Dhcp=0`、`Preconfigured=1`保留存量数值；新增`ManagedStatic=2`。新建界面以自动取址与平台静态为主，旧预配置只保留读写，不增加复杂修补系统。
- VM模式和逐接口要求贯穿草稿、发布冻结、预览/执行计划、重置、资产更新、校验及nullable数据库迁移。新字段仅用于VM，Docker维持既有行为。
- Linux NoCloud按MAC匹配；DHCP真正请求自动取址，静态使用Netplan。QGA在业务网络不可用时应用和回读；声明的接口才纳入管理。
- Windows使用QGA和兼容旧PowerShell的WMI/netsh；有界标准输入传脚本，避免八网卡超过命令长度。区分网卡权重和路由权重，确认旧平台路由已删除后才更新记录，失败可重试。
- 逐端口DHCP支持明确DNS/无DNS、网关/无网关和路由；静态VM没有DHCP租约。网络更新同步路由器/端口版本标记，真实OVN验证NAT查询字符串及资源归属。
- DNS服务机地址变化会在预览和实际执行中替换继承其DNS的静态成员机；网络字段变更可能导致资产替换中断，不称为无损热更新。
- ManagedStatic要求节点能力，Linux还需宿主配置盘工具；宿主能力不证明某个镜像已安装QGA/cloud-init。来宾管理、应用、验证分阶段记录真实结果。

## 验证

证据均在仓库外 `D:/Work/YINYU-Managed-Network-20261001`。

| 门禁 | 结果/证据 |
| --- | --- |
| Release解决方案构建 | 通过；`tests/solution-final.log`，依赖与测试分析器警告保留，不宣称零警告 |
| 完整后端单元测试 | 1241/1241；`tests/unit-verified.trx` |
| 完整集成测试 | 302/302；`tests/integration-verified.trx`，无跳过 |
| PostgreSQL迁移 | 完整集成中验证前向升级、旧行/旧release保留、清空往返、Down/Up与无模型漂移；不操作生产库 |
| 完整前端门禁 | 110文件/373测试；locale、lint、types、architecture、build、manifest与budget通过，`tests/frontend-full.log` |
| OpenAPI | 外部API兼容比较与快照测试通过；生成代码由实际本地OpenAPI生成，类型检查通过 |
| Linux脚本沙箱 | 5/5，配置备份/幂等/失败恢复；`tests/linux-final-sandbox.log` |
| Windows脚本 | 本地WMI/netsh模拟、返回码0/1；真实PowerShell标准输入执行八卡×八路由×三DNS，QGA传输/隐私回归；实机未测 |
| 原生隔离网络 | OVN事务/入口查询、模拟VM的HTTP、隔离、PCAP、断开恢复、controller重启、独立runtime保留与精确清理通过；不代表主站完整实机链路 |
| 界面 | 390/1366/1920/2560、明暗/键盘/减少动画来自独立fixture；review补充为交互测试，整页服务器QA未测 |

扩展门禁暴露并修正既有缺口：远程会话创建/结束缺少异步分发、清理失败事件缺少错误分类；测试的旧403断言、无效release、缓冲审计竞态和共享后台队列污染。未放宽授权或镜像引用检查。真实Hybrid测试还受Windows CRLF启动文件干扰，修正后无需扩大内存/CPU/超时即通过。中间失败日志保留，最终完整门禁单独标识。

## 发布准备和现场

2026-10-02只读核对 `.27`：仍运行 `teamlab-net-279f259-20260927`，主站/Agent active，Config200，本机Agent旧摘要 `993f00ce4874b0e41131ca8cb596e9e3bb76997984008fca795fa9a45963f475`，根盘约92GiB可用；有KVM、virsh、QEMU、xorriso和.NET10.0.8。`.30/.31`直接免密SSH未连通，本轮未重新证实其空间/版本；批准后通过现有平台同步/管理流程复核。

新迁移：`20261001145237_AddTeamLabManagedGuestNetwork`，五个可空列；幂等前向SQL已生成，未在服务器执行。独立SHA命名主站/前端/内置自包含Agent、manifest、传输包和摘要由仓库外 `prepare-release.ps1` 构建；不打包运行配置。发布物身份写入 `release/<SHA>/manifest.json`，不提交二进制、原始日志、凭据或教学问答到Git。

部署步骤、数据库/附件备份、原子切换、Worker同步、独立实机验收和回退见 [本次部署方案](../../operations/teamlab-managed-network-rollout.md)。镜像准备和原理见 [简明说明](../../operations/teamlab-managed-network-authoring.md)。旧release和Agent保留，应用回退不删除新增列；暂停新模式的恢复/待执行任务，不让旧版执行新ManagedStatic计划。

## 尚未完成

部署未批准、未执行；原教学环境、PVE VM118–121和raw模板未更改。真实Linux/Windows QGA初始化、多卡/DNS/路由回读、RDP/域登录、重置、两份实例隔离与销毁仍须服务器验收。Server2008R2及QGA标准输入兼容只做本地语法/模拟，缺组件必须明确失败；需要时准备独立副本。

本轮不实现固定IP原样多副本VRF/地址池设计，不自动修复AD信任或业务配置。DHCP进阶逐卡策略须全部相关Agent升级，暂无单独旧Agent能力标记；不得在混用版本的节点上发布这种新场景。下一位接手者先核对manifest/分支/服务器现场，再确认用户部署批准，不能从旧聊天或旧测试数量推断已部署。
