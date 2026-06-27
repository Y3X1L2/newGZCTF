# Redis + Nginx 容器公网端口代理使用说明

## 架构结论

当前实现不是 FRP 架构，也不是让 Nginx 每次连接实时查询 Redis。

新的链路是：

1. GZCTF/Fleet 创建普通 Docker 容器。
2. Agent 在 Worker 节点上按 `Docker.PublicPortStart/PublicPortEnd` 发布一个 Worker 本地端口。
3. 主服务用 Redis 原子分配一个统一公网端口。
4. Nginx stream 网关监听统一公网端口段，并转发到 `WorkerNode.HostAddress:AgentPublishedPort`。
5. 玩家看到的入口是 `ContainerProvider.PublicEntry:UnifiedPublicPort`。

Redis 的职责是端口池分配和分布式协调；Nginx 的职责是 TCP stream 转发；数据库是当前运行容器映射的权威来源。

启用 `NginxProxyConfig.Enable=true` 时必须配置 `ConnectionStrings:RedisCache`。平台不会在 Nginx 模式下使用本地端口扫描兜底，否则多实例/多节点场景可能重复分配玩家公网端口。

## 固定内网 Registry 边界

当前 Docker 镜像存储固定为 `10.24.0.28:5000`，不再由节点管理页面切换存储服务器。这个 Registry 是平台内网基础设施，只用于主服务和调度节点之间分发题目镜像，不应映射到公网。

安全边界要求：

1. `10.24.0.28:5000` 只允许主服务器和受信任 Worker 节点访问。
2. 公网服务器、防火墙、安全组不得放行 `5000/tcp` 到互联网。
3. 一键注册节点会把固定 Registry 写入 Docker `insecure-registries` 信任列表；如果节点无法拉取镜像，先检查节点到 `10.24.0.28:5000` 的内网连通性和 Docker daemon 是否已重启。
4. 环境模板页面只展示固定 Registry 地址和模板拉取错误，不提供存储服务器切换入口。
5. 若未来要开启 Registry 认证，需要同步改造 Docker daemon 凭据分发、Agent 拉取凭据、上传推送凭据和已有镜像引用迁移，不应只在 UI 层增加账号密码字段。

## 配置示例

`appsettings.json`：

```json
{
  "ConnectionStrings": {
    "RedisCache": "127.0.0.1:6379"
  },
  "RunMode": "Fleet",
  "ContainerProvider": {
    "Type": "Docker",
    "PublicEntry": "203.195.157.191",
    "DockerConfig": {
      "PublicPortStart": 31000,
      "PublicPortEnd": 31999
    },
    "NginxProxyConfig": {
      "Enable": true,
      "SyncLocalConfig": false,
      "ConfigPath": "/etc/nginx/stream-conf.d/gzctf-stream-dynamic.conf",
      "SyncIntervalSeconds": 15,
      "NginxBinaryPath": "nginx",
      "ListenPortStart": 30000,
      "ListenPortEnd": 30999,
      "WriteStreamBlock": false,
      "SyncToken": "change-this-to-the-gateway-token"
    }
  }
}
```

## Nginx 主配置

当前 10.24.0.27 内网主服务 + 203.195.157.191 公网网关的部署中，主服务应设置
`NginxProxyConfig.SyncLocalConfig=false`，由公网网关的
`gzctf-public-gateway-sync.service` 定时请求主服务
`/api/internal/port-map` 并刷新公网 Nginx stream 配置。

公网同步脚本必须携带：

```bash
Authorization: Bearer <ContainerProvider:NginxProxyConfig:SyncToken>
```

`SyncToken` 应与公网服务器 `/etc/gzctf-public-gateway/sync.env` 中的
`GZCTF_API_TOKEN` 一致。该接口也兼容平台 API Token 或管理员会话，但生产网关
建议使用独立同步令牌，避免依赖用户会话。

确认 Nginx 编译了 stream 模块：

```bash
nginx -V 2>&1 | grep -- --with-stream
```

在 `/etc/nginx/nginx.conf` 的顶层增加：

```nginx
stream {
    include /etc/nginx/stream-conf.d/*.conf;
}
```

创建目录并确保运行 GZCTF 的用户可写动态配置文件：

