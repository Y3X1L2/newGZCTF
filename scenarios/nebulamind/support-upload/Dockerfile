# support-upload - NebulaMind 客户支持上传中心
# DMZ 安全域，Python Flask 服务，提供客户日志包上传和文档解析提交。
# B1 题目：文件上传 MIME 绕过 - 后端只检查 MIME，不检查真实内容/扩展名。
# B2 题目：/download 路径穿越 - file 参数未做规范化，可读取 /app/config/worker.yml。
# B3 题目：SSRF 入口 - /api/parse-url 转发到 document-worker（SSRF 执行在 document-worker）。
#
# 设计要点：
# - 基于 python:3.11-alpine，使用 Flask，非 root 用户运行。
# - EXPOSE 8080，健康检查 GET /healthz。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时注入到对应位置，镜像内不硬编码。

FROM python:3.11-alpine

LABEL org.opencontainers.image.title="nebulamind-support-upload"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Customer Support Upload Center"
LABEL org.nebulamind.zone="dmz"
LABEL org.nebulamind.flags="FLAG_UPLOAD_MIME_BYPASS,FLAG_UPLOAD_PATH_TRAVERSAL"

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
COPY support-upload/requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY support-upload/app.py /app/app.py
COPY support-upload/templates /app/templates
COPY support-upload/static /app/static
COPY support-upload/config /app/config
COPY support-upload/entrypoint.sh /entrypoint.sh

# 创建上传目录，修复行尾，设置权限
RUN mkdir -p /app/uploads && \
    sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh && \
    chown -R nmapp:nmapp /app /_shared && \
    chmod -R a+r /app /_shared

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1

USER nmapp

ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "/app/app.py"]
