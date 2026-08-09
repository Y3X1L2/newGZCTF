# Windows VM 镜像制作、部署与故障排查指南

测试人员和镜像制作人员请优先使用简明版：`docs/operations/windows-vm-quick-deployment-guide.md`。本文仅用于了解完整原理和处理复杂故障。

## 1. 文档目的

本文面向 Windows 靶机镜像制作人员、平台管理员、KVM 节点运维人员和测试人员，说明如何把一份 Windows QCOW2 镜像可靠交付为可由 GZCTF 自动创建、自动注入独立凭据并通过 Guacamole 访问的 Windows VM。

本文覆盖以下完整链路：

1. 制作 Windows 基础镜像；
2. 安装和配置驱动、Cloudbase-Init、RDP；
3. 处理 OOBE 和 Sysprep；
4. 使用两个全新实例离线验收；
5. 传输、上传和注册 QCOW2；
6. 认证实例凭据能力并绑定比赛或培训题目；
7. 验收节点调度、DHCP、RDP 和 Guacamole；
8. 定位创建失败、启动后关机、无 IP、无 RDP 等故障；
9. 销毁、重建和回滚。

配套文档：

- `docs/operations/windows-cloudbase-init-image.md`：Cloudbase-Init 镜像和实例凭据安全契约；
- `docs/operations/windows-vm-instance-credentials-deployment-acceptance.md`：平台升级、数据库迁移和上线验收；
- `docs/node-deployment/README.md`：节点部署和镜像目录规划。

## 2. 本次故障结论

### 2.1 结论

本次 Windows VM 卡住不是单一故障。镜像侧是主要原因，Agent 和平台侧也存在真实问题。

| 层级 | 本次发现 | 影响 | 结论 |
| --- | --- | --- | --- |
| Windows 镜像 | 首次启动停在地区、许可和 Administrator 密码 OOBE | 无人值守交付中断 | 主要原因 |
| Cloudbase-Init | 没有正确消费 `CIDATA`，平台 `#ps1_sysnative` 未自动执行 | `player`、独立密码、RDP 和防火墙未自动配置 | 主要原因 |
| 镜像认证 | 未通过双实例测试却被标记 `SupportsInstanceCredentials=true` | 平台误信任镜像能力 | 流程缺陷 |
| Agent 虚拟硬件 | 原配置为 SeaBIOS、i440fx、IDE；镜像需要 UEFI、q35、SATA | 镜像不能稳定启动 | 平台侧问题，已修复代码并部署到实测节点 |
| libvirt 重启策略 | 原 XML 为 `<on_reboot>destroy</on_reboot>` | Windows 首启重启后 VM 被关机 | 平台侧问题，已改为 `restart` |
| 状态恢复 | 首次失败后数据库为 `Error`；人工恢复 VM 后不会自动回到 `Running` | Guacamole 就绪服务不再处理该实例 | 平台侧恢复能力缺口 |
| 大文件分发 | 浏览器经 FRP、Proxifier 上传 6 GiB 级文件容易断线 | 上传失败或需要重传 | 传输路径问题，不等于 QCOW2 损坏 |

### 2.2 为什么确认镜像封装不完整

人工进入 Windows 完成 `player`、RDP 组和 RDP 服务配置后，以下检查均通过：

~~~powershell
Get-LocalUser player
Get-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-555')
Get-NetTCPConnection -LocalPort 3389 -State Listen
~~~

最终事实：

- `player` 已启用；
- `player` 已加入 SID `S-1-5-32-555` 对应的“远程桌面用户”组；
- `0.0.0.0:3389` 和 `[::]:3389` 均处于监听；
- Guacamole 可以登录并看到 Windows Server 2022 桌面。

这证明 Windows 内核、网卡、RDP 服务和 Guacamole 链路可以工作。真正缺失的是“首次启动时自动完成这些配置”的镜像初始化能力。

### 2.3 本次平台侧修复

Windows VM 的 Agent 创建参数已经调整为：

~~~text
--machine q35
--boot uefi
--events on_reboot=restart
--disk <overlay>,bus=sata
--network network=default,model=e1000e
~~~

对应的关键 libvirt 行为是：

~~~xml
<on_reboot>restart</on_reboot>
~~~

该修复不能替代正确制镜。平台能够启动磁盘，不代表 Windows 已自动完成 OOBE、实例凭据注入和 RDP 配置。

### 2.4 当前完成边界

| 项目 | 当前状态 |
| --- | --- |
| Windows UEFI/q35/SATA 启动参数 | 代码已修复，实测节点已验证 |
| Windows 首启重启策略 | 已改为 `on_reboot=restart`，实测通过 |
| Agent 侧专项自动化测试 | 14/14 通过 |
| 本次 VM 的 DHCP、RDP 代理和 Guacamole | 已人工实测通过 |
| 候选 QCOW2 的无人值守 Cloudbase-Init | 未通过，必须重新封装 |
| `Error` 实例与真实运行状态自动对账 | 尚未闭环，本次采用核对 identity 后的受控恢复 |
| Registry trust 并发更新串行化 | 本地补丁已形成，主站和所有节点仍需按同一版本部署验证 |

