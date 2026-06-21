# ai-console-api - NebulaMind AI 控制台 API 服务
# Business 安全域，连接 biz-core 和 data-plane 两个网络。
# C1 题目：IDOR - /api/v1/knowledge-bases?tenantId=xxx 缺少租户归属校验。
# C2 题目：JWT 弱密钥 - dev 环境使用弱 secret，可伪造 operator 角色导出审计日志。
# C3 题目：GraphQL - /graphql 开启 introspection，integrationSecrets(masked:false) 泄露 Flag。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用 Flask + PyJWT，非 root 用户运行。
# - EXPOSE 8080，健康检查 GET /healthz。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时注入到对应位置，镜像内不硬编码。
# - GraphQL 自实现轻量解析（支持 introspection 和 integrationSecrets 查询），不依赖 graphene。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-ai-console-api"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Internal Operations Console API"
LABEL org.nebulamind.zone="business"
LABEL org.nebulamind.flags="FLAG_API_TENANT_IDOR,FLAG_API_JWT_ROLE,FLAG_API_GRAPHQL_AUDIT"

# 安装最小工具
RUN apk add --no-cache curl tzdata && \
    cp /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    echo "Asia/Shanghai" > /etc/timezone && \
    addgroup -S nmapp && adduser -S -G nmapp nmapp

# 复制共享资源
COPY _shared/assets/nebulamind.css /_shared/assets/nebulamind.css
COPY _shared/scripts/flag.sh /_shared/scripts/flag.sh
COPY _shared/scripts/flag.py /_shared/scripts/flag.py
COPY _shared/scripts/runtime-env.sh /_shared/scripts/runtime-env.sh

# 复制应用代码
COPY ai-console-api/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY ai-console-api/seed_data.py /app/seed_data.py
COPY ai-console-api/app.py /app/app.py
COPY ai-console-api/templates /app/templates
COPY ai-console-api/static /app/static
COPY ai-console-api/entrypoint.sh /entrypoint.sh

# 创建工作目录、修复行尾、设置权限
RUN mkdir -p /opt/nebulamind && \
    sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chown -R nmapp:nmapp /app /_shared /opt/nebulamind && \
    chmod -R a+r /app /_shared

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

USER nmapp

ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/app.py"]
