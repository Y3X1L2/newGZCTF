# 快速启动指南: CTF 场景化实战平台

**Feature**: 001-ctf-scenario-engine
**Date**: 2026-05-16

## 前提条件

### 硬件要求
- Linux 服务器 (Ubuntu 22.04+ 或 Debian 12+)
- CPU 支持 Intel VT-x 或 AMD-V 硬件虚拟化
- 内存 ≥ 32GB（推荐 64GB+，用于运行 Windows VM）
- 磁盘 ≥ 200GB（用于存储 VM 磁盘镜像和 Docker 镜像）

### 软件依赖
- .NET 9 SDK
- Node.js 22+ & pnpm
- Docker & Docker Compose v2
- KVM + QEMU + libvirt (`apt install qemu-kvm libvirt-daemon-system libvirt-clients`)
- Apache Guacamole (通过 Docker Compose 部署)
- PostgreSQL 16+ & Redis 7+

### 基础项目
- GZCTF 项目克隆并已可正常运行
- 当前工作在 `001-ctf-scenario-engine` 特性分支

## 安装步骤

### 1. 安装 KVM/libvirt 环境

```bash
# 安装虚拟化组件
sudo apt update
sudo apt install -y qemu-kvm libvirt-daemon-system libvirt-clients virtinst bridge-utils
# 验证 KVM 可用
sudo kvm-ok
# 启动 libvirt 服务
sudo systemctl enable --now libvirtd
# 将当前用户加入 libvirt 组
sudo usermod -aG libvirt $USER
```

### 2. 准备好 Guacamole 服务

```bash
# 在 docker-compose.yml 中添加 Guacamole 服务
# guacd (代理守护进程) + guacamole (Web 服务)
# 详细配置见 contracts/ir-challenge-api.md
docker compose up -d guacd guacamole
```

### 3. 配置存储目录

```bash
# VM 磁盘镜像存储目录
sudo mkdir -p /var/lib/gzctf/images
sudo chown -R $USER:libvirt /var/lib/gzctf/images
# Docker 镜像通过 GZCTF 现有 Docker 配置管理
```

### 4. 数据库迁移

```bash
cd src/GZCTF
dotnet ef database update
# 新迁移将创建 Scenarios, Stages, IRChallenges 等相关表
```

### 5. 启动开发环境

```bash
# 后端 (GZCTF)
cd src/GZCTF
dotnet run
# 后端启动在 http://localhost:8080

# 前端 (React dev server)
cd src/GZCTF/ClientApp
pnpm install
pnpm dev
# 前端启动在 http://localhost:5173
```

## 验证安装

### 1. 上传测试用 VM 镜像
- 登录管理后台 → 环境模板 → 上传
- 上传一个轻量级 Windows/Linux 镜像用于测试
- 确认镜像状态显示为 "Ready"

### 2. 创建测试场景
- 进入赛事管理 → 创建场景
- 添加 2 个阶段，配置网络规则
- 发布场景

### 3. 选手侧测试
- 预约时间段 → 启动场景实例
- 确认环境自动创建完成（SignalR 通知）
- 提交 Flag → 验证阶段解锁
- 检查排行榜更新

## 常见问题

**Q: KVM 报错 "No hardware virtualization support"**
A: 在 BIOS 中开启 Intel VT-x/AMD-V；云服务器需使用裸金属实例。

**Q: VM 启动缓慢 (> 60秒)**
A: 使用 qcow2 格式的预装镜像，而非从 ISO 安装；配置 virtio 驱动提升磁盘/网络性能。

**Q: Guacamole 连接失败**
A: 检查 `guacd` 容器是否运行；确认 VM 的 RDP 服务已启用；验证网络连通性。

**Q: 磁盘空间不足**
A: 使用 qcow2 的 backing file 机制——多个 VM 实例共享基础镜像，只存储差异数据。

## 部署架构图

```
┌──────────────────────────────────────────────────┐
│               Linux Host Server                   │
│                                                   │
│  ┌──────────┐  ┌──────────┐  ┌───────────────┐  │
│  │ GZCTF    │  │ Guacamole│  │ Docker Engine │  │
│  │ (ASP.NET)│  │ (Web RDP)│  │ (Linux 靶机)  │  │
│  └────┬─────┘  └────┬─────┘  └───────┬───────┘  │
│       │              │               │           │
│  ┌────┴──────────────┴───────────────┴────┐      │
│  │            PostgreSQL + Redis           │      │
│  └─────────────────────────────────────────┘      │
│                                                   │
│  ┌─────────────────────────────────────────┐     │
│  │     KVM/QEMU + libvirt (Windows 靶机)   │     │
│  │  ┌──────┐  ┌──────┐  ┌──────┐          │     │
│  │  │VM #1 │  │VM #2 │  │VM #3 │  ...     │     │
│  │  └──────┘  └──────┘  └──────┘          │     │
│  └─────────────────────────────────────────┘     │
└──────────────────────────────────────────────────┘
```
