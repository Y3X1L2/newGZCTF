# object-store - NebulaMind 瀵硅薄瀛樺偍涓庢ā鍨嬭祫婧愭湇鍔?# Data 瀹夊叏鍩燂紝浠呴檺 Data 缃戞璁块棶锛堜笉瀵?Public 鏆撮湶锛夈€?# D2 棰樼洰锛氬璞″瓨鍌?Bucket 鏉冮檺閿欒 - 浣庢潈闄?access key 鍙垪鍑?public-model-artifacts锛?#         璇?bucket 涓鏀?exports/tenant-summary-2026.csv锛孋SV 鍚?Flag 鍜屾暟鎹簱琛ㄥ悕绾跨储銆?# G3 棰樼洰锛堢粓灞€锛岄儴鍒嗗湪姝わ級锛氭ā鍨嬩緵搴旈摼瀹¤ - object-store 鎻愪緵璁粌鏃ュ織鏂囦欢锛?#         鏃ュ織涓寘鍚嚎绱㈡寚鍚?model-registry bucket 鐨?manifest 鍜?customer-db 鐨勫璁¤褰曘€?#         G3 鐨?Flag 鏈韩涓嶅湪 object-store锛屽湪 customer-db 鐨?regulated_model_training_records 琛ㄤ腑銆?#
# 璁捐瑕佺偣锛?# - 鍩轰簬 minio/minio:RELEASE.2024-12-18T13-15-44Z锛岀湡瀹炶繍琛?MinIO S3 API銆?# - EXPOSE 9000锛圫3 API锛? 9001锛圕onsole锛夛紝鍋ュ悍妫€鏌?/minio/health/live銆?# - 閫氳繃 mc锛圡inIO Client锛夊垵濮嬪寲 bucket銆佺敤鎴枫€佺瓥鐣ャ€佺瀛愭暟鎹€?# - Flag 閫氳繃鐜鍙橀噺娉ㄥ叆锛宔ntrypoint.sh 鐢?sed 鏇挎崲 CSV 涓殑鍗犱綅绗︺€傞暅鍍忓唴涓嶇‖缂栫爜 Flag銆?# - 璧勬簮闄愬埗 0.75C/512M 鐢卞钩鍙板鍣ㄧ紪鎺掑眰寮哄埗銆?# - 浣庢潈闄愮敤鎴?nm-low-priv 鍙兘璁块棶 public-model-artifacts锛圖2 鍏ュ彛锛夈€?# - 楂樻潈闄愮敤鎴?nm-admin 鍙闂墍鏈?bucket銆?
# ============================================================
# 闃舵 1锛氭瀯寤洪樁娈?- 涓嬭浇 mc锛圡inIO Client锛?# ============================================================
FROM alpine:3.20 AS builder

RUN apk add --no-cache curl ca-certificates tzdata

# 涓嬭浇 mc锛圡inIO Client锛? 浣跨敤涓?MinIO 鏈嶅姟绔吋瀹圭殑鐗堟湰
RUN curl -fsSL -o /usr/local/bin/mc \
    https://dl.min.io/client/mc/release/linux-amd64/mc && \
    chmod +x /usr/local/bin/mc

# 涓嬭浇闈欐€佺紪璇戠殑 curl锛堢敤浜庡仴搴锋鏌ワ紝minio 闀滃儚鍙兘涓嶅惈 curl锛?# 濡備笅杞藉け璐ュ垯鍒涘缓鍗犱綅鑴氭湰锛宧ealthcheck.sh 浼氬洖閫€鍒?mc
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
# 闃舵 2锛氳繍琛岄樁娈?- MinIO 鏈嶅姟
# ============================================================
FROM minio/minio:RELEASE.2024-12-18T13-15-44Z

LABEL org.opencontainers.image.title="nebulamind-object-store"
LABEL org.opencontainers.image.description="NebulaMind AI Corp - Object Storage & Model Artifacts"
LABEL org.nebulamind.zone="data"
LABEL org.nebulamind.flags="FLAG_OBJECT_BUCKET_POLICY,FLAG_FINAL_MODEL_SUPPLY_CHAIN"
LABEL org.nebulamind.resources="0.75C/512M"

# 浠庢瀯寤洪樁娈靛鍒?mc 鍜岄潤鎬?curl
COPY --from=builder /usr/local/bin/mc /usr/local/bin/mc
COPY --from=builder /usr/local/bin/curl-static /usr/local/bin/curl

# 璁剧疆鏃跺尯锛堜笌鍏朵粬 NebulaMind 鏈嶅姟淇濇寔涓€鑷达級
COPY --from=builder /usr/share/zoneinfo/Asia/Shanghai /etc/localtime
COPY --from=builder /usr/share/zoneinfo/Asia/Shanghai /usr/share/zoneinfo/Asia/Shanghai
RUN echo "Asia/Shanghai" > /etc/timezone

# Default MinIO credentials; runtime variables may override them.
ENV MINIO_ROOT_USER=nm-root-admin \
    MINIO_ROOT_PASSWORD=nm-root-admin-secret-2026

# 澶嶅埗鍏变韩璧勬簮
COPY --from=builder /prestage/_shared/scripts/flag.sh /_shared/scripts/flag.sh
COPY --from=builder /prestage/_shared/scripts/runtime-env.sh /_shared/scripts/runtime-env.sh

# 澶嶅埗鏈嶅姟鏂囦欢
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
