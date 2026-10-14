# TeamLab ManagedStatic Agent 本地开发交接

## 任务目标与授权

将 TeamLab V2 VM 创建阶段接入按本次 MAC 应用的静态网络配置与真实回读。用户允许本地开发和子代理协作；部署前必须再次询问。此子任务没有连接或修改服务器、虚拟机、生产数据库，没有部署、推送或合并 main。

基线为 `cc05eda9`，任务分支为 `codex/teamlab-managed-network-agent`，独立 worktree 位于 `C:/Users/Cloud/.codex/worktrees/teamlab-managed-agent/newGZCTF`。为编译依赖已 cherry-pick 契约任务的 `73a5103d` 和 `bbd8d6f5`；父任务已有这些提交，只需合入本分支最后的 Agent 实现提交。`origin/main` 本次核对为 `bc3599aa`。

## 当前状态

- **IMPLEMENTED**：Agent 执行器、Linux 配置盘、Windows 统一 WMI/netsh 执行器、按 MAC 网络回读、能力上报和本地回归脚本。
- **VERIFIED**：Agent Release 构建；TeamLab/VM 定向单元测试；Linux 沙箱中的配置文件变更/恢复；Windows 本地模拟执行与脚本语法。
- **NOT_RUN**：真实 Linux VM、Windows Server 2022、Windows Server 2008 R2、QGA/libvirt/OVN 数据面验收；全量解决方案、全部单元测试、集成测试和前端门禁由父任务汇总后执行。

## 改动与不变量

`TeamLabExecutionPlanExecutor.ApplyVmAsync` 在 libvirt 启动成功后调用独立的 `TeamLabVmNetworkService`，复用 `VmGuestAgentService` 的 QGA 通道。新阶段为 `guest-ready`、`guest-network-apply`、`guest-network-verify`；仅真实回读地址、前缀、DNS、默认路由和声明静态路由匹配后才成功。已经正确的接口不重复配置。

`Dhcp=0` 沿用原执行路径，`Preconfigured=1` 不生成配置盘、不自动改来宾，`ManagedStatic=2` 才进入新网络服务。Linux ManagedStatic 即使没有 DHCP 租约也生成 NoCloud 网络盘，按 MAC 匹配；可选目标名与原 attachment 的 `ethN` 分开。配置盘仅包含网络要求，不写账号、应用或 AD 设置。

Linux 的新实例/重置 seed 使用 libvirt 稳定 UUID作为 cloud-init instance-id，其中包含运行代次。来宾初始配置没有收敛时，QGA 等待 cloud-init 完成，清理当前 MAC/当前名/声明目标名的冲突 Ethernet 定义，并写入平台自己的 Netplan 文件。其它设备定义保留。先计算完整变更，再将最初文件保存至 `/var/lib/gzctf/teamlab/network-backups/<MAC集合摘要>/`，重试不覆盖最初备份。每次 generate/apply 失败恢复本次开始前的文件，并有界尝试重新生成/应用；恢复不能确认时返回 `guest_network_rollback_failed`。

Windows 使用同一份 PowerShell 2 兼容语法脚本与 WMI/netsh 路径，不依赖 `ConvertFrom-Json`、`Get-NetAdapter` 或 NetTCPIP 模块。先按计划 MAC 校验设备唯一性和改名冲突，再幂等处理当前设备的地址、DNS、默认网关和路由。`gateway=none` 清理当前接口的旧默认网关。只移除平台记录的旧静态路由；未声明设备和它们的路由不变。路由归属记录位于来宾 `ProgramData/GZCTF/TeamLab/network.xml`。

QGA 不经过业务网络，不新增管理网卡。执行前、QGA 就绪后和回读前后重新校验 libvirt 原生 UUID及运行身份。网络命令超时/取消时，通过同一 QGA 控制通道尝试终止相应来宾进程并确认退出；不改变旧 bootstrap 命令语义。网络事件不包含完整脚本、配置、QGA 输出或凭据。

Journal 的 `AlreadyApplied` 路径同样做新网络回读，返回本次真实事件；漂移时不能返回成功，移除缓存成功记录后明确失败，后续部署重试可重新对账。原本 apply 失败的整场补偿行为保留。单资产 create/start 的具体来宾网络错误码向调用方返回。

## 组件与能力前提

