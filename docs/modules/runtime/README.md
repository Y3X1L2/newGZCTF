# Runtime：统一运行底座与节点执行

Runtime 把业务的创建、重置、销毁等请求转换成可排队、可恢复的运行工作。CTF、练习、课程、AWDP 和 TeamLab 共用 DeploymentQueueTicket；业务模块决定用户是否有权使用环境，Runtime 决定工作何时、在哪个节点执行。运维人员用它观察队列、容量、节点与恢复结果，开发者通过统一入口接入新运行用例。

请求先完成业务校验，再写入数据库票据。调度按照工作对象串行约束、创建者公平性、节点能力与资源余量筛选；容量预留成功后准备选中节点的镜像，再调用 Agent 创建网络、容器或虚拟机。访问入口还须完成各自的就绪或网关 ACK，不能把执行进程存在当作用户可以访问。停止、重建与销毁使用已有身份和代次，不另建独立队列。

```mermaid
flowchart LR
  A[业务已授权请求] --> B[数据库部署票据]
  B --> C[公平选择与容量预留]
  C --> D[节点镜像准备]
  D --> E[Agent 本机执行]
  E --> F[就绪、入口与状态投影]
  F --> G[恢复或精确清理]
```

源码分布在 Modules/Runtime、Services/Fleet、Services/Vm 和独立 GZCTF.Agent。Runtime worker 管理调度、执行、信号和对账；Fleet 负责节点通讯、镜像准备、端口租约及现有 VM/容器适配；Agent 使用 Docker、KVM/libvirt 与本机网络工具执行已验证意图。当前没有可直接定位的 Modules/Vm 目录，不能按目标架构目录猜测实现。

PostgreSQL 是票据和运行状态事实源，Redis 提供唤醒、租约和高频状态缓冲；恢复必须对比数据库当前代与 Agent inventory。Docker 和 KVM 独立判定，节点没有 KVM 不阻断 Docker。VM 来宾初始化、管理网络、RDP/SSH 服务与图形控制台各有条件，不能把 Windows 专用访问链路视为全部 VM 能力。

能力可调度不等于真实链路已验收。节点故障、多 worker 恢复、磁盘硬配额与完整冷启动仍按 [当前状态](../../current-state.md) 和真实记录确认，本页不对生产进行操作。

| 任务 | 入口 |
| --- | --- |
| 修改或排查运行链路 | [开发地图](development.md) |
| 理解票据、容量和恢复 | [调度与恢复](reference/scheduling-recovery.md) |
| VM、Docker 与节点边界 | [Agent 与 VM 执行](reference/agent-vm.md) |
| 现场运行恢复 | [运维手册](../../operations/runtime-scheduling-and-recovery.md) |
