# git-service - NebulaMind 内部 Git 服务
# Operations 安全域，提供内部代码仓库的 HTTP 访问。
# E1 题目：Git 仓库泄露低权限凭据 - console-api 历史提交中的 .env.example.old 含 Flag。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用 Flask，非 root 用户运行。
# - 自己实现简单 Git HTTP 协议（smart HTTP），不部署完整 Gitea。
# - EXPOSE 3000，健康检查 GET /healthz。
# - 安装 git 用于仓库初始化和 HTTP 协议（upload-pack）。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时注入到 .env.example.old，
#   镜像内不硬编码 Flag。
# - 仓库历史在启动时由 entrypoint.sh 用真实 git 命令创建。
# - 只读服务：支持 git clone/fetch，不支持 push。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-git-service"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Internal Git Service"
LABEL org.nebulamind.zone="operations"
LABEL org.nebulamind.flags="FLAG_GIT_CONFIG_SECRET"

# 安装 git（仓库初始化 + HTTP 协议）、curl（健康检查）、tzdata
RUN apk add --no-cache git curl tzdata && \
    cp /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    echo "Asia/Shanghai" > /etc/timezone && \
    addgroup -S nmapp && adduser -S -G nmapp nmapp

# 复制共享资源
COPY _shared/assets/nebulamind.css /_shared/assets/nebulamind.css
COPY _shared/scripts/flag.sh /_shared/scripts/flag.sh
COPY _shared/scripts/flag.py /_shared/scripts/flag.py
COPY _shared/scripts/runtime-env.sh /_shared/scripts/runtime-env.sh

# 复制应用代码
COPY git-service/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY git-service/app.py /app/app.py
COPY git-service/templates /app/templates
COPY git-service/static /app/static
COPY git-service/repos /app/repos
COPY git-service/entrypoint.sh /entrypoint.sh

# 创建仓库目录、修复行尾、设置权限
RUN mkdir -p /srv/git/nebulamind && \
    sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chown -R nmapp:nmapp /app /_shared /srv/git && \
    chmod -R a+r /app /_shared

EXPOSE 3000

HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -fsS http://127.0.0.1:3000/healthz || exit 1

USER nmapp

ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/app.py"]