因此，当前结论是“平台已能运行并交付本次人工修复后的 VM”，不是“该 QCOW2 已成为可批量发布的合格模板”。

## 3. Windows VM 全链路

~~~text
Windows ISO
  -> 安装 Windows、驱动和 Cloudbase-Init
  -> 配置 NoCloud ConfigDrive 和 PowerShell user-data
  -> 使用版本匹配的 Unattend.xml 执行 Sysprep
  -> 关机并生成只读发布 QCOW2
  -> 实例 A/B 离线注入验收
  -> 计算并登记 SHA-256
  -> 内网传输到平台允许的导入目录
  -> 注册为 Windows/Qcow2/Ready
  -> 人工认证 SupportsInstanceCredentials
  -> 绑定比赛或培训题目
  -> 调度到具备 KVM/Cloud-Init 能力的 Agent
  -> Agent 建立 overlay 和 CIDATA
  -> UEFI/q35/SATA/e1000e 启动
  -> Windows 获得 DHCP
  -> Cloudbase-Init 创建 player 并启用 RDP
  -> Agent 建立 RDP TCP 代理
  -> 平台创建 Guacamole 连接
  -> 用户登录
~~~

## 4. 不可混淆的状态

Windows VM 至少有八层状态。任意一层成功都不能代替下一层。

| 层级 | 成功标准 | 常用证据 |
| --- | --- | --- |
| 镜像文件完整 | QCOW2 可读且哈希一致 | `qemu-img check`、SHA-256 |
| 镜像能力合格 | OOBE 不阻塞，双实例凭据注入通过 | A/B overlay 验收记录 |
| 平台模板就绪 | `Windows + Qcow2 + Ready` | 镜像详情/API |
| 节点可调度 | KVM、VM 容量和 Agent capability 满足 | 节点页、Agent manifest |
| VM 运行 | libvirt 域为 `running` | `virsh domstate` |
| 客户机有网络 | Windows 获得预期 DHCP 地址 | dnsmasq lease、Agent inventory |
| RDP 就绪 | 3389 监听且从 Agent 可连接 | `Test-NetConnection`、`nc` |
| Guacamole 可用 | 连接已创建且用户能看到桌面 | 平台入口和实际登录截图 |

平台页面只有在最后一层通过后，才应向用户宣称“环境已就绪”。

## 5. 制作前约定

### 5.1 发布记录

每次制镜先创建发布记录：

~~~text
镜像名称:
Windows 版本/Build:
架构:
制作日期:
制作人员:
Cloudbase-Init 版本:
VirtIO 驱动版本:
QEMU Guest Agent 版本:
固件: UEFI
芯片组: q35
系统盘总线: SATA
网卡模型: e1000e
虚拟磁盘容量:
QCOW2 实际文件大小:
SHA-256:
离线验收 A:
离线验收 B:
平台模板 ID:
绑定题目:
~~~

不要使用 `latest.qcow2` 作为长期发布名称。建议使用版本号或日期：

~~~text
win2022-cloudbase-2026.07-v1.qcow2
win2022-cloudbase-2026.07-v1.qcow2.sha256
~~~

### 5.2 制作机要求

建议使用独立 Ubuntu KVM 制作机：

~~~bash
sudo apt-get update
sudo apt-get install -y qemu-kvm qemu-utils libvirt-daemon-system libvirt-clients virtinst ovmf genisoimage
sudo virt-host-validate
sudo virsh list --all
test -e /usr/share/OVMF/OVMF_CODE.fd || find /usr/share -iname 'OVMF_CODE*.fd'
~~~

准备：

- 微软官方 Windows Server 2022/2025 ISO；
- 对应架构的 VirtIO 驱动 ISO；
- Cloudbase-Init 官方签名 x64 MSI；
- 80 GiB 以上临时磁盘；
- 可用的 VNC 或 SPICE 管理通道；
- 与生产 Agent 相同或兼容的 QEMU/libvirt 版本。

### 5.3 运行时硬件契约

| 项目 | 当前兼容基线 |
| --- | --- |
| 架构 | x86_64 |
| 固件 | UEFI/OVMF |
| Machine | q35 |
| 系统盘 | QCOW2，SATA |
| 网卡 | e1000e |
| 首启重启 | restart |
| 元数据盘 | NoCloud ConfigDrive，卷标 `CIDATA` |
| 远程访问 | RDP，经 Agent TCP 代理和 Guacamole |

镜像制作和离线验收必须使用这组参数。不要只在 VMware、Hyper-V 或 SeaBIOS/i440fx 下测试后直接上传。

