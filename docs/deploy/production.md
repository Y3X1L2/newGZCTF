# YINYU CTF平台 生产部署指南

## 前置要求
- Docker 24+ 和 Docker Compose v2
- 至少 4GB RAM, 20GB 磁盘
- 端口 8080, 5432, 6379, 4822 可用

## 部署步骤

### 1. 克隆代码
```bash
git clone <internal-repository-url> yinyu-ctf-platform
cd yinyu-ctf-platform
```

### 2. 配置环境变量
```bash
export DB_PASSWORD=<your-secure-password>
```

### 3. 启动服务
```bash
docker compose up -d
```

### 4. 验证
```bash
curl http://localhost:8080/api/info
```

### 5. 默认管理员
首次启动后，通过 API 创建管理员账户。

## 添加靶机服务器
1. 登录管理面板
2. 进入"节点管理"页面
3. 点击"+ 添加靶机服务器"
4. 填写靶机的 IP、SSH 用户名、密码
5. 平台自动检测 Docker/KVM 能力并注册节点
6. 创建题目时选择目标节点部署

## 一键清理
```bash
docker compose down -v
```
