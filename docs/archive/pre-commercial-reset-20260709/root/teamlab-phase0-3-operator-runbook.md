# TeamLab Phase 0-3 Operator Runbook

本文档记录 TeamLab VPN/VM 多网段重构在 Phase 0-3 的可用边界、验证流程和回滚方式。当前阶段的目标是打通控制面、节点能力标记、dry-run 命令计划、基础部署状态机和运维可见性；生产级队伍 WireGuard 配置、Docker/VM 资产接入、DHCP/DNS、AD 域编排和选手 VPN 页面属于 Phase 4+。

## 架构边界

- 主服务器只做控制面：保存 TeamLab runtime、节点调度状态、UDP 映射计划和事件日志。
- WorkerNode 承担数据面：后续真实 bridge、router namespace、WireGuard endpoint 和 VM/Docker 接入都应落在调度到的 WorkerNode 上。
- Public UDP Gateway 是抽象能力：Phase 0-3 只生成 nftables/iptables 命令计划，不直接修改公网网关。
- 现有普通 CTF Docker TCP proxy、AWDP、旧综合渗透、VM/KVM 默认路径不属于本阶段改造对象，不能依赖 TeamLab 字段才能运行。
- 旧的攻击图、迷雾、题目拓扑、公网目标/入口目标展示不属于 TeamLab 新方案；不要在新 UI/API 中恢复这些概念。

## Feature Flags

主服务配置：

- `TeamLabNetworkConfig:Enable=false`：禁止主服务触发 WorkerNode OS 网络变更。
- `TeamLabNetworkConfig:DryRun=true`：部署流程只返回命令计划，不真实创建 bridge、namespace、WireGuard。
- `TeamLabNetworkConfig:RuntimeNetworkBaseCidr`：TeamLab 队伍网段规划基址，默认 `10.180.0.0/16`。
- `TeamLabNetworkConfig:TeamSubnetPrefixLength`：每队网段长度，默认 `/24`。
- `TeamLabNetworkConfig:PublicUdpPortStart/End`：公网 UDP 映射计划端口段，默认 `32000-32999`。
- `TeamLabNetworkConfig:WorkerWireGuardPortStart/End`：WorkerNode 本地 WireGuard 监听端口段，默认 `42000-42999`。

公网 UDP 网关配置：

- `PublicUdpGatewayConfig:Enable=false`：禁止真实公网 UDP 规则变更。
- `PublicUdpGatewayConfig:Provider=dry-run|nftables|iptables`：只影响生成的命令格式；Phase 0-3 即使启用也不会执行生产规则。
- `PublicUdpGatewayConfig:PublicEndpoint`：后续生成选手 VPN endpoint 时使用；Phase 0-3 不生成正式 peer 配置。

Agent 配置：

- `AgentTeamLabConfig:Enable=false`：Agent 不执行 OS 网络命令。
- `AgentTeamLabConfig:DryRun=true`：Agent 只返回命令列表。

## Safe Local Validation

1. 构建主服务和 Agent：

   ```powershell
   dotnet build src/GZCTF/GZCTF.csproj --no-restore
   dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
   ```

2. 保持 `TeamLabNetworkConfig:Enable=false`、`TeamLabNetworkConfig:DryRun=true`、`PublicUdpGatewayConfig:Enable=false`、`AgentTeamLabConfig:Enable=false`。

3. 验证现有链路不回归：

   ```powershell
   dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet"
   dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Vm"
   pnpm --dir src/GZCTF/ClientApp check
   ```

4. 在节点管理页选择一个 WorkerNode，点击 `dry-run 检查`。预期结果：

   - 后端调用 `/api/v1/nodes/{id}/teamlab/enable`。
   - Agent `/api/teamlab/status` 可达时，节点进入 TeamLab `待验证` 状态。
   - 不应创建 Linux bridge、network namespace、WireGuard interface、nftables/iptables 规则。

5. 调用 TeamLab plan/deploy API 做 dry-run 闭环。预期结果：

   - 没有健康 TeamLab 节点时明确失败，错误指向 TeamLabNetwork 调度条件。
   - 节点被人工标记为健康后可创建 runtime、UDP mapping、事件日志。
   - dry-run deploy 最多到达 `Running` 的命令计划状态，不代表真实 VPN 可连通。

## Real Data Plane Validation

真实数据面必须在隔离 WorkerNode 上逐步开启，不允许直接在线上生产节点批量启用。

1. 准备独立 WorkerNode，确认 `ip`、`wg`、`ip netns`、bridge、nftables/iptables 工具可用。
2. 在 Agent 侧设置 `AgentTeamLabConfig:Enable=true`，先保留 `AgentTeamLabConfig:DryRun=true` 复核命令计划。
3. 在主服务侧设置 `TeamLabNetworkConfig:Enable=true`，先保留 `TeamLabNetworkConfig:DryRun=true`。
4. 对单节点执行 dry-run 检查，确认命令计划中的资源名符合 15 字符限制，且以 runtime id 可追溯。
5. 仅在 isolated WorkerNode 上改为非 dry-run，创建两个测试 TeamLab runtime。
6. 验证同队 runtime 内部 bridge/router 可达，跨队 bridge/router 不可达。
7. 验证销毁后无残留：bridge、veth、namespace、WireGuard interface、路由、规则和临时配置文件都应清理。
8. Public UDP Gateway 仍保持 dry-run；只有 WorkerNode 本地数据面验证通过后，才能在后续阶段接入真实公网 UDP 转发。

## Rollback

1. 立即关闭主服务数据面变更：`TeamLabNetworkConfig:Enable=false`、`TeamLabNetworkConfig:DryRun=true`。
2. 关闭公网网关变更：`PublicUdpGatewayConfig:Enable=false`。
3. 关闭 Agent OS 变更：`AgentTeamLabConfig:Enable=false`、`AgentTeamLabConfig:DryRun=true`。
4. 在节点管理中将相关节点的 TeamLabNetwork 状态移出健康调度，或直接将节点移出调度池。
5. 对失败 runtime 执行 destroy/cleanup；如 Agent 清理失败，按事件日志中的资源名手工检查并删除对应 bridge、namespace、WireGuard interface。
6. 普通 Docker TCP proxy、现有 VM、AWDP 不依赖 TeamLab，回滚 TeamLab 配置不应影响这些链路。

## Known Limits

- Phase 0-3 不生成真实 per-team WireGuard peer 配置。当前部署服务中的 `dry-run-peer-key` 是命令计划占位值，不能用于选手连接。
- Phase 0-3 不把 Docker 题目容器或 VM 接入 TeamLab bridge，也不负责 DHCP/DNS、AD 域、Windows 多网卡初始化。
- Public UDP Gateway Provider 当前是 command-plan only；即使 `PublicUdpGatewayConfig:Enable=true` 也会拒绝执行并记录原因。
- 节点页的 `dry-run 检查` 是探测入口，不是生产启用入口；生产启用必须由后续阶段提供明确的 peer 生成、密钥轮换和连通性验证。

## Verification Gate

每次改动 TeamLab Phase 0-3 相关代码后，至少执行：

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet"
dotnet build src/GZCTF/GZCTF.csproj --no-restore
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
git diff --check
```

静态检查还应确认新增 TeamLab UI/API 不出现旧设计术语：端口级 ACL、拓扑迷雾、攻击图、公网目标、入口目标。历史文档或旧页面中残留术语可以单独评估，但不能作为新 TeamLab 行为描述。
