# TeamLab V2 执行面修复交接（2026-08-14 release 9）

- 交接对象：负责 TeamLab 执行面实机测试与代码质量审查的后续 agent。
- 上游依据：`docs/development/handoffs/2026-08-14-teamlab-v2-acceptance-blocked-report.md` 及后续 release 6/7/8 实机反馈。
- 工作分支：`codex/teamlab-high-performance-a`（HEAD `adb9541`，工作树含本轮未提交修复）。
- 用户约束：不做补丁式修复；不静默降级；V2 是默认执行模型；避免低效重复测试；实机测试与代码审查由后续 agent 负责。

## 本轮修复（未提交，需随部署提交）

### OVN 当前 schema 兼容与清理语义

- 移除 `Logical_Switch_Port` insert 中已过时的 `switch` 列（现代 OVN 通过 `Logical_Switch.ports` 反向引用维护关系）。
- `Logical_Router_Port.peer` 是字符串列，不再错误地 `ClearReferences`；清理改为先清除引用集合（`Logical_Router.ports/static_routes/policies`、`Logical_Switch.ports/acls/dns_records`、`Logical_Switch_Port.dhcpv4_options`），再按固定表序删除归属行。
- `dhcpv4_options` 仅当网络存在 DHCP 租约时写入 `named-uuid`，不再向 JSON 写 `null`。
- OVS 附件 `external_ids` 统一使用 OVSDB `["map", ...]` 编码；接口类型从空串改为 `system`；`PlanDigest` 在主机接口附件（WireGuard 等）中不强制匹配。

### 玩家入口与容器网络闭环

- 新增 `TeamLabResourceNameFactory.PlayerGatewayMac`，编译器为玩家网关生成稳定 MAC，OVN LSP 与 Agent 主机 WireGuard 接口使用同一 MAC，避免 port security 丢包。
- V2 主机 WireGuard 配置接口新增 `MacAddress`，`ConfigureHostWireGuardAsync` 最后设置接口地址。
- Docker 容器接口名改为 `eth{index}`，规避 Linux 接口名 15 字符上限（此前 E2E 拓扑 `docker-switch-nic` 导致 nsenter 失败）。
- 镜像引用统一 `sha256:` 前缀，`gzctf-internal://` 归一化为内部 registry 地址（复用 V1 的解析语义）。
- 容器健康检查改为 `nsenter -t <pid> -n bash` 在容器网络命名空间内探测 TCP/HTTP，不再从 Agent 网络空间直连。

### 全局网络与 VM 生命周期

- 编译/计划语义：`TeamLabExecutionPlanCompiler` 接收 network owner、全局网络基础设施、全部资产与观测点；全局网段只在唯一 owner shard 创建 OVN，非 owner 分片校验资源存在并做本地 OVS 接入。
- `LibvirtTeamLabProvider` 增加 libvirt 原生 `PauseAsync` / `ResumeAsync`，Agent 生命周期接口区分 V1/V2；VM XML 增加 `<target dev="tlv...">` 与 OVS `interfaceid`。

## 部署事实（release 9）

- 发布物：`teamlab-ovn-attach-fix-20260814-9.tar.gz`，SHA-256 `74a81c7b15c89725fb83d7f0c821f5d3af416459e395a33a65084d038741bb30`。
- 软链：`/opt/gzctf/publish -> /opt/gzctf/releases/teamlab-ovn-attach-fix-20260814-9/publish`；上一版本 `teamlab-docker-reference-fix-20260814-8` 保留在 `publish.previous`。
- 数据库：`efbundle` 已执行，`No migrations were applied`（库已是最新迁移头）；备份位于 `/opt/gzctf-vnext/backups/teamlab-ovn-attach-fix-20260814-9/`。
- 服务：`gzctf.service`、`gzctf-agent.service` 均 active，`http://127.0.0.1:8080/` HTTP 200；125 观测读取持续 200。
- Agent：118 `/usr/local/bin/gzctf-agent` SHA-256 `3625447c589f2756c41fc9c6801a26474128ae8028f80c71c0ddc283151b378b`，与发布包内 agent 一致。

## 尚未闭环、需要测试 agent 实测确认

1. **125 Agent 同步**：release 9 部署后 125 仍运行 release 8 Agent（SHA `9d9444bf...` 上一轮已同步）。需通过平台节点管理对 125 执行 `sync-agent`，确认运行进程 SHA 与 release 9 一致、能力清单齐全。
2. **V2 VM 网络**：libvirt XML 已声明 TAP 与 OVS interfaceid，但 `ApplyVmAsync` 尚未显式调用 `TeamLabOvsAttachmentProvider.AttachAsync`；libvirt bridge 模式可能自动创建 TAP，但 OVS 本地 attachment 未实机验证。请以真实 VM apply 结果为准，不要把该链路标为已完成。
3. **OVN 26.03 兼容**：本轮修复 `Logical_Switch_Port.switch` 与 `peer` 列语义后，仍须以生产 OVN schema 跑通完整 network apply/cleanup 与存量清理。
4. **V2 全矩阵**：多资产/多网段、暂停/恢复/重置/销毁、并发、故障注入、观测回放与清理残留核对，按 `docs/development/handoffs/2026-08-12-teamlab-data-plane-and-observation-handoff.md` 继续。

## 本轮门禁

- `dotnet build src/GZCTF.slnx -c Release`：0 错误 0 警告。
- 前端门禁（publish 内嵌）：locales、lint、tsc、architecture、239 个 vitest 用例、vite build 全部通过。
- 定向 TeamLab 单测：OVN provider、执行计划、接口命名、命令构建共 75 个通过。
- 未执行：完整单元套件、集成测试、实机压测、浏览器与故障注入测试（按用户约束交给测试 agent）。