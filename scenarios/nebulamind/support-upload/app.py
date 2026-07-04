#!/usr/bin/env python3
"""NebulaMind AI Corp - Customer Support Upload Center.

DMZ service for customers to upload log packages and request document parsing.

Routes:
  GET  /                  - Upload center homepage
  GET  /healthz           - Health check
  POST /api/upload        - Upload log package (B1 vulnerability entry)
  GET  /api/tasks/<id>    - Query task status and parse report
  GET  /download?file=    - Download file (B2 path traversal vulnerability)
  POST /api/parse-url     - Submit remote URL for parsing (B3 SSRF entry)
  GET  /tickets           - Historical ticket list

Vulnerabilities (by design, for CTF):
  B1: /api/upload only checks MIME type, not file content/extension.
      Uploading .phar.jpg with MIME image/jpeg is accepted.
      The parse report (viewable via /api/tasks/<id>) contains the flag
      in the parse log (disguised as a diagnostic token).
  B2: /download?file= does not sanitize the file parameter.
      file=../config/worker.yml reads /app/config/worker.yml which contains
      the flag in the diagnostic.test_flag field.
  B3: /api/parse-url forwards to the platform-injected document-worker URL.
      The actual SSRF execution happens in document-worker, which returns
      the flag in the SSRF result. This service only forwards the request.
"""

from __future__ import annotations

import json
import os
import re
import socket
import urllib.error
import urllib.request
import uuid
from datetime import datetime

from flask import (
    Flask,
    Response,
    abort,
    jsonify,
    render_template,
    request,
    send_file,
)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "8080"))
HOST = os.environ.get("HOST", "0.0.0.0")

APP_DIR = os.path.dirname(os.path.abspath(__file__))
UPLOAD_DIR = os.environ.get("UPLOAD_DIR", "/app/uploads")
CONFIG_DIR = os.environ.get("CONFIG_DIR", "/app/config")
B1_FLAG_FILE = os.path.join(CONFIG_DIR, "b1_flag.txt")
DOCUMENT_WORKER_URL = os.environ["NM_DOCUMENT_WORKER_URL"]

# B1 vulnerability: only MIME type is checked, not file content or extension.
# The support center accepts images (screenshots), logs, archives, and PDFs.
ALLOWED_MIMES = {
    "text/plain",
    "application/zip",
    "application/octet-stream",
    "application/x-zip-compressed",
    "application/x-gzip",
    "text/csv",
    "application/json",
    "image/jpeg",
    "image/png",
    "application/pdf",
}

# Task ID validation (prevents path traversal via /api/tasks/<id>)
TASK_ID_RE = re.compile(r"^[A-Za-z0-9_-]+$")

# ---------------------------------------------------------------------------
# Historical tickets (realistic data)
# ---------------------------------------------------------------------------

