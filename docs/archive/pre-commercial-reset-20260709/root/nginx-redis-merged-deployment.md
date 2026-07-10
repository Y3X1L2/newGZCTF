# 主服务器一体化部署方案：Nginx + Redis 合并部署

> 分支：`codex/training-platform-polish-20260617`
> 编写日期：2026-06-17
> 适用规模：3000 人以内比赛平台
> 核心目标：退役 frp，用主服务器一体化承载 Nginx 反向代理 + Redis 端口分配

---

## 一、背景与问题

### 1.1 现有 frp 机制的根本问题

现有架构通过 frp 端口池把内网容器端口转发到公网。代码层面通过三个字段实现：

| 配置项 | 位置 | 作用 |
|--------|------|------|
| `PortMappingType` | `src/GZCTF/Models/Internal/Configs.cs:437` | `Default`=端口直连，`PlatformProxy`=WebSocket 代理 |
| `PublicEntry` | `src/GZCTF/Models/Internal/Configs.cs:439` | 所有容器共用的公网入口 IP/域名 |
| `PublicPortStart/End` | `src/GZCTF/Models/Internal/Configs.cs:450-451` | Docker 端口池范围 |

**分布式调度下的致命问题**：

`src/GZCTF/Services/Fleet/FleetContainerManager.cs:131-133` 是问题核心：

```csharp
PublicIP = node!.HostAddress,     // 远程节点的内网IP，如 10.0.7.125
PublicPort = result.PublicPort,   // 远程节点Docker分配的端口
IsProxy = false,                  // 直连模式
```

1. **选手拿到内网 IP，无法访问**：远程节点 `10.0.7.125` 是内网地址，公网选手无法直连
2. **frp 端口池是单机模型**：`src/GZCTF/Services/Container/Manager/DockerManager.cs:615-633` `ResolveHostPortBinding` 只在本机扫描端口，无法跨节点协调
3. **端口分配竞态**：`src/GZCTF/Services/Container/Manager/DockerManager.cs:635-647` `IsTcpPortAvailable` 用 `TcpListener.Start()` 检测，存在 TOCTOU 竞态
4. **frp 单点故障**：单 frps 进程挂掉，所有容器不可达
5. **frp 端口池规模瓶颈**：数千人比赛 × 每人 1-3 容器 = 3000-9000 端口，frps 配置膨胀

### 1.2 现有 Redis 使用情况

代码中 Redis 是可选的，未配置时降级为内存缓存：

- `src/GZCTF/Extensions/Startup/AppBuilderExtensions.cs:61-64`：未配置 `RedisCache` 时用 `AddDistributedMemoryCache`
- `src/GZCTF/Services/Fleet/RedisDistributedLock.cs:29-33`：未配置时降级为本地锁
- `appsettings.Template.json`：`RedisCache` 连接串为空

**确认当前没有独立 Redis 服务器**，需要新增。

---

## 二、方案选型

### 2.1 方案对比

| 方案 | 延迟 | 并发 | 分布式支持 | 运维复杂度 | 适配度 |
|------|------|------|------------|------------|--------|
| **frp 端口池（现状）** | 低 | 差（单点） | 不支持 | 中 | 差 |
| **Nginx stream 四层代理** | 低 | 极高 | 支持 | 低 | **最优** |
| Nginx http 七层代理 | 中 | 高 | 支持 | 低 | 不适用（CTF 多为 TCP） |
| HAProxy 四层 | 低 | 极高 | 支持 | 中 | 可选 |
| PlatformProxy WebSocket | 高 | 差 | 不支持 | 低 | 差 |
| 每节点独立 frp | 低 | 中 | 支持 | 高 | 中 |

### 2.2 为什么选择 Nginx stream

1. **CTF 流量是裸 TCP**（pwn/web/nc），必须四层代理，Nginx `stream` 模块原生支持
2. **Nginx 单进程可承载数万并发连接**，数千人规模绰绰有余
3. **支持动态 upstream**，通过 `map` + 定时同步实现端口动态注册
4. **与现有代码改动最小**：只需修改 `PublicIP`/`PublicPort` 的赋值逻辑
5. **运维成熟**：Nginx 是生产标配，监控/日志/热加载生态完善

### 2.3 为什么合并部署到主服务器

**完全可行**，理由：

