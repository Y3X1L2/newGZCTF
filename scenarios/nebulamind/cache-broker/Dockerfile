# cache-broker - NebulaMind 任务缓存与消息队列服务
# Business 安全域，仅限 Business 网段访问（不对 Public 暴露）。
# D1 题目：未鉴权 Redis - 未设置 requirepass，可通过 SSRF 或 worker token 获取访问路径后
#         直接连接 Redis，遍历 task:result:* 旧任务结果，在 task_003 的 metadata 中发现 Flag。
#
# 设计要点：
# - 基于 redis:7.2-alpine，使用内置 redis 用户（UID 999）非 root 运行。
# - EXPOSE 6379，健康检查 redis-cli ping 返回 PONG。
# - 不设置 requirepass（漏洞本身），bind 0.0.0.0，protected-mode no（由网络隔离保证安全）。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时通过 init.lua 注入到 task:result:task_003。
# - maxmemory 128mb / allkeys-lru，appendonly no（靶场不需要持久化）。
# - 不要求真实 Redis RCE，Flag 藏在旧任务结果 JSON 的 metadata 字段中。
# - 资源限制 0.25C/128M 由平台容器编排层强制，redis.conf maxmemory 与内存限制对齐。

FROM redis:7.2-alpine

LABEL org.opencontainers.image.title="nebulamind-cache-broker"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Task Cache & Message Queue Broker"
LABEL org.nebulamind.zone="business"
LABEL org.nebulamind.flags="FLAG_REDIS_QUEUE_INFO"
LABEL org.nebulamind.resources="0.25C/128M"

# 安装时区数据（与其他 NebulaMind 服务保持一致）
RUN apk add --no-cache tzdata && \
    cp /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    echo "Asia/Shanghai" > /etc/timezone

# 复制共享资源
COPY _shared/scripts/flag.sh /_shared/scripts/flag.sh
COPY _shared/scripts/runtime-env.sh /_shared/scripts/runtime-env.sh

# 复制 Redis 配置与初始化脚本
COPY cache-broker/redis.conf /usr/local/etc/redis/redis.conf
COPY cache-broker/init.lua /usr/local/etc/redis/init.lua
COPY cache-broker/entrypoint.sh /entrypoint.sh

# 修复行尾、设置权限
# redis 用户在 redis:7.2-alpine 中已存在（UID 999），无需创建
RUN sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh /usr/local/etc/redis/init.lua && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod 0644 /usr/local/etc/redis/redis.conf /usr/local/etc/redis/init.lua && \
    mkdir -p /data && chown redis:redis /data

EXPOSE 6379

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD redis-cli ping | grep -q PONG || exit 1

USER redis

ENTRYPOINT ["/entrypoint.sh"]
CMD ["redis-server", "/usr/local/etc/redis/redis.conf"]
