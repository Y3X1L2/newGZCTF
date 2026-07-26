# Windows VM Cloudbase-Init 镜像制作与实例凭据认证

完整的端到端操作、上传、题目绑定、分层验收和本次故障复盘参见 `docs/operations/windows-vm-deployment-guide.md`。

## 1. 安全契约

新建 Windows VM 不再使用平台共享默认密码。每个 `VmInstance` 生成独立高强度密码，密码由 ASP.NET Data Protection 加密后保存，明文只在下发 Cloudbase-Init 和创建 Guacamole 连接时短暂存在内存。

创建条件全部满足才允许调度：

1. 目标是远程 KVM Agent 节点。
2. Agent capability manifest 同时包含 `runtime.kvm.v1` 和 `runtime.vm.cloud-init.v1`。
3. Windows 镜像已由有管理权的教师或管理员认证 `SupportsInstanceCredentials=true`。
4. Guacamole 配置了预置短期 Token，或独立受管 API 账户。

任一条件缺失时 fail closed，不回退共享密码。

## 2. 制作环境与产物约定

建议在独立 KVM 制作机上完成，不要直接修改平台当前使用的基础镜像。制作机至少安装：

```bash
sudo apt-get update
sudo apt-get install -y qemu-kvm libvirt-daemon-system virtinst qemu-utils genisoimage
```

准备以下输入文件：

- 微软官方 Windows Server 2022/2025 ISO；
- 与系统版本、架构匹配的 VirtIO 驱动 ISO；
- Cloudbase-Init 官方签名的稳定版 x64 MSI；当前验证基线为 `1.1.8`；
- 至少 80 GiB 可用磁盘空间。

固定产物命名，避免覆盖原件：

```text
win2022-builder.qcow2       制作过程磁盘，不上传平台
win2022-cloudbase-v1.qcow2  Sysprep 后压平的发布镜像
win2022-cloudbase-v1.sha256 发布镜像摘要
```

当前验证基线的官方发布信息：

```text
URL: https://github.com/cloudbase/cloudbase-init/releases/download/1.1.8/CloudbaseInitSetup_1_1_8_x64.msi
SHA-256: 0e7fa42e0cbc0ce7657f85730b0c6cc7afc4087a3639df0ff51a721a0be19bd5
```

版本升级时必须重新从官方 release 获取摘要，不要沿用上面的摘要验证其他版本。下载 MSI 后在 Windows 中检查数字签名，并记录 SHA-256：

```powershell
Get-AuthenticodeSignature C:\Install\CloudbaseInitSetup_1_1_8_x64.msi
Get-FileHash C:\Install\CloudbaseInitSetup_1_1_8_x64.msi -Algorithm SHA256
```

签名状态必须为 `Valid`。版本、来源 URL、SHA-256 和制作日期应随镜像归档。

## 3. Windows 基础镜像制作

推荐以 Windows Server 2022/2025 的干净 QCOW2 为基线：

### 3.1 创建制作虚拟机

```bash
qemu-img create -f qcow2 win2022-builder.qcow2 80G
sudo virt-install \
  --name win2022-builder \
  --memory 8192 \
  --vcpus 4 \
  --cpu host-passthrough \
  --machine q35 \
  --boot uefi \
  --events on_reboot=restart \
  --disk path="$PWD/win2022-builder.qcow2",bus=sata,format=qcow2 \
  --disk path=/path/to/virtio-win.iso,device=cdrom \
  --cdrom /path/to/windows-server-2022.iso \
  --network network=default,model=e1000e \
  --graphics vnc,listen=127.0.0.1 \
  --osinfo detect=on,require=off
```

安装 Windows 时若找不到系统盘，从 VirtIO ISO 加载对应的 `viostor` 或 `vioscsi` x64 驱动。进入系统后继续安装：

1. VirtIO 存储驱动；
2. VirtIO 网卡驱动；
3. Balloon 驱动；
4. QEMU Guest Agent。

平台普通 Windows 靶机当前使用 `e1000e` 网卡以兼容既有模板，但驱动仍应完整安装，避免后续切换网络模型时重新制镜像。

### 3.2 安装 Cloudbase-Init

将 MSI 放到 `C:\Install`，使用管理员 PowerShell 执行：

```powershell
Start-Process msiexec.exe -Wait -ArgumentList @(
  '/i',
  'C:\Install\CloudbaseInitSetup_1_1_8_x64.msi',
  '/qn',
  '/norestart',
  '/l*v',
  'C:\Install\cloudbase-init-install.log'
)
sc.exe config cloudbase-init obj= LocalSystem
sc.exe qc cloudbase-init
```

`sc.exe qc` 输出中的 `SERVICE_START_NAME` 必须是 `LocalSystem`。若安装包同时创建 `cloudbase-init-unattend` 服务，也应确认其启动身份和配置文件来自同一受控安装目录。

### 3.3 配置 NoCloud ConfigDrive

编辑：

```text
C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\cloudbase-init.conf
```

`cloudbase-init.conf` 至少应包含与当前版本等价的能力：

