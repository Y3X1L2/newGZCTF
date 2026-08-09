# 比赛 Windows VM 固定凭据改造计划

> 状态：代码与自动化门禁完成，等待真实 KVM 验收
> 更新时间：2026-08-09
> 代码基线：`cb801a7`
> 开发分支：`codex/windows-vm-repair`
> 生产环境：本阶段不修改

## 1. 目标

仅完善普通 CTF 比赛的 Windows VM：

```text
比赛题目
  -> 选择 Windows QCOW2 镜像
  -> 读取镜像预配置的固定 RDP 连接凭据
  -> KVM 节点启动镜像
  -> Agent 建立内网 RDP TCP 代理
  -> 平台提供 Guacamole 和原生 mstsc 两种入口
  -> 选手销毁实例后回收全部运行资源
```

本轮采用镜像已有账号，不生成每实例随机密码，不向 Windows 注入 Cloudbase-Init user-data。

## 2. 已确认事实

- 最新开发基线是 `origin/main@cb801a7`。
- 比赛 Windows VM 已具备题目配置、实例事实、统一队列、KVM 调度、镜像分发、Agent 启停、RDP 代理、Guacamole 和前端运行面板。
- `ImageTemplateRemoteAccess` 已提供镜像级协议、端口、用户名和固定凭据配置。
- Agent 已提供 `46000-55999` 范围内的 VM RDP TCP 代理。
- 候选镜像模板 1 的 SHA-256 为 `81e45879880a016760d0507cc347cc43a32d2a7725540743f55cd1cc1eb6eefe`。
- 该镜像通过 QCOW2 完整性检查，RDP 与剪切板重定向已开启，存在 `player` 用户，历史实机可通过 `e1000e` 获取 DHCP。
- 候选镜像仍需完成固定密码、远程桌面用户组、Evaluation 有效期、mstsc/Guacamole 和双实例实机验收。

## 3. 范围边界

### 3.1 本轮实现

- 普通 CTF Windows VM 的固定镜像凭据模式；
- 管理端镜像远程访问配置作为唯一凭据来源；
- 无 Cloudbase-Init 的 KVM 创建与调度；
- Guacamole 浏览器 RDP；
- 内网原生 mstsc 地址、用户名、密码复制和 `.rdp` 下载；
- 3389 就绪判断、状态查询、销毁和资源回收；
- 自动化测试和真实 KVM 验收文档。

### 3.2 本轮不实现

- 培训课程 Windows VM；
- TeamLab VM、拓扑组网或 Guest Supervisor 改造；
- 公网原生 RDP 映射；
- 动态客户端 IP 授权；
- Windows 快照、暂停、迁移、域管理；
- 每实例随机密码、CIDATA 或 Cloudbase-Init 首启注入。

### 3.3 删除边界

从普通比赛 Windows VM 活跃路径删除：

- `VmCredentialService.Initialize` 随机密码创建；
- `BuildWindowsCloudInit`；
- `SupportsInstanceCredentials` 运行门禁；
- KVM 节点的 Cloud-Init capability 门禁；
- 新实例对 `RdpPasswordProtected` 的依赖。

不全局删除 Agent、TeamLab、镜像工厂或 Linux VM 使用的 Cloud-Init/Cloudbase 基础设施。共享能力的全局删除会破坏 Phase 9，且不属于比赛 VM 修复范围。

历史 `VmInstance.RdpPasswordProtected` 与 `ImageTemplate.SupportsInstanceCredentials` 数据先保留，只作为兼容数据，不再参与新比赛 VM。待生产活动旧实例清理且统一镜像迁移完成后，另行删除数据库字段。

## 4. 设计约束

### 4.1 镜像配置

管理员使用现有镜像远程访问面板配置：

```text
Enabled = true
Protocol = RDP
Port = 3389
CredentialMode = ExistingAccount
Username = 镜像内固定账号
Credential = 镜像内固定密码
```

Data Protection 仅作为已有持久化实现，不引入随机密码、实例隔离、动态授权或额外业务步骤。

### 4.2 创建与调度

- 比赛 Controller 只校验比赛、题目和用户权限并提交 VM 用例。
- VM application/service 加载题目镜像及远程访问配置。
- Windows 固定凭据镜像只要求 `KVM + VM image download` 能力。
- Agent 创建请求不包含 Cloud-Init、CIDATA 或 user-data。
- Windows 默认使用当前已验证的 UEFI/q35/SATA/e1000e 兼容路径。

### 4.3 访问入口

Guacamole 与 mstsc 使用同一份镜像固定凭据。

状态模型增加：

```text
RdpHost
RdpPort
RdpUsername
RdpPassword
RdpUrl
```

访问数据只返回给实例所有者，响应使用 `Cache-Control: no-store`，不得写入日志或浏览器持久化存储。

前端提供：

- 打开浏览器远程桌面；
- 复制 `host:port`；
- 复制用户名；
- 复制密码；
- 下载启用剪切板且禁用磁盘/打印机重定向的 `.rdp` 文件；
- 销毁靶机。

Agent 使用已有静态 `Kvm:RdpProxyAllowedSources`。部署时允许内网或 Proxifier 出口网段，不增加按用户动态授权。

### 4.4 就绪和清理

- 获得 DHCP 地址不等于 RDP 就绪。
- Agent/平台必须确认目标 RDP 端口可连接后再建立可交付入口。
- 销毁继续回收 domain、overlay、NVRAM、RDP 代理、Guacamole 和容量预留。
- 不以增大超时掩盖启动失败；Windows 约 170 秒冷启动属于当前 10 分钟边界内的正常行为。

## 5. 实施步骤

### A. 固定凭据运行契约

