#!/usr/bin/env python3
"""NebulaMind AI Corp - 企业官网 HTTP 服务。

使用 Python 标准库 http.server，无外部依赖。
提供企业官网页面、隐藏目录、静态资源、健康检查等路由。
"""

from __future__ import annotations

import html
import http.server
import mimetypes
import os
import socketserver
from datetime import datetime
from urllib.parse import unquote, urlparse

# ---------------------------------------------------------------------------
# 配置
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "8080"))
HOST = os.environ.get("HOST", "0.0.0.0")

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TEMPLATES_DIR = os.path.join(BASE_DIR, "templates")
STATIC_DIR = os.path.join(BASE_DIR, "static")
SHARED_DIR = "/_shared"

# 路由 -> 模板文件映射
TEMPLATE_ROUTES = {
    "/": "index.html",
    "/products": "products.html",
    "/customers": "customers.html",
    "/news": "news.html",
    "/downloads": "downloads.html",
    "/login": "login.html",
}

# 隐藏归档目录（A2 题目核心路径）
ARCHIVE_DIR = os.path.join(STATIC_DIR, "resources", "archive")


# ---------------------------------------------------------------------------
# 辅助函数
# ---------------------------------------------------------------------------

def guess_mime(path: str) -> str:
    """根据文件扩展名猜测 MIME 类型。"""
    guessed, _ = mimetypes.guess_type(path)
    return guessed or "application/octet-stream"


def read_file(path: str) -> bytes:
    """读取文件内容为 bytes。"""
    with open(path, "rb") as f:
        return f.read()


def safe_join(base: str, *parts: str) -> str | None:
    """安全拼接路径，防止目录穿越。返回 None 表示不安全。"""
    target = os.path.normpath(os.path.join(base, *parts))
    base_abs = os.path.abspath(base)
    target_abs = os.path.abspath(target)
    if not target_abs.startswith(base_abs + os.sep) and target_abs != base_abs:
        return None
    return target_abs


def generate_directory_listing(dir_path: str, url_path: str) -> str:
    """生成简单的目录列表 HTML。"""
    entries = sorted(os.listdir(dir_path))
    rows = []
    parent = os.path.dirname(url_path.rstrip("/"))
    rows.append(
        f'<tr><td><a href="{parent}/">../</a></td><td>-</td><td>-</td></tr>'
    )
    for name in entries:
        full = os.path.join(dir_path, name)
        if os.path.isdir(full):
            size = "-"
            mtime = datetime.fromtimestamp(os.path.getmtime(full)).strftime(
                "%Y-%m-%d %H:%M"
            )
            link = f"{url_path.rstrip('/')}/{name}/"
            icon = "&#128193;"
        else:
            size = f"{os.path.getsize(full):,}"
            mtime = datetime.fromtimestamp(os.path.getmtime(full)).strftime(
                "%Y-%m-%d %H:%M"
            )
            link = f"{url_path.rstrip('/')}/{name}"
            icon = "&#128196;"
        rows.append(
            f"<tr><td>{icon} <a href=\"{link}\">{html.escape(name)}</a></td>"
            f"<td>{size}</td><td>{mtime}</td></tr>"
        )

    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Index of {html.escape(url_path)}</title>
<link rel="stylesheet" href="/static/css/portal.css">
</head>
<body>
<nav class="nm-nav">
    <span class="nm-nav-brand">NebulaMind</span>
    <div class="nm-nav-links">
        <a href="/">Home</a>
        <a href="/products">Products</a>
        <a href="/customers">Customers</a>
    </div>
</nav>
<div class="nm-container">
    <h1 style="margin-bottom:24px;font-family:var(--nm-mono);font-size:20px;">
        Index of {html.escape(url_path)}
    </h1>
    <div class="nm-card" style="padding:0;overflow:hidden;">
        <table class="nm-table">
            <thead><tr><th>Name</th><th>Size</th><th>Modified</th></tr></thead>
            <tbody>
            {''.join(rows)}
            </tbody>
        </table>
    </div>
    <p class="nm-text-soft nm-mt-16" style="font-size:12px;">
        NebulaMind Internal Resource Archive &mdash; Server: nginx/1.27 (auto-index)
    </p>