```ini
[DEFAULT]
metadata_services=cloudbaseinit.metadata.services.nocloudservice.NoCloudConfigDriveService
plugins=cloudbaseinit.plugins.common.sethostname.SetHostNamePlugin,cloudbaseinit.plugins.common.userdata.UserDataPlugin
config_drive_cdrom=true
config_drive_raw_hhd=true
process_userdata=true
allow_reboot=false
```

不同 Cloudbase-Init 版本的插件完整路径可能不同，应以所安装版本文档和服务日志为准。验收重点是 NoCloud ConfigDrive 能读取 `meta-data`、`network-config` 和以 `#ps1_sysnative` 开头的 `user-data`。

同时检查：

```powershell
Set-Service cloudbase-init -StartupType Automatic
Get-Service cloudbase-init
Get-Content 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\cloudbase-init.conf'
```

不要把制作者密码、平台密码或最终 `player` 密码写入镜像。`player` 用户由每次实例的 user-data 创建或更新。

### 3.4 清理并执行 Sysprep

先删除 Cloudbase-Init 历史状态和制作残留：

```powershell
Stop-Service cloudbase-init -ErrorAction SilentlyContinue
Remove-Item 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\log\*' -Force -ErrorAction SilentlyContinue
Remove-Item 'C:\Windows\Temp\*' -Recurse -Force -ErrorAction SilentlyContinue
Clear-RecycleBin -Force -ErrorAction SilentlyContinue
```

确认主配置、`cloudbase-init-unattend.conf` 和版本匹配的 `Unattend.xml` 仍然存在，然后执行：

```powershell
$Unattend = 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\Unattend.xml'
$QuotedUnattend = '"{0}"' -f $Unattend
$Args = @('/generalize','/oobe','/shutdown','/mode:vm',"/unattend:$QuotedUnattend")
Start-Process "$env:WINDIR\System32\Sysprep\Sysprep.exe" -ArgumentList $Args -Wait
```

等待虚拟机完全关机。不要在 Sysprep 后再次启动发布磁盘，否则必须重新执行清理和 Sysprep。

在 KVM 制作机上压平镜像并生成摘要：

```bash
sudo virsh undefine win2022-builder --nvram 2>/dev/null || sudo virsh undefine win2022-builder
qemu-img convert -p -O qcow2 -c win2022-builder.qcow2 win2022-cloudbase-v1.qcow2
qemu-img check win2022-cloudbase-v1.qcow2
sha256sum win2022-cloudbase-v1.qcow2 | tee win2022-cloudbase-v1.sha256
```

## 4. 平台注入行为

每次创建实例时平台下发的 user-data 会：

1. 创建或更新本地 `player` 用户。
2. 设置该实例独有密码。
3. 通过 SID `S-1-5-32-555` 定位“远程桌面用户”内置组，避免系统语言差异。
4. 开启 RDP 注册表开关。
5. 按内部规则名启用 `RemoteDesktop-UserMode-In-*`；若镜像没有该规则，则创建 `GZCTF-RDP-In-TCP`。

Agent 将 seed 目录设置为 `0700`，将 `user-data`、`meta-data` 和 `network-config` 设置为 `0600`，销毁 VM 时删除 seed 目录。日志和遥测不得输出 user-data 或 RDP 密码。

## 5. 镜像离线验收

不要先在管理端点击“认证 Cloudbase-Init”。先在隔离 KVM 节点使用与平台相同的 `CIDATA` ConfigDrive 做离线测试。

创建测试目录：

```bash
mkdir -p acceptance-a
cd acceptance-a
cp ../win2022-cloudbase-v1.qcow2 base.qcow2
qemu-img create -f qcow2 -F qcow2 -b "$PWD/base.qcow2" instance-a.qcow2
```

准备 `meta-data`：

```yaml
instance-id: gzctf-image-acceptance-a
local-hostname: gzctf-win-a
```

准备 `user-data`，其中测试密码只用于隔离验收，验收完成后删除：

```powershell
#ps1_sysnative
$ErrorActionPreference = 'Stop'
$Username = 'player'
$Password = ConvertTo-SecureString 'Acceptance-Aa1!ReplaceMe' -AsPlainText -Force
$ExistingUser = Get-LocalUser -Name $Username -ErrorAction SilentlyContinue
if ($null -eq $ExistingUser) {
  New-LocalUser -Name $Username -Password $Password -PasswordNeverExpires -AccountNeverExpires | Out-Null
} else {
  Set-LocalUser -Name $Username -Password $Password -PasswordNeverExpires $true
}
$RdpGroup = Get-LocalGroup -SID 'S-1-5-32-555'
Add-LocalGroupMember -Group $RdpGroup -Member $Username -ErrorAction SilentlyContinue
Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
Get-NetFirewallRule -Name 'RemoteDesktop-UserMode-In-*' -ErrorAction SilentlyContinue | Enable-NetFirewallRule
```

生成 seed ISO 并启动：