HISTORICAL_TICKETS = [
    {
        "ticketId": "SUP-2026-0421",
        "customer": "云擎科技有限公司",
        "contact": "王经理",
        "title": "知识库同步异常 - PDF 文档解析卡在 70%",
        "status": "resolved",
        "statusLabel": "已解决",
        "priority": "P2",
        "category": "文档解析",
        "createdAt": "2026-06-15 09:23:41",
        "updatedAt": "2026-06-15 14:12:08",
        "assignee": "李工",
        "description": "客户反馈 RAG 知识库同步 200+ PDF 文档时，部分文档解析卡在 70%",
        "resolution": "定位到 OCR 引擎内存不足，已扩容 document-worker 节点",
    },
    {
        "ticketId": "SUP-2026-0418",
        "customer": "恒生证券",
        "contact": "张总",
        "title": "智能问答返回结果包含过期信息",
        "status": "resolved",
        "statusLabel": "已解决",
        "priority": "P1",
        "category": "问答引擎",
        "createdAt": "2026-06-12 14:05:22",
        "updatedAt": "2026-06-13 10:30:00",
        "assignee": "陈工",
        "description": "投研知识库问答返回了已下架的理财产品信息",
        "resolution": "向量索引未及时更新，已触发全量重建并调整增量同步频率",
    },
    {
        "ticketId": "SUP-2026-0415",
        "customer": "某省级政务云",
        "contact": "赵处长",
        "title": "SSO 单点登录集成失败 - SAML 响应签名校验不通过",
        "status": "in_progress",
        "statusLabel": "处理中",
        "priority": "P2",
        "category": "集成对接",
        "createdAt": "2026-06-18 10:15:00",
        "updatedAt": "2026-06-19 16:45:30",
        "assignee": "周工",
        "description": "客户 AD FS IdP 签名证书更换后，NebulaMind SSO 登录失败",
        "resolution": None,
    },
    {
        "ticketId": "SUP-2026-0412",
        "customer": "新能源车企",
        "contact": "孙总监",
        "title": "上传维修手册 ZIP 包后解析报错",
        "status": "resolved",
        "statusLabel": "已解决",
        "priority": "P3",
        "category": "文档解析",
        "createdAt": "2026-06-08 11:20:33",
        "updatedAt": "2026-06-09 09:15:00",
        "assignee": "李工",
        "description": "客户上传 500MB 维修手册 ZIP 包，解析时出现 OOM",
        "resolution": "已调整 worker 内存配置并增加分卷解析支持",
    },
    {
        "ticketId": "SUP-2026-0408",
        "customer": "医疗集团",
        "contact": "刘主任",
        "title": "权限隔离失效 - A 部门用户可见 B 部门知识库",
        "status": "resolved",
        "statusLabel": "已解决",
        "priority": "P0",
        "category": "安全权限",
        "createdAt": "2026-06-03 08:45:12",
        "updatedAt": "2026-06-03 12:00:00",
        "assignee": "陈工",
        "description": "跨部门知识库权限隔离失效，紧急安全工单",
        "resolution": "向量检索 filter 逻辑缺陷，已紧急修复并全量审计",
    },
    {
        "ticketId": "SUP-2026-0405",
        "customer": "教育科技公司",
        "contact": "钱老师",
        "title": "API 调用频率限制咨询",
        "status": "closed",
        "statusLabel": "已关闭",
        "priority": "P4",
        "category": "咨询",
        "createdAt": "2026-05-28 15:30:00",
        "updatedAt": "2026-05-29 10:00:00",
        "assignee": "王工",
        "description": "客户咨询标准版 API 调用频率限制及升级方案",
        "resolution": "已提供企业版方案报价，客户考虑中",
    },
]

# ---------------------------------------------------------------------------
# Flask app
# ---------------------------------------------------------------------------

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = 50 * 1024 * 1024  # 50MB


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def now_str() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def read_b1_flag() -> str:
    """Read B1 flag from injected file, fallback to env var."""
    try:
        with open(B1_FLAG_FILE, "r", encoding="utf-8") as f:
            return f.read().strip()
    except (IOError, OSError):
        key = "GZCTF_FLAG_UPLOAD_MIME_BYPASS"
        return os.environ.get(
            key,
            os.environ.get("GZCTF_FLAG", "flag{b1_mime_bypass_placeholder}"),
        )


def generate_task_id() -> str:
    """Generate realistic task ID: SUP-YYYYMMDD-XXXX."""
    now = datetime.now()
    rand = uuid.uuid4().hex[:4].upper()
    return f"SUP-{now.strftime('%Y%m%d')}-{rand}"


def generate_parse_log(task_id: str, filename: str, mime: str, flag: str) -> str:
    """Generate realistic parse log with B1 flag embedded as diagnostic token."""
    ts = now_str()
    return (
        f"[{ts}] [INFO] Worker#3 (host: doc-worker-2) picked up task {task_id}\n"
        f"[{ts}] [INFO] Opening upload container: {filename}\n"
        f"[{ts}] [INFO] Client-declared MIME: {mime}\n"
        f"[{ts}] [WARN] Extension/MIME mismatch detected. File extension suggests "
        f"non-text content, but MIME header claims {mime}. "
        f"Applying permissive MIME-only policy "
        f"(config: upload.mime_check=strict, upload.content_check=disabled).\n"
        f"[{ts}] [INFO] Probing file signature...\n"
        f"[{ts}] [DEBUG] File signature: 3c3f706870 (text \"<?php\")\n"
        f"[{ts}] [WARN] File appears to be PHP source disguised as {mime}. "
        f"Forwarding to sandboxed parser.\n"
        f"[{ts}] [INFO] Sandboxed parser initialized. "
        f"Diagnostic token for cross-service tracing: {flag}\n"
        f"[{ts}] [INFO] Extracting text content from PHP stream...\n"
        f"[{ts}] [INFO] Found 0 valid log entries, 1 embedded script block.\n"
        f"[{ts}] [INFO] Parse complete in 1.84s. Result: accepted-with-warnings.\n"
        f"[{ts}] [DEBUG] Report written to /tmp/worker/{task_id}/report.json"
    )


