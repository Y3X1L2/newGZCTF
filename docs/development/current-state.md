# YINYU 当前开发状态

文档整理日期：2026-09-10
最近一次生产核验：2026-09-08 09:49 UTC（北京时间 17:49）

本文件仅保留最新已知基线、功能边界、未解决事项和接手入口。生产信息是上述核验时点的记录，不能代替下一次操作前的现场检查；本次文档整理没有重新连接服务器。长期协作规则见 [AGENTS.md](../../AGENTS.md)。

## 测试环境部署事实（2026-09-09）

- 用户授权对测试服务器 `10.0.7.118` / `10.0.7.125` 部署与清理；生产 10.24 环境未改动。
- `10.0.7.118` 已原子切换至 `teamlab-full-af20f9bb-20260909`：gitCommit `af20f9bb9eebdf6e3223f405202f22dfd6bd814d`，manifest 1010 文件，archive SHA-256 `220d960b8df909fb9db80c93add2286f6f923e25c02e9c559140cc477a65a7bd`。efbundle 前向应用 9 条迁移（`AddAssetAndChallengeOwnership`、`AddExerciseCreatorTracking` 及 7 条 TeamLab 迁移）。`publish.previous` 保留旧 release `teamlab-capture-restore-fix-20260824-01`。冒烟：主站 active、首页 200、`/api/Config` 200。
- 部署前数据库备份：`/opt/gzctf/backups/gzctf-pre-af20f9bb-20260909T1129Z.dump`（670,764,978 bytes，2,101 个 catalog 条目，SHA-256 `95c697111dc8e897cc507c5bd6d88b835d88e9bf201c30a411477ee290db16e4`）。`.118` 的 `/opt/gzctf/shared/files` 已建为指向 `/opt/gzctf/persistent/files` 的软链接（本机真实附件存储位置），以满足发布激活脚本前置检查。
- `.118` 磁盘从 99%（1.5G 可用）恢复至约 87%（16G 可用）：删除 4 个非当前/非回退旧 release、`/var/crash` 转储、home 下 15 个 6-8 月旧 tarball（886M）及 nuget/npm 缓存；对 internal registry 执行 `registry garbage-collect --delete-untagged` 清理无 tag 引用的孤儿 blobs（blobs 曾达 40G）。6 个 `yinyu-pentest-*` 仍带 tag 的镜像保留。
- `.118` 与 `.125` 的 `gzctf-agent` 此前均因主机缺少 `gzmgt0 / 100.127.0.1/16` 管理网桥以 `SocketException (99)` 崩溃循环（`.118` 重启计数 4065）。已按 `docs/operations/agent-guest-network-recovery.md` 在两台安装 `restore-agent-guest-network.py` 与 `gzctf-agent.service.d/20-guest-network.conf` drop-in，恢复网桥与 `gzctf_guest_mgmt` nft 隔离规则并持久化；重启后两台 agent 均 active/running、NRestarts=0，心跳到 `10.0.7.118:8080` 全部 200。整机重启持久化未经真实重启验证。
- 上述 `.118` 数据库迁移仅在生产 10.24 副本策略之外、于测试服务器直接前向应用，不构成生产迁移验证；Agent 同步与节点调度实测见下节。

## TeamLab 测试环境运行修复（2026-09-10）

