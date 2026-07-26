# Windows VM 配置与验收速查

> 适用对象：Windows 镜像制作人员、平台管理员、测试人员。
>
> 目标：上传一份能自动启动、自动创建 `player`、自动启用 RDP，并能从平台直接进入的 Windows QCOW2。

## 1. 本次镜像没做完什么

> **以下五项是本次镜像缺失或没有完成验收的部分，必须补做。**

| 未完成项 | 实际表现 | 必须达到的结果 |
| --- | --- | --- |
| OOBE 无人值守 | 首次启动停在地区、许可协议和 Administrator 密码页面 | 首次启动不需要人工点击 |
| Cloudbase-Init 配置 | 没有正确读取平台挂载的 `CIDATA` | 自动执行 `#ps1_sysnative` 脚本 |
| 实例用户创建 | `player` 需要人工创建 | 每个实例自动创建或更新 `player` |
| RDP 初始化 | RDP、用户组和防火墙需要人工配置 | 3389 自动监听，`player` 自动加入远程桌面组 |
| 双实例验收 | 没有证明两台实例使用不同密码 | 实例 A、B 密码不同，A 的密码不能登录 B |

本次 QCOW2 文件可以启动，因此不是单纯的文件损坏。但它目前只适合人工调试，**不能作为合格的自动化发布镜像**。

## 2. 平台侧当前情况

| 项目 | 状态 | 测试人员是否需要处理 |
| --- | --- | --- |
| UEFI + q35 + SATA 启动 | 已修复 | 不需要 |
| Windows 重启后被关机 | 已改为 `on_reboot=restart` | 不需要 |
| Agent 专项测试 | 14/14 通过 | 不需要 |
| 失败实例自动从 `Error` 恢复 | 尚未完全闭环 | 遇到后联系平台开发，不要直接改数据库 |
| 大文件经 FRP 上传不稳定 | 仍可能发生 | 使用内网传输和本地导入 |

## 3. 镜像制作人员：必须补做

### 3.1 安装 Cloudbase-Init

在 Windows 内安装官方 x64 Cloudbase-Init，并确认服务使用 `LocalSystem`：

~~~powershell
sc.exe qc cloudbase-init
Get-Service cloudbase-init
~~~

`sc.exe qc` 输出中的 `SERVICE_START_NAME` 必须是 `LocalSystem`。

### 3.2 配置 NoCloud

编辑：

~~~text
C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\cloudbase-init.conf
~~~

至少包含：

~~~ini
[DEFAULT]
metadata_services=cloudbaseinit.metadata.services.nocloudservice.NoCloudConfigDriveService
plugins=cloudbaseinit.plugins.common.sethostname.SetHostNamePlugin,cloudbaseinit.plugins.common.userdata.UserDataPlugin
config_drive_cdrom=true
config_drive_raw_hhd=true
process_userdata=true
allow_reboot=false
~~~

检查以下三个文件：

~~~powershell
$Conf = 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf'
Test-Path "$Conf\cloudbase-init.conf"
Test-Path "$Conf\cloudbase-init-unattend.conf"
Test-Path "$Conf\Unattend.xml"
~~~

三项必须全部返回 `True`。

> **本次镜像重点缺失：不能只安装 Cloudbase-Init，还必须使用版本匹配的 `Unattend.xml`，否则仍会停在 OOBE。**

### 3.3 清理并执行 Sysprep

~~~powershell
Stop-Service cloudbase-init -ErrorAction SilentlyContinue
Remove-Item 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\log\*' -Force -ErrorAction SilentlyContinue

$Unattend = 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\conf\Unattend.xml'
$QuotedUnattend = '"{0}"' -f $Unattend
$Args = @('/generalize','/oobe','/shutdown','/mode:vm',"/unattend:$QuotedUnattend")
Start-Process "$env:WINDIR\System32\Sysprep\Sysprep.exe" -ArgumentList $Args -Wait
~~~

等待 Windows 自动关机。

> **关机后不要再次启动发布基盘。** 如果误启动，必须重新清理并重新执行 Sysprep。

### 3.4 导出 QCOW2

在 Ubuntu KVM 制作机执行：

~~~bash
qemu-img convert -p -O qcow2 -c win2022-builder.qcow2 win2022-cloudbase-v1.qcow2
qemu-img check win2022-cloudbase-v1.qcow2
sha256sum win2022-cloudbase-v1.qcow2 | tee win2022-cloudbase-v1.sha256
~~~

`qemu-img check` 必须成功，并保存 SHA-256。

## 4. 管理员：上传和平台配置

### 4.1 传输文件

不要优先使用公网浏览器上传 6 GiB 级 QCOW2。建议走内网：

~~~bash
rsync -avh --partial --append-verify --info=progress2 \
  win2022-cloudbase-v1.qcow2 \
  <USER>@<PLATFORM_HOST>:/var/lib/gzctf/images/incoming/
~~~

传输完成后在服务器核对：

~~~bash
sha256sum /var/lib/gzctf/images/incoming/win2022-cloudbase-v1.qcow2
qemu-img check /var/lib/gzctf/images/incoming/win2022-cloudbase-v1.qcow2
~~~

服务器哈希必须与制作机一致。

### 4.2 导入平台

1. 登录平台管理员账号；
2. 进入“管理 -> 镜像”；
3. 点击“本地导入”；
4. 路径填写 `/var/lib/gzctf/images/incoming/win2022-cloudbase-v1.qcow2`；
5. 名称填写 `Windows Server 2022 Cloudbase v1`；
6. 等待状态变为 `Ready`；
7. 确认系统为 `Windows`；
8. 确认类型为 `Qcow2`；
9. 核对大小和 SHA-256。

