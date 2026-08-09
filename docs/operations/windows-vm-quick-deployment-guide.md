# 比赛 Windows VM 配置与验收速查

> 适用范围：普通 CTF 比赛的 Windows QCOW2 靶机。
>
> 不适用于：培训 Windows VM、TeamLab Guest Supervisor、每实例动态账号。

## 1. 当前运行契约

普通比赛直接使用镜像中已有的固定 RDP 账号：

```text
Ready Windows QCOW2
  -> 镜像远程访问配置（ExistingAccount / RDP）
  -> KVM Worker 下载并启动镜像
  -> Agent 等待 DHCP 和目标 RDP 端口就绪
  -> Agent 发布内网 RDP 代理
  -> 平台提供 Guacamole 与 mstsc 两种入口
```

普通比赛不再生成实例随机密码，不挂载 `CIDATA`，不下发 Cloudbase-Init user-data，也不要求
`SupportsInstanceCredentials` 或 `runtime.vm.cloud-init.v1`。

## 2. 镜像内必须完成的配置

在发布镜像中直接配置账号。下面仅为示例，正式密码由镜像制作人员交付给管理员，不要写入文档或 Git：

```powershell
$Username = 'player'
$Password = Read-Host 'RDP password' -AsSecureString

$User = Get-LocalUser -Name $Username -ErrorAction SilentlyContinue
if ($null -eq $User) {
  New-LocalUser -Name $Username -Password $Password -PasswordNeverExpires -AccountNeverExpires
} else {
  Set-LocalUser -Name $Username -Password $Password -PasswordNeverExpires $true
}

$RdpGroup = Get-LocalGroup -SID 'S-1-5-32-555'
Add-LocalGroupMember -Group $RdpGroup -Member $Username -ErrorAction SilentlyContinue
Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
Set-Service TermService -StartupType Automatic
Start-Service TermService
Get-NetFirewallRule -Name 'RemoteDesktop-UserMode-In-*' -ErrorAction SilentlyContinue | Enable-NetFirewallRule
```

发布前检查：

```powershell
Get-LocalUser player
Get-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-555')
Get-Service TermService
Get-NetTCPConnection -LocalPort 3389 -State Listen
Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' fDenyTSConnections
slmgr /xpr
```

必须满足：

- `player` 已启用并位于远程桌面用户组；
- `TermService` 自动启动；
- RDP 端口监听且防火墙允许；
- 首次启动不进入 OOBE，不要求人工设置 Administrator 密码；
- DHCP 可获取地址；
- Windows 许可或 Evaluation 有效期覆盖比赛周期；
- 关机前没有挂载安装 ISO、测试共享目录或制作人员凭据。

Cloudbase-Init 不是普通比赛镜像的必需组件。镜像中已有且不会阻塞启动时可以保留；新镜像无需安装。

## 3. 导出与传输

在 KVM 制作机生成发布文件：

```bash
qemu-img convert -p -O qcow2 -c win2022-builder.qcow2 win2022-rdp-fixed-v1.qcow2
qemu-img info --output=json win2022-rdp-fixed-v1.qcow2
qemu-img check win2022-rdp-fixed-v1.qcow2
sha256sum win2022-rdp-fixed-v1.qcow2 | tee win2022-rdp-fixed-v1.sha256
```

6 GiB 级镜像优先走内网并支持续传：

```bash
rsync -avh --partial --append-verify --info=progress2 \
  win2022-rdp-fixed-v1.qcow2 \
  <USER>@<PLATFORM_HOST>:/var/lib/gzctf/images/incoming/
```

上传前后必须核对 SHA-256。不要把公网浏览器、FRP 或多层代理作为大镜像的唯一传输方式。

## 4. 平台配置

1. 打开“管理 -> 镜像”。
2. 使用“本地导入”选择服务器上的 QCOW2。
3. 等待模板状态变为 `Ready`。
4. 确认系统为 `Windows`，类型为 `Qcow2`，哈希和大小正确。
5. 打开该镜像的“远程访问”。
6. 启用远程访问，协议选择 `RDP`。
7. 账号来源选择“使用镜像已有账号（普通比赛）”。
8. 填写镜像内真实端口、用户名和密码并保存。
9. 等待镜像分发到目标 KVM Worker。
10. 在比赛题目管理中选择 Windows VM 和该镜像。

更换镜像内密码后，必须同步更新镜像远程访问配置。平台不会修改已启动或待启动镜像中的账号。

## 5. 节点要求

目标节点必须：

- 在线且可调度；
- `KvmCapacity > 0` 且有可用 VM 槽位；
- 报告 `runtime.kvm.v1` 与 `image.vm.download.v1`；
- 能从镜像存储下载并校验 QCOW2；
- 配置 OVMF、q35、SATA、e1000e 和 libvirt default DHCP 网络；
- `Kvm:RdpProxyAllowedSources` 允许平台、Guacamole 和实际内网/代理出口访问；
- 代理端口范围 `46000-55999` 未被防火墙阻断。

普通比赛不要求节点报告 Cloud-Init capability。

## 6. 双实例验收

至少使用两个不同选手账号同时创建实例 A、B：

1. 两个请求均进入统一部署队列并分配到可用 KVM Worker。
2. 页面从“正在准备”进入“运行中”，期间不能仅因获取 DHCP 就提前就绪。
3. A、B 具有不同 VM、overlay、NVRAM、运行身份和 Agent 代理端口。
4. 使用页面显示的固定凭据分别打开 Guacamole。
5. 下载 `.rdp`，使用内网 `mstsc` 登录 A、B。
6. 验证 `mstsc` 双向文本剪贴板。
7. 确认 A 的入口不会连接到 B，反之亦然。
8. 销毁 A、B，核对 VM、overlay、NVRAM、代理、Guacamole 和节点容量均已回收。
9. 确认公网未直接开放 Worker RDP 代理端口。

同一镜像的 A、B 使用同一组固定账号密码，这是本轮设计，不是验收失败。

## 7. 故障定位

| 现象 | 首要检查 |
| --- | --- |
| `No schedulable node... VM=1` | Worker 在线状态、KVM/VM download capability、VM capacity |
| 镜像一直准备 | 模板分发任务、Worker 磁盘、存储下载和 SHA-256 |
| VM 启动后无 DHCP | `virsh domiflist`、default DHCP lease、e1000e 驱动 |
| 有 IP 但页面一直准备 | Windows RDP 端口、TermService、防火墙、镜像配置端口 |
| Guacamole 失败但 mstsc 可用 | Guacamole API、连接记录及 Guacamole 到 Worker 代理网络 |
| mstsc 地址无法访问 | Agent 代理监听、AllowedSources、客户端内网/Proxifier 出口 |
| 账号密码错误 | 镜像内真实凭据与远程访问配置是否一致 |
| 销毁后容量未恢复 | 部署票据、VM 状态、Agent domain/overlay 和节点 CurrentVms |

详细平台链路与生产检查见 `docs/operations/windows-vm-deployment-guide.md`。
