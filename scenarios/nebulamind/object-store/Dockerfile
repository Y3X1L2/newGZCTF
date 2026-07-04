# object-store - NebulaMind 对象存储与模型资源服务
# Data 安全域，仅限 Data 网段访问（不对 Public 暴露）。
#
# D2 题目：对象存储 Bucket 权限错误 - 低权限 access key 可列出 public-model-artifacts，
#         该 bucket 中误放 exports/tenant-summary-2026.csv，CSV 含 Flag 和数据库表名线索。
# G3 题目（终局，部分在此）：模型供应链审计 - object-store 提供训练日志文件，
#         日志中包含线索指向 model-registry bucket 的 manifest 和 customer-db 的审计记录。
#         G3 的 Flag 本身在 customer-db 的 regulated_model_training_records 表 id=13（不在 object-store）。
#
# 设计要点：
# - 基于 minio/minio:RELEASE.2024-12-18T13-15-44Z，真实运行 MinIO S3 API。
# - EXPOSE 9000（S3 API）、9001（Console），健康检查 /minio/health/live。
# - 通过 mc（MinIO Client）初始化 bucket、用户、策略、种子数据。
# - Flag 通过环境变量注入，entrypoint.sh 用 sed 替换 CSV 中的占位符。镜像内不硬编码 Flag。
# - 资源限制 0.75C/512M 由平台容器编排层强制。
# - 低权限用户 nm-low-priv 只能访问 public-model-artifacts（D2 入口）。
# - 高权限用户 nm-admin 可访问所有 bucket。

# ============================================================
# 阶段 1：构建阶段 - 下载 mc（MinIO Client）
# ============================================================
FROM alpine:3.20 AS builder

RUN apk add --no-cache curl ca-certificates tzdata

# 下载 mc（MinIO Client），使用与 MinIO 服务端兼容的版本
RUN curl -fsSL -o /usr/local/bin/mc \
    https://dl.min.io/client/mc/release/linux-amd64/mc && \
    chmod +x /usr/local/bin/mc

# 下载静态编译的 curl（用于健康检查，minio 镜像可能不含 curl）。
# 如下载失败则创建占位脚本，healthcheck.sh 会回退到 mc。
RUN (curl -fsSL -o /tmp/curl.tar.xz \
    https://github.com/stunnel/static-curl/releases/download/8.6.0/curl-8.6.0-linux-amd64.tar.xz && \
    cd /tmp && tar xf curl.tar.xz && \
    cp /tmp/curl/bin/curl /usr/local/bin/curl-static && \
    chmod +x /usr/local/bin/curl-static) || \
    (printf '#!/bin/sh\nexit 127\n' > /usr/local/bin/curl-static && \
    chmod +x /usr/local/bin/curl-static && \
    echo "[builder] static curl download failed, created placeholder")

COPY _shared/scripts/flag.sh /prestage/_shared/scripts/flag.sh
COPY _shared/scripts/runtime-env.sh /prestage/_shared/scripts/runtime-env.sh
COPY object-store/entrypoint.sh /prestage/entrypoint.sh
COPY object-store/init-buckets.sh /prestage/init-buckets.sh
COPY object-store/healthcheck.sh /prestage/healthcheck.sh

RUN sed -i 's/\r$//' \
        /prestage/entrypoint.sh \
        /prestage/init-buckets.sh \
        /prestage/healthcheck.sh \
        /prestage/_shared/scripts/flag.sh \
        /prestage/_shared/scripts/runtime-env.sh && \
    chmod +x \
        /prestage/entrypoint.sh \
        /prestage/init-buckets.sh \
        /prestage/healthcheck.sh \
        /prestage/_shared/scripts/flag.sh \
        /prestage/_shared/scripts/runtime-env.sh

# ============================================================
# 阶段 2：运行阶段 - MinIO 服务
# ============================================================
FROM minio/minio:RELEASE.2024-12-18T13-15-44Z

LABEL org.opencontainers.image.title="nebulamind-object-store"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Object Storage & Model Artifacts"
LABEL org.nebulamind.zone="data"
LABEL org.nebulamind.flags="FLAG_OBJECT_BUCKET_POLICY,FLAG_FINAL_MODEL_SUPPLY_CHAIN"
LABEL org.nebulamind.resources="0.75C/512M"

# 从构建阶段复制 mc 和静态 curl
COPY --from=builder /usr/local/bin/mc /usr/local/bin/mc
COPY --from=builder /usr/local/bin/curl-static /usr/local/bin/curl

# 设置时区（与其他 NebulaMind 服务保持一致）
COPY --from=builder /usr/share/zoneinfo/Asia/Shanghai /etc/localtime
COPY --from=builder /usr/share/zoneinfo/Asia/Shanghai /usr/share/zoneinfo/Asia/Shanghai
RUN echo "Asia/Shanghai" > /etc/timezone

# Default MinIO credentials; runtime variables may override them.
ENV MINIO_ROOT_USER=nm-root-admin \
    MINIO_ROOT_PASSWORD=nm-root-admin-secret-2026

# 复制共享资源
COPY --from=builder /prestage/_shared/scripts/flag.sh /_shared/scripts/flag.sh
COPY --from=builder /prestage/_shared/scripts/runtime-env.sh /_shared/scripts/runtime-env.sh

# 复制服务文件
COPY --from=builder /prestage/entrypoint.sh /entrypoint.sh
COPY --from=builder /prestage/init-buckets.sh /init-buckets.sh
COPY --from=builder /prestage/healthcheck.sh /healthcheck.sh
COPY object-store/policies/ /opt/nebulamind/policies/
COPY object-store/seed/ /opt/nebulamind/seed/

# Set permissions in the MinIO runtime image. Scripts are normalized in the builder stage.
RUN chmod +x /entrypoint.sh /init-buckets.sh /healthcheck.sh /_shared/scripts/flag.sh /_shared/scripts/runtime-env.sh /usr/local/bin/mc /usr/local/bin/curl && \
    chmod -R 0644 /opt/nebulamind/policies/*.json && \
    chmod -R 0644 /opt/nebulamind/seed/* && \
    mkdir -p /data

EXPOSE 9000 9001

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD /healthcheck.sh

ENTRYPOINT ["/entrypoint.sh"]
CMD ["server", "/data", "--console-address", ":9001"]