| 组件 | CPU | 内存 | 说明 |
|------|-----|------|------|
| Redis | <1 核 | <100MB | 端口映射表 6000 条仅 600KB，单线程 |
| Nginx (HTTP) | <0.5 核 | <50MB | 反代 GZCTF API |
| Nginx (stream) | 1-2 核 | <200MB | 6000 连接 × 16KB buffer ≈ 100MB |

Redis + Nginx 总开销 < 2 核 / 500MB，主服务器如果是 16C32G，占比不到 15%。

**端口无冲突**：

```
主服务器端口分配：
:80/:443    → Nginx HTTP（对外，反代 GZCTF）
:8080       → GZCTF 平台（仅本机访问，被 Nginx 反代）
:5432       → PostgreSQL（仅本机）
:6379       → Redis（仅本机）
:30000-30999 → Nginx stream（对外，CTF 容器代理）
```

---

## 三、一体化架构

```
┌─────────────────────────────────────────────────────────────┐
│                    公网（选手）                               │
│         ctf.example.com:443 (API)                           │
│         ctf.example.com:30000-30999 (容器)                   │
└──────────────────────────┬──────────────────────────────────┘
                           │
        ┌──────────────────▼──────────────────┐
        │      主服务器 10.0.7.118             │
        │  ┌─────────────────────────────┐    │
        │  │  Nginx (:80/:443)           │    │
        │  │  ├─ HTTP 反代 → :8080       │    │
        │  │  ├─ SignalR → :8080         │    │
        │  │  └─ 静态文件缓存            │    │
        │  │  Nginx (:30000-30999)       │    │
        │  │  └─ stream → 查 Redis 转发  │    │
        │  └──────────┬──────────────────┘    │
        │             │                        │
        │  ┌──────────▼──────────┐             │
        │  │  Redis (:6379)      │             │
        │  │  ├─ 端口映射表       │             │
        │  │  ├─ 分布式锁         │             │
        │  │  ├─ SignalR backplane│             │
        │  │  └─ 分布式缓存       │             │
        │  └──────────┬──────────┘             │
        │             │                        │
        │  ┌──────────▼──────────┐             │
        │  │  GZCTF (:8080)      │             │
        │  │  ├─ API             │             │
        │  │  ├─ SignalR         │             │
        │  │  └─ PortAllocator   │             │
        │  └──────────┬──────────┘             │
        │             │                        │
        │  ┌──────────▼──────────┐             │
        │  │  PostgreSQL (:5432) │             │
        │  └─────────────────────┘             │
        │             │                        │
        │  ┌──────────▼──────────┐             │
        │  │  Docker (本地容器)   │             │
        │  └─────────────────────┘             │
        └──────────────────────────────────────┘
                           │
        ┌──────────────────▼──────────────────┐
        │      Worker 节点 10.0.7.125          │
        │  ┌─────────────────────────────┐    │
        │  │  Docker (远程容器)           │    │
        │  │  gzctf-agent (:5001)        │    │
        │  └─────────────────────────────┘    │
        └──────────────────────────────────────┘
```

**核心优势**：
- **零新增服务器**：所有组件装在主服务器
- **网络延迟最低**：Nginx → Redis → GZCTF 全在本机，延迟 <0.1ms
- **运维简单**：一台机器管全部
- **现有 frp 直接退役**：Nginx stream 替代 frps

---

## 四、实施步骤

### 4.1 主服务器安装 Redis + Nginx

```bash
# 1. 安装 Redis
sudo apt install redis-server

# 配置仅本机访问 + 内存限制
sudo tee /etc/redis/redis.conf <<'EOF'
bind 127.0.0.1
port 6379
maxmemory 256mb
maxmemory-policy allkeys-lru
timeout 0
tcp-keepalive 60
save ""  # 不持久化，端口映射表无需落盘
EOF

sudo systemctl restart redis-server

# 2. 安装 Nginx（需 stream 模块）
sudo apt install nginx
# Ubuntu 默认带 stream 模块，确认：
nginx -V 2>&1 | grep stream
```

### 4.2 GZCTF 配置 Redis 连接

修改 `appsettings.Production.json`：

```json
{
  "ConnectionStrings": {
    "Database": "Host=127.0.0.1;Port=5432;Database=gzctf;Username=postgres;Password=xxx",
    "RedisCache": "127.0.0.1:6379",
    "Storage": "disk://path=./files"
  }
}
```