## 6. 制作 Windows 基础镜像

### 6.1 创建制作盘

~~~bash
export BUILD_DIR=/srv/windows-image-build/win2022-v1
export BUILDER_DISK="$BUILD_DIR/win2022-builder.qcow2"
export WINDOWS_ISO=/srv/iso/windows-server-2022.iso
export VIRTIO_ISO=/srv/iso/virtio-win.iso

sudo mkdir -p "$BUILD_DIR"
sudo chown "$USER":"$USER" "$BUILD_DIR"
qemu-img create -f qcow2 "$BUILDER_DISK" 80G
~~~

### 6.2 使用生产兼容硬件安装

~~~bash
sudo virt-install \
  --name win2022-builder \
  --memory 8192 \
  --vcpus 4 \
  --cpu host-passthrough \
  --machine q35 \
  --boot uefi \
  --events on_reboot=restart \
  --disk path="$BUILDER_DISK",format=qcow2,bus=sata \
  --disk path="$VIRTIO_ISO",device=cdrom \
  --cdrom "$WINDOWS_ISO" \
  --network network=default,model=e1000e \
  --graphics vnc,listen=127.0.0.1 \
  --osinfo detect=on,require=off
~~~

安装 Windows Server Desktop Experience。分区和版本选择应写入发布记录。

### 6.3 驱动和 Guest Agent

进入 Windows 后，从 VirtIO ISO 安装：

1. VirtIO 存储驱动；
2. VirtIO 网卡驱动；
3. Balloon 驱动；
4. QEMU Guest Agent。

即使当前运行时使用 SATA 和 e1000e，也应安装完整 VirtIO 驱动，便于后续迁移。

~~~powershell
Get-Service QEMU-GA -ErrorAction SilentlyContinue
Get-PnpDevice | Where-Object Status -ne 'OK'
~~~

### 6.4 系统更新和题目软件

在 Sysprep 前完成 Windows 累积更新、题目服务和工具安装、安全策略、空间清理和题目功能自测。

不要在基础镜像中保存平台密码、最终 `player` 密码、IAM/Registry Token、SSH 私钥、浏览器 Cookie、制作者个人文件或带真实凭据的日志。

## 7. 配置 Cloudbase-Init

### 7.1 安装

下载官方签名版本。安装前验证：

~~~powershell
$Msi = 'C:\Install\CloudbaseInitSetup_x64.msi'
Get-AuthenticodeSignature $Msi
Get-FileHash $Msi -Algorithm SHA256
~~~

签名状态必须为 `Valid`。

~~~powershell
Start-Process msiexec.exe -Wait -ArgumentList @('/i','C:\Install\CloudbaseInitSetup_x64.msi','/qn','/norestart','/l*v','C:\Install\cloudbase-init-install.log')
sc.exe qc cloudbase-init
Get-Service cloudbase-init
~~~

服务运行账户必须是 `LocalSystem`。

### 7.2 主配置

文件通常位于：

~~~text
C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\cloudbase-init.conf
~~~

至少需要等价于以下能力：

~~~ini
[DEFAULT]
metadata_services=cloudbaseinit.metadata.services.nocloudservice.NoCloudConfigDriveService
plugins=cloudbaseinit.plugins.common.sethostname.SetHostNamePlugin,cloudbaseinit.plugins.common.userdata.UserDataPlugin
config_drive_cdrom=true
config_drive_raw_hhd=true
process_userdata=true
allow_reboot=false
~~~

插件路径会随版本变化。最终以 Cloudbase-Init 日志和双实例测试为准。

### 7.3 Unattend 配置

安装目录通常还包含：

~~~text
C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\cloudbase-init-unattend.conf
C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\Unattend.xml
~~~

~~~powershell
$Conf = 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf'
Test-Path "$Conf\cloudbase-init.conf"
Test-Path "$Conf\cloudbase-init-unattend.conf"
Test-Path "$Conf\Unattend.xml"
~~~

三项应全部返回 `True`。如果没有 `Unattend.xml`，应使用该 Cloudbase-Init 版本官方提供、与当前 Windows Build 匹配的应答文件，隔离验证后再继续。不要复制未知来源的 XML。

仅运行 `sysprep /generalize /oobe` 不代表已经实现无人值守。必须证明：

1. 不停在地区或语言选择；
2. 不停在许可协议；
3. 不要求人工设置 Administrator 密码；
4. Cloudbase-Init 会读取 `CIDATA`；
5. `#ps1_sysnative` user-data 会执行。

~~~powershell
Set-Service cloudbase-init -StartupType Automatic
Get-Service cloudbase-init
Get-Content "$Conf\cloudbase-init.conf"
Get-Content "$Conf\cloudbase-init-unattend.conf"
~~~

此时不要提前手工创建最终 `player` 账户。该账户必须由每个实例的 user-data 创建或更新。