def generate_report(task_id, customer, title, filename, mime, size):
    """Generate parse report with B1 flag in parse log."""
    flag = read_b1_flag()
    return {
        "taskId": task_id,
        "customer": customer,
        "title": title,
        "status": "completed",
        "createdAt": now_str(),
        "completedAt": now_str(),
        "file": {
            "name": filename,
            "size": size,
            "mime": mime,
        },
        "summary": {
            "totalFiles": 1,
            "parsedFiles": 1,
            "failedFiles": 0,
            "extractedEntities": 0,
            "duration": "1.84s",
            "result": "accepted-with-warnings",
        },
        "parseLog": generate_parse_log(task_id, filename, mime, flag),
        "artifacts": [
            {
                "name": "report.json",
                "size": 0,
                "path": f"/tmp/worker/{task_id}/report.json",
            },
        ],
    }


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.route("/")
def index():
    return render_template("index.html", recent_tickets=HISTORICAL_TICKETS[:3])


@app.route("/healthz")
def healthz():
    return Response("ok", status=200, mimetype="text/plain")


@app.route("/favicon.ico")
def favicon():
    return Response("", status=204, mimetype="image/x-icon")


@app.route("/tickets")
def tickets():
    return render_template("tickets.html", tickets=HISTORICAL_TICKETS)


@app.route("/_shared/<path:filename>")
def shared_files(filename):
    """Serve shared assets (nebulamind.css, etc.)."""
    shared_path = os.path.join("/_shared", filename)
    if not os.path.isfile(shared_path):
        abort(404)
    return send_file(shared_path)


@app.route("/api/upload", methods=["POST"])
def upload():
    """B1: Upload log package. Only checks MIME type, not content/extension."""
    customer = request.form.get("customer", "").strip()
    title = request.form.get("title", "").strip()

    if not customer or not title:
        return jsonify({"error": "客户名称和工单标题为必填项"}), 400

    file = request.files.get("file")
    if not file or not file.filename:
        return jsonify({"error": "请上传日志包文件"}), 400

    # B1 VULNERABILITY: Only check MIME type, not file extension or content.
    # A .phar.jpg file with Content-Type: image/jpeg will be accepted.
    mime = file.mimetype or "application/octet-stream"
    if mime not in ALLOWED_MIMES:
        return jsonify({
            "error": f"不支持的文件类型: {mime}",
            "allowed": "日志文件、压缩包、截图、PDF",
        }), 415

    task_id = generate_task_id()
    task_dir = os.path.join(UPLOAD_DIR, task_id)
    os.makedirs(task_dir, exist_ok=True)

    # Save file. We sanitize the filename to prevent directory traversal via
    # the filename itself, but we do NOT check the extension or content.
    filename = os.path.basename(file.filename)
    file.save(os.path.join(task_dir, filename))

    file_path = os.path.join(task_dir, filename)
    size = os.path.getsize(file_path)

    # Generate report with B1 flag in parse log
    report = generate_report(task_id, customer, title, filename, mime, size)
    report_path = os.path.join(task_dir, "report.json")
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    return jsonify({
        "taskId": task_id,
        "status": "queued",
        "message": "上传成功，日志包已加入解析队列",
        "reportUrl": f"/api/tasks/{task_id}",
    })


@app.route("/api/tasks/<task_id>")
def get_task(task_id):
    """Query task status and parse report."""
    # Validate task_id to prevent path traversal via this route
    if not TASK_ID_RE.match(task_id):
        abort(404)

    report_path = os.path.join(UPLOAD_DIR, task_id, "report.json")
    if not os.path.isfile(report_path):
        abort(404)

    with open(report_path, "r", encoding="utf-8") as f:
        report = json.load(f)

    return render_template("task.html", task=report)


