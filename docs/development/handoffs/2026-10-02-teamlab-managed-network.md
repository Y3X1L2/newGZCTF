# TeamLab 自动网络配置：部署与验收交接

2026-10-02；状态：complete / VERIFIED（限定范围）。开发、子代理分工、部署及专用实例验收均已完成。用户明确批准部署10.24.0.27，并允许关闭现有测试实例。本记录覆盖实际部署；下述未测范围不得视为通过。

## 目标、基线和分工

平台声明每张网卡的网络要求，创建实例后用本次MAC找到网卡、应用并回读结果；镜像准备兼容驱动和管理组件，不为每个题目另写网卡修复任务。AD、账户及题目业务保持独立，不由网络执行器自动修复。

任务worktree：`C:/Users/Cloud/.codex/worktrees/teamlab-windows-network-review/newGZCTF`；分支`codex/teamlab-managed-network`，从`origin/main bc3599aa`创建并正常合入实际已部署的Windows网络分支。开发和审阅子代理使用独立worktree。原主工作区、学习文档分支、子代理分支全部保留；未合并main，学习文档分支未推送。

## 实现与已部署身份

- `Dhcp=0`、`Preconfigured=1`保留存量解释，新增`ManagedStatic=2`。新建优先DHCP/平台静态；旧预配置仅保留存量读取执行，没有复杂旧镜像修补系统。
- VM逐接口名称、DNS继承/无DNS/列表、默认网关、静态路由贯通草稿、发布冻结、计划、重置及更新。Docker保持原路径。
- Linux使用cloud-init/Netplan和QGA；Windows通过QGA执行WMI/netsh，不使用Linux NoCloud。按MAC对应本次网卡，应用后回读真实IP/DNS/路由。
- ManagedStatic节点能力门禁为`teamlab.guest-network.managed-static.v1`。进阶逐卡DHCP没有独立旧Agent能力标记，必须确认网络owner和Worker已升级。
- Windows回读排除未声明路由的权重；QGA未指定环境时继承系统环境；主站等待预算覆盖来宾阶段，错误代码与阶段保留到界面。

活动release：`/opt/gzctf/releases/teamlab-managed-fa97187c-20261002/publish`。

| 组件 | 源提交 | 实际SHA256 |
| --- | --- | --- |
| 主站GZCTF.dll | fa97187c50ca2d44e5e56a72dee20d898ca562b8 | 048f6329178fd4276fabd2e4e5f83a4fa08eaa6bb222d1626e31d89c68ebe5ef |
| 本机及内置Agent | bb09393c414d9ac7a6e0068382b68de336bf34ea | c7b66c3408e3328bcc56f2577a9e363abc06f129c597d6c7acd4009ebb52ede8 |
| 前端 | 4fccca67ab801b45d315553243237068ed755afd | frontend-manifest.json对应225文件 |

本次只升级`.27`；最后核对`.27/.30/.31`心跳在线并可调度，`.30/.31`仍为旧Agent（摘要993f00ce…）。测试期间临时限制它们调度，结束后已正常恢复。**新网络策略的场景当前明确选择`.27`作为网络owner和Worker**，不得据此宣称混合旧Agent支持进阶逐卡DHCP。

## 备份、迁移和发布

备份目录：`/opt/gzctf/backups/teamlab-managed-4fccca67-20261002`。保留完整主库dump（1,594,406,363字节）、Guacamole库dump、共享附件归档（183,525,664字节）、受保护配置/原Agent/服务配置及摘要。完整压缩数据库内容可读取；生产结构与迁移历史恢复到独立`gzctf_managed_verify_4fccca67`后，前向迁移及五列检查通过。该验证不等同于恢复全部业务行后的业务回归；验证库保留。

应用迁移`20261001145237_AddTeamLabManagedGuestNetwork`，五个可空列；旧数据/计划不重解释。旧release和备份全部保留；采用独立release加原子软链接切换。没有修改公网网关或PVE源虚拟机。

首次切换因部署脚本遗漏主站apphost执行权限而自动回退，补齐755权限后同包成功。完整包直传约551秒，后续仅更新主站/Agent的GitHub prerelease资产由服务器下载约5/8秒，逐次验签。二进制是Release附件，没有提交到Git源码。

实机Windows测试发现QGA传`env:[]`会清空系统环境，导致脚本不能找到ProgramData；bb09393c修正为不传env时继承环境。Server2008R2缺QGA测试又暴露主站过早超时并丢失错误代码，fa97187c修正等待预算与错误传递。两项都已部署、补回归。

## 自动化门禁

证据根目录（仓库外）：`D:/Work/YINYU-Managed-Network-20261001`。