## 8. RDP 和实例用户契约

平台为每个 VM 生成独立随机密码，并通过 Cloudbase-Init user-data：

1. 创建或更新本地 `player`；
2. 设置实例独立密码；
3. 将其加入 SID `S-1-5-32-555` 对应的远程桌面组；
4. 设置 `fDenyTSConnections=0`；
5. 启动 RDP 服务；
6. 启用或创建 TCP 3389 防火墙规则。

镜像内不应预置所有实例共享的 `player` 密码。测试时使用 SID 查找组，避免中英文系统组名不同：

~~~powershell
$RdpGroup = Get-LocalGroup -SID 'S-1-5-32-555'
Get-LocalGroupMember -Group $RdpGroup
~~~

## 9. 清理、Sysprep 和发布

### 9.1 清理

~~~powershell
Stop-Service cloudbase-init -ErrorAction SilentlyContinue
Remove-Item 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\log\*' -Force -ErrorAction SilentlyContinue
Remove-Item 'C:\Windows\Temp\*' -Recurse -Force -ErrorAction SilentlyContinue
Clear-RecycleBin -Force -ErrorAction SilentlyContinue
~~~

确认主配置和 Unattend 仍存在、没有挂载旧 `CIDATA`、没有测试密码、系统时间正确、磁盘空间充足。

### 9.2 执行 Sysprep

~~~powershell
$Unattend = 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\Unattend.xml'
$QuotedUnattend = '"{0}"' -f $Unattend
$Args = @('/generalize','/oobe','/shutdown','/mode:vm',"/unattend:$QuotedUnattend")
Start-Process "$env:WINDIR\System32\Sysprep\Sysprep.exe" -ArgumentList $Args -Wait
~~~

必须等待 VM 完全关机。Sysprep 关机后不得再次启动发布基盘；需要检查时只能创建临时 overlay。误启动基盘后必须重新清理和 Sysprep。

### 9.3 生成发布镜像

~~~bash
export RELEASE="$BUILD_DIR/win2022-cloudbase-2026.07-v1.qcow2"

sudo virsh undefine win2022-builder --nvram 2>/dev/null || sudo virsh undefine win2022-builder
qemu-img convert -p -O qcow2 -c "$BUILDER_DISK" "$RELEASE"
qemu-img info --output=json "$RELEASE"
qemu-img check "$RELEASE"
sha256sum "$RELEASE" | tee "$RELEASE.sha256"
~~~

不要对发布文件执行带修复参数的 `qemu-img check -r`。发现损坏时应回到制作盘重新导出。

## 10. 离线双实例验收

### 10.1 为什么必须做两次

一次成功不能证明镜像没有保存第一次实例状态、第二个实例会消费新 metadata、旧密码不能登录新实例，或 Cloudbase-Init 不是只在制镜时偶然运行一次。因此必须从同一只读基盘创建实例 A 和 B。

### 10.2 创建 overlay

~~~bash
export ACCEPT=/srv/windows-image-acceptance/win2022-v1
export BASE=/srv/windows-image-build/win2022-v1/win2022-cloudbase-2026.07-v1.qcow2

mkdir -p "$ACCEPT/a" "$ACCEPT/b"
qemu-img create -f qcow2 -F qcow2 -b "$BASE" "$ACCEPT/a/root.qcow2"
qemu-img create -f qcow2 -F qcow2 -b "$BASE" "$ACCEPT/b/root.qcow2"
~~~

### 10.3 准备 CIDATA

实例 A 的 `meta-data`：

~~~yaml
instance-id: gzctf-win-acceptance-a
local-hostname: gzctf-win-a
~~~

实例 A 的 `user-data`：

~~~powershell
#ps1_sysnative
$ErrorActionPreference = 'Stop'
$Username = 'player'
$PlainPassword = 'Acceptance-A-Replace-With-Random!'
$Password = ConvertTo-SecureString $PlainPassword -AsPlainText -Force
$Existing = Get-LocalUser -Name $Username -ErrorAction SilentlyContinue
if ($null -eq $Existing) {
  New-LocalUser -Name $Username -Password $Password -PasswordNeverExpires -AccountNeverExpires | Out-Null
} else {
  Set-LocalUser -Name $Username -Password $Password -PasswordNeverExpires $true
}
$RdpGroup = Get-LocalGroup -SID 'S-1-5-32-555'
Add-LocalGroupMember -Group $RdpGroup -Member $Username -ErrorAction SilentlyContinue
Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
Set-Service TermService -StartupType Automatic
Start-Service TermService
$Rules = Get-NetFirewallRule -Name 'RemoteDesktop-UserMode-In-*' -ErrorAction SilentlyContinue
if ($Rules) {
  $Rules | Enable-NetFirewallRule
} elseif (-not (Get-NetFirewallRule -Name 'GZCTF-RDP-In-TCP' -ErrorAction SilentlyContinue)) {
  New-NetFirewallRule -Name 'GZCTF-RDP-In-TCP' -DisplayName 'GZCTF RDP TCP 3389' -Direction Inbound -Protocol TCP -LocalPort 3389 -Action Allow | Out-Null
}
~~~