**这一步立即获得的好处**（即使不上 Nginx 代理）：
- `src/GZCTF/Extensions/Startup/AppBuilderExtensions.cs:67-75` SignalR 启用 Redis backplane，支持多实例
- `src/GZCTF/Services/Fleet/RedisDistributedLock.cs:37` 分布式锁真正生效（不再降级本地锁）
- 分布式缓存生效，CronJob 跨实例协调

### 4.3 Nginx 配置

```nginx
# /etc/nginx/nginx.conf

user nginx;
worker_processes auto;
worker_rlimit_nofile 100000;
pid /run/nginx.pid;

events {
    worker_connections 65535;
    use epoll;
    multi_accept on;
}

# ========== HTTP 层：反代 GZCTF 平台 ==========
http {
    upstream gzctf_backend {
        server 127.0.0.1:8080;
        keepalive 64;
    }

    # gzip 压缩
    gzip on;
    gzip_types application/json text/css application/javascript;
    gzip_min_length 1024;

    # 限速防刷
    limit_req_zone $binary_remote_addr zone=api:10m rate=20r/s;

    server {
        listen 80;
        server_name ctf.example.com;
        return 301 https://$host$request_uri;
    }

    server {
        listen 443 ssl http2;
        server_name ctf.example.com;

        ssl_certificate /etc/nginx/ssl/ctf.crt;
        ssl_certificate_key /etc/nginx/ssl/ctf.key;
        ssl_protocols TLSv1.2 TLSv1.3;

        # 客户端上传大小（writeup 等）
        client_max_body_size 50m;

        # 平台 API
        location /api/ {
            limit_req zone=api burst=50 nodelay;
            proxy_pass http://gzctf_backend;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_set_header Connection "";
            proxy_connect_timeout 5s;
            proxy_read_timeout 300s;
            proxy_send_timeout 300s;
        }

        # SignalR（长连接）
        location /hub/ {
            proxy_pass http://gzctf_backend;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
            proxy_read_timeout 86400s;
        }

        # 静态文件（Vite 构建产物，含哈希可长期缓存）
        location /static/ {
            proxy_pass http://gzctf_backend;
            expires 365d;
            add_header Cache-Control "public, immutable";
        }

        # 其他请求
        location / {
            proxy_pass http://gzctf_backend;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        }
    }
}

# ========== Stream 层：CTF 容器 TCP 代理 ==========
stream {
    # 监听整个端口段，通过 map 查询真实 upstream
    # map 配置由 sync-nginx-stream.sh 定时生成
    include /etc/nginx/conf.d/stream-dynamic.conf;
}
```

### 4.4 简化方案：不用 Lua，用定时同步

由于 Nginx 和 GZCTF 在同一台机器，直接让 GZCTF 生成 Nginx 配置，省去 Lua/Redis 查询环节：

```bash
#!/bin/bash
# /opt/gzctf/sync-nginx-stream.sh
# 由 GZCTF 的 CronJob 调用，或定时执行

# 从 GZCTF API 获取当前端口映射
MAP=$(curl -s http://127.0.0.1:8080/api/internal/port-map)

# 生成 stream 配置
cat > /etc/nginx/conf.d/stream-dynamic.conf <<EOF
stream {
    map \$server_port \$upstream_addr {
        default 127.0.0.1:1;  # 黑洞，快速失败
EOF

echo "$MAP" | jq -r '.[] | "        \(.publicPort) \(.ip):\(.port);"' >> /etc/nginx/conf.d/stream-dynamic.conf

cat >> /etc/nginx/conf.d/stream-dynamic.conf <<EOF
    }
    server {
        listen 30000-30999;
        proxy_pass \$upstream_addr;
        proxy_connect_timeout 3s;
        proxy_timeout 3600s;
    }
}
EOF

# 测试并重载
nginx -t 2>/dev/null && nginx -s reload
```

**生成的配置示例**：

```nginx
stream {
    map $server_port $upstream_addr {
        default 127.0.0.1:1;  # 黑洞，快速失败
        30000 172.18.0.5:80;   # Node A 上的容器
        30001 10.0.7.125:32145; # Node B 上的容器
        30002 172.18.0.8:22;   # Node A 上的容器
    }
    server {
        listen 30000-30999;
        proxy_pass $upstream_addr;
        proxy_connect_timeout 3s;
        proxy_timeout 3600s;
    }
}
```

