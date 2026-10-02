# TeamLab 自动网络配置：主站与契约交接

## 任务目标与基线

用户授权本地开发并分配代理。此任务负责主站契约、保存、发布、运行计划、校验与迁移；Agent 来宾执行、OVN 与前端由其他任务负责。部署必须另行取得用户批准。本任务未连接服务器、推送分支或合并 `main`。

- 起点：`cc05eda9`，包含最新 `origin/main bc3599aa` 及已部署的 DHCP/DNS 修复。
- 分支：`codex/teamlab-managed-network-contracts`。
- 工作区：`C:/Users/Cloud/.codex/worktrees/teamlab-managed-contracts/newGZCTF`。
- 契约先行提交：`73a5103d`；执行计划与租约提交：`bbd8d6f5`。

## 技术结论

资产 `vmNetworkMode=null` 在草稿中继承镜像模板；`Dhcp=0`、`Preconfigured=1` 数值保持不变，新增 `ManagedStatic=2`。新发布版本与新运行资产定义固定解析后的模式，避免随后修改镜像模板使已发布行为漂移。旧版本与旧执行快照不改写。

每接口的 `guestInterfaceName`、`useDefaultGateway`、`dnsServers`、`staticRoutes` 完整贯通草稿存取、v1/v2 发布编码、执行定义、预览计划、运行重置与运行资产更新：

| 字段 | 语义与限制 |
| --- | --- |
| `guestInterfaceName` | 空值由执行器选择；明确值只支持 ManagedStatic，安全名称最长 15 字符；同资产不能重名。 |
| `useDefaultGateway` | 空值沿用该接口 Primary；false 明确无默认网关；同资产最多一个有效默认网关。 |
| `dnsServers` | 空值继承本接口所属网段 DNS；空数组明确无 DNS；最多三个不同 IPv4 地址，允许明确的 `127.0.0.1`。 |
| `staticRoutes` | 最多八条不同目的网段的 typed 路由；字段为 destinationCidr、nextHop、可选 metric。IPv4 规范 CIDR `/1..32`；默认路由使用网关开关；metric 为 1..9999 且只支持 ManagedStatic。 |

静态路由下一跳为明确 IPv4 地址，必须在本次实际分配给接口的运行子网内且不是自身、网络、广播或回环地址。地址池分配到不同子网时，作者需要检查明确下一跳仍有效；主站在编译请求时返回 `guest_route_next_hop_invalid`，Agent 仍执行同等检查。此任务不增加地址池或 VRF 设计。

上述新增接口字段仅支持 VM。Docker 保留既有网络行为，草稿创建/更新、发布校验与共享执行计划会拒绝 VM 专用字段；编译器不为 Docker 生成新的来宾策略或逐端口 DHCP 策略。Preconfigured 保留自行维护来宾网络的行为，明确的新来宾字段会被拒绝，避免界面声明与执行行为不一致。

共享附件包含稳定 interface key、本次 MAC 与 prefix。ManagedStatic 必须齐全，MAC 必须匹配同一个网络端口，prefix 必须匹配运行网段。DHCP 的逐接口网关、DNS、静态路由还写入租约，因此 DNS 或网关编辑会改变 NetworkDigest；静态和预配置 VM 不产生 DHCP 租约。旧 JSON 新字段默认省略，保留旧冻结计划摘要读取。

运行更新比较完整资产执行定义。新模式、名称、DNS、网关和路由变化会产生 `replace`，沿原部署票据清理并重建该来宾；这是有中断的资产替换，不能称为无损网络热更新。没有增加第二套队列，Controller 没有来宾命令编排。

## 迁移与恢复

`20261001145237_AddTeamLabManagedGuestNetwork` 仅添加五个 nullable 列：资产 VmNetworkMode，接口 GuestInterfaceName、UseDefaultGateway、DnsServersJson、StaticRoutesJson。原草稿行为空值，原 release 内容与摘要不变。实体、模型快照与迁移同批更新。

PostgreSQL 16 Testcontainers 已验证从 `20260927121625_AddTeamLabNetworkDnsAsset` 前向升级、旧行保留、实际草稿 Update 与明确清空往返、旧 release 内容和 hash 保留、Down 后再 Up、无待应用迁移及模型无 drift。Down 会删除新增配置列，生产应用回退应保留数据库新增列；如需数据库回退，必须先备份并接受新草稿配置丢失。未在生产数据库副本验证历史迁移来源差异。

## 验证证据与后续

- 主站 Release 构建通过，0 错误；存在原有 `Microsoft.Build.Tasks.Git` NU1902 警告。
- TeamLab 定向单元测试 515/515 通过，包含旧执行资产/租约 JSON 原样重编码、v1/v2 网络要求往返、草稿持久化、发布继承模式冻结、重置定义、运行替换判定、DHCP digest、MAC/prefix/下一跳校验。
- PostgreSQL 迁移专项 1/1 通过。
- `git diff --check` 通过。
- 日志与 TRX 在仓库外 `D:/Work/YINYU-Managed-Network-20261001/contracts`。

本任务未运行全量解决方案、全量后端单测/集成或前端门禁；父任务汇总代码后执行。未进行真实 Linux/Windows、QGA、cloud-init、OVN、重置与替换运行验收。Agent 新阶段由 Agent 任务维护 `TeamLabExecutionProtocolV2.cs`。父任务还需生成并审查 OpenAPI、合入 OVN 逐端口 DHCP 选项及 Agent/前端提交，然后按计划准备部署材料与请求用户批准。

## 能力门禁增量

ManagedStatic 要求 Agent 声明 `teamlab.guest-network.managed-static.v1`；Linux 还要求现有 `runtime.vm.cloud-init.v1` 宿主配置盘工具能力，Windows 不要求宿主 seed 工具。模式按明确意图、运行资产冻结定义、模板继承解析，OS 取实际模板。旧 DHCP、Preconfigured 没有新增要求。

能力要求接入预览计划、实际逻辑组放置与重校验、旧代次放置复用、已有分配/票据恢复、运行资产新增/替换预览与执行前验证、部署前和执行计划编译。缺少能力报告 `teamlab_guest_network_capability_unavailable` 或节点缺失特征；阻止旧 Agent 忽略新模式后仅凭 QEMU running 报成功。

宿主 feature 只表示执行实现与宿主工具，不证明镜像中的 QGA、驱动、cloud-init 已准备；Agent 必须继续执行来宾验证。这次增量没有迁移。TeamLab 与放置定向测试 522/522 通过，覆盖旧 Agent 拒绝、新 Agent 可放置、Linux 缺 seed 拒绝、Windows 无 seed 可放置、继承模式与模板 OS、已有节点资产变更门禁。日志为仓库外 `contracts/capability-unit.log` 与 `managed-capability.trx`，全量门禁仍由父任务执行。