- 新增比赛 Windows 镜像配置验证器/查询服务。
- 题目保存时要求 Ready 的 Windows VM 镜像具有有效 ExistingAccount RDP 配置。
- 创建时不生成实例随机密码，不构建 Cloudbase user-data。
- 调度不要求 Cloud-Init capability。
- 为旧随机凭据实例保留只读兼容，不允许新建旧模式实例。

### B. 统一访问解析

- 抽取比赛 VM 访问解析服务，集中解析 Worker RDP 代理端点和镜像固定凭据。
- `VmReadyService` 使用该服务创建 Guacamole 连接。
- 比赛状态 API 使用同一服务返回原生 RDP 信息，避免 Controller 直接访问 Agent 或数据库内部实现。
- 增加 RDP 端口就绪检查。

### C. 前端交互

- 扩展 VM 状态 contract 和 feature adapter。
- 在现有 Windows VM 面板中增加原生 RDP 信息与 `.rdp` 下载。
- 保留现有创建、排队、错误、Guacamole 和销毁状态。
- 不在 localStorage/sessionStorage 保存连接凭据。

### D. 清理旧比赛路径

- 删除比赛运行路径的随机密码生成和 Cloudbase user-data 构造。
- 删除管理端针对普通比赛的旧 `SupportsInstanceCredentials` 认证入口或误导文案。
- 更新 Windows VM 文档，明确比赛固定凭据与 TeamLab prepared image 是不同运行契约。
- 不修改历史 migration；需要删除字段时使用后续前向 migration。

### E. 验证

自动化：

- 固定 RDP 配置缺失时禁止保存/启动 Windows 比赛题；
- 新比赛 VM 不生成随机密码，不发送 Cloud-Init；
- Windows 比赛 VM 不要求 Cloud-Init capability；
- 只有实例所有者能获取原生 RDP 凭据；
- Guacamole 与 mstsc 使用相同镜像凭据；
- RDP 未监听时不标记入口就绪；
- 销毁清理 VM、代理、Guacamole 和容量；
- Docker、Linux、TeamLab 和培训现有测试不回归。

真实环境：

1. 在隐藏比赛中绑定候选镜像。
2. 同时创建 A、B 两个选手实例。
3. 验证不同 VM IP、Worker 代理端口和运行身份。
4. 使用固定凭据分别通过 Guacamole 与 `mstsc` 登录。
5. 验证 mstsc 双向文本剪切板。
6. 执行 `slmgr /xpr` 核对 Evaluation 有效期。
7. 销毁 A、B，核对 domain、overlay、NVRAM、代理、Guacamole 和容量无残留。
8. 确认公网没有直接暴露 Worker RDP 端口。

## 6. 持久开发清单

- [x] 锁定仅比赛 Windows VM 的产品范围
- [x] 核对最新主线和现有运行链路
- [x] 核对候选镜像文件、哈希、系统和静态 RDP 配置
- [x] 将实施计划写入仓库并重新读取
- [x] 实现固定镜像凭据验证和查询
- [x] 删除新比赛实例的随机密码和 Cloudbase 依赖
- [x] 实现共享访问端点解析与 RDP 就绪判断
- [x] 扩展比赛 VM 状态 API
- [x] 完成原生 RDP 前端交互和 `.rdp` 下载
- [x] 清理普通比赛的旧 Cloudbase 管理入口和文案
- [x] 完成定向后端、前端和架构测试
- [x] 完成全量构建与回归门禁
- [ ] 在真实 KVM 环境完成双实例人工验收
- [x] 更新 `docs/development/current-state.md` 和当前执行记录

## 7. 进度记录

| 时间 | 状态 | 结果 |
| --- | --- | --- |
| 2026-08-09 | 已完成 | 范围收敛为普通比赛 Windows VM；培训不进入本轮。 |
| 2026-08-09 | 已完成 | 候选镜像通过哈希和 QCOW2 静态检查；真实固定凭据登录待验收。 |
| 2026-08-09 | 已完成 | 从 `origin/main@cb801a7` 实现固定镜像凭据运行链路；生产环境尚未修改。 |
| 2026-08-09 | 已完成 | 固定 RDP 配置成为题目保存、VM 创建、Guacamole 和原生 RDP 的统一来源；新比赛实例不再生成随机密码或发送 Cloudbase user-data。 |
| 2026-08-09 | 已完成 | Agent 仅在镜像配置的 RDP 目标端口可连接后发布 TCP 代理；主站与 Agent Release 编译通过。 |
| 2026-08-09 | 已完成 | vNext 提供 Guacamole、内网 RDP 信息复制和 `.rdp` 下载；旧普通比赛 Cloudbase 认证入口与误导文档已清理。 |
| 2026-08-09 | 已完成 | 最终审计修正统一调度器残留的 Cloud-Init 门禁，比赛 VM 改为要求 KVM 与 VM image download；比赛编辑器不再按旧 `SupportsInstanceCredentials` 字段隐藏固定账号镜像。 |
| 2026-08-09 | 已完成 | 删除无调用者的旧 `instance-credentials` 写接口和前端 adapter；数据库字段只保留为历史兼容数据，不再提供新写入入口。 |
| 2026-08-09 | 已完成 | Release 解决方案 0 warning/0 error；后端单元 `763/763`、集成 `265/265`、前端 `218/218` 及 locale/lint/TypeScript/架构/build 全部通过。 |
| 2026-08-09 | 已确认 | 实时 OpenAPI 包含新增 RDP 字段；完整 `genapi` 同时产生约 2 万行历史契约漂移，本分支继续使用 feature adapter，生成快照治理仍按现有缺口文档单独处理。 |
| 2026-08-09 | 待执行 | 候选镜像固定凭据、双实例、Guacamole、mstsc、剪贴板、许可有效期和销毁残留需要真实 KVM 环境验收。 |
