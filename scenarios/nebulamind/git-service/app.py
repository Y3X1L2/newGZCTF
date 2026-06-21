#!/usr/bin/env python3
"""NebulaMind AI Corp - Internal Git Service.

Operations-zone service providing HTTP access to internal Git repositories.
Implements a lightweight Git HTTP server (smart HTTP protocol) plus a web UI
for browsing repositories, commits, and file contents.

Routes:
  Web UI:
    GET  /                                    - Repository list (index)
    GET  /nebulamind/<repo>                   - Repository main page (files + recent commits)
    GET  /nebulamind/<repo>/commits           - Full commit history
    GET  /nebulamind/<repo>/commit/<sha>      - Commit detail with diff
    GET  /nebulamind/<repo>/blob/<ref>/<path> - File content at specific ref
    GET  /nebulamind/<repo>/raw/<ref>/<path>  - Raw file content (download)

  Git Smart HTTP (clone/fetch only, no push):
    GET  /nebulamind/<repo>.git/info/refs?service=git-upload-pack
    GET  /nebulamind/<repo>.git/HEAD
    POST /nebulamind/<repo>.git/git-upload-pack

  Utility:
    GET  /healthz                             - Health check
    GET  /_shared/<path>                      - Shared assets (CSS)

Security:
  - Repository names validated (alphanumeric + dash/underscore only)
  - No push support (git-receive-pack disabled)
  - Non-root user
  - Flag is never hardcoded; injected by entrypoint.sh into git history

E1 Challenge:
  The console-api repository contains a historical commit (commit #3) that
  added .env.example.old with dev credentials and the E1 flag. A later
  commit removed the file from HEAD, but it remains accessible in git
  history. Players must discover this by browsing commit history or
  cloning the repository and inspecting past versions.
"""

from __future__ import annotations

import html
import os
import re
import subprocess
from typing import Optional

from flask import (
    Flask,
    Response,
    abort,
    render_template,
    request,
    send_file,
)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

PORT = int(os.environ.get("PORT", "3000"))
HOST = os.environ.get("HOST", "0.0.0.0")
GIT_SERVICE_URL = os.environ["NM_GIT_SERVICE_URL"].rstrip("/")

REPO_ROOT = os.environ.get("REPO_ROOT", "/srv/git")
ORG = "nebulamind"

# Repository name validation: alphanumeric, dash, underscore only
REPO_NAME_RE = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9._-]*$")
# SHA validation: hex string
SHA_RE = re.compile(r"^[0-9a-f]{4,64}$")
# Ref name validation (branch names, tags)
REF_RE = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9._/-]*$")

# Maximum diff output size (to avoid huge responses)
MAX_DIFF_SIZE = 256 * 1024  # 256KB
# Maximum blob content size for web display
MAX_BLOB_DISPLAY = 512 * 1024  # 512KB

# ---------------------------------------------------------------------------
# Flask app
# ---------------------------------------------------------------------------

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = 100 * 1024 * 1024  # 100MB for clone


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def repo_path(repo: str) -> str:
    """Get the filesystem path to a bare repository."""
    return os.path.join(REPO_ROOT, ORG, f"{repo}.git")


def is_valid_repo(repo: str) -> bool:
    """Validate repository name and check it exists."""
    if not repo or not REPO_NAME_RE.match(repo):
        return False
    path = repo_path(repo)
    return os.path.isdir(path) and os.path.isfile(os.path.join(path, "HEAD"))


def is_valid_sha(sha: str) -> bool:
    """Validate a git SHA hash."""
    return bool(sha and SHA_RE.match(sha))


def is_valid_ref(ref: str) -> bool:
    """Validate a git ref name (branch/tag)."""
    if not ref or len(ref) > 200:
        return False
    return bool(REF_RE.match(ref))


def git(repo: str, *args: str, input_data: Optional[bytes] = None,
        binary: bool = False, timeout: int = 30) -> subprocess.CompletedProcess:
    """Run a git command in the specified repository.

    Returns the CompletedProcess result. Raises RuntimeError on failure.
    """
    cmd = ["git", "-C", repo_path(repo)] + list(args)
    result = subprocess.run(
        cmd,
        input=input_data,
        capture_output=True,
        text=not binary,
        timeout=timeout,
    )
    return result


