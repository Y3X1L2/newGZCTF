# TeamLab 组网最佳实践

> 适用范围：TeamLab/组网相关设计、实现、排障与验收。
> 原则：只记录最佳实践；以真实数据面验证为准，不写猜测。

## 1. 总体设计

- 网络资源必须可复制、可清理、可恢复：以不可变执行计划快照为事实源，清理只按同一快照。
- 所有资源确定性命名：WireGuard 接口、OVN LSP、OVS iface-id、libvirt interfaceid、IP/网段使用同一稳定标识。
- 创建、修改、删除全部幂等；删除可重入、无残留。
- 控制面与数据面分离：主站只下发意图，Agent 执行本机原子操作；Agent 不读业务实体。
- 先校验后执行；失败保留真实错误，不做泛化错误包装。
- 网络配置尽量事务化（OVSDB 单事务）；跨系统变更必须有补偿/清理路径。
- IP/端口分配前检查，释放后确认，禁止地址复用冲突。
- 观测与数据面解耦：抓包/镜像/ACL 不得影响业务转发。

## 2. OVN / OVS

- 使用 OVN 逻辑交换机/路由器抽象，不手工跨节点拼 OVS flows。
- 稳定语义化命名 + external_ids；同一 UUID 贯穿 OVN LSP、OVS iface-id、libvirt interfaceid。
- OVSDB 操作：
  - 单事务提交，有界超时（如 15 秒）。
  - 响应 ID 校验，防止异步响应错配。
  - upsert/幂等写入，不重复插入同名对象。
  - 错误详情若为非字符串，不因二次解析掩盖真实错误。
- 删除顺序：先 ACL/DNS/Logical_Switch_Port/路由策略/静态路由，再 Router/Switch，最后 DHCP_Options。
- 隧道使用 Geneve（OVN 默认），保留逻辑 datapath 与逻辑端口 metadata。
- 逻辑端口必须绑定 chassis，避免跨节点漂移。
- NAT/DNAT/SNAT：NB 规则 + SB 流都必须能查询验证，并做真实流量转换测试。
- 多网关/HA：从 active-backup 起步；生产多网关再评估 active-active、BGP 或 MLAG，不默认复杂方案。
- 不依赖物理拓扑假设；OVN 逻辑网络按逻辑拓扑工作。

## 3. WireGuard

- 私钥只在节点生成/保存；权限 600；不写入日志、仓库、测试快照。
- 每个 runtime/network 独立接口名和独立 IP/端口，禁止与残留接口共用网关 IP。
- 接口生命周期绑定 runtime：
  - 创建：先建链路/IP → `ip link set up` → 再添加路由。
  - WireGuard 接口不支持 `ip link set address`，不要设置 MAC。
  - 销毁必须删除接口及 OVS 挂载，幂等重试。
- AllowedIPs 最小化，只放真实需要网段。
- PersistentKeepalive 按 NAT/隧道场景设置。
- 多 runtime/多节点使用独立路由表或策略路由，避免互相影响。
- 验证：接口 up、对端 handshake、路由可达、真实业务连通。

## 4. 链路策略（tc / netem / tbf / u32）

- 策略施加在宿主侧 veth/OVS 端口，不进入 guest。
- qdisc/filter 操作要幂等：先清理旧策略再添加，或使用 replace。
- netem：丢包/延迟/乱序；tbf：限速；clsact+u32：allow/deny ACL。
- 策略对象绑定 runtime/network；清理时恢复原始 qdisc/filter。
- 验证用真实流量（ping/iperf/curl），不只检查规则存在。
- 批量策略要可重放、可批量清理，不产生逐端口手写脚本。

## 5. 观测与抓包

- 抓包是后台任务，状态持久化在服务端，生命周期独立于前端页面。
- 优先 OVS Mirror/受控捕获口，避免在 veth 上 AF_PACKET；OVS 快速路径可能绕过 per-veth 抓包。
- 抓包进程最小权限（如 `tcpdump -Z user` / 受控 dumpcap），不使用 root 执行业务捕获。
- 大流量抓包：限时、限大小、分段、自动上传/归档，防止磁盘写满。
- 元数据（分段 SHA、大小、状态、下载引用）持久化到 DB；文件按引用管理。
- 抓包/镜像不能改变业务路径。

## 6. DHCP / DNS / 路由

- DHCP/DNS 优先使用 OVN 原生能力，避免自建冲突服务。
- 网段、网关、路由 NextHop 必须校验；空路由合法时跳过创建/校验。
- 多网段共享路由器时明确 NAT 范围；东西向 SNAT 不触发是预期行为，北向 SNAT 需按真实场景验证。
- 路由和 IP 分配要有冲突检测；释放后确认地址不再占用。

## 7. 生命周期与清理

- 创建/暂停/恢复/销毁全链路都有可重试清理路径。
- 使用不可变 plan 快照；清理不依赖当前拓扑行实时反推。
- 清理顺序：业务网络资源 → 主机 WireGuard/OVS 挂载 → DHCP/DNS → 残留确认。
- 失败不得留下孤儿资源；每类资源有 inventory 对账。
- 数据库状态是业务事实源；实际资源存在性以 Agent inventory 为准，两者必须能对齐。

## 8. 安全边界

- WireGuard 私钥、隧道密钥、Registry 凭据不落日志/文档/快照/提交。
- 管理 API 与数据面分离；Agent 只接受已认证、已校验的操作。
- 网络访问默认拒绝，显式放行；ACL/access-rule 按最小权限。
- 公网入口/网关最小暴露；管理端口不出现在业务网段。
- 所有变更可审计：actor、runtime、资源、时间、结果。

## 9. 验证门禁

- 网络能力验收必须真实链路：容器互 ping、TCP 服务访问、tcpdump 抓帧、NAT 实际转换、WireGuard 实际 handshake。
- 每个能力有自动化单测 + 真机验收记录；mock 不能作为完成依据。
- 变更后核验命令：`ip -details`、`ovn-nbctl show`、`ovs-vsctl show`、`tc -s qdisc`、`ovn-sbctl show`。
- 演练恢复场景：Agent 重启、主站重启、节点断连、销毁后资源对账。

## 10. 参考依据

- OVN General FAQ: <https://docs.ovn.org/en/stable/faq/general.html>
- OVN Architecture: <https://manpages.debian.org/bookworm/ovn-common/ovn-architecture.7.en.html>
- OVN Gateway HA: <https://docs.ovn.org/en/stable/topics/high-availability.html>
- Docker/基础设施与虚拟化实践: <https://docs.docker.com/build/building/best-practices/>
- Linux tc/netem 手册: <https://man7.org/linux/man-pages/man8/tc-netem.8.html>
- tcpdump 官方手册: <https://www.tcpdump.org/manpages/tcpdump.1.html>