> **此时不要直接点击“认证实例凭据”。必须先收到制作人员提供的离线 A/B 验收记录。**

离线 A/B 验收需要在隔离 KVM 上为同一基盘分别挂载 `CIDATA`，注入两个不同的测试密码。操作命令参见详细文档第 10 节。验收记录必须证明：

- A、B 均未停在 OOBE；
- A、B 均自动创建 `player` 并开启 RDP；
- 密码 A 只能登录 A；
- 密码 A 不能登录 B，密码 B 可以登录 B；
- 两个实例销毁后没有残留。

收到完整记录后，管理员才能点击“认证实例凭据”，使：

~~~text
SupportsInstanceCredentials=true
~~~

### 4.3 节点要求

目标 KVM 节点必须：

- 在线；
- `KvmCapacity > 0`；
- 具有可用 VM 容量；
- Agent 报告 `runtime.kvm.v1`；
- Agent 报告 `runtime.vm.cloud-init.v1`；
- 已收到该 QCOW2。

平台当前使用的 Windows 硬件参数为：

~~~text
UEFI + q35 + SATA + e1000e
on_reboot=restart
~~~

## 5. 测试人员：平台验收

### 5.1 验收前检查

确认管理员已经提供：

- 镜像 SHA-256；
- 离线实例 A/B 验收记录；
- 平台模板 ID；
- `SupportsInstanceCredentials=true`；
- 测试题目地址。

缺少离线 A/B 记录时，直接退回镜像制作人员，不开始平台验收。

### 5.2 第一次启动必须检查

创建测试实例 A，观察启动过程。

以下任一情况出现即判定失败：

- 停在地区或语言选择；
- 停在许可协议；
- 要求设置 Administrator 密码；
- 需要人工创建 `player`；
- 需要人工开启 RDP。

### 5.3 Windows 内检查

进入 Windows 后执行：

~~~powershell
Get-Service cloudbase-init
Get-LocalUser player
Get-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-555')
Get-NetTCPConnection -LocalPort 3389 -State Listen
Get-Content 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\log\cloudbase-init.log' -Tail 100
~~~

正确结果：

- `cloudbase-init` 正常运行；
- `player` 存在且启用；
- `player` 在远程桌面用户组；
- 3389 同时监听 IPv4 或 IPv6；
- 日志显示读取 NoCloud ConfigDrive；
- 日志显示 user-data 执行成功。

### 5.4 平台入口检查

1. 页面状态由“正在准备”变为“运行中”；
2. 页面出现远程桌面入口；
3. 点击后可看到 Windows 桌面；
4. 键盘和鼠标可用；
5. 关闭页面后可以重新进入；
6. 重启 Windows 后 VM 不会被销毁。

Windows 可能不响应 `ping`。是否成功应以 DHCP、TCP 3389 和实际远程桌面为准。

### 5.5 必须创建第二个平台实例

销毁实例 A，再从同一模板创建实例 B：

1. B 也不能出现 OOBE；
2. B 必须自动创建 `player`；
3. B 的 Guacamole 入口可以使用；
4. B 的入口不能打开已销毁的 A；
5. B 销毁后页面状态和节点容量恢复。

密码 A/B 隔离由认证前的离线验收负责；平台测试人员主要验证业务入口、状态和资源回收。

## 6. 绑定题目并复测

1. 打开“管理 -> 比赛 -> 题目管理”；
2. 编辑 Windows VM 题目；
3. 在环境模板中选择新镜像；
4. 保存；
5. 使用管理员测试实例验证一次；
6. 使用普通参赛账号再创建一次；
7. 确认创建、入口、延期和销毁都正常。

培训题目同样需要在课程环境模板中绑定新镜像，并使用教师和学员账号各测一次。

## 7. 测试结果模板

~~~text
镜像名称:
镜像 SHA-256:
模板 ID:
测试节点:

[ ] 无 OOBE
[ ] Cloudbase-Init 运行
[ ] player 自动创建
[ ] player 自动加入 RDP 组
[ ] 3389 自动监听
[ ] 实例 A 可登录
[ ] 实例 B 可登录
[ ] 已附离线 A/B 密码隔离记录
[ ] Windows 重启后 VM 仍运行
[ ] Guacamole 可进入桌面
[ ] 比赛题目创建/延期/销毁正常
[ ] 培训题目创建/延期/销毁正常
[ ] 销毁后容量已回收

结论: 通过 / 不通过
失败步骤:
错误信息:
截图或日志位置:
~~~

## 8. 常见失败判断

| 现象 | 判断 |
| --- | --- |
| QCOW2 无法读取或哈希不一致 | 文件传输或镜像文件问题 |
| VM 无法引导 | UEFI/q35/SATA 不匹配 |
| VM 重启后关机 | Agent 未使用 `on_reboot=restart` |
| 停在 OOBE | 镜像制作问题 |
| 手工创建 `player` 后才可登录 | Cloudbase-Init/user-data 没做好 |
| 有 IP 但 3389 不通 | RDP 服务或防火墙没自动配置 |
| 3389 可达但平台无入口 | Guacamole 或平台状态问题 |
| 平台一直是 `Error`，但 VM 实际运行 | 平台状态恢复问题，联系开发处理 |
| `No schedulable node... VM=1` | 节点离线、能力不匹配或 VM 容量不足 |

详细原理和故障恢复流程参见 `docs/operations/windows-vm-deployment-guide.md`。
