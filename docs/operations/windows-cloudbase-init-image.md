# Windows VM Cloudbase-Init 镜像制作与实例凭据认证

## 1. 安全契约

新建 Windows VM 不再使用平台共享默认密码。每个 `VmInstance` 生成独立高强度密码，密码由 ASP.NET Data Protection 加密后保存，明文只在下发 Cloudbase-Init 和创建 Guacamole 连接时短暂存在内存。

创建条件全部满足才允许调度：

1. 目标是远程 KVM Agent 节点。
2. Agent capability manifest 同时包含 `runtime.kvm.v1` 和 `runtime.vm.cloud-init.v1`。
3. Windows 镜像已由有管理权的教师或管理员认证 `SupportsInstanceCredentials=true`。
4. Guacamole 配置了预置短期 Token，或独立受管 API 账户。

任一条件缺失时 fail closed，不回退共享密码。

## 2. 镜像制作要求

推荐以 Windows Server 2022/2025 的干净 QCOW2 为基线：

1. 安装 VirtIO 存储、网卡和 Balloon 驱动。
2. 安装 Cloudbase-Init 稳定版。
3. Cloudbase-Init 服务使用 `LocalSystem` 运行。
4. 启用 NoCloud ConfigDrive metadata service 和 PowerShell user-data 插件。
5. 确认镜像允许本地用户创建、修改密码、加入“远程桌面用户”组。
6. 启用 RDP 服务；防火墙规则可由实例 user-data 启用，不依赖中文或英文显示组名。
7. 清除制作者账户密码、日志、临时文件和历史 metadata，执行 Sysprep 后关机。

`cloudbase-init.conf` 至少应包含与当前版本等价的能力：

```ini
[DEFAULT]
metadata_services=cloudbaseinit.metadata.services.nocloudservice.NoCloudConfigDriveService
plugins=cloudbaseinit.plugins.common.sethostname.SetHostNamePlugin,cloudbaseinit.plugins.common.userdata.UserDataPlugin
allow_reboot=false
```

不同 Cloudbase-Init 版本的插件完整路径可能不同，应以所安装版本文档和服务日志为准。验收重点是 NoCloud ConfigDrive 能读取 `meta-data`、`network-config` 和以 `#ps1_sysnative` 开头的 `user-data`。

## 3. 平台注入行为

每次创建实例时平台下发的 user-data 会：

1. 创建或更新本地 `player` 用户。
2. 设置该实例独有密码。
3. 通过 SID `S-1-5-32-555` 定位“远程桌面用户”内置组，避免系统语言差异。
4. 开启 RDP 注册表开关。
5. 按内部规则名启用 `RemoteDesktop-UserMode-In-*`；若镜像没有该规则，则创建 `GZCTF-RDP-In-TCP`。

Agent 将 seed 目录设置为 `0700`，将 `user-data`、`meta-data` 和 `network-config` 设置为 `0600`，销毁 VM 时删除 seed 目录。日志和遥测不得输出 user-data 或 RDP 密码。

## 4. 镜像离线验收

在隔离 KVM 节点上至少执行两次全新实例测试：

1. 为实例 A 注入随机密码 A，启动后通过 RDP 登录 `player`。
2. 销毁实例 A，确认 seed 目录已删除。
3. 用同一模板创建实例 B，注入随机密码 B。
4. 密码 A 不能登录实例 B，密码 B 可以登录。
5. Cloudbase-Init 日志无 PowerShell 错误，无固定默认密码。
6. 重启实例 B 后密码 B 仍有效。
7. Guacamole 能建立连接，平台日志不出现凭据明文。

未完成上述验证的镜像不得在管理端标记为已认证。

## 5. 上传与认证

1. 将 QCOW2 上传或注册到镜像服务器。
2. 在“管理 -> 镜像”确认：系统为 Windows、类型不是 Docker、状态为 Ready。
3. 打开镜像详情，点击“认证 Cloudbase-Init”。
4. 详情中的“实例凭据认证”应变为“已认证”。
5. 更换镜像文件、重新制作模板或修改 Cloudbase-Init 后，应先撤销认证，复测后重新认证。

认证只表示该镜像已通过实例凭据注入验证，不代表镜像内容安全扫描、许可证或题目功能已经验收。

## 6. 节点与 Guacamole 配置

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

## 7. 故障定位

| 错误 | 含义 | 处理 |
| --- | --- | --- |
| `does not advertise Cloud-Init` | Agent 未报告能力 | 升级 Agent，检查 virt-install/Cloud-Init 探测 |
| `image is not verified` | 镜像未认证 | 完成双实例验收后在镜像详情认证 |
| `has no protected RDP credential` | 旧实例或数据异常 | 不修补为默认密码，销毁后重新创建 |
| Guacamole authentication not configured | 没有 Token 或受管账户 | 配置密钥后重试，不启用默认管理员回退 |
| VM 运行但 RDP 未就绪 | Cloudbase-Init、驱动、网络或防火墙失败 | 查看 Agent 与 Windows Cloudbase-Init 日志 |
