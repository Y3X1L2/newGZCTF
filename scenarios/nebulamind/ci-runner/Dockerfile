# ci-runner - NebulaMind CI 构建执行节点
# Operations 安全域，连接 ops-control 和 data-plane 两个网络。
# E2 题目：CI 变量泄露 - /api/projects/{id}/variables 错误暴露 masked 变量明文。
# E3 题目：CI Runner 任务注入 - /api/projects/{id}/trigger 的 variables 通过字符串
#          替换注入到 .nebulaci.yml 脚本中，再用 shell=True 执行，导致 $(cmd) 命令注入。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用 Flask，非 root 用户运行。
# - EXPOSE 8080，健康检查 GET /healthz。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时注入到对应位置，镜像内不硬编码。
# - E3 命令注入仅在容器内执行（非 root、无 Docker socket、无特权），无法逃逸宿主机。
# - 构建命令（flake8/pytest/pip 等）使用 mock 输出，不真实执行。
# - 真实命令（echo/cat/ls 等）通过 subprocess 执行，$(cmd) 和 `cmd` 会被 shell 求值。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-ci-runner"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - CI Build Runner"
LABEL org.nebulamind.zone="operations"
LABEL org.nebulamind.flags="FLAG_CI_VARIABLE_LEAK,FLAG_CI_RUNNER_EXEC"

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
COPY ci-runner/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY ci-runner/app.py /app/app.py
COPY ci-runner/templates /app/templates
COPY ci-runner/static /app/static
COPY ci-runner/config /app/config
COPY ci-runner/entrypoint.sh /entrypoint.sh

# 创建工作目录、修复行尾、设置权限
# /opt/nebulamind 用于存放 E3 Flag 和 Vault 凭据文件
RUN mkdir -p /app /opt/nebulamind /tmp/ci-workspace && \
    sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chown -R nmapp:nmapp /app /_shared /opt/nebulamind /tmp/ci-workspace && \
    chmod -R a+r /app /_shared

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

USER nmapp

ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/app.py"]
