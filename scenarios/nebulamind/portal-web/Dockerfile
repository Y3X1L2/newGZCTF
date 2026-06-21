# portal-web - NebulaMind AI 企业官网
# DMZ 安全域，Python HTTP 服务，对外提供企业官网页面。
# A2 题目：隐藏目录 /resources/archive/ 中的旧版白皮书 HTML 注释泄露 Flag。
# A3 题目：生产 JS 保留 .map 文件，sourcesContent 中泄露 Flag 和内部 API 线索。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用标准库 http.server，无外部依赖。
# - 非 root 用户运行，EXPOSE 8080。
# - 健康检查：GET /healthz 返回 200。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时渲染到对应文件，镜像内不硬编码。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-portal-web"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Enterprise Portal Website"
LABEL org.nebulamind.zone="dmz"
LABEL org.nebulamind.flags="FLAG_PORTAL_HIDDEN_DOCS,FLAG_PORTAL_SOURCEMAP"

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
RUN sed -i 's/\r$//' /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod +x /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh

# 复制应用代码
COPY portal-web/app.py /app/app.py
COPY portal-web/templates /app/templates
COPY portal-web/static /app/static
COPY portal-web/entrypoint.sh /entrypoint.sh

RUN sed -i 's/\r$//' /entrypoint.sh && \
    chmod +x /entrypoint.sh && \
    chown -R nmapp:nmapp /app /_shared && \
    chmod -R a+r /app /_shared

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/app.py"]