```bash
sudo mkdir -p /etc/nginx/stream-conf.d
sudo touch /etc/nginx/stream-conf.d/gzctf-stream-dynamic.conf
sudo chown gzctf:gzctf /etc/nginx/stream-conf.d/gzctf-stream-dynamic.conf
sudo nginx -t
sudo systemctl reload nginx
```

如果你的运维方式要求动态文件自己包含完整 `stream {}` 块，则把 `WriteStreamBlock` 改成 `true`，并确保该文件是在 nginx 顶层 include，而不是在 `http {}` 或另一个 `stream {}` 内 include。

## 怎么调整映射端口

有两组端口，不要混淆：

- `ContainerProvider:NginxProxyConfig:ListenPortStart/ListenPortEnd`
  - 玩家访问的统一公网端口池。
  - 例如 `30000-30999`，玩家访问 `10.0.7.118:30001`。
  - Nginx stream 必须监听这一段，防火墙/安全组也必须放行这一段。

- `ContainerProvider:DockerConfig:PublicPortStart/PublicPortEnd`
  - Worker 节点上 Docker/Agent 发布容器时使用的端口池。
  - 主服务器 Nginx 会把统一公网端口转发到 `WorkerNode.HostAddress:这个端口`。
  - 主服务器必须能访问所有 Worker 的这一段端口。

如果只想改玩家看到的端口段，改 `NginxProxyConfig.ListenPortStart/ListenPortEnd`。

如果只想改 Worker 上 Docker 发布端口段，改 `DockerConfig.PublicPortStart/PublicPortEnd`，并同步更新 Agent 节点配置。

如果两段端口都改，需要同时更新：

1. 主服务 `appsettings.json`
2. Agent 节点 Docker 配置
3. 主服务器防火墙/安全组
4. Worker 节点防火墙/安全组
5. Nginx reload
6. GZCTF 服务重启

## 运行时行为

- 只有 `PublishPort=true` 且 `BypassPublicProxy=false` 的普通 Docker 容器会分配统一公网端口并进入 Nginx 映射。
- 玩家看到的 Docker 题目入口应是 `PublicEntry:NginxProxyConfig.ListenPortStart-ListenPortEnd` 中的端口，例如 `203.195.157.191:30012`。
- `DockerConfig.PublicPortStart/PublicPortEnd` 只是 Worker 节点上的上游端口池，例如 `10.24.0.125:42762`；这个端口用于 Nginx upstream，不应作为玩家公网入口展示。
- 综合渗透环境的入口节点会设置 `BypassPublicProxy=true`，按比赛设计让选手直接扫描分配到的节点地址；它不是普通 CTF 容器公网代理链路。
- 主节点本机 Docker 容器仅在 `SyncLocalConfig=true` 的本机 Nginx 模式下进入 Nginx 映射；外部公网网关模式下主节点通常不参与 Docker 调度。
- 内网节点、非公开节点不会被 Nginx 暴露。
- Nginx 动态配置由 `NginxSyncService` 按 `SyncIntervalSeconds` 周期生成。
- 服务会先写临时文件、替换动态配置、执行 `nginx -t`，失败时回滚旧配置。
- 同步服务会用数据库中的运行中映射刷新 Redis 端口占用，避免 Redis/服务重启后重复分配存量端口。

## 排障命令

```bash
sudo nginx -t
sudo systemctl status nginx --no-pager
sudo journalctl -u gzctf.service -n 200 --no-pager
cat /etc/nginx/stream-conf.d/gzctf-stream-dynamic.conf
redis-cli keys 'gzctf:port:*'
ss -lntp | grep -E '30000|30999|31000|31999'
```

如果玩家入口无法访问，按顺序检查：

1. 容器在 Worker 上是否运行。
2. Worker 节点的 Docker 发布端口是否监听。
3. 主服务器能否访问 `WorkerNode.HostAddress:AgentPublishedPort`。
4. 动态 Nginx 配置是否包含 `UnifiedPublicPort WorkerHost:AgentPublishedPort`。
5. 主服务器 Nginx 是否监听统一公网端口段。
6. 防火墙/安全组是否放行统一公网端口段。
