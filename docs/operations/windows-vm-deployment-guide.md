# 比赛 Windows VM 部署与运行说明

本文件解释普通 CTF Windows VM 的平台实现、镜像要求、节点配置、发布验收和回滚。面向镜像制作人员、平台开发、节点运维与测试人员。日常操作优先使用
`docs/operations/windows-vm-quick-deployment-guide.md`。

## 1. 产品边界

本契约只覆盖普通 CTF 比赛：

- 镜像预配置一组固定 RDP 账号密码；
- 同一镜像的所有实例使用同一组凭据；
- 不生成每实例随机密码；
- 不创建 `CIDATA`，不注入 Cloudbase-Init user-data；
- 提供 Guacamole 与内网原生 RDP；
- 本轮不提供培训 Windows VM。

TeamLab、Linux cloud-init、Guest Supervisor 和后续统一 VM 模板管理仍有独立契约。不得为了简化普通比赛而删除这些共享基础设施。

## 2. 端到端链路

```text
管理员导入 Windows QCOW2
  -> 模板进入 Ready
  -> 管理员配置 ExistingAccount RDP profile
  -> 比赛题目绑定模板
  -> 选手创建实例
  -> DeploymentQueueTicket 统一排队和调度
  -> Worker 确保镜像文件存在且校验通过
  -> Agent 创建 overlay/NVRAM/libvirt domain
  -> Windows 从 libvirt default 网络获取 DHCP
  -> Agent 探测镜像配置的 RDP 目标端口
  -> Agent 创建受 AllowedSources 约束的 TCP 代理
  -> 主站创建 Guacamole connection
  -> 状态 API 向实例所有者返回 Guacamole 和 mstsc 信息
  -> 销毁时清理运行资源、代理、Guacamole 和容量
```

PostgreSQL 仍是实例、队列和节点容量的事实源。Agent 只执行已校验的本机 KVM 操作，不读取比赛或用户实体。

## 3. 镜像远程访问配置

比赛 Windows 镜像必须同时满足：

```text
ImageTemplate.Status = Ready
ImageTemplate.OSType = Windows
ImageTemplate.ImageType != Docker
RemoteAccess.Enabled = true
RemoteAccess.Protocol = RDP
RemoteAccess.CredentialMode = ExistingAccount
RemoteAccess.Port = 1..65535
RemoteAccess.Username != empty
RemoteAccess.Credential != empty
```

凭据使用现有镜像远程访问配置持久化，不复制到新 `VmInstance`。历史
`VmInstance.RdpPasswordProtected` 与 `ImageTemplate.SupportsInstanceCredentials` 暂时保留用于旧数据兼容，但不参与新比赛 VM 的创建和调度。

更新模板配置不会修改 QCOW2 内部账号。管理员必须保证配置与镜像一致。

## 4. 镜像制作

### 4.1 推荐硬件契约

```text
Machine: q35
Firmware: UEFI/OVMF
System disk: SATA QCOW2
Network: e1000e
Reboot policy: restart
RDP: default 3389, configurable per image
```

### 4.2 Windows 内配置

发布前完成：

- Windows 安装、更新和许可检查；
- e1000e/存储驱动和 QEMU Guest Agent；
- 固定本地账号及密码；
- 远程桌面用户组；
- `TermService` 自动启动；
- RDP 注册表和防火墙；
- DHCP；
- 无 OOBE、安装 ISO、共享目录和制作人员凭据。

普通比赛不要求安装 Cloudbase-Init。若制作流程使用 Sysprep，可以使用自有无人值守应答文件，但平台不会在实例启动时提供 Cloudbase metadata。

### 4.3 离线验证

从只读基盘创建两个 overlay，分别启动并使用相同固定凭据登录。验证的重点是基盘可重复启动、运行资源独立和销毁完整，不再要求 A/B 密码不同。

```bash
qemu-img create -f qcow2 -F qcow2 -b "$BASE" instance-a.qcow2
qemu-img create -f qcow2 -F qcow2 -b "$BASE" instance-b.qcow2
qemu-img check "$BASE"
```

## 5. 镜像导入与分发