@app.route("/download")
def download():
    """B2: Download file. Path traversal vulnerability - file param not sanitized."""
    file_param = request.args.get("file", "")
    if not file_param:
        abort(400, description="file parameter required")

    # B2 VULNERABILITY: No path boundary check on file parameter.
    # Normal: /download?file=<taskId>/report.json -> /app/uploads/<taskId>/report.json
    # Exploit: /download?file=../config/worker.yml -> /app/config/worker.yml
    file_path = os.path.normpath(os.path.join(UPLOAD_DIR, file_param))

    if not os.path.isfile(file_path):
        abort(404)

    return send_file(file_path)


@app.route("/api/parse-url", methods=["POST"])
def parse_url():
    """B3: Submit remote URL for parsing. Forwards to document-worker."""
    data = request.get_json(silent=True) or {}
    url = data.get("url", "").strip()

    if not url:
        return jsonify({"error": "url is required"}), 400

    # Forward to document-worker's parse endpoint.
    # The actual SSRF execution happens in document-worker.
    # Forward optional "profile" parameter (used by D3 command injection chain):
    # document-worker /api/parse accepts {url, profile} and runs `convert --profile {profile}`.
    # Without forwarding profile, D3 (and 14 downstream flags) become unreachable.
    try:
        forward_payload = {"url": url, "source": "support-upload"}
        if "profile" in data:
            forward_payload["profile"] = data["profile"]
        req_data = json.dumps(forward_payload).encode("utf-8")
        req = urllib.request.Request(
            f"{DOCUMENT_WORKER_URL}/api/parse",
            data=req_data,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=15) as resp:
            body = resp.read().decode("utf-8")
            try:
                result = json.loads(body)
                return jsonify({
                    "status": "forwarded",
                    "worker": "document-worker",
                    "result": result,
                })
            except json.JSONDecodeError:
                return Response(
                    body, status=resp.status, content_type="application/json"
                )
    except urllib.error.URLError as e:
        reason = e.reason if hasattr(e, "reason") else str(e)
        return jsonify({
            "error": "document-worker service unavailable",
            "detail": str(reason),
            "hint": "document-worker may not be deployed in this environment",
        }), 502
    except socket.timeout:
        return jsonify({"error": "document-worker request timed out"}), 504
    except Exception as e:
        return jsonify({
            "error": "failed to forward request to document-worker",
            "detail": str(e),
        }), 500


# ---------------------------------------------------------------------------
# Error handlers
# ---------------------------------------------------------------------------

@app.errorhandler(404)
def not_found(e):
    if request.path.startswith("/api/"):
        return jsonify({"error": "not found"}), 404
    return Response(
        '<!DOCTYPE html><html lang="zh-CN"><head><meta charset="UTF-8">'
        '<title>404 - NebulaMind Support</title>'
        '<link rel="stylesheet" href="/_shared/assets/nebulamind.css">'
        '</head><body><nav class="nm-nav">'
        '<a href="/" class="nm-nav-brand">NebulaMind Support</a>'
        '</nav><div class="nm-container" style="text-align:center;padding-top:80px;">'
        '<h1 style="font-size:64px;color:var(--nm-text-soft);">404</h1>'
        '<p class="nm-text-soft nm-mt-16">页面不存在</p>'
        '<a href="/" class="nm-btn nm-btn-primary nm-mt-32">返回首页</a>'
        '</div></body></html>',
        status=404,
        content_type="text/html; charset=utf-8",
    )


@app.errorhandler(400)
def bad_request(e):
    if request.path.startswith("/api/"):
        desc = e.description if hasattr(e, "description") else "bad request"
        return jsonify({"error": str(desc)}), 400
    return Response("Bad Request", status=400)


@app.errorhandler(413)
def too_large(e):
    return jsonify({"error": "文件过大，最大支持 50MB"}), 413


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    os.makedirs(UPLOAD_DIR, exist_ok=True)
    print(
        f"[support-upload] NebulaMind Support Upload Center "
        f"starting on {HOST}:{PORT}",
        flush=True,
    )
    print(f"[support-upload] Upload dir: {UPLOAD_DIR}", flush=True)
    print(f"[support-upload] Document worker: {DOCUMENT_WORKER_URL}", flush=True)
    app.run(host=HOST, port=PORT, threaded=True, debug=False)
