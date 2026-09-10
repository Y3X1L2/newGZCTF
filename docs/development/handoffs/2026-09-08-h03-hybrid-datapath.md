# H03 本地混合数据面验收

## 目标与基线

用户要求完成 H03，并补齐相关代码缺口；允许模拟缺失的物理条件。当前工作区 `codex/teamlab-commercial-closure`，基线 `origin/main 3a70126`，保留原有在研修改。没有生产发布或远端操作。

## 当前结果

后续增量：`TeamLabManagedNicProvider` 已实际接入本测试，模拟外部设备不再直接调用通用 OVS attachment 作为连接器替身。专用网卡的 prepare/probe/attach/detach、另一 runtime 抢占拒绝及解绑保留网卡均通过，`hybrid-connector.trx` 为 1/1（19 秒）；与 Modbus 联合回归为 `hybrid-modbus-final.trx`，2/2（29 秒）。后续主站节点约束、网络计划与前端登记已实现，尚未完成真实主站端到端。前端完整门禁 324/324；后端相关测试 475/475。下文原始 31 秒结果仍是第一阶段证据，不代表全部后续功能。

**网络 provider 专项与测试服务器主站全链路均 VERIFIED。**

`TeamLabHybridDatapathTests.RealOvnTrafficIsolationFailureRecoveryAndExactCleanup` 在 2026-09-08 通过，1/1，31 秒。夹具使用真实 OVN NB/SB、ovn-northd、ovn-controller、OVS userspace datapath 和 QEMU TCG Linux 来宾，应用与拆除逻辑网络、OVS 端口调用当前 Agent provider。客户端和模拟外部设备使用隔离的 Linux network namespace；不是物理设备，也不是主站创建的独立 Docker 业务资产。VM 使用测试 initramfs 和网卡，没有伪造网络响应。

| 检查 | 实际证据 | 结果 |
| --- | --- | --- |
| 客户端 → Linux VM | HTTP 返回 `hybrid-vm` | 通过 |
| 客户端 → 模拟外部端点 | HTTP 返回 `hybrid-external` | 通过 |
| 隔离 | 两个独立逻辑交换机使用相同地址段，双向探测不能跨交换机访问 | 通过 |
| 抓包 | 实际 TCP 流量生成非空 PCAP，tcpdump 解码包含客户端地址 | 通过 |
| 断链识别 | 删除外部端点 OVS 端口，provider probe 失败、HTTP 失败 | 通过 |
| 重接恢复 | 原 provider 接回同一端口，HTTP 再次返回预期内容 | 通过 |
| 控制器重启 | 停止并启动夹具 ovn-controller 后，VM HTTP 恢复 | 通过 |
| 精确清理 | 删除本次端口/逻辑网络，probe 不再报告存在，另一 runtime 网络保留 | 通过 |
| 最终回收 | 清理第二 runtime；测试容器自动删除，原本地服务保留 | 通过 |

TRX 在仓库外：`C:/Users/87701/AppData/Local/Temp/teamlab-h03-20260908/hybrid-datapath.trx`。PCAP 在测试容器内生成并解码验证，随夹具销毁，未导出为长期留存附件。

## 代码修复

- 已执行计划的重复 apply 现在复查 OVN 意图和 OVS 端口，缺失时返回 network 失败，不仅凭计算资源 running 返回已应用；检查失败不自动重建或销毁正在运行的资源。
- OVS probe 核对端口归属、逻辑 iface-id、Bridge → Port → Interface 引用以及有效 ofport；VM 使用 libvirt 的 vm-id/iface-id 归属，而非要求 Docker 的 Agent 标签。该检查不等同业务 HTTP 健康检查。
- VM-only 分片读取 inventory 不再无条件读取 Docker daemon。
- 独立可重复夹具位于 `src/GZCTF.Integration.Test/Fixtures/Hybrid/`，没有挂载宿主 Docker socket、没有使用 host network。夹具需要 privileged 以创建 namespace 和 TAP。

测试中修复了夹具网关字段、OVN controller 启动选项、网卡模块解压依赖。用户态 OVS 下 ICMP 可通但 TCP 超时；关闭测试 veth TX checksum offload 后，完整数据面测试通过。该设置只存在于夹具，不修改正式节点的网络配置。

## 重跑

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~TeamLabHybridDatapathTests
```

首次构建需下载 Linux 内核、QEMU 和 OVN 软件包；后续按夹具内容摘要缓存。测试从真实探测等待就绪，失败有界退出并回收夹具。

## 测试服务器主站全链路（2026-09-10）

测试环境为主站 `10.0.7.118`、执行节点 `10.0.7.125`。验收通过正式管理员 API、
统一 `DeploymentQueueTicket`、H01 受管网卡连接器和 Agent 执行，不直接调用 provider。
拓扑 `H03 虚实混合现场设备验收环境` 保留发布版本和已销毁运行历史；模拟现场端点在
验收结束后删除。服务器账号、平台凭据和 Cookie 均未写入仓库。

| 检查 | 服务器实证 | 结果 |
| --- | --- | --- |
| 主站编排 | 失败的 generation 2 通过正式 reset 收敛为 generation 3 `ready`；破坏恢复后 generation 4 再次 `ready` | 通过 |
| Docker + VM + 现场端点 | PLC `10.96.1.10`、libvirt VM `10.96.1.20`、模拟端点 `10.96.1.50` 真实互通 | 通过 |
| Modbus TCP | 现场端点读取 unit 7、holding register 0-3，响应值为 `12/34/56/78` | 通过 |
| VM 网络配置 | Agent 创建 380,928 字节 CIDATA；VM 回报计划 MAC、`10.96.1.20/24`，SSH banner 可达 | 通过 |
| 双向混合互通 | PLC 访问现场 HTTP 返回 200；VM 经 QGA ping 现场端点 2/2、0% 丢包 | 通过 |
| 逻辑隔离 | 现场端点和 PLC 均不能访问 `isolated` 网段的 `10.97.0.10` | 通过 |
| 平台抓包 | `field` 两观测点捕获 3,090 + 20,692 字节；下载 tar 可解析，两段 PCAP SHA-256 与平台一致 | 通过 |
| 连接器断链恢复 | 删除 `h03fld0` OVS port 后 Modbus 超时；正式 reset 自动以新代次重新挂接并恢复 | 通过 |
| OVN 恢复 | 重启 `ovn-controller` 后服务 active，首轮 Modbus 探测成功 | 通过 |
| 独占租约 | 同一发布创建第二运行返回 HTTP 409、`connector_occupied` | 通过 |
| 销毁对账 | 资产/分片/运行均 `destroyed`；容器、域、qcow2/seed、运行 OVS 接口均无残留 | 通过 |

本次发现并修复 libvirt VM 的真实缺口：原实现只创建 qcow2 overlay 和 TAP，来宾系统
不会获得执行计划中的静态地址。Agent 现在按计划生成 NoCloud `meta-data`、`user-data`
和 `network-config`，使用节点现有的 `cloud-localds`、`genisoimage`、`mkisofs` 或
`xorriso` 生成 CIDATA，并挂载 QEMU Guest Agent channel；销毁同步删除 seed 文件。
`TeamLabVmArtifactSafetyTests` 4/4 通过。

边界：本轮使用 network namespace 模拟现场设备，验证的是与物理网卡相同的二层接入、
协议和故障行为，不替代特定 PLC 型号、驱动、时序和电气层的硬件认证。抓包在销毁前已
下载验签；运行销毁后抓包接口返回 404，符合随运行回收的当前生命周期，证据摘要保留在
本文。测试服务器不是生产环境，未改动生产节点。
