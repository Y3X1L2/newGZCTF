# TeamLab 基础能力验收手册

## 1. 验收范围

本手册验证 TeamLab 作为独立拓扑、发布、运行、访问和流量控制面工作。Penetration 只负责目标、提交、计分和战队绑定的业务适配，不拥有 TeamLab 拓扑或 runtime。

本手册面向真实可销毁环境，不以 mock、页面显示或历史测试数字替代节点、Agent、容器、虚拟机和网络验收。

## 2. 发布前门禁

1. 进入维护窗口，暂停拓扑写入、发布、runtime 创建/重置/销毁和抓包创建。
2. 等待 TeamLab 部署任务进入终态。
3. 备份 PostgreSQL，记录活动 runtime、generation、shard、网络租约、资产、访问授权、UDP 映射和节点资源事实。
4. 执行迁移。迁移前置条件失败时修复绑定或 runtime 事实后重新执行完整事务，不绕过检查。
5. 确认所有 Agent 的 manifest 可解析，并具备当前场景所需 Docker/KVM/Fabric/WireGuard 能力。

## 3. 独立 Docker 流程

1. 创建只有 TeamLab topology、runtime、traffic 和 capture scope 的 API token，不绑定 Game 或 Team。
2. 创建两个 RFC1918 网段的拓扑，包含入口 Docker、内部 Docker、健康检查和一条允许连接。
3. 完成校验、发布、计划并创建 runtime，记录稳定外部引用。
4. 轮询 operation 和 runtime 事件，直到服务端报告 Ready。
5. 创建并消费一次性 WireGuard 访问授权。
6. 验证入口网络可达、路由后的内部 HTTP 服务可达、未连接网络不可达。
7. 查询流量元数据，启动有时长/大小限制的抓包，产生流量、停止抓包并下载 PCAP。
8. 销毁 runtime，逐节点核对容器、网络命名空间、路由、WireGuard、抓包进程和临时文件均已清理。

## 4. 独立 Linux VM 流程

1. 使用 Ready 的 Linux cloud-image 模板发布至少两个 RFC1918 网段的拓扑。
2. 创建 runtime，核对 cloud-init 主机名、按 MAC 固定地址、DNS、路由、qemu guest-agent 和服务健康。
3. 通过路由路径验证 SSH 和目标 HTTP 服务。
4. 重置 runtime，确认 PublicId 和外部引用稳定、generation 增加、旧访问授权失效、旧 generation 事实仍可查询。
5. 销毁 runtime，确认 overlay、seed ISO、bridge、namespace、route、capture 和 staging 文件不存在。

## 5. Penetration 适配流程

1. 创建 Penetration 比赛，绑定已有 TeamLab topology 和活动 release。
2. 使用 topology asset key 配置目标，至少包含一个动态 Flag 和一个前置条件。
3. 为两支队伍启动环境，确认各自绑定不同 TeamLab runtime。
4. 提交动态 Flag，核对得分和提交审计，然后只重置其中一支队伍。
5. 确认另一支队伍的 runtime、得分、授权和流量事实未改变。
6. 停止两套环境，核对 runtime 清理、队列事件和系统事件。

## 6. 资源残留检查

对每个已销毁 runtime 的 PublicId，在所有参与节点核对：容器、libvirt domain、qcow2 overlay、seed ISO、bridge、路由 namespace、WireGuard interface、路由、capture 进程、PCAP 和 staging 文件。任何残留都阻断生产验收，必须记录资源归属和清理结果后重验。

## 7. 验收证据

至少保存 topology/release/runtime ID、operation/ticket ID、节点和 Agent manifest、关键事件 correlation、入口访问结果、流量/PCAP 元数据和清理核对结果。证据中不得保存 token、Flag、密码、私钥、完整 user-data 或 Registry 凭据。

## 8. 回滚边界

涉及删除旧 Penetration topology/runtime 表的契约迁移时，应用回滚必须同时恢复迁移前数据库备份和匹配的应用版本。只回滚二进制而不恢复数据库不受支持。
