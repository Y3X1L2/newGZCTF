# secrets-vault - NebulaMind 密钥配置服务（Vault mock）
# Data 安全域，仅限 Data 网段访问（不对 Public 暴露）。
# G1 题目：Vault Bootstrap Token 滥用 - CI 变量中的 bootstrap token 权限过大，
#         可读取 secret/data/nebulamind/model-registry，其中包含 G1 Flag 和
#         模型仓库管理员凭据（用于 G2 链路）。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用 Flask 实现 Vault 风格 API mock（不部署真实 Vault）。
# - EXPOSE 8200（HTTP API），健康检查 GET /healthz 返回 200。
# - 非 root 用户运行，资源限制 0.5C/256M 由平台容器编排层强制。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时注入到 secret/data/nebulamind/model-registry。
# - 镜像内不硬编码 Flag。
# - bootstrap token = s.bootstrap-nebulamind-2026（与 ci-runner 的 vault-credentials.json 一致）。
# - API 响应格式接近真实 HashiCorp Vault（request_id, lease_id, data.data, data.metadata）。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-secrets-vault"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Secrets Vault (HashiCorp Vault mock)"
LABEL org.nebulamind.zone="data"
LABEL org.nebulamind.flags="FLAG_VAULT_POLICY_BYPASS"
LABEL org.nebulamind.resources="0.5C/256M"

# 安装最小工具
RUN apk add --no-cache curl tzdata && \
    cp /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    echo "Asia/Shanghai" > /etc/timezone && \
    addgroup -S nmapp && adduser -S -G nmapp nmapp

# 复制共享资源
COPY _shared/scripts/flag.sh /_shared/scripts/flag.sh
COPY _shared/scripts/flag.py /_shared/scripts/flag.py
COPY _shared/scripts/runtime-env.sh /_shared/scripts/runtime-env.sh

# 复制应用代码
COPY secrets-vault/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY secrets-vault/app.py /app/app.py
COPY secrets-vault/templates /app/templates
COPY secrets-vault/static /app/static
COPY secrets-vault/seed /app/seed
COPY secrets-vault/policies /app/policies
COPY secrets-vault/entrypoint.sh /entrypoint.sh

# 创建工作目录、修复行尾、设置权限
RUN mkdir -p /app /opt/nebulamind && \
    sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chown -R nmapp:nmapp /app /_shared /opt/nebulamind && \
    chmod -R a+r /app /_shared

EXPOSE 8200

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8200/healthz || exit 1

USER nmapp

ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/app.py"]
