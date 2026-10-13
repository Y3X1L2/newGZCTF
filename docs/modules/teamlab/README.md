# TeamLab：多机组网演练环境

TeamLab 把 Docker、Linux/Windows 虚拟机、网段和网络设备组成一套可重复部署的演练环境。场景作者负责选模板、连网络和声明资源；教师或管理员发布并试运行；外部系统用开放 API 管理隔离环境；运维人员处理访问、观测和清理。比赛目标、Flag 判定和计分由 Penetration 等业务模块负责，TeamLab 不拥有这些玩法事实。

使用时先编辑拓扑草稿，校验网络、地址、模板和资源需求，再发布不可变版本。运行环境引用指定版本，草稿后续修改不会覆盖已运行资产。创建进入统一部署队列，选择适配节点后准备缺失镜像，再部署网络和资产、执行已配置的就绪检查。运行期间可按授权开通访问、操作单个资产、更新计划或抓包；结束时先关闭访问和观测，再销毁资产与网络、释放容量和镜像引用。失败清理保留真实状态，不能只把数据库改成成功。

```mermaid
flowchart LR
  A[拓扑草稿] --> B[校验与不可变发布]
  B --> C[统一队列及节点准备]
  C --> D[网络与资产运行]
  D --> E[访问和观测]
  E --> F[关闭访问并清理]
```

主站的 Contracts/Application 管理授权、版本、计划、代次和状态，Fleet 客户端向独立 Agent 下发已验证的节点意图。Agent 执行 Docker/libvirt、OVN/OVS、WireGuard 和文件/会话操作，不读取比赛或课程实体。数据库保存运行事实，节点 inventory 提供现场观测；清理按冻结执行计划和资源归属进行。

权限同时约束操作类型和资源范围。浏览器管理 API、外部 token API、节点机器 API 不可混用。分配 IP、模板配置完整、计划可用、operation 成功都不能证明来宾网络或桌面登录已经验收；Windows 文件管理与 Linux SFTP 的能力也不能互相替代。跨节点、物理连接器和故障恢复按实际环境签收，不由本页推定。

| 要完成的工作 | 入口 |
| --- | --- |
| 理解功能、交付镜像与场景 | [功能说明](guides/features.md)、[制作交付](guides/authoring.md) |
| 配置来宾网卡并验证 | [受管网络制作](guides/managed-network-authoring.md)、[组网原则](reference/networking-practices.md) |
| 外部系统接入 | [对接链路](guides/integration.md)、[API 参考导航](reference/README.md) |
| 开发或排障 | [源码与测试地图](development.md) |
| 真实基础设施验收 | [验收手册](guides/foundation-acceptance.md)、[当前事实](../../current-state.md) |