测试密码只能用于隔离验收，验收后必须删除 seed 和 overlay。实例 B 使用不同的随机密码。

生成 seed：

~~~bash
touch "$ACCEPT/a/network-config"
genisoimage \
  -output "$ACCEPT/a/seed.iso" \
  -volid CIDATA -joliet -rock -graft-points \
  user-data="$ACCEPT/a/user-data" \
  meta-data="$ACCEPT/a/meta-data" \
  network-config="$ACCEPT/a/network-config"
~~~

### 10.4 使用生产硬件契约启动

~~~bash
sudo virt-install \
  --name gzctf-win-accept-a \
  --memory 4096 \
  --vcpus 2 \
  --machine q35 \
  --boot uefi \
  --events on_reboot=restart \
  --disk path="$ACCEPT/a/root.qcow2",format=qcow2,bus=sata \
  --disk path="$ACCEPT/a/seed.iso",device=cdrom \
  --network network=default,model=e1000e \
  --graphics vnc,listen=127.0.0.1 \
  --import --noautoconsole \
  --osinfo detect=on,require=off
~~~

### 10.5 实例内检查

通过 VNC 观察首启过程。出现任何地区、许可或密码 OOBE 都判定失败。

~~~powershell
Get-Service cloudbase-init
Get-LocalUser player
Get-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-555')
Get-NetTCPConnection -LocalPort 3389 -State Listen
Get-Content 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\log\cloudbase-init.log' -Tail 200
~~~

Cloudbase-Init 日志必须证明：

- 找到 NoCloud ConfigDrive；
- 读取到本实例 `instance-id`；
- 执行了 PowerShell user-data；
- 脚本返回成功；
- 日志不包含生产密码；
- 没有沿用旧实例 metadata。

### 10.6 主机侧检查

~~~bash
sudo virsh domstate gzctf-win-accept-a
sudo virsh domiflist gzctf-win-accept-a
sudo virsh net-dhcp-leases default
nc -vz <WINDOWS_DHCP_IP> 3389
~~~

Windows 默认可能不响应 ICMP。`ping` 失败不能单独判定 VM 失败，应优先检查 DHCP 和 TCP 3389。

### 10.7 A/B 验收标准

1. A 不出现 OOBE；
2. A 使用密码 A 可以登录；
3. A 重启后仍为 `running`，密码 A 仍可登录；
4. 销毁 A；
5. B 从同一基盘的新 overlay 启动；
6. B 不出现 OOBE；
7. 密码 A 不能登录 B；
8. 密码 B 可以登录 B；
9. A/B 的 Cloudbase-Init `instance-id` 不同；
10. 销毁后 seed、overlay、NVRAM 和 libvirt 域均被清理。

只有十项全部通过，才允许认证实例凭据能力。

## 11. 大文件传输

### 11.1 推荐路径

6 GiB 以上 QCOW2 不建议通过公网浏览器、FRP、Proxifier 或多层代理上传。推荐：

1. 制作机与平台导入目录在同一内网时使用 `rsync`；
2. 先传到平台允许的本地导入目录，再调用“本地导入”；
3. 使用支持续传和传后哈希核对的工具；
4. 保留源文件，导入成功前不删除。

~~~bash
rsync -avh --partial --append-verify --info=progress2 \
  win2022-cloudbase-2026.07-v1.qcow2 \
  <IMPORT_USER>@<PLATFORM_OR_STORAGE_HOST>:/var/lib/gzctf/images/incoming/
~~~

传输前后分别执行：

~~~bash
sha256sum win2022-cloudbase-2026.07-v1.qcow2
sha256sum /var/lib/gzctf/images/incoming/win2022-cloudbase-2026.07-v1.qcow2
~~~

两端摘要必须完全一致。

### 11.2 浏览器上传的定位

平台接口支持大型 VM 文件，但浏览器上传仍受反向代理超时、FRP 链路、客户端代理、连接抖动和磁盘空间影响。浏览器上传适合小文件和稳定内网，不应作为大型生产镜像的唯一发布方式。

如果必须使用浏览器：

- 检查 Nginx/FRP 请求体大小和读写超时；
- 检查平台临时目录与镜像目录空间；
- 上传期间不要切换网络；
- 上传完成后核对平台记录的大小和 SHA-256；
- 断线后不要假设服务器已有完整文件。

本次传输不稳定不能证明 `xray_tun` 是唯一原因；多层转发使长连接更脆弱，关闭其中一层只能减少变量。

## 12. 平台上传、注册与认证

### 12.1 管理页面流程

