# Agent、Docker 与 VM 执行边界

主站完成身份、权限、资源归属和参数校验，再通过 AgentClient 调用节点机器 API。独立 GZCTF.Agent 只执行节点本机意图，不能读取比赛、课程、计分或用户权限实体。当前 VM 实现分布在 Services/Fleet、Services/Vm 与 Agent/Services/Vm，不存在 Modules/Vm 目录。

## 节点能力和资源身份

Docker 与 KVM 单独判断；只有 Docker 的节点仍能承载容器。能力清单、Agent feature 与现场资源观测各有用途，在线不等于全部执行能力正常。机器 API 的认证、超时、结构化错误和版本协商由 Agent/Fleet 维护，不给业务用户暴露节点管理地址、凭据或自由 shell。

容器和虚拟机创建、控制及销毁校验运行归属与代次。VM 原生 UUID、域名、执行计划和节点应一致；不能按显示名称或“第一个域”选择目标。可重入动作和 operation receipt、runtime signal journal 负责收敛和恢复，但仍需实机断连/重启验收。

## VM 从磁盘到访问

镜像来源和 backing 链由 Content 与 Fleet 制品存储管理；VM provider 创建域和磁盘，Agent VmDomainBuilder 生成受控定义，VmBootstrapService 与 VmGuestAgentService 完成声明的来宾初始化。VmRuntimeReadinessCoordinator 汇集就绪条件，TeamLabVmNetworkService 按计划处理来宾网卡、路由、DNS 和回读。

受管静态网络有独立 feature 门禁。Windows 依赖受管来宾网络能力，Linux 还需 cloud-init；DHCP 策略与原系统配置配合的制作要求见 [受管网络指南](../../teamlab/guides/managed-network-authoring.md)。平台分配的地址必须与可信当前代回读区别标识，QGA 存在或计划成功不代表真实网络已通。

访问条件分别判断：VNC 是 libvirt 图形控制台，不依赖来宾 IP 或账号，但需要匹配的活动域与 loopback 图形监听；RDP/SSH 需要相应来宾服务和管理网络。Linux VM 的 SFTP 使用受控模板账号和 SSH 主机身份；Windows/unknown VM 不能因为配置了远程账号就使用 Linux 文件能力。Docker 使用终端与容器文件通道。

## 就绪与入口

端口映射、网关和 Guacamole 会话由主站授权并短时建立；浏览器不能获得节点端口或密码。比赛 VM 访问、玩家实例状态与 TeamLab 会话各走自身投影，Ready 不自动等于用户登录成功。公网入口未完成 ACK 时，不能把候选地址展示成可用地址。

真实验收需要 Docker 网络、KVM 域、来宾初始化、受管网络回读、入口/桌面登录和销毁后 inventory 对账。TCG 本地控制台、mock provider 或模板认证各只证明各自范围。源码和测试见 [开发地图](../development.md)，现场事实见 [当前状态](../../../current-state.md)，恢复按 [运维手册](../../../operations/runtime-scheduling-and-recovery.md)。