宿主新能力为 `teamlab.guest-network.managed-static.v1`，需要 KVM、TeamLab V2 执行、native libvirt 与 `virsh`。它只证明 Agent 支持该执行协议，不证明某个镜像的组件已经准备。主站须要求该能力，避免旧 Agent 忽略字段并仅按 QEMU running 报成功；Linux 还需现有 `runtime.vm.cloud-init.v1` 能力以生成 seed。主站调度及运行资产更新门禁由父任务/契约任务负责。

| 来宾 | 必须准备 |
| --- | --- |
| Linux | QGA、virtio-serial 通道、cloud-init/NoCloud、Netplan、`/usr/bin/python3`/PyYAML、iproute2 的 `ip -j`、systemd-resolved/`resolvectl`、GNU timeout |
| Windows Server 2022 / 2008 R2 | QGA、virtio-serial 驱动、e1000e 驱动、内建 Windows PowerShell/WMI/netsh |

缺少 QGA、当前 MAC 设备或 Linux 网络工具均明确失败。Windows 2008 R2 的语法和本地 mock 验证不代表真实 2008 R2 镜像已经通过；尤其 WMI 路由 metric、DNS 清空和设备改名仍需实机签收。

## 验证证据

证据位于仓库外 `D:/Work/YINYU-Managed-Network-20261001/agent`，包含 TRX、固定测试网络生成的脚本、脚本生成器和本地模拟运行资料。

| 验证 | 结果 |
| --- | --- |
| `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release` | 0 警告、0 错误 |
| `dotnet test ... --filter 'FullyQualifiedName~UnitTests.TeamLab\|FullyQualifiedName~UnitTests.Vm\|FullyQualifiedName~AgentRuntimeSignalJournalTests'` | 580/580；见该目录最新 TRX |
| Windows/Python 生成脚本语法解析 | 通过；本机 Windows PowerShell 解析，不能冒充 PS2 实机 |
| Linux 沙箱脚本执行 | 5/5；旧目标名无 MAC 配置、其它设备保留、generate/apply 失败恢复、重试、最初备份保护、失败更新恢复上次成功计划 |
| Windows WMI/netsh 本地模拟 | MAC 选卡、静态 IPv4、无网关/空 DNS、改名、静态路由、XML回读、重复执行通过 |
| `git diff --check` | 随最终实现提交执行 |

Linux 脚本执行回归入口为 `scripts/validation/teamlab/test-managed-linux-network-script.py --script <生成的linux-apply.py>`。Windows 入口为 `scripts/validation/teamlab/test-managed-windows-network-script.ps1 -ApplyScript <生成的windows-apply.ps1> -ReadScript <生成的windows-read.ps1> -StateRoot <本地临时目录>`。Windows 固定测试要求脚本包含示例 MAC、`field0`、无 DNS/网关、`172.16.0.0/16` via `10.96.1.1` metric 25；WMI/netsh 全部模拟，替换不匹配时拒绝执行。

单元构建存在既有 NU1902（Microsoft.Build.Tasks.Git 8.0.0）与历史测试分析器警告，Agent 自身构建无新增警告。没有执行真实来宾、完整集成、生产迁移或部署。数据库模型与共享资产字段由契约任务负责，前端由 UI 任务负责。

## 提交与后续

本记录与 Agent 实现同一提交。最终 SHA 使用 `git log -1 codex/teamlab-managed-network-agent` 核对；未推送、未合并 main，分支与 worktree 保留供父任务审阅和合入。

下一步在父任务汇总契约、UI、OVN DHCP 端口策略和节点能力门禁后执行完整本地门禁；得到用户部署批准后，按独立 release、备份/回退和隔离实例流程验收 Linux、2022 和 2008 R2。不得对已有教学 VM直接试运行网络脚本，不以 ARP 代答或 libvirt running 代替真实来宾网络结果。

## 2026-10-02 本地评审补充

Windows 回读脚本现在仅输出 IPv4 DNS；宿主 XML 解析原有的 IPv4 过滤继续保留。新增空/非空 IPv4 目标并存 `fec0`、`2001:db8` DNS 的回读用例，本地 Windows WMI 模拟也验证脚本出口没有输出 IPv6 DNS。

WMI 返回值 0 和 1 均作为调用成功，1 表示需要重启；平台不自动重启，仍由独立网络回读判断是否已经收敛。Windows 模拟分别以 0、1 执行应用、回读与幂等回归通过；来宾应用返回成功但实际网络未收敛的现有定向测试仍必须失败。仅本地修改与验证，真实来宾及部署仍未执行。

本次 Managed VM 网络定向单元测试 38/38，Windows 三份脚本语法解析通过，`git diff --check` 通过。完整门禁由父任务整合后执行。
