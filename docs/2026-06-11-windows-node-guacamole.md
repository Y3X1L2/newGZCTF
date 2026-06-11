# Windows 节点调度与 Guacamole 接入说明

本次版本主要修复 Windows 靶机在多节点环境下的远程桌面接入问题，并整理远程 worker 节点的部署方式。

## 已解决的问题

- 远程节点上的 Windows VM 能启动，但主平台无法直接访问 VM 内部 `192.168.122.x:3389`，导致页面长期停在“正在获取网络地址并配置远程桌面”。
- 同时打开两台 Windows 靶机时，第二个 Guacamole 页面可能把第一个页面踢回 Guacamole 登录页。
- 节点自动部署时，平台可能把 `0.0.0.0:8080` 当作 agent 回连地址，远程节点无法下载 agent 或镜像模板。
- 部分远程 shell 脚本在目标机上解析失败，例如 `.NET runtime` 检测脚本里的多行 `elif` 被错误拆分。

## 当前实现方法

### 远程 Windows VM 访问

远程 worker 节点仍然使用本机 libvirt NAT 网络启动 VM。VM 内部地址只在该 worker 上可达，因此 agent 会在 worker 上为每台 VM 建立一个 TCP RDP 代理：

```text
0.0.0.0:<46000-55999> -> <VM 内部 IP>:3389
```

主平台创建 Guacamole 连接时：

- 本地节点 VM：Guacamole 直接连接 VM 内部 IP 的 `3389`。
- 远程节点 VM：Guacamole 连接 worker 的 `HostAddress:<agent 返回的 RDP 代理端口>`。

这样浏览器仍然只访问统一入口：

```text
http://<主平台>:8081/guacamole
```

真实靶机可以调度到本地节点，也可以调度到远程 worker。

### VM IP 获取

agent 侧按顺序尝试以下方式发现 VM IP：

1. `virsh domifaddr --source agent`
2. `virsh domifaddr`
3. `virsh net-dhcp-leases default`
4. `ip neigh show dev virbr0`

只要获取到 VM IP，agent 就返回 `IpAddress` 和 `RdpPort` 给主平台。

### Guacamole token

主平台侧 `GuacamoleService` 现在按应用级缓存 Guacamole token，并注册为 singleton。这样同一个平台实例不会在每次获取 RDP 链接时重新登录 Guacamole，降低同源浏览器里多个 RDP 标签页互相覆盖认证状态的概率。

这是短期兼容方案。后续更正规的方案是为平台用户映射独立 Guacamole 用户，或者在平台内封装 Guacamole tunnel，不让选手直接进入 Guacamole 自身的登录体系。

### 节点部署

节点部署逻辑现在会优先使用：

1. `Agent:ServerPublicUrl`
2. `ContainerProvider:PublicEntry` + 平台监听端口
3. `Urls` 中非 wildcard 的可路由地址

避免把 `http://0.0.0.0:8080` 下发给远程节点。

同时新增了节点部署指南和初始化脚本：

- `docs/node-deployment/README.md`
- `docs/node-deployment/setup-gzctf-worker-node.sh`
- `scripts/prepare-agent-node.sh`

## 已验证状态

- `10.0.7.125` 远程节点可部署并注册为 worker。
- Docker 题目可调度到远程节点并正常启动、销毁。
- Windows VM 可分别调度到本地节点和远程节点。
- 远程节点 Windows VM 通过 worker RDP 代理接入 Guacamole 后可以打开。
- 主平台部署后 `http://127.0.0.1:8080/` 返回 200。

## 后续优化建议

- 为每个平台用户创建或复用独立 Guacamole 用户，避免所有 RDP 会话共用 `guacadmin`。
- 将基础 VM 镜像目录和运行时 overlay 目录拆开，基础镜像可共享，运行时磁盘落 worker 本地 SSD/NVMe。
- 为 RDP 代理端口增加持久化记录和冲突观测，方便服务重启后排查端口占用。
- 在节点详情页展示 VM 实际调度节点、RDP 代理端口和 Guacamole 连接 ID，便于运维定位。
