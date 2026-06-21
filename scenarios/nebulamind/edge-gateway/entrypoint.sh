#!/bin/sh
set -e

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_PORTAL_WEB_URL NM_SUPPORT_UPLOAD_URL
nm_render_required_placeholders /etc/nginx/conf.d/default.conf \
  "__NM_PORTAL_WEB_URL__=NM_PORTAL_WEB_URL" \
  "__NM_SUPPORT_UPLOAD_URL__=NM_SUPPORT_UPLOAD_URL"

FLAG_A1="$(get_flag 'FLAG_PUBLIC_DISCOVERY' 'flag{a1_public_discovery_placeholder}')"
ARTIFACT_HASH="$(printf '%s' "$FLAG_A1" | sha256sum | cut -c1-16)"

cat > /app/build-info.json <<EOF
{
  "service": "nebulamind-mirror-site",
  "version": "2026.06.1",
  "commit": "a3f7c2e8b9d1",
  "branch": "release/2026.06",
  "buildTime": "2026-06-15T08:42:13Z",
  "builder": "ci-runner@nebulamind-internal",
  "goVersion": "go1.22.3",
  "moduleName": "github.com/nebulamind/portal-mirror",
  "deployEnv": "production-edge",
  "region": "cn-east-1",
  "replica": "edge-gateway-01",
  "featureFlags": {
    "enableNewDownloadCenter": true,
    "enableBuildInfoEndpoint": true,
    "enableS3DirectDownload": false
  },
  "dependencies": {
    "portal-web": "2026.06.2",
    "support-upload": "2026.06.1",
    "ai-console-api": "2026.06.3"
  },
  "internalEndpoints": {
    "portalWeb": "$NM_PORTAL_WEB_URL",
    "supportUpload": "$NM_SUPPORT_UPLOAD_URL"
  },
  "buildFlags": {
    "debugInfo": "FLAG_EMBEDDED_IN_BUILD_METADATA",
    "artifactHash": "$ARTIFACT_HASH"
  },
  "metadata": {
    "note": "Internal build metadata. Do not expose externally.",
    "owner": "platform-team@nebulamind.ai",
    "ticket": "PLAT-2841",
    "flag": "$FLAG_A1"
  }
}
EOF
chmod 0644 /app/build-info.json

mkdir -p /app/mirror/downloads
cat > /app/mirror/index.html <<'HTMLEOF'
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>NebulaMind 资源下载中心</title>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/water.css@2/out/water.css">
<style>
body { font-family: -apple-system, "Segoe UI", "Microsoft YaHei", sans-serif; max-width: 900px; margin: 0 auto; padding: 24px; }
header { border-bottom: 2px solid #6366f1; padding-bottom: 12px; margin-bottom: 24px; }
h1 { color: #6366f1; }
.resource-list { list-style: none; padding: 0; }
.resource-list li { padding: 12px; border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 8px; }
.resource-list a { color: #6366f1; text-decoration: none; font-weight: 500; }
.resource-list .meta { color: #6b7280; font-size: 12px; margin-top: 4px; }
footer { margin-top: 40px; padding-top: 16px; border-top: 1px solid #e5e7eb; color: #6b7280; font-size: 12px; }
footer .build-info { color: #9ca3af; }
</style>
</head>
<body>
<header>
    <h1>NebulaMind 资源下载中心</h1>
    <p>企业 AI 平台 · 客户端工具 · SDK · 模型权重镜像</p>
</header>

<ul class="resource-list">
    <li>
        <a href="/downloads/nebulamind-sdk-python-1.4.2.tar.gz">nebulamind-sdk-python-1.4.2.tar.gz</a>
        <div class="meta">Python SDK · 2.4 MB · 2026-06-10</div>
    </li>
    <li>
        <a href="/downloads/nebulamind-cli-1.4.2-linux-amd64">nebulamind-cli-1.4.2-linux-amd64</a>
        <div class="meta">命令行工具 · 18 MB · 2026-06-10</div>
    </li>
    <li>
        <a href="/downloads/nebulamind-console-agent-2.1.0.tar.gz">nebulamind-console-agent-2.1.0.tar.gz</a>
        <div class="meta">控制台 Agent · 42 MB · 2026-05-28</div>
    </li>
    <li>
        <a href="/downloads/model-recommendation-v3-public.bin">model-recommendation-v3-public.bin</a>
        <div class="meta">公开推荐模型权重 · 256 MB · 2026-05-20</div>
    </li>
    <li>
        <a href="/downloads/nebulamind-kb-importer-0.9.1.tar.gz">nebulamind-kb-importer-0.9.1.tar.gz</a>
        <div class="meta">知识库导入工具 · 5.1 MB · 2026-04-15</div>
    </li>
</ul>

<footer>
    <div>© 2026 NebulaMind AI Corp. All rights reserved.</div>
    <div class="build-info">Build: release/2026.06 · commit a3f7c2e · 2026-06-15 · <a href="/status/build-info">build info</a></div>
</footer>
</body>
</html>
HTMLEOF

for f in nebulamind-sdk-python-1.4.2.tar.gz nebulamind-cli-1.4.2-linux-amd64 nebulamind-console-agent-2.1.0.tar.gz model-recommendation-v3-public.bin nebulamind-kb-importer-0.9.1.tar.gz; do
    [ -f "/app/mirror/downloads/$f" ] || printf 'NebulaMind placeholder artifact: %s\n' "$f" > "/app/mirror/downloads/$f"
done

printf 'ok\n' > /app/healthz.html

echo "[edge-gateway] A1 flag injected, mirror site ready"
echo "[edge-gateway] starting nginx..."

exec "$@"