1. 使用教师、管理员或超级管理员登录；
2. 打开“管理 -> 镜像”；
3. 选择“上传 VM”或“本地导入”；
4. 填写可识别的名称，名称应包含 `windows`、`winserver` 或明确版本；
5. 等待模板状态为 `Ready`；
6. 核对系统为 `Windows`、类型为 `Qcow2`；
7. 核对文件大小和 SHA-256；
8. 完成第 10 节 A/B 验收后，再点击“认证实例凭据/Cloudbase-Init”；
9. 确认 `SupportsInstanceCredentials=true`；
10. 等待镜像分发到目标 KVM 节点。

系统类型当前可能根据文件名推断。若名称不含 Windows 特征而被识别为 Linux，不要继续绑定题目，应先纠正模板数据或重新导入。

### 12.2 API 参考

直接上传 QCOW2：

~~~bash
curl -X POST '<PLATFORM_URL>/api/v1/image-templates' \
  -H 'Cookie: GZCTF_Token=<SESSION_TOKEN>' \
  -F 'file=@win2022-cloudbase-2026.07-v1.qcow2'
~~~

从服务器允许目录导入：

~~~bash
curl -X POST '<PLATFORM_URL>/api/v1/image-templates/import-local' \
  -H 'Cookie: GZCTF_Token=<SESSION_TOKEN>' \
  -H 'Content-Type: application/json' \
  -d '{
    "localPath":"/var/lib/gzctf/images/incoming/win2022-cloudbase-2026.07-v1.qcow2",
    "displayName":"Windows Server 2022 Cloudbase 2026.07 v1"
  }'
~~~

认证能力：

~~~bash
curl -X PATCH '<PLATFORM_URL>/api/v1/image-templates/<TEMPLATE_ID>/instance-credentials' \
  -H 'Cookie: GZCTF_Token=<SESSION_TOKEN>' \
  -H 'Content-Type: application/json' \
  -d '{"supported":true}'
~~~

该 PATCH 只是记录人工验收结论，不会自动修复镜像。禁止在没有 A/B 实机证据时调用。

## 13. 节点部署检查

### 13.1 Agent capability

目标节点必须报告：

~~~text
runtime.kvm.v1
runtime.vm.cloud-init.v1
image.vm.download.v1
~~~

### 13.2 主机依赖

~~~bash
sudo apt-get install -y qemu-kvm qemu-utils libvirt-daemon-system libvirt-clients virtinst ovmf genisoimage
sudo systemctl status libvirtd
sudo virsh list --all
sudo virsh net-list --all
sudo virt-host-validate
~~~

### 13.3 容量

节点至少需要：

- `KvmCapacity > 0`；
- 足够的可用 VM 槽位、内存和 CPU；
- 镜像基盘加每个实例 overlay 的磁盘空间；
- 可读取 OVMF 固件；
- 到镜像存储、平台和 Guacamole 的网络。

“`No schedulable node has enough capacity for Docker=0, VM=1`”表示调度层没有找到有空闲 VM 容量且在线、能力匹配的节点，不是 Windows 内部故障。

### 13.4 镜像分发

~~~bash
ls -lh /var/lib/gzctf/images
sha256sum /var/lib/gzctf/images/<DISTRIBUTED_IMAGE>
qemu-img info /var/lib/gzctf/images/<DISTRIBUTED_IMAGE>
qemu-img check /var/lib/gzctf/images/<DISTRIBUTED_IMAGE>
~~~

镜像分发和 Docker Registry trust 配置并发时，不应由多个任务同时重启 Docker。发现 `docker.service: start-limit-hit` 时，应先恢复 Docker，再检查平台/Agent 是否已部署串行化配置补丁。

## 14. 绑定题目

### 14.1 比赛题目

1. 打开“管理 -> 比赛 -> 题目管理”；
2. 新建或编辑题目；
3. 题目类型选择 Windows VM；
4. 在环境模板中选择已验收的新模板；
5. 设置 CPU、内存、实例时长和 Flag；
6. 保存；
7. 使用“测试实例”先验证；
8. 再由普通参赛用户从比赛页面创建。

更换模板后，应确认题目没有继续引用旧模板 ID。

### 14.2 培训题目

1. 打开课程详情；
2. 在课程隔离的环境模板中确认目标 Windows 镜像可见；
3. 创建或编辑课程实例题；
4. 绑定该模板；
5. 将实例题挂到章节；
6. 使用教师测试入口创建；
7. 再使用学员账号创建。

比赛和培训各做一次，是为了验证业务入口、权限和状态投影，而不是重复验证 Windows 本身。

## 15. 启动验收

### 15.1 平台侧

按顺序记录：

1. 创建请求返回成功；
2. 部署任务进入队列；
3. 分配到预期 KVM 节点；
4. 模板分发状态为可用；
5. `VmInstance` 从 `Creating` 进入 `Running`；
6. 记录 VM name、generation、native ID；
7. 获得客户机 IP；
8. 生成 Guacamole connection ID；
9. 页面出现可访问入口。