- `.118` 测试主站已切换到 `/opt/gzctf/releases/teamlab-gateway-healthfix-20260910/publish`，`publish.previous` 指向 `teamlab-full-af20f9bb-20260909`。主站与本机 Agent 均 active，`/api/Config` 返回 200；本次没有数据库迁移。
- `.118` 与 `.125` Agent 已统一为 SHA-256 `738ff2630cf292bf7ce0b88eca4359960171bcbfc8bf43dccefd22b8ee35e72e`。容器健康检查改由 Agent 自身进入容器网络命名空间执行，并在 10 秒启动窗口内重试，不再依赖工作负载镜像包含 bash、curl、nc 或 Python。
- 公网 UDP 网关同步会在规则写入前确保共享 nftables 表及 `prerouting`、`postrouting` NAT 基础链存在；销毁仅按运行映射注释删除规则，不删除共享链。隔离 nftables 测试覆盖空状态建链、双映射隔离、单映射删除和最终清理。
- 正式 API 连续完成两次 v5 工控演示场景创建与销毁。运行 `01a086f1-6490-743b-974a-eb6b0848241f` 和 `01a086f3-b1e0-7466-9a6d-f2f333b98bba` 均到达 `ready`，两个资产均为 Running；销毁后运行及资产均为 Destroyed、错误为空。第二次运行中核对 DNAT/SNAT 两条规则存在，销毁后映射规则为 0；节点上对应 runtime 232/233 的容器、network namespace、OVS 资源和状态文件无残留。
- 网关定向测试 6/6、主站及 Agent Release 构建通过。全量单测 1119/1121；两条既有调度观察点计数断言失败，单独复跑仍失败，未经过本次网关或 Agent 探测代码。该门禁缺口不得记录为全绿。
- H03 测试服务器全链路已通过：运行 `01a0870a-6bc4-7c7e-8bc4-fb5300ea2986` 经正式队列在 `.125` 创建 Docker PLC、隔离 SCADA、libvirt VM 和 H01 受管网卡连接器。现场 namespace 端点读取 Modbus TCP 寄存器 `12/34/56/78`；PLC、VM 与现场端点双向互通，`field` 与 `isolated` 保持隔离。平台抓包得到 23,782 字节、两段 PCAP，下载归档内摘要与平台一致。
- H03 验收补齐 libvirt VM NoCloud CIDATA 静态网络配置和 QEMU Guest Agent channel；VM 回报计划 MAC 和 `10.96.1.20/24`，ICMP、SSH banner 与反向 QGA ping 均通过。删除连接器 OVS port 后协议链路超时，正式 reset 在 generation 4 自动恢复；`ovn-controller` 重启后链路保持可用；第二运行被 `connector_occupied` 明确拒绝。
- H03 最终通过正式 API 销毁：运行、分片和资产均为 Destroyed，容器、libvirt 域、qcow2/seed 文件及运行 OVS 接口无残留；专建的模拟 namespace/veth 已删除，Agent 与 OVN Controller 保持 active。拓扑、发布、设备包和已销毁运行历史保留供平台查看。抓包在销毁前已下载验签，销毁后抓包接口为 404，不表述为长期留存附件。详见 `docs/development/handoffs/2026-09-08-h03-hybrid-datapath.md`。

## 本地在研补充（尚未合并/发布）

首批远端分支已推送：`codex/teamlab-foundation-v1-20260908` 指向 `98d9d839fa5f72a863c4775cc0e680b354c3d9d7`，已通过远端引用回读核对。仅包含第一笔提交及其历史，未包含工作区未提交的前端和验收改动；未合并 main，未部署生产，未发送 QQ 消息。

2026-09-08 首批本地提交：`98d9d83`（`feat(teamlab): add runtime operations and device execution foundation`），包含运行底座、资产操作、终端/文件/VM 控制台、会话审计、设备执行与监督契约、迁移和后端测试。用户随后要求停止验收并优先提交；本次仅完成这一笔本地提交，没有推送或发送 QQ 消息。前端集成、模拟设备/混合环境夹具和后续验收文档仍留在工作区。H03 已通过正式队列部署及实际互通、隔离；完整抓包、故障恢复和最终销毁验收不得标记为全部通过。

H01/H02 后续在研（2026-09-08）：专用网卡 provider 已加入不可变网络计划及 Agent 接入/探测/解绑，主站按登记节点约束调度；登记表单新增节点选择、网卡名称与 MAC，未支持类型不可选。真实 OVN/OVS/QEMU＋该 provider 的互通、隔离、断开恢复、另一 runtime 抢占拒绝、解绑保留网卡专项通过；主站驱动的全流程尚未验收。新增可构建 Modbus TCP 设备源代码及原始 TCP 协议测试，参数、寄存器/线圈状态、异常、实例隔离和重启语义已有真实验证。前端完整门禁 324/324、相关后端 475/475、混合网络/Modbus 联合专项 2/2 通过。H02 持续监督、协议事件自动采集、H03 主站全链路仍未完成；未推送稳定分支。用户已明确排除 A06，并要求 H01–H03 与协议模拟全部闭环后才作为稳定版本推送。