**这个方案的优势**：
- **无需 Lua 模块**：标准 Nginx 即可
- **无需 Redis 查询**：Nginx 内存中直接有映射表
- **Nginx reload 是原子操作**：不影响存量连接
- **map 查询是 O(1)**：性能极高

### 4.5 改进的同步脚本：有变化才 reload

```bash
#!/bin/bash
# /opt/gzctf/sync-nginx-stream.sh
# 由 GZCTF CronJob 每 15 秒调用

NEW_MAP=$(curl -s http://127.0.0.1:8080/api/internal/port-map | jq -S .)
OLD_MAP=$(cat /var/lib/gzctf/port-map.cache 2>/dev/null)

# 端口映射无变化则跳过
if [ "$NEW_MAP" == "$OLD_MAP" ]; then
    exit 0
fi

echo "$NEW_MAP" > /var/lib/gzctf/port-map.cache

# 生成 stream 配置
cat > /etc/nginx/conf.d/stream-dynamic.conf <<EOF
stream {
    map \$server_port \$upstream_addr {
        default 127.0.0.1:1;
EOF

echo "$NEW_MAP" | jq -r '.[] | "        \(.publicPort) \(.ip):\(.port);"' >> /etc/nginx/conf.d/stream-dynamic.conf

cat >> /etc/nginx/conf.d/stream-dynamic.conf <<EOF
    }
    server {
        listen 30000-30999;
        proxy_pass \$upstream_addr;
        proxy_connect_timeout 3s;
        proxy_timeout 3600s;
    }
}
EOF

# 测试并重载
nginx -t 2>/dev/null && nginx -s reload
```

---

## 五、GZCTF 代码改动

### 5.1 新增端口分配服务（用 Redis）

```csharp
// Services/PortAllocationService.cs
using StackExchange.Redis;

namespace GZCTF.Services;

public class PortAllocationService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly int _portStart = 30000;
    private readonly int _portEnd = 30999;

    private static readonly LuaScript AllocateScript = LuaScript.Prepare(@"
        for port = @start, @end do
            if redis.call('SETNX', 'port:' .. port, @containerId) == 1 then
                redis.call('EXPIRE', 'port:' .. port, 7200)
                return port
            end
        end
        return 0
    ");

    public PortAllocationService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<int> AllocatePortAsync(Guid containerId, CancellationToken token)
    {
        var db = _redis.GetDatabase();
        var result = (long)await db.ScriptEvaluateAsync(AllocateScript, new
        {
            start = _portStart,
            end = _portEnd,
            containerId = containerId.ToString()
        });
        return (int)result;
    }

    public async Task ReleasePortAsync(int port, CancellationToken token)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"port:{port}");
    }
}
```

### 5.2 FleetContainerManager 统一走 Nginx 代理

修改 `src/GZCTF/Services/Fleet/FleetContainerManager.cs:125-136`：

```csharp
// 原代码：
// var remoteContainer = new DataContainer
// {
//     ...
//     PublicIP = node!.HostAddress,     // 远程节点的内网IP
//     PublicPort = result.PublicPort,
//     IsProxy = false,
//     ...
// };

// 修改为：
var publicPort = await _portAllocator.AllocatePortAsync(
    Guid.NewGuid(), token);

var remoteContainer = new DataContainer
{
    ContainerId = result.ContainerId,
    Image = config.Image,
    IP = result.IP,                        // 容器内网 IP（Nginx 据此转发）
    Port = result.Port,                    // 容器端口
    PublicIP = _meta.PublicEntry,          // 主服务器公网 IP/域名
    PublicPort = publicPort,               // Nginx 分配的端口（30000-30999）
    IsProxy = false,                       // 直连模式（Nginx stream 代理）
    Status = ContainerStatus.Running,
    NodeId = nodeId.Value,
};
```

同样修改 `CreateOnPreferredNodeAsync` 方法（line 189-200）。

### 5.3 新增端口映射查询 API

```csharp
// Controllers/InternalController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/internal")]
[Authorize] // 仅本机 Nginx 脚本访问，可加 IP 白名单
public class InternalController : ControllerBase
{
    private readonly IContainerRepository _containerRepository;

    public InternalController(IContainerRepository containerRepository)
    {
        _containerRepository = containerRepository;
    }

    [HttpGet("port-map")]
    public async Task<IActionResult> GetPortMap(CancellationToken token)
    {
        // 返回所有活跃容器的端口映射
        var containers = await _containerRepository.GetActiveContainersAsync(token);
        var map = containers
            .Where(c => c.PublicPort.HasValue && c.PublicPort >= 30000)
            .Select(c => new
            {
                publicPort = c.PublicPort!.Value,
                ip = c.IP,
                port = c.Port
            });
        return Ok(map);
    }
}
```