def git_str(repo: str, *args: str, timeout: int = 30) -> str:
    """Run a git command and return stdout as string. Returns '' on failure."""
    result = git(repo, *args, timeout=timeout)
    if result.returncode != 0:
        return ""
    return result.stdout


def git_bytes(repo: str, *args: str, input_data: Optional[bytes] = None,
              timeout: int = 30) -> bytes:
    """Run a git command and return stdout as bytes."""
    result = git(repo, *args, input_data=input_data, binary=True, timeout=timeout)
    if result.returncode != 0:
        return b""
    return result.stdout


def get_default_branch(repo: str) -> str:
    """Get the default branch name (master or main)."""
    result = git(repo, "symbolic-ref", "--short", "HEAD")
    if result.returncode == 0:
        return result.stdout.strip()
    return "master"


def resolve_ref(repo: str, ref: str) -> Optional[str]:
    """Resolve a ref (branch/tag/sha) to a full SHA. Returns None if invalid."""
    if not ref:
        return None
    result = git(repo, "rev-parse", ref)
    if result.returncode != 0:
        return None
    sha = result.stdout.strip()
    if is_valid_sha(sha):
        return sha
    return None


def format_size(size: int) -> str:
    """Format byte size as human-readable string."""
    if size < 1024:
        return f"{size} B"
    elif size < 1024 * 1024:
        return f"{size / 1024:.1f} KB"
    else:
        return f"{size / (1024 * 1024):.1f} MB"


def get_file_icon(name: str, mode: str) -> str:
    """Get an emoji icon for a file based on name and mode."""
    if mode.startswith("040") or mode == "040000":
        return "&#128193;"  # folder
    ext = os.path.splitext(name)[1].lower()
    icons = {
        ".md": "&#128196;",
        ".py": "&#128013;",
        ".yml": "&#9881;",
        ".yaml": "&#9881;",
        ".json": "&#9881;",
        ".txt": "&#128196;",
        ".cfg": "&#9881;",
        ".conf": "&#9881;",
        ".sh": "&#128187;",
        ".html": "&#127760;",
        ".css": "&#127912;",
        ".js": "&#127760;",
        ".env": "&#128274;",
    }
    return icons.get(ext, "&#128196;")


def parse_ls_tree(output: str) -> list:
    """Parse git ls-tree output into file list."""
    files = []
    for line in output.strip().split("\n"):
        if not line:
            continue
        parts = line.split("\t", 1)
        if len(parts) != 2:
            continue
        meta, name = parts
        meta_parts = meta.split()
        if len(meta_parts) < 4:
            continue
        mode, obj_type, sha, size_str = meta_parts[0], meta_parts[1], meta_parts[2], meta_parts[3]
        size = 0
        if size_str != "-":
            try:
                size = int(size_str)
            except ValueError:
                pass
        files.append({
            "mode": mode,
            "type": obj_type,
            "sha": sha,
            "name": name,
            "path": name,
            "size": format_size(size) if size else "",
            "icon": get_file_icon(name, mode),
        })
    return files


def parse_log(output: str) -> list:
    """Parse git log --format output into commit list.

    Expected format: %H|%an|%ae|%ad|%s with --date=short
    Records separated by newlines.
    """
    commits = []
    for line in output.strip().split("\n"):
        if not line:
            continue
        parts = line.split("|", 4)
        if len(parts) < 5:
            continue
        sha, author, email, date, message = parts
        commits.append({
            "sha": sha,
            "sha_short": sha[:8],
            "author": author,
            "email": email,
            "date": date,
            "message": message,
        })
    return commits


def render_markdown(content: str) -> str:
    """Simple markdown to HTML conversion (basic formatting only)."""
    lines = content.split("\n")
    html_lines = []
    in_code_block = False
    in_list = False

    for line in lines:
        stripped = line.strip()

        # Code block toggle
        if stripped.startswith("```"):
            if in_code_block:
                html_lines.append("</code></pre>")
                in_code_block = False
            else:
                if in_list:
                    html_lines.append("</ul>")
                    in_list = False
                html_lines.append("<pre><code>")
                in_code_block = True
            continue

        if in_code_block:
            html_lines.append(html.escape(line))
            continue

        # Headers
        if stripped.startswith("### "):
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append(f"<h3>{html.escape(stripped[4:])}</h3>")
        elif stripped.startswith("## "):
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append(f"<h2>{html.escape(stripped[3:])}</h2>")
        elif stripped.startswith("# "):
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append(f"<h1>{html.escape(stripped[2:])}</h1>")
        elif stripped.startswith("- ") or stripped.startswith("* "):
            if not in_list:
                html_lines.append("<ul>")
                in_list = True
            html_lines.append(f"<li>{render_inline(stripped[2:])}</li>")
        elif stripped == "":
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append("")
        else:
            if in_list:
                html_lines.append("</ul>")
                in_list = False
            html_lines.append(f"<p>{render_inline(stripped)}</p>")

    if in_list:
        html_lines.append("</ul>")
    if in_code_block:
        html_lines.append("</code></pre>")

    return "\n".join(html_lines)