H03 最新专项（2026-09-08）：真实 OVN/OVS＋QEMU Linux 来宾＋namespace 外部端点模拟的数据面集成测试 1/1 通过（31 秒），覆盖 HTTP、同地址段逻辑网络隔离、PCAP、端口断开/重接、ovn-controller 重启、精确清理及另一 runtime 保留。代码新增重复 apply 的网络/端口复查，并修复 VM-only inventory 误依赖 Docker。该专项直接调用网络 provider；未通过主站队列和 H01 连接器创建，不能签收完整 H03。详细证据与剩余项见 [H03 混合数据面验收](handoffs/2026-09-08-h03-hybrid-datapath.md)。

本轮收尾事实（2026-09-08）：主站现运行仓库外 `teamlab-main-recovery-20260908` 制品，前端为 5173 的 Vite 服务；本地 Guacamole 测试配置已在受控主站进程中恢复。最终单元测试 1114/1114、前端完整门禁 323/323 通过；恢复后完整集成测试 286/286 通过，之后失败场景文件运维增量另经单测与真实 API/Edge 读取、删除验证，未再重复整套集成。设备包停用/启用详情不更新、归档提前关闭及失败信息被弹窗遮挡已修复；操作结果直接使用接口回读。测试设备包已归档，两份容器内临时验收文件已通过页面删除（本地原件保留）；临时浏览器账号已退出并禁用，保留审计身份，原账号未重置。两个 Agent 活动远程会话、Guacamole 临时连接和临时用户均为 0。

最新本地验收（2026-09-08）：Edge 已完成真实登录、资产暂停/恢复、差异识别与原队列修复、中文文件上传/取消覆盖/确认覆盖、标准附件下载事件、终端中文输入输出/尺寸同步/显式中断/重新创建/关闭回收，以及会话审计文件展示与下载事件。设备包登记、镜像自动匹配、参数表单与 JSON 一致性已在浏览器验证；验收草稿已撤销回 0 个资产并保存，修订号正常增长至 7，未修改既有发布版本。登记表单错位、诊断审计白名单导致的 500、本地 `/api` WebSocket 代理缺失、匿名课程误报加载失败已修复。文件附件 GET 复用原文件服务，真实字节比对通过。

重启后已补证：恢复 Guacamole 原测试配置后，Edge 经主站创建 VNC 会话并显示真实 BIOS 画面；结束后主站显示已结束、两个 Agent 活动会话均为 0、Guacamole 临时连接和临时用户均为 0。该测试域仍是无来宾网络、无系统盘的专用控制台域，不能代替 H03。TCG 节点 inventory 500 已修复：能力清单确认 Docker 守护进程可达，聚合资源查询隔离单个计算提供方故障并保留取消语义；实测主 Agent Docker=True/KVM=False，libvirt Agent Docker=False/KVM=True，均返回 200。Alpine 模拟容器使用仓库外的 linux-musl-x64 Agent 制品。

当前尚未完成：H03 完整混合场景及物理端点实测，H01 provider 和能力地图中已注明的软件缺口。另发现资产恢复后运行/分片仍保留 resource_not_running 失败状态，需要补有实际网络证据的显式重对账恢复，不能直接修改数据库状态伪造成功。文件运维已允许失败场景中的有效资产进入排障流程，仍验证权限、代次、资源身份与操作冲突。

最近门禁：后端单元测试 1113/1113、前端完整门禁 321/321；最后失败场景文件运维增量另做真实审计写入器参与的定向单测。登记排版已核验 390/1366/1920/2560。曾出现集成测试 97 通过/189 失败；诊断发现 PostgreSQL 容器内部正常、Windows 经 Docker 映射端口超时、Docker 引擎初始化接口失去响应。电脑重启并恢复服务后，同一批完整集成测试 286/286 通过（3 分 33 秒）；报告在仓库外 teamlab-recovery-tests-20260908/recovery-full.trx。数据卷未删除，开发库 TCP SQL 探针、两种 Agent inventory 与 VNC 浏览器链路均已重新验证。