大文件优先使用内网 `rsync --partial --append-verify` 传到平台允许的本地导入目录。导入前后都运行 `sha256sum` 与 `qemu-img check`。

平台导入后核对：

- 模板 ID、名称、文件大小和 SHA-256；
- `Windows / Qcow2 / Ready`；
- 镜像主状态和各节点分发状态分别正确；
- 失败分发只在 `Retryable=true` 时自动重试；
- 目标 Worker 上的文件哈希与模板一致。

## 6. 调度与创建

新比赛 Windows VM 需要 Worker 同时报告：

```text
runtime.kvm.v1
image.vm.download.v1
```

并具有可用 VM capacity。本轮继续拒绝本机 KVM 调度，避免进入未完成镜像分发和访问代理的本地分支。

创建请求不包含 Cloud-Init、CIDATA 或 RDP 密码。Agent 使用模板文件创建独立 overlay、NVRAM 和 libvirt domain。平台保存 generation 与 native ID，后续状态、销毁和恢复必须校验运行身份。

## 7. 就绪与访问

DHCP 地址不是最终就绪条件。Agent 必须成功连接镜像配置的目标 RDP 端口后，才创建并返回 Worker TCP 代理端口。

平台使用同一镜像 profile：

- 创建 Guacamole RDP connection；
- 向实例所有者返回 `RdpHost`、`RdpPort`、`RdpUsername`、`RdpPassword` 和 `RdpUrl`；
- 生成不包含密码的 `.rdp` 文件。

状态响应使用 `Cache-Control: no-store, private`，凭据不得进入日志、测试快照、localStorage 或 sessionStorage。

`.rdp` 默认开启文本剪贴板，关闭磁盘和打印机重定向。用户仍需在 mstsc 中输入页面显示的固定密码。

## 8. 网络配置

Agent 代理默认使用 `46000-55999`。`Kvm:RdpProxyAllowedSources` 应静态允许：

- 平台主站地址；
- Guacamole 地址；
- 实际内网客户端网段；
- 使用 Proxifier 时最终到达 Worker 的代理出口地址。

不要把 Worker RDP 代理直接映射到公网。公网用户继续使用 Guacamole；原生 mstsc 面向内网或既有受控代理通道。

## 9. 销毁

销毁必须清理：

- libvirt domain；
- overlay；
- NVRAM；
- generation/native identity 文件；
- Agent RDP 代理；
- Guacamole connection；
- 活动数据库状态；
- 节点 VM capacity 预留。

不得只删除数据库行。身份不一致时不要猜测修改状态；应核对 generation、native ID 与 `virsh domuuid`，无法证明一致时清理孤儿资源并重新创建。

## 10. 发布验收

自动化门禁：

```powershell
dotnet build src/GZCTF.slnx -c Release
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release

cd src/GZCTF/ClientApp
pnpm validate:locales
pnpm lint:check
pnpm check
pnpm check:architecture
pnpm test
pnpm build
```

真实 KVM 验收：

1. 在隐藏比赛绑定候选镜像。
2. 用两个不同选手账号同时创建 A、B。
3. 核对队列、调度节点、VM identity、overlay、NVRAM 和代理端口。
4. 分别通过 Guacamole 登录。
5. 分别通过内网 mstsc 登录并验证双向文本剪贴板。
6. 核对两个入口不串实例。
7. 销毁 A、B 并核对全部资源与容量回收。
8. 检查主站、Agent 与 Guacamole 日志没有凭据。

## 11. 回滚

上线前记录旧平台 release、Agent release、模板 ID、镜像哈希和题目绑定。发现问题时：

1. 停止候选模板的新实例创建；
2. 销毁候选版本创建的测试实例；
3. 恢复主站与 Agent 到同一兼容 release；
4. 恢复题目原模板绑定；
5. 核对队列、VM、代理、Guacamole 和容量无残留；
6. 保留失败日志和 correlation ID。

Cloudbase-Init 随机凭据字段仍保留在数据库，因此本轮不需要 destructive migration。后续统一 VM 模板管理完成后再单独清理历史字段。
