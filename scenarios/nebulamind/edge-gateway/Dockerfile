# edge-gateway - NebulaMind external entry gateway.
# Public-zone nginx reverse proxy for portal-web and support-upload.
# A1 uses port 80 for the corporate portal and port 8080 for the mirror site.
#
# Platform rules:
# - Do not embed internal IPs or Docker service names.
# - Upstream targets are rendered from NM_PORTAL_WEB_URL and NM_SUPPORT_UPLOAD_URL.
# - /status/build-info returns build metadata plus the low-score discovery flag.

FROM nginx:1.27-alpine

LABEL org.opencontainers.image.title="nebulamind-edge-gateway"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - External Edge Gateway"
LABEL org.nebulamind.zone="public"
LABEL org.nebulamind.flags="FLAG_PUBLIC_DISCOVERY"

# 安装最小工具
RUN apk add --no-cache curl tzdata && \
    cp /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    echo "Asia/Shanghai" > /etc/timezone && \
    addgroup -S nmapp && adduser -S -G nmapp nmapp

# 复制共享资源
COPY _shared/assets/nebulamind.css /_shared/assets/nebulamind.css
COPY _shared/scripts/flag.sh /_shared/scripts/flag.sh
COPY _shared/scripts/runtime-env.sh /_shared/scripts/runtime-env.sh
RUN chmod +x /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh

# 复制 nginx 配置和入口脚本
COPY edge-gateway/nginx.conf /etc/nginx/nginx.conf
COPY edge-gateway/default.conf /etc/nginx/conf.d/default.conf
COPY edge-gateway/mirror.conf /etc/nginx/conf.d/mirror.conf
COPY edge-gateway/entrypoint.sh /entrypoint.sh
COPY edge-gateway/build-info.json /app/build-info.json
COPY edge-gateway/healthz.html /app/healthz.html

RUN chmod +x /entrypoint.sh && \
    chown -R nmapp:nmapp /app /_shared /var/cache/nginx /var/run /var/log/nginx && \
    # nginx 默认监听 80/8080 需要 root 绑定，这里用 setcap 或改用非特权端口
    # 80/8080 是对外入口端口，保持 root 绑定但 worker 以 nmapp 运行
    mkdir -p /var/cache/nginx /var/run /var/log/nginx

EXPOSE 80 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -fsS http://127.0.0.1/healthz || exit 1

ENTRYPOINT ["/entrypoint.sh"]
CMD ["nginx", "-g", "daemon off;"]