2026-09-08 后续恢复：Edge 浏览器控制已经恢复。原包含环境变量注入和隐藏后台启动的复合命令被工具拒绝，未提供具体触发条件；使用现有配置在受控终端前台启动主站成功。主站制品位于仓库外 `teamlab-main-network-device-20260908`，`http://127.0.0.1:8080/api/Config` 实测 200，Docker Agent inventory 实测 200；前端 `http://127.0.0.1:5173/` 已在 Edge 打开，赛事和通知能够返回真实数据，课程区仍显示加载失败，不能宣称首页全部通过。当前进程依赖本地终端会话，不是生产部署。未注入此前临时 Guacamole 管理账号配置，因此远程图形会话仍需单独复核。

本轮 A07/O01/H02 增量已落代码，边界见商业清单及组网操作手册第 9.0 节：资产差异检查与原队列修复、NAT 归属和端口级执行修复、设备包参数/制品/资源校验及编辑器绑定改善。H01 provider、完整端口入口分配/冲突/探测、持续设备监督与 H03 物理验收仍未完成。开发库已在仓库外备份后应用 `20260908024141_TeamLabLinkPolicyGeneration`；旧策略的代次保持未知，不猜测回填。新代码尚未提交、推送或生产发布。

本地在研基线已快进至 `origin/main 3a70126`。R05/A01/A04/A05 的前后端实现已接入：A01 复用部署票据，包含原节点身份、阶段继续执行、电源期望状态、生命周期权限和会话/文件协调；A04 包含容器文件与 VM SFTP、覆盖确认、SSH 身份登记/更新及超时暂存回收。真实主站登录、文件往返、单资产队列操作、终端、审计归档下载及 SHA-256 校验通过。A05 已通过真实 libvirt/QEMU TCG → Agent → Guacamole 图形数据接收及关闭清理，域没有来宾网络；不是硬件 KVM 验收。OpenSSH 协议测试通过，完整 VM 管理网络、OVN 网络重接和硬件 KVM 仍须现场验收。浏览器工具初始化连续报运行文件路径缺失（os error 3），有数据的多尺寸/主题/键盘视觉验收尚未完成。

本地已在备份后应用 `20260907154522_TeamLabAssetPowerIntent`、`20260907162107_TeamLabSftpHostIdentity`；隔离 PostgreSQL 前向升级、旧数据保留和回退专项通过。备份在仓库外，未删除数据卷。最新代码门禁与制品路径见 [商业能力补齐清单](teamlab-commercial-closure-plan.md) 的最终验证记录。原临时浏览器管理员保留用于复核；新增 API 验收账号已禁用并保留审计身份，未重置原管理员。所有改动尚未提交、推送或生产部署。

最终门禁：后端全量单测 1086/1086，前端 312/312 与完整构建通过，全量集成 284/284。最后文件错误映射增量另做 16 项定向测试和真实 API/Agent 验证；未重跑该增量后的全量集成。主站与 Agent 已更新，实际远程会话和 Guacamole 临时连接均已清零；上述环境/浏览器现场验收仍未签收。

2026-09-07 在 `D:\newgz\newGZCTF-main`，基于 `origin/main bbd5a5d` 的 `codex/teamlab-commercial-closure` 分支进行商业能力补齐，改动尚未提交。已实现的本地变更、验证结果、集成测试失败及未完成能力见 [商业能力补齐清单](teamlab-commercial-closure-plan.md)。CodeGraph 已初始化；本地 Docker 模拟 Agent 已更新，真实终端的中文输入输出、尺寸调整、Ctrl-C、取消及进程回收已验证。该结果不代表前后端全部链路、多 Worker、KVM、OVN 或物理端点通过；未对生产做修改。下列生产记录仍为此前核验事实，不能视作本次远程复核结果。

## 1. 基线

