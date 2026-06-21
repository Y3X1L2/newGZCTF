# document-worker - NebulaMind 文档解析 Worker
# Business 安全域，连接 dmz-service 和 biz-core 两个网络。
# B3 题目：SSRF - /api/parse 抓取远程 URL，在 trace metadata 中泄露 Flag。
# D3 题目：命令注入 - /api/parse 的 profile 参数过滤不严，可注入 $() ` ` 和换行。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用 Flask，非 root 用户运行。
# - EXPOSE 8080，健康检查 GET /healthz。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时注入到对应位置，镜像内不硬编码。
# - 内置一个假的 convert 脚本（/usr/local/bin/convert），用于 D3 命令注入场景：
#   worker 模拟调用 `convert --profile {profile}`，profile 过滤不严导致命令执行。
#   convert 脚本会回显参数，使注入的 $(cat flag) 结果出现在转换日志中。
# - 不挂载 Docker socket，不使用特权容器，命令注入仅在容器内执行，无法逃逸。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-document-worker"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Document Parsing Worker"
LABEL org.nebulamind.zone="business"
LABEL org.nebulamind.flags="FLAG_WORKER_SSRF_METADATA,FLAG_WORKER_COMMAND_INJECTION"

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
COPY document-worker/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY document-worker/app.py /app/app.py
COPY document-worker/config /app/config
COPY document-worker/static /app/static
COPY document-worker/entrypoint.sh /entrypoint.sh

# 创建假的 convert 转换工具（D3 命令注入目标）
# worker 模拟调用 `convert --profile {profile}`，convert 会回显参数，
# 使注入的命令替换结果（如 $(cat /opt/nebulamind/worker.flag)）出现在转换日志中。
RUN printf '#!/bin/sh\n# NebulaMind document conversion tool (sandboxed)\n# Echoes profile and args for logging; real conversion happens downstream.\necho "[convert] init profile=$*"\necho "[convert] loading conversion profiles..."\necho "[convert] conversion complete (exit 0)"\nexit 0\n' > /usr/local/bin/convert && \
    chmod 755 /usr/local/bin/convert

# 创建工作目录、修复行尾、设置权限
RUN mkdir -p /app/uploads /opt/nebulamind /tmp/worker && \
    sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chown -R nmapp:nmapp /app /_shared /opt/nebulamind /tmp/worker && \
    chmod -R a+r /app /_shared

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

USER nmapp

ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/app.py"]