def render_inline(text: str) -> str:
    """Render inline markdown (code, bold, links)."""
    escaped = html.escape(text)
    # Inline code
    escaped = re.sub(r"`([^`]+)`", r"<code>\1</code>", escaped)
    # Bold
    escaped = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", escaped)
    return escaped


def is_binary_content(data: bytes) -> bool:
    """Check if content appears to be binary."""
    if b"\x00" in data[:8192]:
        return True
    return False


def pkt_line(data: bytes) -> bytes:
    """Format data as a git pkt-line packet."""
    if data is None:
        return b"0000"  # flush packet
    length = len(data) + 4
    return f"{length:04x}".encode("ascii") + data


# ---------------------------------------------------------------------------
# Health and utility routes
# ---------------------------------------------------------------------------

@app.route("/healthz")
def healthz():
    return Response("ok", status=200, mimetype="text/plain")


@app.route("/favicon.ico")
def favicon():
    return Response("", status=204, mimetype="image/x-icon")


@app.route("/_shared/<path:filename>")
def shared_files(filename):
    """Serve shared assets (nebulamind.css, etc.)."""
    shared_path = os.path.join("/_shared", filename)
    if not os.path.isfile(shared_path):
        abort(404)
    return send_file(shared_path)


# ---------------------------------------------------------------------------
# Web UI routes
# ---------------------------------------------------------------------------

@app.route("/")
def index():
    """Repository list page."""
    repos = []
    repos_dir = os.path.join(REPO_ROOT, ORG)
    if os.path.isdir(repos_dir):
        for name in sorted(os.listdir(repos_dir)):
            if not name.endswith(".git"):
                continue
            repo = name[:-4]
            if not is_valid_repo(repo):
                continue

            # Get description
            desc = git_str(repo, "config", "gitweb.description").strip()
            if not desc:
                desc = f"NebulaMind {repo} repository"

            # Get last commit
            log_output = git_str(
                repo, "log", "-1",
                "--format=%H|%an|%ae|%ad|%s",
                "--date=short"
            )
            commits = parse_log(log_output)
            last_commit = commits[0] if commits else None

            repos.append({
                "name": repo,
                "description": desc,
                "last_commit_date": last_commit["date"] if last_commit else "",
                "last_commit_msg": last_commit["message"] if last_commit else "",
            })

    return render_template("index.html", repos=repos, git_service_url=GIT_SERVICE_URL)


@app.route(f"/{ORG}/<repo>")
def repo_main(repo):
    """Repository main page: file tree + recent commits + README."""
    if not is_valid_repo(repo):
        abort(404)

    branch = get_default_branch(repo)

    # File tree
    tree_output = git_str(repo, "ls-tree", "-l", branch)
    files = parse_ls_tree(tree_output)

    # Recent commits
    log_output = git_str(
        repo, "log", "-5",
        "--format=%H|%an|%ae|%ad|%s",
        "--date=short"
    )
    commits = parse_log(log_output)

    # README
    readme_html = ""
    readme_result = git(repo, "show", f"{branch}:README.md")
    if readme_result.returncode == 0 and readme_result.stdout:
        readme_html = render_markdown(readme_result.stdout)

    # Description
    description = git_str(repo, "config", "gitweb.description").strip()

    return render_template(
        "repo.html",
        repo=repo,
        branch=branch,
        description=description,
        files=files,
        commits=commits,
        readme=readme_html,
        git_service_url=GIT_SERVICE_URL,
    )


@app.route(f"/{ORG}/<repo>/commits")
def repo_commits(repo):
    """Full commit history page."""
    if not is_valid_repo(repo):
        abort(404)

    branch = get_default_branch(repo)

    log_output = git_str(
        repo, "log", "-50",
        "--format=%H|%an|%ae|%ad|%s",
        "--date=short"
    )
    commits = parse_log(log_output)

    return render_template("commits.html", repo=repo, branch=branch, commits=commits)