```bash
touch network-config
genisoimage -output seed-a.iso -volid CIDATA -joliet -rock \
  -graft-points user-data=user-data meta-data=meta-data network-config=network-config
sudo virt-install \
  --name gzctf-win-accept-a \
  --memory 4096 \
  --vcpus 2 \
  --machine q35 \
  --boot uefi \
  --events on_reboot=restart \
  --disk path="$PWD/instance-a.qcow2",format=qcow2,bus=sata \
  --disk path="$PWD/seed-a.iso",device=cdrom \
  --network network=default,model=e1000e \
  --graphics vnc,listen=127.0.0.1 \
  --import --noautoconsole \
  --osinfo detect=on,require=off
```

通过 DHCP 租约或 `virsh domifaddr` 获取地址，验证 RDP 登录后检查：

```powershell
Get-LocalUser player
Get-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-555')
Get-Content 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\log\cloudbase-init.log' -Tail 200
```

日志必须显示识别 NoCloud ConfigDrive 并成功执行 PowerShell user-data，不能出现脚本解析错误或固定生产密码。

在隔离 KVM 节点上至少执行两次全新实例测试：

1. 为实例 A 注入随机密码 A，启动后通过 RDP 登录 `player`。
2. 销毁实例 A，确认 seed 目录已删除。
3. 用同一模板创建实例 B，注入随机密码 B。
4. 密码 A 不能登录实例 B，密码 B 可以登录。
5. Cloudbase-Init 日志无 PowerShell 错误，无固定默认密码。
6. 重启实例 B 后密码 B 仍有效。
7. Guacamole 能建立连接，平台日志不出现凭据明文。
8. 实例 A 和 B 均不能停在地区、许可协议或 Administrator 密码 OOBE。

未完成上述验证的镜像不得在管理端标记为已认证。

验收结束后销毁测试 VM、overlay 和包含测试密码的 seed：

```bash
sudo virsh destroy gzctf-win-accept-a 2>/dev/null || true
sudo virsh undefine gzctf-win-accept-a --remove-all-storage 2>/dev/null || true
rm -rf acceptance-a acceptance-b
```

## 6. 上传、认证与题目绑定

1. 将 `win2022-cloudbase-v1.qcow2` 上传到镜像存储服务器，或通过管理端上传/本地导入。
2. 在“管理 -> 镜像”确认：系统为 Windows、类型为 Qcow2、状态为 Ready、大小和 SHA-256 与发布记录一致。
3. 打开镜像详情，点击“认证 Cloudbase-Init”。
4. 详情中的“实例凭据认证”应变为“已认证”。
5. 编辑 Windows 题目，在环境配置中选择这份新模板并保存，不要继续绑定旧的 `Windows Server 2022` 模板。
6. 从比赛页连续创建两个实例，分别验证 RDP/Guacamole、密码隔离、销毁和容量回收。
7. 更换镜像文件、重新制作模板或修改 Cloudbase-Init 后，应先撤销认证，复测后重新认证。

认证只表示该镜像已通过实例凭据注入验证，不代表镜像内容安全扫描、许可证或题目功能已经验收。

## 7. 节点与 Guacamole 配置

Agent 心跳返回的 capability manifest 必须包含：

```text
runtime.kvm.v1
runtime.vm.cloud-init.v1
image.vm.download.v1
```

平台配置：

```json
{
  "GuacamoleSettings": {
    "GuacamoleApiUrl": "http://<guacamole>/guacamole/api",
    "GuacamoleAuthToken": "",
    "ApiUsername": "<独立受管 API 账户>",
    "ApiPassword": "<密钥配置注入>"
  }
}
```

不得使用 `guacadmin/guacadmin`。生产优先使用权限受限的独立 API 账户，并在首次部署后轮换初始管理员密码。

Data Protection key 已持久化在平台数据库的 `DataProtectionKeys` 表。数据库备份和恢复必须包含该表，否则重启或迁移后无法解密活动 VM 凭据。

## 8. 故障定位

| 错误 | 含义 | 处理 |
| --- | --- | --- |
| `remote_node_required` | 只有本地 KVM 符合基础条件 | 注册或恢复远程 KVM Agent |
| `agent_feature_unavailable` | Agent 未报告 Cloud-Init 等必需能力 | 升级 Agent，确认 capability manifest |
| `does not advertise Cloud-Init` | 已分配 Agent 未报告能力 | 升级 Agent，检查 virt-install/Cloud-Init 探测 |
| `image is not verified` | 镜像未认证 | 完成双实例验收后在镜像详情认证 |
| `has no protected RDP credential` | 旧实例或数据异常 | 不修补为默认密码，销毁后重新创建 |
| Guacamole authentication not configured | 没有 Token 或受管账户 | 配置密钥后重试，不启用默认管理员回退 |
| VM 运行但 RDP 未就绪 | Cloudbase-Init、驱动、网络或防火墙失败 | 查看 Agent 与 Windows Cloudbase-Init 日志 |

本次 `challenge=40` 排查中，原模板 `/var/lib/gzctf/images/win2022-base-player-rdp-fixed.qcow2` 未安装 Cloudbase-Init，因此必须更换镜像。不得仅把数据库中的 `SupportsInstanceCredentials` 改为 `true`。
