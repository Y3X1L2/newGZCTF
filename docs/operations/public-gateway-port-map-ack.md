# 公网 Nginx 实例入口同步与 ACK 部署指南

## 1. 目标

公网网关不再依赖“创建后等待固定秒数”。平台为走公网网关的容器保存入口状态：

- `Pending`：容器已启动，但公网路由尚未确认，前端不展示入口。
- `Ready`：网关完成 `nginx -t` 和 reload，并 ACK 当前 revision，前端展示入口。
- `Error`：网关明确报告发布失败，前端展示可恢复错误与刷新、销毁操作。

公网网关只负责 TCP 转发，不承担题目调度、镜像存储或容器生命周期管理。

## 2. 数据流

```text
GZCTF / Worker 创建容器
  -> 数据库入口状态 Pending
  -> 公网网关 GET /api/internal/port-map
  -> 生成 Nginx stream 配置
  -> nginx -t
  -> nginx reload
  -> POST /api/internal/port-map/ack
  -> 数据库入口状态 Ready 或 Error
  -> 比赛和培训前端轮询到真实状态
```

`GET` 响应体仍是兼容旧同步器的 JSON 数组，新增响应头：

```text
X-GZCTF-Port-Map-Revision: <64 位 sha256>
```

ACK 必须携带该 revision 和本次快照的完整 `leaseId` 集合。同步期间映射发生变化时，平台返回 `409 Conflict`，同步器不得把旧快照标记为 Ready，下一轮会自动收敛。

## 3. 平台配置

生产配置至少包含：

```json
{
  "ContainerProvider": {
    "PublicEntry": "<玩家访问的公网 IP 或域名>",
    "NginxProxyConfig": {
      "Enable": true,
      "SyncLocalConfig": false,
      "ListenPortStart": 30000,
      "ListenPortEnd": 30999,
      "SyncToken": "<独立随机同步令牌>"
    }
  }
}
```

要求：

1. `PublicEntry` 必须是玩家实际可访问的地址，不能填 Worker 内网地址。
2. `SyncLocalConfig=false` 表示配置由独立公网网关拉取。
3. `SyncToken` 只写入平台密钥配置和网关 root 可读文件，不进入 Git、命令行或日志。
4. Redis 端口租约必须可用；多实例平台不得回退到单机端口扫描。

## 4. 网关准备

安装依赖并确认 Nginx 包含 stream 模块：

```bash
sudo apt-get update
sudo apt-get install -y nginx jq curl util-linux coreutils
nginx -V 2>&1 | grep -- --with-stream
```

在 Nginx 顶层配置中加入一次：

```nginx
stream {
    include /etc/nginx/stream-conf.d/*.conf;
}
```

`stream` 不能放在 `http {}` 内。放行平台公网端口池，并确保公网网关能访问所有 Worker 的 `HostAddress:容器发布端口`。

## 5. 安装同步器

仓库文件：

- `scripts/gateway/sync-nginx-port-map.sh`
- `scripts/gateway/gzctf-port-map-sync.service`
- `scripts/gateway/gzctf-port-map-sync.timer`

安装命令：

```bash
sudo install -d -m 0755 /usr/local/libexec/gzctf /etc/gzctf
sudo install -m 0755 scripts/gateway/sync-nginx-port-map.sh \
  /usr/local/libexec/gzctf/sync-nginx-port-map.sh
sudo install -m 0644 scripts/gateway/gzctf-port-map-sync.service \
  /etc/systemd/system/gzctf-port-map-sync.service
sudo install -m 0644 scripts/gateway/gzctf-port-map-sync.timer \
  /etc/systemd/system/gzctf-port-map-sync.timer
```

创建非敏感环境文件：

```bash
sudo tee /etc/gzctf/gateway-sync.env >/dev/null <<'EOF'
GZCTF_BASE_URL=http://<平台内网地址>:8080
GZCTF_SYNC_TOKEN_FILE=/etc/gzctf/gateway-sync.token
NGINX_CONFIG_PATH=/etc/nginx/stream-conf.d/gzctf-dynamic.conf
NGINX_BIN=/usr/sbin/nginx
EOF
sudo chmod 0600 /etc/gzctf/gateway-sync.env
```

单独写入同步令牌：

```bash
sudo install -m 0600 /dev/null /etc/gzctf/gateway-sync.token
sudoedit /etc/gzctf/gateway-sync.token
```

先手工执行一次，再启用定时器：

```bash
sudo systemctl daemon-reload
sudo systemctl start gzctf-port-map-sync.service
sudo nginx -t
sudo systemctl enable --now gzctf-port-map-sync.timer
systemctl list-timers gzctf-port-map-sync.timer
```

## 6. 验收

1. 创建一项 Docker 比赛实例。
2. API 首次返回 `entryStatus=Pending`，`entry=null`。
3. 网关日志出现一次成功同步：

```bash
journalctl -u gzctf-port-map-sync.service -n 50 --no-pager
```

4. 再次查询实例，预期 `entryStatus=Ready`、`entryReadyAt` 非空、`entry` 为公网地址。
5. 从平台所在内网和客户公网各访问一次入口。
6. 培训章节实例重复同一流程，状态语义必须一致。

失败演练应在预发布网关执行：临时提供无效候选配置，使 `nginx -t` 失败，确认旧配置恢复、API 为 `Error`、修复后下一轮回到 `Ready`。

## 7. 故障判断

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `401` | Token 不一致或未配置 | 核对平台 `SyncToken` 与 token 文件，勿输出令牌值 |
| `409` | 拉取后映射已变化 | 正常竞态，等待下一轮，不手工 ACK 旧 revision |
| 一直 `Pending` | timer 未运行、网络不通或 ACK 失败 | 查 service 日志和平台内部接口连通性 |
| `Error: validation failed` | 动态配置无法通过 `nginx -t` | 检查主配置的 stream include 和端口冲突 |
| `Ready` 但外网不通 | 防火墙、安全组、NAT 或 Worker 路由问题 | 依次检查公网端口、网关到 Worker、Worker 发布端口 |

## 8. 回滚

停止新同步器：

```bash
sudo systemctl disable --now gzctf-port-map-sync.timer
sudo systemctl stop gzctf-port-map-sync.service
```

恢复升级前 Nginx 配置并执行 `nginx -t && nginx -s reload`。旧同步器不支持 ACK，因此回滚应用版本时必须同时恢复升级前数据库；禁止在新数据库上运行旧应用并依赖固定等待时间。
