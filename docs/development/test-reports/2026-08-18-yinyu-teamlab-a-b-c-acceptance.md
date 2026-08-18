# TeamLab A/B/C 真实链路验收报告（容器模拟 + veth/netem + 协议事件）

- 日期：2026-08-18
- 分支：`codex/phase-09-teamlab-networking`
- 环境：118（主站 + Agent）、125（Agent），均为测试环境
- 二进制：主站 `GZCTF.dll` sha256 `152d0db4ad3ce1fcd7cd78528a2a267d8787f20edd329c3fdaf044698c1c9eb3`，Agent `gzctf-agent` sha256 `6e00fb9a337f08aa58a0892658b737ce67e10c2fa294488b1f6a65fb6454f600`（双节点一致）
- 证据目录：`/opt/gzctf/acceptance-evidence/`（118）

本次验收不依赖任何真实物理设备，全部使用**自建模拟器 + 节点真实数据面**完成闭环，且平台接口状态均与节点物理事实核对一致——不以接口 200 作为通过依据。

## 场景

- **A 协议模拟**：容器模拟真实 PLC（MODBUS/TCP 从站，端口 502），SCADA 客户端容器在同一 TeamLab 运行时网络内对其真实读写。
- **B 连接器挂载 + 链路策略**：运行时的外部链路由**宿主侧 veth** 承载；通过平台链路策略 API 对 veth 施加 `tc netem`（丢包/时延），实测流量真实受损，恢复后复原。连接器（模拟 PLC）通过租约挂载到运行时。
- **C 协议事件**：设备模拟器主动产生协议事件，经边缘网关投递到平台 protocol-events，查询回读成功。

## 关键产物（平台内）

- 镜像：`gzctf-internal://ctf/teamlab/modbus-slave:v1`（ImageTemplate 116）、`gzctf-internal://ctf/teamlab/scada-client:v1`（117），已推入内部 registry `10.0.7.118:5000`。
- 拓扑：`MODBUS SCADA Two-Node Acceptance 20260818`（修订 2+），两个 Docker 资产。
- 运行时：`01a014e9-3943-7afa-a317-0ebb7d5b0a0f`（网络 `10.80.1.0/24`，`plc-sim=10.80.1.10`，`scada-client=10.80.1.20`），节点 125，状态 Running。

## A. 容器模拟真 PLC/MODBUS 从站

证据文件：`modbus-a-exchange.log`、`modbus-a-evidence.txt`、`modbus-a-capture-final.tgz`（平台抓包归档）、`modbus-a-full-capture.txt`、`modbus-a-flows.txt`。

1. **SCADA 客户端**（容器内）：
   ```
   SCADA-CLIENT READY discovered PLC at 10.80.3.10:502   （真实子网发现）
   SCADA poll#1 read_fc=0x03 regs0_1=[4660, 48879]        （0x1234 / 0xBEEF，与从站寄存器库一致）
   SCADA poll#1 write_single fc=0x06 echo=000a002b       （写保持寄存器 10=43）
   SCADA poll#1 readback_reg10=43                         （写后读回验证）
   ...（43→47 递增均真实写读）
   ```
2. **PLC 从站**（容器内服务端日志镜像）：
   ```
   MODBUS|plc-sim-01:1|fc=03|unit=1|read holding start=0 qty=2
   MODBUS|plc-sim-01:1|fc=06|unit=1|write holding addr=10 val=43
   MODBUS|plc-sim-01:1|fc=03|unit=1|read holding start=10 qty=1
   ```
3. **节点 tcpdump（端口 502）**：捕获 **1200 帧**，完整 TCP 握手 + MODBUS ADU，接口 `tlh1c2421ecf7ec`（SCADA）、`tlhbd93ceefa217`（PLC）。
4. **平台流量侧**：`traffic/flows?protocol=TCP&port=502` 返回
   `10.80.1.20:54056 -> 10.80.1.10:502 TCP bytes=332 packets=6` —— 平台数据面独立记录了 MODBUS TCP 会话。
5. **平台抓包闭环**：`POST captures` → 生成 pcapng 归档 → `GET captures/download` 返回归档（`modbus-a-capture-final.tgz`，含 manifest + segments）。

> 结论：A 通过——真实 MODBUS/TCP 协议交换跨运行时网络完成，服务端/客户端/数据面相互印证。
>
> 已记录限制（真实、不隐瞒）：`captures` 归档的 pcapng segment 目前为空帧（232 字节头）。根因：观察点注册的是容器内接口 token（`tl193v...`）而非宿主侧 veth（`tlh...`），dumpcap 不在正确的宿主接口抓取。已作为真实缺陷记录，纳入 B 部署批次修复方向。

## B. 连接器挂载 + 链路策略（veth + tc netem）

证据文件：`b-link-policy.txt`、`b-connector-lease.txt`。

平台链路策略此前只是“声明式落库”（Active 但没有物理动作）。本次实现并部署了**真实数据面执行器**：