| 项目 | 最新已知事实 |
| --- | --- |
| 仓库 / 开发分支 | `https://github.com/Y3X1L2/newGZCTF.git` / `main`；开发从最新 `origin/main` 创建任务分支 |
| 固定发布标签 | `stable-20260908` → `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a`；已推送 annotated tag，不移动标签 |
| 主站发布 | `10.24.0.27` 的主站及本机 Agent 使用 `/opt/gzctf/releases/pr9-converged-ab2bd54b7e16d454e3a8f54960c4bb4689047c4a-20260908/publish`；994 个 manifest 文件长度/摘要匹配 |
| 持久化附件 | `/opt/gzctf/publish/files` → `/opt/gzctf/shared/files` |
| 数据库 | 134 条迁移历史，head `20260816192540_TeamLabCapabilityClosure`；当前发布无新增迁移 |
| 应用回退 | `/opt/gzctf/publish.previous` → `/opt/gzctf/releases/practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237/publish` |
| 备份 | `/opt/gzctf/backups/pr9-rollout-pre-ab2bd54b-20260908T080802Z`；预备与最终数据库备份均实际恢复验证，最终数据库/附件备份摘要复核通过 |
| 执行节点 | 最近核验三节点均 Online/Stable/schedulable，Fabric Disabled；Agent 摘要前缀：`.27` `d93cf212...`、`.30` `3747f353...`、`.31` `2f12bca5...`，远端版本未完全统一 |
| 健康状态 | 指标端口 `3001/healthz` 为 HTTP 200 / Degraded；业务端口 `8080/healthz` 按契约返回 404；降级原因见未解决事项 |
| 技术栈 | .NET 10、ASP.NET Core、EF Core、PostgreSQL、Redis、React 19、TypeScript、Vite、pnpm |

版本关系以实时 `git fetch origin --prune`、`git status` 和 `git log` 为准。需要复现该次生产源码时使用固定标签；`main` 后续是否仅有文档变化，应重新比较，不能长期假定。发布包摘要、回退边界和验收依据集中在 [稳定基线说明](handoffs/2026-09-08-stable-baseline.md)。

## 2. 当前功能边界

### 已存在的代码能力

- **CTF 赛事**：赛事、战队、题目、附件、静态/动态 Flag、Docker、KVM/Windows VM、提交、计分和榜单。
- **理论考试**：题库、组卷、单选/多选/判断、草稿、最终提交、成绩和答案回顾。
- **培训课程**：课程、共同教师、报名审核、章节、资源、实验、课后理论、学习进度和学员详情。
- **自主练习**：`/practice`、题库浏览、筛选、来源导入、附件、多 Flag、Docker 实例、提交、统计和后台题库管理；实例继续复用 `DeploymentQueueTicket`。
- **AWDP**：服务、轮次、Checker、攻击、修补、重置、恢复、停止、计分和日志；真实攻击/修补流程按人工验收文档执行。
- **运行底座**：Docker/KVM 节点、镜像模板、镜像导入与分发、容量预留、统一部署队列、实例、事件、日志和恢复。
- **TeamLab**：场景草稿、校验、不可变发布版本、试运行、混合资产、执行计划 V2、OVN/OVS 数据面、访问授权、远程运维、链路策略、连接器、设备包、资源池、流量和抓包基础能力。
- **身份与通用管理**：本地登录、Portal SSO、用户、战队、学员组、系统设置、个人主页和主要管理页面。

### 前端事实

正式前端入口位于 `src/GZCTF/ClientApp/src/vnext`。路由注册以以下文件为准：