| 检查 | 结果及证据 |
| --- | --- |
| Release构建 | 通过；已有依赖/分析器警告保留，不宣称零警告 |
| 最终后端完整单元 | 1251/1251；tests/unit-final-deployment.trx |
| 最终后端完整集成 | 302/302；tests/integration-deployed-fixes.trx |
| 完整前端 | 110文件373测试，locale/lint/types/architecture/build/manifest/budget通过；tests/frontend-full.log；后续两次修复未改前端 |
| PostgreSQL | Testcontainers前向/旧行/旧release/Down-Up/模型检查通过；部署前生产结构副本验证另完成 |
| 原生网络/脚本 | OVN/OVS数据面、沙箱及最大8卡输入模拟通过；8卡模拟不能等同8卡实机 |
| 界面 | 390/1366/1920/2560、主题/键盘/减少动画为独立fixture；未做服务器完整页面视觉验收 |

## 真实平台验收

新建、发布、重置、销毁通过正常平台API和统一队列。网络观察通过限定QGA/宿主探测，没有为成功手动补IP，没有查看第二套题目业务。

| 场景 | 实际结果 | 边界 |
| --- | --- | --- |
| Windows模板507双网卡 | A/B分别取到计划MAC/IP/名称/DNS、关闭DHCP、无默认网关；A重置第3代仍正确；A到B访问失败 | 原507关闭RDP，仅A第3代测试写入层临时启用，3389双接口TCP通过；源模板未改，未做图形登录 |
| Windows明确路由 | 持久/当前路由198.51.100.7/32经192.168.210.1，额外metric7正确 | WMI合计32包含网卡25；不证明该下一跳实际对外转发 |
| DHCP/AD模板504/505 | 新建DC .10/成员 .20，实际DHCP/IP/DNS/网关正确；ad.yinyu.test解析、成员安全通道、LDAP/SMB/RDP端口通过；student01系统交互认证及已知教学共享读取成功；重置第2代网络参数仍正确 | 系统认证非图形桌面登录；未改AD/账户/加域关系或读取Lab2业务 |
| Ubuntu24新中性模板518 | 2VM×2NIC；两份实例、新建、A重置第7代、销毁B后A继续通过真实ping/HTTP200及验收标记；无默认网关、无转发，实际参数正确 | DNS只检查配置值；不同CIDR隔离，不是同CIDR/VRF证明；模板517保留为失败制作参考 |
| Server2008R2原模板509负例 | 返回guest_qga_unavailable，阶段guest-ready，明确要求QGA/管理通道 | 原模板缺组件，没有列为旧系统正例或修改原镜像 |
| Docker模板495 | 新建和重置第2代为不同容器；实际8080服务HTTP200，随后正常销毁 | 初次探测80端口选错，修正到模板声明8080；未记录响应内容/Flag |

Linux v1虽生成正确网络文件，实际IPv4为空。镜像离线清理删除了machine-id，网络服务初始化系统身份失败。v2仅修正镜像准备：保留空的正常machine-id文件和D-Bus链接，让首次启动生成新身份；未关闭IPv6、未加网络补丁开机任务。两台身份不同仅记布尔结果，未保存身份值。v2文件1,030,750,208字节、虚拟8GiB，SHA256为`45ee4dadf839706bc57c97a3d3a7791cf424545f8ff5c831558df942462189eb`。

## 结束状态和资源保护

本次9个专用runtime均Destroyed；最后只读审计确认其VM、Docker、运行磁盘/配置盘、OVS接口以及10种OVN归属表资源均为0，部署队列无活动项。清理通过平台完成，没有使用广泛删除命令。

原8台VM定义及磁盘保留并关机，末次XML核对10个原磁盘/配置盘文件路径全部存在；PVE VM118–121/原模板/快照和已有学习容器保留；新模板517/518、镜像制作资料、备份、旧release和GitHub发布附件保留，没有自动清理。`.27`主站/Agent active、NRestarts=0，首页/Config200，磁盘约85GiB可用。活动release清单373个文件长度/摘要全部匹配；最后主站切换后日志优先级错误及未处理异常计数均为0（不能据此代替业务验收）。

原始证据入口：acceptance-linux/REAL-ACCEPTANCE.md、acceptance-scenes/evidence-readonly/RESULT.md、tests/agent-review/windows-qga-diagnosis.md。最终清理审计在服务器备份目录final-cleanup-audit.json；节点最终回读在仓库外平台证据deployment-final-nodes.json。密码、Cookie、token、完整user-data、业务内容与原始运行日志不进入本文档或提交。

## 后续与回退

本轮不实现同固定IP多副本VRF/地址池，也不自动修复AD信任或改造所有旧模板。第二套VM118–121的原始四机环境未作为整套重新发布验收；旧509缺QGA需在独立制作副本补组件，Linux原镜像也须满足工具前提。当前验收证明准备合格镜像后的平台网络链路，不能泛化为所有旧镜像、八网卡或跨Worker已通过。

后续使用见[镜像准备说明](../../operations/teamlab-managed-network-authoring.md)。部署/回退见[维护记录](../../operations/teamlab-managed-network-rollout.md)。应用回退保留新增列；停止新模式待执行/恢复任务，不让旧Agent执行ManagedStatic计划。恢复完整备份涉及另行维护窗口和数据范围确认。