### 5.4 容器销毁时释放端口

修改 `src/GZCTF/Services/Fleet/FleetContainerManager.cs:206` `DestroyContainerAsync`：

```csharp
public async Task DestroyContainerAsync(DataContainer container, CancellationToken token = default)
{
    // 释放 Redis 端口
    if (container.PublicPort is { } port && port >= 30000)
        await _portAllocator.ReleasePortAsync(port, token);

    // 原有销毁逻辑...
    if (!container.NodeId.HasValue)
    {
        await _localManager.DestroyContainerAsync(container, token);
        return;
    }
    // ...
}
```

### 5.5 定时同步 CronJob

```csharp
// Services/CronJob/NginxSyncJob.cs
using System.Diagnostics;

namespace GZCTF.Services.CronJob;

public class NginxSyncJob : ICronJob
{
    private readonly ILogger<NginxSyncJob> _logger;

    // 每 15 秒同步一次端口映射到 Nginx
    public TimeSpan Interval => TimeSpan.FromSeconds(15);

    public NginxSyncJob(ILogger<NginxSyncJob> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/opt/gzctf/sync-nginx-stream.sh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return;

            await process.WaitForExitAsync(token);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(token);
                _logger.LogWarning("Nginx stream sync failed: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nginx stream sync job failed");
        }
    }
}
```

### 5.6 配置变更

`appsettings.Production.json`：

```json
{
  "ConnectionStrings": {
    "Database": "Host=127.0.0.1;Port=5432;Database=gzctf;Username=postgres;Password=xxx",
    "RedisCache": "127.0.0.1:6379",
    "Storage": "disk://path=./files"
  },
  "ContainerProvider": {
    "Type": "Docker",
    "PortMappingType": "Default",
    "PublicEntry": "ctf.example.com",
    "DockerConfig": {
      "PublicPortStart": 30000,
      "PublicPortEnd": 30999
    }
  }
}
```

**关键**：`PublicEntry` 改为公网域名，`PublicPortStart/End` 改为 Nginx stream 监听段。

---

## 六、数据流

### 6.1 选手访问容器

```
选手 → ctf.example.com:30001
    → Nginx stream (:30001)
    → map 查询: 30001 → 10.0.7.125:32145
    → proxy_pass → 10.0.7.125:32145
    → Worker 节点 Docker 容器
```

### 6.2 容器创建流程

```
1. 选手请求创建 → GZCTF API (:8080)
2. FleetContainerManager 调度到 Worker 节点
3. Agent 在 Worker 创建容器，返回 IP:Port (10.0.7.125:32145)
4. GZCTF 调用 PortAllocationService → Redis 原子分配端口 30001
5. GZCTF 保存 container.PublicIP=ctf.example.com, PublicPort=30001
6. CronJob 每 15s 同步端口映射到 Nginx
7. 选手通过 ctf.example.com:30001 访问容器
```

### 6.3 选手访问平台 API

```
选手 → ctf.example.com:443/api/...
    → Nginx HTTP (:443)
    → proxy_pass → 127.0.0.1:8080
    → GZCTF 平台
```

---

## 七、性能与容量

### 7.1 主服务器资源需求（3000 人规模）

| 组件 | CPU | 内存 | 磁盘 |
|------|-----|------|------|
| GZCTF 平台 | 4-6 核 | 4-8 GB | - |
| PostgreSQL | 2-4 核 | 4 GB | 100 GB SSD |
| Redis | <1 核 | 256 MB | - |
| Nginx HTTP | <1 核 | 100 MB | - |
| Nginx stream | 1-2 核 | 200 MB | - |
| 本地 Docker | 2-4 核 | 8-16 GB | - |
| **合计** | **10-16 核** | **16-32 GB** | **100 GB** |

**建议主服务器配置**：16C32G + 200GB SSD，或 32C64G 留足余量。

### 7.2 Nginx stream 性能

- 3000 选手 × 2 连接 = 6000 活跃连接
- Nginx stream 单实例可处理 5万+ 并发连接
- `map` 查询是内存哈希表，O(1)，纳秒级
- **瓶颈在网络带宽**：3000 人 × 1Mbps = 3Gbps，需万兆网卡或 BGP 多线