- `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- `src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`

新增页面必须使用 vNext 壳层、feature API adapter、CSS Module 和语义 Token；未实现能力显示真实空态，不加载旧页面套壳，不伪造数据。

## 3. 运行架构

主站为模块化单体，Agent 为独立执行面；PostgreSQL 保存业务和运行事实，Redis 承载缓存、协调和高频缓冲。Docker、VM、培训、AWDP 和 TeamLab 共用 `DeploymentQueueTicket`。

依赖方向、模块所有权和前端边界遵循 [AGENTS.md](../../AGENTS.md) 与 [模块边界图](../commercialization/module-boundary-map.md)，不在状态文档重复维护另一套规则。

## 4. 未解决事项

- **数据保留 SQL**：`teamlab-flow` 分区清理存在 PostgreSQL `42601` 语法错误，最近核验仍按小时发生；需要独立修复及定向回归，不能用删除生产数据绕过。
- **指标并发**：2026-09-08 观察到一次指标持久化 `DbUpdateConcurrencyException`，后续心跳/指标恢复；仍需并发和失败批次重试回归，不能将自动恢复等同于根因已修复。
- **迁移来源**：`20260604165857_AddTheoryExamEntities`、`20260604193010_SyncTheoryExam` 的来源仍未恢复，导致 134 条数据库历史与 132 条可发现迁移存在差异。另有 `20260802023000_RemoveDestroyedTeamLabUdpMappings.cs` 缺少迁移元数据，不能按源码文件数认定 bundle 会执行。禁止伪造或删除历史；后续迁移须在新鲜生产备份副本验证。详见 [迁移交接](handoffs/2026-09-02-migration-drift-reconciliation.md) 与 [发布核验](handoffs/2026-09-08-pr9-production-rollout.md)。
- **依赖与存储回收**：SSH.NET 已知依赖风险和 Blob 自动 GC 缺口仍未处理；风险范围见 [PR 审计](handoffs/2026-09-07-pr9-review-merge.md)。
- **节点一致性与持久性**：远端 Worker Agent 仍为兼容的混合版本；如需统一，应另开维护任务逐节点同步。已安装的 `.31` 网桥恢复机制尚未完成整机重启验收。
- **自主练习与回退**：核心业务测试已完成并由用户确认；内容运营验收和真实应用回退演练仍未完成。
- **TeamLab**：双 Worker 故障接管、长期流量、复杂服务注入、规模并发和完整跨节点场景仍需现场签收；Fabric Disabled 不代表组网验收通过。
- **Windows VM**：当前仅按比赛场景支持，仍需合格镜像双实例、RDP/Guacamole、剪贴板、隔离与销毁清理验收。
- **AWDP / Portal SSO**：AWDP 真实攻击、修补和异常恢复按 [人工验收手册](../yinyu-awdp-manual-acceptance.md) 执行；门户源码不在本仓库，SSO 跨网联调仍需目标环境验证。

## 5. 验证依据

- 固定发布提交已通过完整 CI、隔离真实 Docker 生命周期及附件引用保护验证；测试数量和运行编号见 [候选交接](handoffs/2026-09-07-pr9-runtime-convergence.md)，不作为未来提交已通过验证的依据。
- 生产发布完成两份数据库备份恢复、制品/进程核验、关键 API 和共享附件核验；用户接手完成业务测试并删除临时题，随后只读确认对应题目、实例和容器记录不存在。详见 [生产发布交接](handoffs/2026-09-08-pr9-production-rollout.md)。
- 已修复缺陷、旧版本切换、测试过程、旧计数和会话流水移出当前状态；需追溯时查看 [环境核验历史](../archive/implementation-records/2026-09-08-environment-verification-history.md)。归档不参与当前执行决策。

## 6. 文档入口

| 需要了解什么 | 入口 |
| --- | --- |
| 固定生产基线、发布物与版本关系 | [稳定基线说明](handoffs/2026-09-08-stable-baseline.md) |
| 本次部署、备份、回退及人工验收 | [生产发布交接](handoffs/2026-09-08-pr9-production-rollout.md) |
| 选择功能、接口和专项文档 | [文档导航](../README.md) |
| 开发与模块边界 | [AGENTS.md](../../AGENTS.md)、[模块边界图](../commercialization/module-boundary-map.md) |
| 发布流程 | [生产发布与回滚手册](../operations/vnext-maintenance-window-rollout.md) |

## 7. 新任务起点

1. 核对用户当前目标、权限范围、工作树及最新远端提交；不要继承历史交接中的一次性授权或限制。
2. 优先处理数据保留 SQL 缺陷和指标并发回归，再按任务范围推进专项验收；从最新 `origin/main` 创建独立任务分支。
3. 以当前源码、合同、测试及必要的现场证据确认问题；发布使用新候选、新标签和新鲜备份，不原地覆盖固定基线。
4. 完成后更新本文件的最新基线或未解决事项；修复过程、运行编号、完整测试与部署记录写入对应 handoff，避免再次向本文件追加时间线。