@app.route(f"/{ORG}/<repo>/commit/<sha>")
def commit_detail(repo, sha):
    """Commit detail page with diff."""
    if not is_valid_repo(repo):
        abort(404)
    if not is_valid_sha(sha):
        abort(404)

    # Resolve full SHA
    full_sha = resolve_ref(repo, sha)
    if not full_sha:
        abort(404)

    # Commit metadata
    log_output = git_str(
        repo, "log", "-1",
        "--format=%H|%an|%ae|%ad|%s",
        "--date=short",
        full_sha
    )
    commits = parse_log(log_output)
    if not commits:
        abort(404)
    commit = commits[0]

    # Parents
    parent_output = git_str(repo, "log", "-1", "--format=%P", full_sha)
    parents = parent_output.strip().split() if parent_output.strip() else []

    # Diff stat
    stat_output = git_str(repo, "show", "--stat", "--format=", full_sha)
    insertions = 0
    deletions = 0
    changed_files = []

    for line in stat_output.strip().split("\n"):
        line = line.strip()
        if not line:
            continue
        # Parse lines like " file.txt | 10 ++++--"
        if "|" in line:
            parts = line.split("|", 1)
            filepath = parts[0].strip()
            change_part = parts[1].strip()
            # Count + and - signs
            additions = change_part.count("+")
            deletions_count = change_part.count("-")
            insertions += additions
            deletions += deletions_count
            changed_files.append({
                "path": filepath,
                "status": "M",
                "icon": get_file_icon(filepath, "100644"),
                "additions": additions,
                "deletions": deletions_count,
            })

    # Full diff
    diff_result = git(repo, "show", "--format=", full_sha, timeout=30)
    diff = diff_result.stdout if diff_result.returncode == 0 else ""
    if len(diff) > MAX_DIFF_SIZE:
        diff = diff[:MAX_DIFF_SIZE] + "\n\n... (diff truncated)"

    stats = {
        "files": len(changed_files),
        "insertions": insertions,
        "deletions": deletions,
    }

    return render_template(
        "commit.html",
        repo=repo,
        sha=full_sha,
        sha_short=full_sha[:8],
        commit=commit,
        commit_parents=parents,
        changed_files=changed_files,
        stats=stats,
        diff=diff,
    )


@app.route(f"/{ORG}/<repo>/blob/<ref>/<path:filepath>")
def blob(repo, ref, filepath):
    """File content at a specific ref (web view)."""
    if not is_valid_repo(repo):
        abort(404)
    # ref can be a branch name or SHA
    full_ref = resolve_ref(repo, ref) or ref
    if not is_valid_ref(ref) and not is_valid_sha(ref):
        abort(404)

    # Get file content
    content_bytes = git_bytes(repo, "show", f"{full_ref}:{filepath}")
    if not content_bytes and not _blob_exists(repo, full_ref, filepath):
        abort(404)

    is_binary = is_binary_content(content_bytes)
    content = ""
    if not is_binary:
        content = content_bytes.decode("utf-8", errors="replace")
        if len(content) > MAX_BLOB_DISPLAY:
            content = content[:MAX_BLOB_DISPLAY] + "\n\n... (truncated)"

    return render_template(
        "blob.html",
        repo=repo,
        ref=full_ref,
        ref_short=full_ref[:8],
        path=filepath,
        content=content,
        is_binary=is_binary,
        size=format_size(len(content_bytes)),
    )


@app.route(f"/{ORG}/<repo>/raw/<ref>/<path:filepath>")
def raw(repo, ref, filepath):
    """Raw file content (download)."""
    if not is_valid_repo(repo):
        abort(404)
    full_ref = resolve_ref(repo, ref) or ref
    if not is_valid_ref(ref) and not is_valid_sha(ref):
        abort(404)

    content_bytes = git_bytes(repo, "show", f"{full_ref}:{filepath}")
    if not content_bytes and not _blob_exists(repo, full_ref, filepath):
        abort(404)

    # Determine content type
    ext = os.path.splitext(filepath)[1].lower()
    content_types = {
        ".md": "text/plain",
        ".py": "text/plain",
        ".yml": "text/plain",
        ".yaml": "text/plain",
        ".json": "application/json",
        ".txt": "text/plain",
        ".html": "text/html",
        ".css": "text/css",
        ".js": "text/javascript",
        ".sh": "text/plain",
        ".cfg": "text/plain",
        ".conf": "text/plain",
    }
    content_type = content_types.get(ext, "application/octet-stream")

    return Response(content_bytes, content_type=content_type)


