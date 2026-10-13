# 2026-08-13 125 V2 能力间歇缺失根因修复交接

日期：2026-08-13
范围：TeamLab 工作流 A 的 Agent 同步、OVSDB 控制面连接与 V2 能力上报。
不在范围：V2 完整实机矩阵、前端、公开 API、模板制作、工作流 B。

## 结论

125 的 V2 能力此前不是“始终缺失”，而是“间歇性缺失”。修复后 118、125 已同时运行同一 Agent 二进制，连续 10 次以上采样均保持 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`，执行计划并发上限为 1。主站数据库投影同步更新，两节点均 Stable、可调度。

## 根因

1. Agent 同步曾出现“文件已替换但进程未重启”。旧版 `AgentCapabilityService` 计算的是 `/usr/local/bin/gzctf-agent` 安装文件哈希，不是正在运行进程的哈希，导致主站误以为节点已运行新二进制。

2. 即使进程切换后，125 的 V2 就绪探测仍周期性失败。OVSDB 服务端有闲置探测机制：连接空闲时会先向客户端发送 `echo` 请求，再关闭连接。现有 `OvsdbJsonRpcClient` 只在事务进行中读取 socket，空闲期间不会消费服务器发来的 `echo`。下次探测时 socket 缓冲区里残留可读数据，`Poll(SelectRead)` 返回有数据，客户端误判连接健康并继续写事务，随后收到 RST，探测失败，进入 20 秒失败缓存窗口，表现为 V2/NO_V2 交替。

## 修复

1. `AgentCapabilityService.ComputeBinarySha256Async()` 改为读取 `/proc/self/exe`，即运行中进程的哈希。
2. `AgentMaintenanceService.SyncAgentAsync()` 增加“安装文件已更新但运行进程仍是旧版”的检测，发现不一致时调度重启并明确返回。
3. `OvsdbJsonRpcClient` 对低频控制面事务增加闲置重建：会话空闲超过 5 秒，下次使用前主动关闭并新建连接。这消除了半开连接与残留 echo 的竞态，同时保留对已关闭 socket 的即时检测。
4. 补充单元测试：已关闭连接立即重连、闲置连接重连、超时后等服务端释放再重试。定向测试 6/6 通过。

## 部署事实

- 发布版本：`teamlab-hpa-fix-20260813-5`
- 发布目录：`/opt/gzctf/releases/teamlab-hpa-fix-20260813-5/publish`
- 发布包 SHA-256：`a3df348a72841889de3b5f049519ab4b5bc5e2ac92f92a8ff122011b01f9ee6e`
- Agent SHA-256：`c4835cb5945590674c51027f51aee9c3daa2f05e59940fd56fb32b4b3a8e99ce`
- 迁移头：`20260812113416_AlignTeamLabExecutionRuntimeSchema`，本次无新增迁移
- 118、125 均通过平台 `/sync-agent` 完成正式同步，`AgentUpdateState=Stable`，`AgentUpdateExpectedSha256` 与运行进程一致
- 首页 200，`gzctf.service`、`gzctf-agent.service` 均为 active
- 125 在 2026-08-13 上线后连续采样 10/10 与 5/5 均为 V2，覆盖超过两个 20 秒探测缓存周期

## 后续测试入口

测试 Agent 可以继续此前中断的 V2 全链路矩阵：正常链路、并发与故障注入、观测回放、暂停恢复、销毁清理。现有测试资源 `019ff92a`（queued）与 `019ffb04`（ready）属于上一轮验收残留，按既有清理流程处理；不要在本轮交接之外扩大资源。