### 7.3 Redis 性能

- 端口分配：Lua 脚本原子执行，<1ms
- 端口映射表：6000 条 × 100 字节 = 600KB
- 同时承担 SignalR backplane + 分布式锁 + 缓存
- 总 QPS < 1万，Redis 单实例 10万+ QPS，绰绰有余

### 7.4 Nginx reload 影响

- `nginx -s reload` 是优雅重载，不影响存量连接
- 新连接使用新配置
- reload 耗时 <100ms
- 每 15 秒一次完全无感

---

## 八、风险与应对

### 8.1 单点风险

**风险**：主服务器挂了，全部不可用。

**应对**：
- 主服务器用 PVE 虚拟机，支持快照和迁移
- 数据库定时备份（pg_dump）
- Redis 不持久化（端口映射可重建）
- 准备冷备机，PVE 故障可快速恢复

### 8.2 资源竞争

**风险**：Docker 容器和 Nginx/Redis 抢 CPU。

**应对**：
- 用 systemd 限制 GZCTF 和 Docker 的 CPU 配额
- Docker 容器本身有 CPU 限制（`config.CPUCount`）
- Nginx/Redis 优先级高，Docker 优先级低
- 大型比赛时主服务器不调度容器（仅 Worker 节点跑容器）

```bash
# 限制 GZCTF 最多用 8 核
sudo systemctl set-property gzctf.service CPUQuota=800%

# 限制 Docker 容器资源
# 已在代码中通过 config.CPUCount/MemoryLimit 控制
```

### 8.3 Nginx reload 频率

**风险**：高频创建容器时 reload 过于频繁。

**应对**：
- 同步脚本加去重：端口映射无变化则不 reload
- 合并多次变更：15 秒窗口内的变更一次 reload
- 大型比赛开始前预热容器，避免开赛瞬间高频创建

### 8.4 Redis 不可用

**风险**：Redis 挂了，端口分配失败。

**应对**：
- Redis 配置持久化（save ""），重启后端口映射表丢失
- GZCTF 启动时扫描数据库中活跃容器，重建 Redis 端口映射表
- Nginx map 配置独立于 Redis，Redis 挂了不影响存量连接

---

## 九、迁移路径

### 阶段 1：装 Redis（立即收益，零风险）

1. 主服务器装 Redis
2. 配置 `RedisCache` 连接串
3. SignalR backplane + 分布式锁 + 缓存生效
4. **不影响现有 frp**

### 阶段 2：装 Nginx HTTP 反代（低风险）

1. 主服务器装 Nginx
2. 配置 HTTP 反代 GZCTF（:443 → :8080）
3. 选手访问入口从 :8080 改为 :443
4. **frp 端口池仍保留**

### 阶段 3：Nginx stream 代理容器（核心改动）

1. 新增 `PortAllocationService`
2. 修改 `FleetContainerManager`，新容器走 Nginx 端口
3. 部署 stream 同步脚本
4. **灰度**：新容器走 Nginx，存量容器保持 frp
5. 存量容器销毁后，下线 frp

### 阶段 4：优化（赛后）

1. Nginx stream 改 Lua 动态查询（如需更低延迟）
2. 加监控（Nginx status、Redis info、端口使用率）
3. 考虑主服务器 HA（如规模增长超 3000 人）

---

## 十、与原方案对比

| 维度 | 原方案（独立 Nginx+Redis） | 合并部署方案 |
|------|--------------------------|-------------|
| 服务器数量 | 主 + 2 Nginx + 1 Redis + N Worker | 主 + N Worker |
| 网络延迟 | Nginx→Redis 0.5ms + Redis→GZCTF 0.5ms | 全本机 <0.1ms |
| 运维复杂度 | 4 类服务器 | 2 类服务器 |
| 单点故障 | Nginx/Redis 可 HA | 主服务器是单点 |
| 成本 | 多 2-3 台服务器 | 零新增 |
| Nginx 配置 | 需 Lua 动态查询 | 静态 map + 定时同步 |
| **适用规模** | 5000+ 人 | **3000 人以内** |

---

## 十一、结论

**完全可以让主服务器一体化部署 Nginx + Redis**，而且这是当前规模下的最优选择：