</div>
</body>
</html>"""


# ---------------------------------------------------------------------------
# 请求处理器
# ---------------------------------------------------------------------------

class PortalHandler(http.server.BaseHTTPRequestHandler):
    """NebulaMind 企业官网请求处理器。"""

    server_version = "NebulaMindPortal/2.1.0"
    sys_version = "Python/" + os.sys.version.split()[0]

    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        """简洁的访问日志。"""
        ts = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{ts}] {self.client_address[0]} - {fmt % args}", flush=True)

    # -- 响应辅助 -----------------------------------------------------------

    def _send_bytes(self, code: int, body: bytes, content_type: str,
                    extra_headers: dict | None = None):
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Server", self.server_version)
        self.send_header("X-Powered-By", "NebulaMind Portal")
        self.send_header("X-Frame-Options", "SAMEORIGIN")
        if extra_headers:
            for k, v in extra_headers.items():
                self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _send_text(self, code: int, text: str, content_type: str = "text/plain"):
        self._send_bytes(code, text.encode("utf-8"), content_type + "; charset=utf-8")

    def _send_html(self, code: int, html_str: str):
        self._send_bytes(code, html_str.encode("utf-8"), "text/html; charset=utf-8")

    def _send_404(self):
        body = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>404 - NebulaMind</title>
<link rel="stylesheet" href="/static/css/portal.css">
</head>
<body>
<nav class="nm-nav">
    <a href="/" class="nm-nav-brand">NebulaMind</a>
</nav>
<div class="nm-container" style="text-align:center;padding-top:80px;">
    <h1 style="font-size:64px;color:var(--nm-text-soft);">404</h1>
    <p class="nm-text-soft nm-mt-16">The page you are looking for does not exist.</p>
    <a href="/" class="nm-btn nm-btn-primary nm-mt-32">Back to Home</a>
</div>
</body>
</html>"""
        self._send_html(404, body)

    def _serve_template(self, template_name: str):
        path = os.path.join(TEMPLATES_DIR, template_name)
        if not os.path.isfile(path):
            self._send_404()
            return
        body = read_file(path)
        self._send_bytes(200, body, "text/html; charset=utf-8")

    def _serve_static_file(self, fs_path: str):
        if not os.path.isfile(fs_path):
            self._send_404()
            return
        body = read_file(fs_path)
        self._send_bytes(200, body, guess_mime(fs_path))

    def _serve_directory(self, fs_path: str, url_path: str):
        # 检查是否有 index.html
        index = os.path.join(fs_path, "index.html")
        if os.path.isfile(index):
            body = read_file(index)
            self._send_bytes(200, body, "text/html; charset=utf-8")
            return
        # 生成目录列表
        listing = generate_directory_listing(fs_path, url_path)
        self._send_html(200, listing)

    # -- 路由分发 -----------------------------------------------------------

    def do_GET(self):
        self._handle_request()

    def do_HEAD(self):
        self._handle_request()

    def _handle_request(self):
        parsed = urlparse(self.path)
        raw_path = unquote(parsed.path)

        # 规范化路径：去掉末尾斜杠（根路径除外）
        if len(raw_path) > 1 and raw_path.endswith("/"):
            raw_path = raw_path.rstrip("/")
        path = raw_path if raw_path else "/"

        # 健康检查
        if path == "/healthz":
            self._send_text(200, "ok")
            return

        # robots.txt
        if path == "/robots.txt":
            self._serve_static_file(os.path.join(STATIC_DIR, "robots.txt"))
            return

        # favicon
        if path == "/favicon.ico":
            self._send_bytes(204, b"", "image/x-icon")
            return

        # 共享资源（/_shared/assets/...）
        if path.startswith("/_shared/"):
            rel = path[len("/_shared/"):]
            fs_path = safe_join(SHARED_DIR, rel)
            if fs_path and os.path.isfile(fs_path):
                self._serve_static_file(fs_path)
            else:
                self._send_404()
            return

        # 静态资源（/static/...）
        if path.startswith("/static/"):
            rel = path[len("/static/"):]
            fs_path = safe_join(STATIC_DIR, rel)
            if fs_path is None:
                self._send_404()
                return
            if os.path.isfile(fs_path):
                self._serve_static_file(fs_path)
            elif os.path.isdir(fs_path):
                url_path = "/static/" + rel + "/"
                self._serve_directory(fs_path, url_path)
            else:
                self._send_404()
            return

        # 模板路由
        if path in TEMPLATE_ROUTES:
            self._serve_template(TEMPLATE_ROUTES[path])
            return

        # 隐藏归档目录（A2 题目）
        if path == "/resources" or path.startswith("/resources/"):
            rel = path[len("/resources/"):] if path != "/resources" else ""
            fs_path = safe_join(os.path.join(STATIC_DIR, "resources"), rel)
            if fs_path is None:
                self._send_404()
                return
            if os.path.isfile(fs_path):
                self._serve_static_file(fs_path)
            elif os.path.isdir(fs_path):
                url_path = "/resources/" + rel + "/"
                self._serve_directory(fs_path, url_path)
            else:
                self._send_404()
            return

        # 其他路径 -> 404
        self._send_404()


# ---------------------------------------------------------------------------
# 服务器启动
# ---------------------------------------------------------------------------

class ThreadingHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    """多线程 HTTP 服务器。"""
    daemon_threads = True
    allow_reuse_address = True


def main():
    mimetypes.init()
    # 确保常见类型正确
    mimetypes.add_type("application/javascript", ".js")
    mimetypes.add_type("application/json", ".map")
    mimetypes.add_type("text/css", ".css")
    mimetypes.add_type("text/html", ".html")

    server = ThreadingHTTPServer((HOST, PORT), PortalHandler)
    print(f"[portal-web] NebulaMind Portal listening on {HOST}:{PORT}", flush=True)
    print(f"[portal-web] Templates: {TEMPLATES_DIR}", flush=True)
    print(f"[portal-web] Static: {STATIC_DIR}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[portal-web] shutting down...", flush=True)
        server.shutdown()


if __name__ == "__main__":
    main()