### 15.2 Agent/libvirt

~~~bash
sudo virsh list --all
sudo virsh domstate <VM_NAME>
sudo virsh domuuid <VM_NAME>
sudo virsh dumpxml <VM_NAME> > /tmp/<VM_NAME>.xml
sudo virsh domiflist <VM_NAME>
sudo virsh net-dhcp-leases default
~~~

XML 应确认 UEFI loader/NVRAM、q35、SATA、e1000e、`on_reboot=restart`、`CIDATA` 和正确的 generation metadata。

### 15.3 RDP

从 Agent 检查：

~~~bash
nc -vz <WINDOWS_DHCP_IP> 3389
ss -lntp | grep <AGENT_RDP_PROXY_PORT>
~~~

从 Windows 检查：

~~~powershell
Get-NetIPConfiguration
Get-NetTCPConnection -LocalPort 3389 -State Listen
Get-Service TermService
Get-LocalUser player
Get-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-555')
~~~

### 15.4 Guacamole

最终必须实际点击平台入口，确认页面可打开、看到桌面、键鼠可用、关闭浏览器后可重连、VM 重启后可恢复，且入口没有串到其他用户实例。

## 16. 销毁和重建

正常销毁应同时清理：

- libvirt 域；
- VM overlay；
- NVRAM；
- generation 文件；
- Cloudbase-Init seed 目录和 ISO；
- Agent RDP 代理；
- Guacamole connection；
- 数据库活动状态；
- 节点 VM 容量预留。

~~~bash
sudo virsh dominfo <VM_NAME>
find /var/lib/gzctf -maxdepth 4 -iname "*<VM_NAME>*"
ss -lntp | grep <OLD_RDP_PROXY_PORT>
~~~

不要只删除数据库行。数据库删除不会自动证明物理 VM、seed 和 Guacamole 已被清理。

## 17. 故障矩阵

| 现象 | 最可能层级 | 首要检查 | 处理 |
| --- | --- | --- | --- |
| `No schedulable node... VM=1` | 调度/容量 | 节点在线、KVM capability、VM capacity | 恢复节点或增加容量 |
| 镜像上传中断 | 传输 | FRP/Nginx 超时、代理链、磁盘空间 | 改用内网 rsync 和本地导入 |
| `qemu-img check` 失败 | 镜像文件 | 传前传后哈希 | 重新传输或重新导出 |
| VM 立即关机 | 固件/重启策略 | dumpxml、libvirt 日志 | 使用 UEFI/q35/SATA，`on_reboot=restart` |
| `No bootable device` | 固件/磁盘总线 | UEFI 与原制镜方式、磁盘 bus | 按硬件契约重制或转换 |
| VM 一直在 OOBE | 镜像封装 | VNC、Unattend.xml | 修复 Cloudbase/Unattend 后重新 Sysprep |
| VM running 但无 DHCP | 网卡/网络 | e1000e 驱动、dnsmasq lease、bridge | 修复驱动或节点网络 |
| 能拿 IP 但 3389 不通 | 客户机初始化 | Cloudbase 日志、TermService、防火墙 | 修复 user-data 执行 |
| 手工配置后 RDP 可用 | Cloudbase/OOBE | user-data 是否执行 | 重制镜像，不能以人工操作作为上线方案 |
| VM 已恢复但平台仍为 Error | 状态恢复 | DB 状态、generation、native ID | 核实身份后受控 reconcile，不直接猜测改库 |
| 有 IP 和 3389 但无入口 | Guacamole | API 认证、connection ID、VmReadyService | 修复 Guacamole 配置并重试 |
| ping 失败但 RDP 正常 | 验收规则 | TCP 3389、DHCP、Windows 防火墙 | 不使用 ICMP 作为唯一标准 |
| 旧密码能登录新实例 | 镜像状态污染 | A/B overlay、Cloudbase cache | 立即撤销认证并重制 |
| Docker `start-limit-hit` | 节点配置并发 | journal、Registry trust 更新 | 恢复 Docker，部署串行化补丁 |

## 18. 失败实例恢复原则

平台状态和物理 VM 状态冲突时，按以下顺序处理：

1. 暂停对该实例的自动重试；
2. 核对数据库 VM name、generation、assigned node 和 native ID；
3. 在目标 Agent 核对 `virsh domuuid` 和 generation metadata；
4. 确认物理 VM 属于同一业务实例；
5. 检查 DHCP、3389 和 Cloudbase 日志；
6. 只有身份完全一致时，才允许执行受控状态 reconcile；
7. 身份不一致时销毁孤儿资源并重新创建；
8. 保存 correlation ID 和脱敏日志。

不得仅因为看到同名 VM 就把数据库从 `Error` 改成 `Running`。这可能把旧实例或其他代次实例错误地交付给用户。

