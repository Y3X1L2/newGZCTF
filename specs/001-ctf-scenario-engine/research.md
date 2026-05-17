# 技术调研: CTF 场景化实战平台

**Feature**: 001-ctf-scenario-engine
**Date**: 2026-05-16

## 1. Windows 靶机虚拟化方案

**Decision**: KVM/QEMU + libvirt

**Rationale**:
- KVM 是 Linux 内核原生虚拟化技术，性能最优，无需额外授权费用
- libvirt 提供完整的 C API 和命令行工具 (virsh)，可通过 .NET 绑定或 Process 调用进行生命周期管理
- 支持 .qcow2 格式的磁盘镜像（快照、写时复制），天然适配"每个选手独立环境副本"的需求
- 支持通过快照快速恢复至初始状态（环境重置功能），`virsh snapshot-revert` 可在数秒内完成
- GZCTF 部署在单台 Linux 服务器上，KVM 可直接利用宿主机硬件虚拟化能力

**Alternatives considered**:
- VMware Workstation/ESXi: 商业授权成本高，vmrun CLI 功能有限
- VirtualBox: 性能不如 KVM，生产环境稳定性存疑
- 外部 Hyper-V 服务器: 引入额外硬件依赖，违反单机部署约束

**Integration approach**: 通过编写 `VmManager` 服务，使用 `System.Diagnostics.Process` 调用 `virsh` 命令行工具，封装 VM 的创建（从 qcow2 模板克隆）、启动、暂停、快照恢复、销毁等操作。避免引入 C 库绑定带来的跨平台兼容性问题。

## 2. 选手并发与资源调度模型

**Decision**: 预约分时制

**Rationale**:
- 单机服务器资源有限，无法同时支持 200 个 Windows VM（每个至少 2GB RAM）
- 预约分时制与 CTF 竞赛的天然分时特性吻合（不同队伍在不同时段参赛）
- 进度数据持久保留，选手可在下一时段无缝继续
- 时间段结束后自动回收 VM/容器资源，防止资源泄漏

**Alternatives considered**:
- 降低并发目标: 不解决根本问题，200 人仍需要环境
- 按需创建 + 自动休眠: 实现复杂度高，VM 休眠/恢复时间不可控（可达分钟级）
- 瘦环境策略: 仅部分缓解，不能完全解决问题

**Implementation**: FR-020 定义预约分时制；需要新增 `TimeSlot` 实体和调度服务；时间段结束前 N 分钟给予选手提醒。

## 3. Windows 靶机选手访问方式

**Decision**: 分场景策略
  - 攻击场景 (US1): 选手自行搭建内网隧道访问内部 Windows 靶机（渗透挑战的一部分）
  - IR 场景 (US2): 纯 Windows 靶机通过 Web 桌面代理（Apache Guacamole）在浏览器内访问

**Rationale**:
- 攻击场景保留"自行渗透"的挑战性——内网穿透、代理转发本就是考察能力的一环
- IR 场景提供便捷的 Web 桌面代理——IR 关注的是"修复与响应"而非"如何连进去"
- Apache Guacamole 是成熟的 HTML5 远程桌面网关，支持 RDP 协议，无需客户端安装
- Guacamole 通过 Docker 部署，与 GZCTF 容器化运维模式一致

**Alternatives considered**:
- RDP 直连: 需选手安装 RDP 客户端，暴露 3389 端口有安全风险
- WinRM: 纯命令行，无法满足需要 GUI 操作的应急响应场景（如事件查看器、注册表编辑）
- noVNC: 需要 VNC Server，Windows 原生不支持，需额外安装配置

**Integration approach**: Guacamole 作为独立容器部署，GZCTF 后端通过 Guacamole REST API 动态创建连接配置，前端通过 Guacamole JavaScript 客户端嵌入 Web 桌面。

## 4. 环境模板（镜像）管理

**Decision**: 混合方案
  - Linux 靶机: Docker/OCI Registry 拉取（Docker Hub、Harbor 等）
  - Windows 靶机: Web 后台上传 VM 磁盘镜像（.qcow2/.ova/.vmdk）至本地存储池

**Rationale**:
- Docker 镜像有成熟的 Registry 生态，支持分层存储和增量拉取，适合频繁部署的 Linux 靶机
- VM 磁盘镜像文件巨大（通常 10-50GB），不适合推送到容器 Registry
- 本地存储避免了每次创建环境时从远程拉取大文件的时间开销
- Web 后台上传降低管理员操作门槛，无需 SSH 登录服务器

