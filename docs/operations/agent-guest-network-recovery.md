# Agent 来宾管理网桥启动恢复

适用于启用了 `Agent:GuestManagement:Enabled`，但重启后因管理地址缺失而以
`SocketException (99): Cannot assign requested address` 退出的 Linux Worker。

## 原因

现有节点初始化流程使用 `ip link add` 创建管理网桥，未持久化该链路。
Agent 的 HTTPS listener 在启动时绑定配置中的管理地址；主机重启后链路消失，
listener 无法启动，普通 Docker Agent 接口也随整个进程退出。

## 修复

先核对真实服务工作目录、配置路径、现有链路、nftables、运行容器及 VM。
备份服务配置与 Agent 配置后，安装以下两个文件：

- `scripts/deployment/restore-agent-guest-network.py` 到
  `/usr/local/libexec/gzctf-agent-guest-network.py`，root 拥有，权限 `0755`。
- `scripts/deployment/gzctf-agent-guest-network.conf` 到
  `/etc/systemd/system/gzctf-agent.service.d/20-guest-network.conf`，权限 `0644`。

示例 drop-in 使用 `/etc/gzctf-agent/appsettings.json`；必须按实际服务调整。
脚本解析配置中的大小写兼容 GuestManagement 属性，禁用时不操作网络。
启用时先检查接口类型、地址冲突和 nft 语法，再原子恢复专属隔离规则与网桥地址。
不修改默认路由、其他防火墙表、Docker 网络或 TeamLab/Fabric 开关。

执行 `systemctl daemon-reload` 和 `systemctl restart gzctf-agent.service` 后验证：

1. `gzmgt0` 存在并具有配置的管理地址，`gzctf_guest_mgmt` 隔离规则存在。
2. Agent 状态保持 `active/running`，至少两个心跳间隔内不新增自动重启。
3. 主站收到该节点新心跳，能力和调度状态与预期一致。
4. 重新运行恢复脚本不会重建已存在网桥，不影响原有实例。

回退只移除本次新安装的 drop-in，恢复备份后 `daemon-reload`；不得删除有来宾接入的
管理网桥。禁用 GuestManagement 会影响既有 VM 控制能力，不能作为默认修复。

自动测试：`python -m unittest discover -s scripts/deployment -p test_restore_agent_guest_network.py -v`。
真实主机重启验收需要维护窗口；服务重启验证不能冒充整机重启验证。
