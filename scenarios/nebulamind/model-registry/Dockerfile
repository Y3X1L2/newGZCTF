# model-registry - NebulaMind 模型仓库与推理配置服务
# Data 安全域，连接 data-plane 网络（不对 Public 暴露）。
# G2 题目：模型仓库越权下载 - 使用 Vault 中得到的 admin token
#         (nm-model-admin-token-2026，从 G1 获得) 访问私有模型
#         recommendation-v4-private 的 manifest，其中包含 G2 Flag。
# G3 题目（终局，部分在此）：manifest 的 compliance 字段提供训练日志路径和
#         审计记录位置线索，引导选手前往 object-store 和 customer-db 完成终局链路。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用 Flask 实现模型仓库 API（HuggingFace 风格）。
# - EXPOSE 8080（HTTP API），健康检查 GET /healthz 返回 200。
# - 非 root 用户运行，资源限制 0.5C/256M 由平台容器编排层强制。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时注入到 recommendation-v4-private manifest。
# - 镜像内不硬编码 Flag。
# - admin token = nm-model-admin-token-2026（与 secrets-vault 一致）。
# - 普通用户 viewer/viewer123 只能访问公开模型，私有模型需要 admin token。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-model-registry"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Model Registry & Inference Configuration Service"
LABEL org.nebulamind.zone="data"
LABEL org.nebulamind.flags="FLAG_MODEL_REGISTRY_ADMIN"
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
COPY model-registry/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY model-registry/app.py /app/app.py
COPY model-registry/templates /app/templates
COPY model-registry/static /app/static
COPY model-registry/seed /app/seed
COPY model-registry/entrypoint.sh /entrypoint.sh

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