def _blob_exists(repo: str, ref: str, filepath: str) -> bool:
    """Check if a blob exists at the given ref:path."""
    result = git(repo, "cat-file", "-e", f"{ref}:{filepath}")
    return result.returncode == 0


# ---------------------------------------------------------------------------
# Git Smart HTTP protocol (clone/fetch only)
# ---------------------------------------------------------------------------

@app.route(f"/{ORG}/<repo>.git/info/refs")
def git_info_refs(repo):
    """Git smart HTTP: info/refs endpoint for git-upload-pack."""
    if not is_valid_repo(repo):
        abort(404)

    service = request.args.get("service", "")

    # Only allow upload-pack (fetch/clone). No push (receive-pack).
    if service == "git-receive-pack":
        return Response("Push is not supported on this server.\n",
                        status=403, mimetype="text/plain")
    if service != "git-upload-pack":
        # Dumb HTTP protocol not supported
        abort(403)

    # Run git upload-pack --advertise-refs
    result = subprocess.run(
        ["git", "-C", repo_path(repo),
         "upload-pack", "--stateless-rpc", "--advertise-refs", "."],
        capture_output=True,
        timeout=30,
    )

    if result.returncode != 0:
        return Response("Internal git error\n", status=500,
                        mimetype="text/plain")

    # Build response: service announcement + flush + refs
    body = b""
    service_line = f"# service={service}\n".encode("ascii")
    body += pkt_line(service_line)
    body += b"0000"  # flush packet
    body += result.stdout

    return Response(
        body,
        status=200,
        content_type="application/x-git-upload-pack-advertisement",
    )


@app.route(f"/{ORG}/<repo>.git/HEAD")
def git_head(repo):
    """Git dumb HTTP: HEAD file."""
    if not is_valid_repo(repo):
        abort(404)
    head_path = os.path.join(repo_path(repo), "HEAD")
    if not os.path.isfile(head_path):
        abort(404)
    with open(head_path, "r", encoding="utf-8") as f:
        content = f.read()
    return Response(content, content_type="text/plain")


@app.route(f"/{ORG}/<repo>.git/git-upload-pack", methods=["POST"])
def git_upload_pack(repo):
    """Git smart HTTP: git-upload-pack endpoint (clone/fetch)."""
    if not is_valid_repo(repo):
        abort(404)

    # Verify content type
    content_type = request.headers.get("Content-Type", "")
    if "git-upload-pack" not in content_type:
        return Response("Invalid content type\n", status=400,
                        mimetype="text/plain")

    body = request.get_data()

    result = subprocess.run(
        ["git", "-C", repo_path(repo),
         "upload-pack", "--stateless-rpc", "."],
        input=body,
        capture_output=True,
        timeout=120,
    )

    if result.returncode != 0:
        return Response("Internal git error\n", status=500,
                        mimetype="text/plain")

    return Response(
        result.stdout,
        status=200,
        content_type="application/x-git-upload-pack-result",
    )


# ---------------------------------------------------------------------------
# Error handlers
# ---------------------------------------------------------------------------

@app.errorhandler(404)
def not_found(e):
    if request.path.startswith("/api/") or "Accept" in request.headers:
        accept = request.headers.get("Accept", "")
        if "application/json" in accept or "application/x-git" in accept:
            return Response('{"error": "not found"}', status=404,
                            content_type="application/json")
    return render_template("error.html", code=404,
                           message="Repository or resource not found"), 404


@app.errorhandler(403)
def forbidden(e):
    return Response("Forbidden\n", status=403, mimetype="text/plain")


@app.errorhandler(500)
def internal_error(e):
    return Response("Internal server error\n", status=500,
                    mimetype="text/plain")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    print(
        f"[git-service] NebulaMind Internal Git Service "
        f"starting on {HOST}:{PORT}",
        flush=True,
    )
    print(f"[git-service] Repository root: {REPO_ROOT}/{ORG}", flush=True)
    print(f"[git-service] Smart HTTP enabled (clone/fetch only)", flush=True)
    app.run(host=HOST, port=PORT, threaded=True, debug=False)