## 19. 本次 challenge 40 案例

### 19.1 镜像

~~~text
文件: win2022-cloudbase-v1-compressed.qcow2
大小: 6,511,457,792 bytes
SHA-256: fee90d3b52da55354377e0a06402ec01441614689c90c3354be059cb7e13fa11
~~~

### 19.2 最终运行事实

~~~text
平台: 10.24.0.27
比赛: 23
题目: 40
模板 ID: 121
KVM 节点: 10.24.0.31
DHCP: 192.168.122.92
Agent RDP 代理: 10.24.0.31:47730
平台状态: Running / ready
Guacamole: 已实际登录 Windows Server 2022 桌面
~~~

这些地址是本次测试证据，不是部署默认值。

### 19.3 复盘

1. QCOW2 文件本身可以启动，不是简单的文件损坏；
2. 它依赖 UEFI/q35/SATA，原 Agent 参数不匹配；
3. Windows 首启会重启，原 `on_reboot=destroy` 导致 VM 被关机；
4. 修正硬件和重启策略后，系统进入 OOBE；
5. Cloudbase-Init 没有自动完成 `player`、密码和 RDP 配置；
6. 人工完成配置后，DHCP、RDP 和 Guacamole 均通过；
7. 因此该文件可用于诊断和人工测试，但当前不能作为“合格无人值守发布镜像”；
8. 应按本文重新封装、完成 A/B 验收，再替换正式模板。

## 20. 上线检查清单

### 20.1 镜像

- [ ] Windows 版本和许可证明确；
- [ ] 使用 UEFI/q35/SATA/e1000e 制作和验收；
- [ ] 驱动和 QEMU Guest Agent 正常；
- [ ] Cloudbase-Init 使用 LocalSystem；
- [ ] NoCloud ConfigDrive 可读；
- [ ] 配套 Unattend.xml 已使用；
- [ ] 首启不出现 OOBE；
- [ ] `#ps1_sysnative` 执行成功；
- [ ] A/B 密码隔离通过；
- [ ] 重启后 VM 不被销毁；
- [ ] QCOW2 check 通过；
- [ ] SHA-256 已登记；
- [ ] 发布基盘在 Sysprep 后未再次启动。

### 20.2 平台和节点

- [ ] 平台、Agent 和迁移来自同一 Git SHA；
- [ ] Agent 已部署 Windows UEFI/q35/SATA/restart 参数；
- [ ] 节点报告 KVM、Cloud-Init 和镜像下载能力；
- [ ] 节点有 VM capacity；
- [ ] OVMF、libvirt、virt-install 可用；
- [ ] 镜像已分发且哈希一致；
- [ ] 模板为 Windows/Qcow2/Ready；
- [ ] 只有验收后才设置 `SupportsInstanceCredentials=true`；
- [ ] Guacamole 使用受管账户或 Token；
- [ ] Data Protection keys 已持久化并纳入备份；
- [ ] 状态 reconcile 流程可审计。

### 20.3 业务

- [ ] 比赛题目测试实例通过；
- [ ] 普通参赛用户创建、延期、重启和销毁通过；
- [ ] 培训教师测试通过；
- [ ] 培训学员创建和销毁通过；
- [ ] 两个用户实例密码不同；
- [ ] 入口不串实例；
- [ ] 销毁后 VM、seed、代理、Guacamole 和容量均回收；
- [ ] 日志不包含密码、Token、Cookie 或完整 user-data。

## 21. 回滚

更换正式 Windows 模板前：

1. 记录旧模板 ID、哈希和绑定题目；
2. 保留旧模板文件，不立即删除；
3. 停止旧模板的新实例创建；
4. 等待或销毁旧活动实例；
5. 将题目绑定到新模板；
6. 完成一次真实创建和登录；
7. 发现问题时恢复题目到旧模板；
8. 清理新模板失败实例和 Guacamole 连接；
9. 保存故障证据后再重新制镜。

若回滚平台版本，还必须同时考虑数据库迁移、`DataProtectionKeys`、Agent 版本和活动 VM。不得只回滚前端或主站容器而保留不兼容 Agent。

## 22. 最终完成标准

Windows VM 只有同时满足以下条件才算完成：

1. QCOW2 文件完整；
2. A/B 双实例通过；
3. OOBE 不需要人工操作；
4. Cloudbase-Init 自动注入不同实例密码；
5. UEFI/q35/SATA/e1000e 启动稳定；
6. Windows 重启后 VM 保持运行；
7. DHCP、TCP 3389、Agent RDP 代理和 Guacamole 全部通过；
8. 比赛和培训业务入口至少各通过一次；
9. 销毁和容量回收完整；
10. 全链路没有泄露实例密码。

“人工进入 VNC 后设置账户可以登录”只证明客户机可修复，不等于镜像可以交付。