**Alternatives considered**:
- 全部手动部署: 运维负担重，不符合"10 分钟创建场景"的 SC-001 目标
- 全部通过 URL 引用: VM 镜像文件托管方案不成熟，传输时间长
- 对象存储 (MinIO/S3): 引入额外组件，单机部署不需要

**Implementation**: FR-023/FR-024/FR-025；新增 `ImageStorage` 服务管理本地镜像文件存储和元数据；上传时校验文件格式和大小；支持断点续传（大文件场景）。

## 5. GZCTF 集成模型

**Decision**: Game 下扩展新 Challenge 子类型

**Rationale**:
- GZCTF 已有成熟的 Game（赛事）→ Challenge（题目）→ Submission（提交）三层模型
- Scenario 和 IRChallenge 作为 Challenge 的子类型，自然复用 Game 的时间窗口、权限控制、排行榜
- 避免破坏 GZCTF 现有 API 和数据模型，保障向下兼容
- 前台选手在 Game 详情页看到混合的题目列表（传统单题 + 场景 + IR），体验统一

**Alternatives considered**:
- 完全独立模块: 导致管理界面和 API 碎片化，增加维护成本
- Scenario 作为 Game 的容器: 过于颠覆 GZCTF 现有层级，迁移成本高
- 混合层级: 层级关系复杂，容易引起混乱

**Implementation approach**: 
- 在 GZCTF Challenge 实体中增加 `ChallengeType` 判别字段（Standard / Scenario / IRChallenge）
- 使用 EF Core 的 Table-Per-Hierarchy (TPH) 或 Table-Per-Type (TPT) 继承策略
- 新增独立的 Controller 和前端页面处理场景/IR 特有的创建和交互逻辑

## 6. 网络隔离实现方案

**Decision**: Linux Bridge + iptables/nftables 规则，结合 libvirt 虚拟网络

**Rationale**:
- 场景阶段间的网络隔离（如阶段 B 只能从阶段 A 的容器/VM 访问）需要 OS 级网络控制
- Linux Bridge 可将 Docker 容器和 KVM VM 纳入同一二层网络，实现统一拓扑管理
- iptables/nftables 规则提供精细的访问控制（源 IP、端口过滤、单向连通等）
- libvirt 的虚拟网络 (virbr) 可与 Linux Bridge 桥接，简化 VM 网络配置
- 单机部署无需 SDN/Overlay 网络，直接操作主机网络栈性能最优

**Alternatives considered**:
- Open vSwitch (OVS): 功能强大但配置复杂，单机部署过度设计
- 纯 Docker 网络: 无法管理 KVM VM 的网络
- SDN (如 Weave/Calico): 引入额外组件和网络开销，单机环境不适用

**Implementation**: `EnvironmentService` 在创建场景实例时动态创建 Linux Bridge 和 iptables 规则；场景结束时清理网络资源。

## 7. Apache Guacamole 集成方案

**Decision**: Guacamole 作为 Docker 容器部署，通过 REST API 和 JavaScript 客户端集成

**Rationale**:
- Guacamole 提供 `guacd` (代理守护进程) + `guacamole-client` (Web 服务) 的标准 Docker 部署
- REST API 支持动态创建/删除连接配置，无需重启服务
- JavaScript 客户端 (`guacamole-common-js`) 可嵌入 React 页面，提供完整的远程桌面交互
- 支持剪贴板共享和文件传输（需额外配置）

**Implementation**:
- Docker Compose 中加入 `guacd` 和 `guacamole` 服务
- GZCTF 后端通过 HTTP 调用 Guacamole REST API 创建/管理连接
- 前端组件封装 Guacamole JS 客户端，提供统一的远程桌面交互体验
- 每个选手的 IR 实例创建独立的 Guacamole 连接，实例结束时销毁连接

## 8. .NET 与 libvirt 交互方案

**Decision**: 通过 `virsh` CLI 封装，而非原生 libvirt C 绑定

**Rationale**:
- `virsh` 是 libvirt 的标准 CLI 工具，功能完整，输出可解析（`--output json`）
- 避免引入 C 库依赖和 P/Invoke 兼容性问题
- 便于调试和运维——管理员可直接在命令行执行相同的 virsh 命令排查问题
- .NET 中通过 `Process.Start()` 调用外部命令是成熟模式

**Implementation**: `VmManager` 服务封装 `virsh` 命令调用，使用异步 Process API，设置超时和错误处理。