1. **零新增服务器**：主服务器 16C32G 足够承载全部组件
2. **性能更好**：全本机通信，延迟 <0.1ms
3. **运维更简单**：一台机器管全部，无需多机协调
4. **方案更简单**：Nginx 静态 map + 定时同步，无需 Lua 模块
5. **适用 3000 人规模**：超过 5000 人再考虑拆分

**唯一限制**：主服务器成为单点。对于 CTF 平台这是可接受的——比赛期间专人值守，PVE 快照兜底，赛后可优化 HA。

建议按阶段 1→2→3 迁移，每个阶段独立可验证，风险可控。

---

## 附录 A：完整 Nginx 配置文件

```nginx
# /etc/nginx/nginx.conf

user nginx;
worker_processes auto;
worker_rlimit_nofile 100000;
pid /run/nginx.pid;

events {
    worker_connections 65535;
    use epoll;
    multi_accept on;
}

http {
    upstream gzctf_backend {
        server 127.0.0.1:8080;
        keepalive 64;
    }

    gzip on;
    gzip_types application/json text/css application/javascript;
    gzip_min_length 1024;

    limit_req_zone $binary_remote_addr zone=api:10m rate=20r/s;

    server {
        listen 80;
        server_name ctf.example.com;
        return 301 https://$host$request_uri;
    }

    server {
        listen 443 ssl http2;
        server_name ctf.example.com;

        ssl_certificate /etc/nginx/ssl/ctf.crt;
        ssl_certificate_key /etc/nginx/ssl/ctf.key;
        ssl_protocols TLSv1.2 TLSv1.3;

        client_max_body_size 50m;

        location /api/ {
            limit_req zone=api burst=50 nodelay;
            proxy_pass http://gzctf_backend;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_set_header Connection "";
            proxy_connect_timeout 5s;
            proxy_read_timeout 300s;
            proxy_send_timeout 300s;
        }

        location /hub/ {
            proxy_pass http://gzctf_backend;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
            proxy_read_timeout 86400s;
        }

        location /static/ {
            proxy_pass http://gzctf_backend;
            expires 365d;
            add_header Cache-Control "public, immutable";
        }

        location / {
            proxy_pass http://gzctf_backend;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        }
    }
}

stream {
    include /etc/nginx/conf.d/stream-dynamic.conf;
}
```

## 附录 B：Redis 配置文件

```conf
# /etc/redis/redis.conf

bind 127.0.0.1
port 6379
maxmemory 256mb
maxmemory-policy allkeys-lru
timeout 0
tcp-keepalive 60
save ""
```

## 附录 C：同步脚本

```bash
#!/bin/bash
# /opt/gzctf/sync-nginx-stream.sh
# 由 GZCTF CronJob 每 15 秒调用

NEW_MAP=$(curl -s http://127.0.0.1:8080/api/internal/port-map | jq -S .)
OLD_MAP=$(cat /var/lib/gzctf/port-map.cache 2>/dev/null)

if [ "$NEW_MAP" == "$OLD_MAP" ]; then
    exit 0
fi

echo "$NEW_MAP" > /var/lib/gzctf/port-map.cache

cat > /etc/nginx/conf.d/stream-dynamic.conf <<EOF
stream {
    map \$server_port \$upstream_addr {
        default 127.0.0.1:1;
EOF

echo "$NEW_MAP" | jq -r '.[] | "        \(.publicPort) \(.ip):\(.port);"' >> /etc/nginx/conf.d/stream-dynamic.conf

cat >> /etc/nginx/conf.d/stream-dynamic.conf <<EOF
    }
    server {
        listen 30000-30999;
        proxy_pass \$upstream_addr;
        proxy_connect_timeout 3s;
        proxy_timeout 3600s;
    }
}
EOF

nginx -t 2>/dev/null && nginx -s reload
```

## 附录 D：systemd 定时器（可选，替代 CronJob）

```ini
# /etc/systemd/system/gzctf-nginx-sync.service
[Unit]
Description=GZCTF Nginx Stream Sync

[Service]
Type=oneshot
ExecStart=/opt/gzctf/sync-nginx-stream.sh
```

```ini
# /etc/systemd/system/gzctf-nginx-sync.timer
[Unit]
Description=GZCTF Nginx Stream Sync Timer

[Timer]
OnBootSec=10s
OnUnitInactiveSec=15s
AccuracySec=1s

[Install]
WantedBy=timers.target
```

```bash
sudo systemctl enable --now gzctf-nginx-sync.timer
```
