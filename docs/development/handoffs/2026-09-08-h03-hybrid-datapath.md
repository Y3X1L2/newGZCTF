# H03 本地混合数据面验收

## 目标与基线

用户要求完成 H03，并补齐相关代码缺口；允许模拟缺失的物理条件。当前工作区 `codex/teamlab-commercial-closure`，基线 `origin/main 3a70126`，保留原有在研修改。没有生产发布或远端操作。

## 当前结果

后续增量：`TeamLabManagedNicProvider` 已实际接入本测试，模拟外部设备不再直接调用通用 OVS attachment 作为连接器替身。专用网卡的 prepare/probe/attach/detach、另一 runtime 抢占拒绝及解绑保留网卡均通过，`hybrid-connector.trx` 为 1/1（19 秒）；与 Modbus 联合回归为 `hybrid-modbus-final.trx`，2/2（29 秒）。后续主站节点约束、网络计划与前端登记已实现，尚未完成真实主站端到端。前端完整门禁 324/324；后端相关测试 475/475。下文原始 31 秒结果仍是第一阶段证据，不代表全部后续功能。

**网络 provider 专项 VERIFIED；完整 H03 尚未签收。**

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

## 尚未完成的完整 H03 链路

1. H01 连接器 prepare/attach/probe/detach/inventory 接入主站和原 DeploymentQueueTicket，目前登记/租约不能代表接通。
2. 通过主站发布并创建真实 Docker＋libvirt VM＋连接器绑定场景；本专项直接调用网络 provider，未经过主站授权、计划编译和调度。
3. 主站/Agent/节点故障恢复以及 runtime/shard 父状态恢复；本次只验证 OVN controller 重启。
4. 平台抓包 API、下载、流量页面与销毁对账的同一混合场景完整链路；本次抓包使用夹具 tcpdump。
5. 实际物理设备及多 Worker 跨节点验证。本地 namespace 模拟只能验证网络行为，不能替代硬件签收。

本轮未提交、推送、合并或部署。不要将此报告、旧 BIOS VNC 测试或以前的全量门禁合并解释为“全部商业能力闭环”。
