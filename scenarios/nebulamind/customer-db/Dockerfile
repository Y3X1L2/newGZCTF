# customer-db - NebulaMind 客户与标注数据库服务
# Data 安全域，仅限 Data 网段访问（不对 Public 暴露）。
# F1 题目：数据库只读凭据复用 - readonly 账号凭据从对象存储/CI 变量泄露，
#         选手以 readonly 登录后 SELECT * FROM security_findings 获得 Flag。
# F2 题目：SECURITY DEFINER 函数权限提升 - readonly 被错误授予 export_internal_data
#         函数的 EXECUTE 权限，调用该函数读取 internal_exports 获得 Flag（业务权限提升，非 RCE）。
# F3 题目：核心客户训练数据 - 需更高权限账号（admin），凭据经 Vault/CI 高权限变量获取，
#         登录后 SELECT regulated_model_training_records 的 compliance_audit 字段获得 Flag。
#
# 设计要点：
# - 基于 postgres:16-alpine，使用内置 postgres 用户运行。
# - EXPOSE 5432，健康检查 pg_isready -U postgres。
# - POSTGRES_USER=postgres, POSTGRES_DB=nebulamind, POSTGRES_PASSWORD 由环境变量或默认。
# - Flag 通过环境变量注入，entrypoint.sh 在启动时用 sed 替换 init/*.sql 中的占位符，
#   处理后的 SQL 复制到 /docker-entrypoint-initdb.d/ 由官方 entrypoint 执行。镜像内不硬编码 Flag。
# - 资源限制 1C/768M 由平台容器编排层强制。
# - admin 密码由 NM_DB_ADMIN_PASSWORD 环境变量注入（默认 nm_admin_dev_2026），
#   场景中应通过 secrets-vault 的 secret/nebulamind/db-credentials 或 CI 高权限变量暴露给选手（F3 链路）。

FROM postgres:16-alpine

LABEL org.opencontainers.image.title="nebulamind-customer-db"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Customer & Annotation Database"
LABEL org.nebulamind.zone="data"
LABEL org.nebulamind.flags="FLAG_DB_READONLY_CUSTOMERS,FLAG_DB_PRIVESC_FUNCTION,FLAG_DB_CORE_CUSTOMER_DATA"
LABEL org.nebulamind.resources="1C/768M"

# 安装时区数据（与其他 NebulaMind 服务保持一致）
RUN apk add --no-cache tzdata && \
    cp /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    echo "Asia/Shanghai" > /etc/timezone

# 默认数据库配置（可在运行时通过环境变量覆盖）
ENV POSTGRES_USER=postgres \
    POSTGRES_DB=nebulamind \
    POSTGRES_PASSWORD=nm_postgres_dev_2026 \
    NM_DB_ADMIN_PASSWORD=nm_admin_dev_2026 \
    PGDATA=/var/lib/postgresql/data

# 复制共享资源
COPY _shared/scripts/flag.sh /_shared/scripts/flag.sh

# 复制初始化 SQL（占位符由 entrypoint.sh 在启动时替换）
COPY customer-db/init/ /opt/nebulamind/init/
COPY customer-db/entrypoint.sh /entrypoint.sh

# 修复行尾、设置权限
RUN sed -i 's/\r$//' /entrypoint.sh /_shared/scripts/flag.sh && \
    chmod +x /entrypoint.sh /_shared/scripts/flag.sh && \
    chmod -R 0644 /opt/nebulamind/init/*.sql

EXPOSE 5432

HEALTHCHECK --interval=15s --timeout=3s --start-period=15s --retries=5 \
    CMD pg_isready -U postgres || exit 1

ENTRYPOINT ["/entrypoint.sh"]
CMD ["postgres"]