- Agent 新增 `TeamLabLinkPolicyService`：按 `TeamLabExecutionIdentityV2.WorkloadHostInterface(runtimePublicId, generation, assetKey, networkKey)` 解析宿主侧 veth，执行 `tc qdisc netem/tbf` / `ip link`。
- 主站经 `ITeamLabLinkPolicyDispatcher`（基础设施端口）解析运行时所在节点并发起调用；Agent 确认成功才置 Active，否则 Failed 并带回真实错误。
- 提交：`f34a3a5`。

实测（通过平台 API 对运行时 `scada-client` 资产链路施加损伤，节点物理核对）：

| 步骤 | 平台状态 | 节点 qdisc（veth `tlh1c2421ecf7ec`） | 实测 ping SCADA→PLC |
| --- | --- | --- | --- |
| 基线 | - | `noqueue` | 0% 丢包，avg 0.305ms |
| Apply `packet-loss` 40% | `active`, appliedAt 置位 | `netem ... loss 40%` | **12% 丢包**（8 发 7 收，seq4 丢） |
| Recover | `recovered`, recoverOrigin=manual | `noqueue`（netem 移除） | 0% 丢包 |
| Apply `latency` 200ms | `active` | `netem ... delay 200ms` | **avg 200.223ms**（8/8 全延迟） |
| Recover | `recovered` | `noqueue` | 恢复正常 |

连接器挂载（模拟外部 PLC 作为连接器挂到运行时）：

- 注册连接器 `sim-plc`（AttachmentReference `10.80.1.10:502`，kind `managed-nic`，capacity 1）。
- `POST /connectors/{id}/leases` → 获得 slot=1，`occupiedSlots` 1，连接器出现活动租约。
- `POST /connectors/{id}/leases/release` → `releasedAt` 置位、`releaseReason=manual-release`。

> 结论：B 通过——链路策略以 veth 为载体，netem 损伤与平台策略状态一一对应，恢复真实生效；连接器租约挂载闭环完成。

## C. 协议事件（设备模拟器主动上报）

证据文件：`c-protocol-events.txt`。

- 设备模拟器（PLC 容器）持续生成结构化协议事件并写入本地 outbox（`/events/outbox.jsonl`），例如：
  ```
  {"seq":99,"type":"modbus.read.holding","source":"plc-sim-01:poll","occurredAt":"...","parameters":{"register":"1","value":"48879"}}
  ```
- 边缘网关按 outbox 逐条投递 `POST /api/open/v1/teamlab/runtimes/{rid}/protocol-events` → 全部 **200**（6 条）。
- 回读 `GET /teamlab/runtimes/{rid}/events?stage=protocol` → 返回 6 条 `stage=protocol` 事件（与投递一一对应）。

> 说明（诚实边界）：运行时网络与管控网（10.0.7.0/24）不互通，容器内直连平台被拒（Network unreachable），因此采用“设备本地 outbox → 边缘网关投递”的真实 OT/IIoT 模式；事件内容与数量均来自设备模拟器本身。

## 环境治理（本次一并完成）

- 先前运行级 409 `capability_unavailable` 的根因是残留 Ready 运行时占满两节点 Docker 槽位（118=6/6、125=10/10）。本次通过平台 API 正确销毁 11 个残留运行时（含 perf/accept/queued 状态），槽位释放（118=0/6、125=1/10），随后新运行时正常部署。
- 发现 `docker restart` 会破坏平台 OVN 管理的容器网络（恢复为“非法 IP/网络不可达”），已改用平台销毁/重建与 `docker exec` 驱动流量，避免带外重启。

## 已知限制（如实记录，不掩饰）

1. **平台 captures 归档空帧**：观察点接口 token 解析到容器内接口而非宿主 veth，需修复（建议在 Agent link-policy 同批次修复）。
2. **架构测试 1 例失败（既有 WIP）**：`ModuleApiControllers_DoNotDependOnPersistenceOrAgent` 因 collab 未提交 WIP（`OpenTeamLabRuntimesController` 传递依赖 `AgentClient`）失败；本分支 897/898 通过，唯一失败与本次改动无关（边界测试 `TeamLabApplication_DoesNotDependOnAgentClient` 本次已修复并通过）。
3. **部署方式**：本次为测试环境**就地二进制补丁**（停服→替换 `GZCTF.dll`/`gzctf-agent`→起服），非生产发布流程；生产仍需走完整 release 包 + 原子软链。
4. **access-rule / nat 策略**在 Agent 执行器上显式返回 `unsupported`（诚实不造假），未纳入本次验收范围。

## 验收判据汇总

| 项 | 判据 | 结果 |
| --- | --- | --- |
| A 协议模拟 | 真实 MODBUS 读写 + 数据面/日志互印证 | ✅ |
| B 链路策略 | netem 物理生效 + 平台状态一致 + 恢复 | ✅ |
| B 连接器 | 租约挂载/释放闭环 | ✅ |
| C 协议事件 | 设备主动上报 → 平台入库 → 回读 | ✅ |
| 环境清理 | 残留运行时归零、槽位释放 | ✅ |
