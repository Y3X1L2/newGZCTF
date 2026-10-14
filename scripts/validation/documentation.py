"""文档工具的公共逻辑；仅使用标准库，不联网、不执行文档中的内容。"""

import html
import json
import os
from pathlib import Path
import posixpath
import re
import subprocess
import sys
import unicodedata
from urllib.parse import unquote, urlsplit

CATALOG_PATH = "docs/catalog.json"
IGNORED_DIRS = {".git", "node_modules", "bin", "obj", "dist", "coverage", "__pycache__"}


def configure_cli_output():
    """Windows 控制台/管道均输出 UTF-8；StringIO 等捕获流无需重配置。"""
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8")


def read_text(path):
    return path.read_text(encoding="utf-8-sig")


def without_examples(content, inline=True):
    """保留行号，忽略围栏、行内代码和注释中的示例链接。"""
    content = re.sub(r"<!--.*?-->", lambda m: "\n" * m.group().count("\n"), content, flags=re.S)
    fence, fence_indent = None, 0
    list_indents = []
    lines = []
    for line in content.splitlines():
        expanded = line.expandtabs(4)
        indent = len(expanded) - len(expanded.lstrip(" "))
        if fence is None and line.strip():
            while list_indents and indent < list_indents[-1]:
                list_indents.pop()
        container_indent = fence_indent if fence is not None else list_indents[-1] if list_indents else 0
        relative = expanded[container_indent:] if indent >= container_indent else expanded
        marker = re.match(r"^ {0,3}(`{3,}|~{3,})", relative)
        if marker:
            token = marker.group(1)
            if fence is None:
                fence, fence_indent = token, container_indent
            elif token[0] == fence[0] and len(token) >= len(fence) and not relative[marker.end():].strip():
                fence = None
            lines.append("")
        elif fence is not None or indent >= container_indent + 4:
            lines.append("")
        else:
            # 列表的内容缩进不是代码；代码块需再缩进四格。
            item = re.match(r"^ *(?:[-+*]|\d+[.)]) +", expanded)
            if item:
                list_indents.append(item.end())
            lines.append(re.sub(r"(`+)(.+?)\1(?!`)", "", line) if inline else line)
    return "\n".join(lines)


def destination(text, position):
    """读取 Markdown 目标，支持尖括号、转义和路径中的成对括号。"""
    while position < len(text) and text[position].isspace():
        position += 1
    if position >= len(text):
        return None
    if text[position] == "<":
        end = text.find(">", position + 1)
        return text[position + 1:end] if end >= 0 else None
    result, depth = [], 0
    while position < len(text):
        char = text[position]
        if char == "\\" and position + 1 < len(text):
            position += 1
            result.append(text[position])
        elif char == "(":
            depth += 1
            result.append(char)
        elif char == ")":
            if not depth:
                break
            depth -= 1
            result.append(char)
        elif char.isspace() and not depth:
            break
        else:
            result.append(char)
        position += 1
    return "".join(result) or None


def markdown_links(content):
    text = without_examples(content)
    # Definitions are themselves navigable targets, including shortcut references.
    for match in re.finditer(r"(?m)^ {0,3}\[([^\]\n]+)\]:[ \t]*", text):
        target = destination(text, match.end())
        if target:
            yield text.count("\n", 0, match.start()) + 1, html.unescape(target)
    for match in re.finditer(r"(?<!!)\[[^\]\n]*\]\([ \t]*|!\[[^\]\n]*\]\([ \t]*", text):
        target = destination(text, match.end())
        if target:
            yield text.count("\n", 0, match.start()) + 1, html.unescape(target)
    for match in re.finditer(r"<(?!/)([^<>\n]+)>", text):
        target = match.group(1)
        if re.match(r"(?:https?://|mailto:)", target):
            yield text.count("\n", 0, match.start()) + 1, target


def markdown_anchors(content):
    """GitHub 常用标题锚点（含重复标题）及显式 HTML id/name。"""
    text = without_examples(content, inline=False)
    anchors = set(re.findall(r"\b(?:id|name)\s*=\s*[\"']([^\"']+)[\"']", text, flags=re.I))
    references = {" ".join(m.group(1).split()).casefold() for m in re.finditer(r"(?m)^ {0,3}\[([^\]\n]+)\]:", text)}
    headings = []
    lines = text.splitlines()
    for index, line in enumerate(lines):
        match = re.match(r"^ {0,3}#{1,6}(?:[ \t]+|$)(.*?)(?:[ \t]+#+[ \t]*)?$", line)
        if match:
            headings.append(match.group(1))
        elif index + 1 < len(lines) and line.strip() and re.match(r"^ {0,3}(?:=+|-+)[ \t]*$", lines[index + 1]):
            headings.append(line)
    used = set()
    for heading in headings:
        heading = html.unescape(re.sub(r"<[^>]+>", "", heading))
        heading = re.sub(r"!?\[([^\]]+)\]\[([^\]]*)\]",
                         lambda m: m[1] if " ".join((m[2] or m[1]).split()).casefold() in references else m[0], heading)
        heading = re.sub(r"!?\[([^\]]+)\]\([^)]*\)", r"\1", heading).strip().lower()
        slug = "".join(c for c in heading if c in "_- " or unicodedata.category(c)[0] in "LMN")
        slug = slug.replace(" ", "-")
        anchor, counter = slug, 0
        while anchor in used:
            counter += 1
            anchor = f"{slug}-{counter}"
        used.add(anchor)
        anchors.add(anchor)
    return anchors


def in_archive(path, root):
    return path.is_relative_to(root / "docs/archive")


def check_links(root):
    root = root.resolve()
    files = [root / "AGENTS.md", root / "README.md"]
    files.extend(sorted(p for p in (root / "docs").rglob("*.md") if not in_archive(p, root)))
    errors, warnings, anchor_cache = [], [], {}
    for source in files:
        if not source.is_file():
            errors.append(f"缺少入口：{source.relative_to(root).as_posix()}")
            continue
        for line, target in sorted(set(markdown_links(read_text(source)))):
            prefix = f"{source.relative_to(root).as_posix()}:{line}"
            try:
                url = urlsplit(target)
            except ValueError:
                errors.append(f"{prefix}：链接格式无效：{target}")
                continue
            if url.scheme or url.netloc:
                continue  # 外链不联网，也不把 URL 当本地路径。
            path = unquote(url.path)
            resolved = (root / path.lstrip("/") if path.startswith("/") else source.parent / path).resolve() if path else source
            if not resolved.is_relative_to(root):
                errors.append(f"{prefix}：链接越出仓库：{target}")
                continue
            if not resolved.exists():
                errors.append(f"{prefix}：链接文件不存在：{target}")
                continue
            if not url.fragment or in_archive(resolved, root):
                continue  # 只确认归档文件仍在；不读取归档正文。
            fragment = unquote(url.fragment)
            if resolved.suffix.lower() == ".md":
                if resolved not in anchor_cache:
                    anchor_cache[resolved] = markdown_anchors(read_text(resolved))
                if fragment not in anchor_cache[resolved]:
                    errors.append(f"{prefix}：标题/锚点不存在：{target}")
            elif resolved.is_file():
                match = re.fullmatch(r"L([1-9]\d*)(?:-L?([1-9]\d*))?", fragment)
                if match:
                    try:
                        size = len(read_text(resolved).splitlines())
                    except UnicodeError:
                        errors.append(f"{prefix}：不能核验二进制文件的行号：{target}")
                        continue
                    start, end = int(match[1]), int(match[2] or match[1])
                    if not start <= end <= size:
                        errors.append(f"{prefix}：源码行号超出范围（文件 {size} 行）：{target}")
                else:
                    warnings.append(f"{prefix}：仅确认文件存在，非 Markdown 锚点无法静态核验：{target}")
    return files, errors, warnings


def repository_files(root):
    files = []
    for directory, children, names in os.walk(root):
        children[:] = sorted(c for c in children if c not in IGNORED_DIRS)
        files.extend((Path(directory) / n).relative_to(root).as_posix() for n in sorted(names))
    return files


def safe_path(value, glob=False):
    if not isinstance(value, str) or not value or "\\" in value or value.startswith("/"):
        return False
    if re.search(r"[\x00-\x1f:<>|\[\]{}]", value) or any(p in {"", ".", ".."} for p in value.split("/")):
        return False
    if not glob and ("*" in value or "?" in value):
        return False
    return all("**" not in part or part == "**" for part in value.split("/"))


def glob_matches(path, pattern):
    """固定大小写的仓库 POSIX glob；** 匹配任意层级，* 不跨目录。"""
    segments = pattern.split("/")
    pieces = []
    for index, segment in enumerate(segments):
        if segment == "**":
            pieces.append(".*" if index == len(segments) - 1 else "(?:[^/]+/)*")
        else:
            pieces.append("".join("[^/]*" if c == "*" else "[^/]" if c == "?" else re.escape(c) for c in segment))
            if index < len(segments) - 1:
                pieces.append("/")
    return re.fullmatch("".join(pieces), path) is not None


def owners(path, modules, field):
    return [module for module in modules if any(glob_matches(path, pattern) for pattern in module[field])]


def catalog_modules(data):
    errors, modules, ids = [], [], set()
    if not isinstance(data, dict) or set(data) != {"version", "modules"} or type(data.get("version")) is not int or data.get("version") != 1 or not isinstance(data.get("modules"), list):
        return [], [f"{CATALOG_PATH}：需要 version=1 和 modules 数组，且不允许额外顶层字段"]
    if not data["modules"]:
        errors.append(f"{CATALOG_PATH}：modules 不能为空")
    for index, module in enumerate(data["modules"]):
        location = f"{CATALOG_PATH}：modules[{index}]"
        if not isinstance(module, dict) or set(module) != {"id", "title", "overview", "development", "code", "docs"}:
            errors.append(f"{location}：字段必须为 id/title/overview/development/code/docs")
            continue
        module_id = module["id"]
        if not isinstance(module_id, str) or not re.fullmatch(r"[a-z][a-z0-9-]*", module_id) or module_id in ids:
            errors.append(f"{location}：id 无效或重复：{module_id!r}")
            continue
        ids.add(module_id)
        invalid = False
        if not isinstance(module["title"], str) or not module["title"].strip() or re.search(r"[\r\n|<>]", module["title"]):
            errors.append(f"{location}：title 必须是单行名称，不能含表格/HTML分隔符")
            invalid = True
        for field, filename in (("overview", "README.md"), ("development", "development.md")):
            directory = "docs/architecture" if module_id == "platform" else f"docs/modules/{module_id}"
            expected = f"{directory}/{filename}"
            if module[field] != expected:
                errors.append(f"{location}：{field} 应为 {expected}")
                invalid = True
        for field in ("code", "docs"):
            values = module[field]
            if not isinstance(values, list) or not values or any(not safe_path(p, glob=True) for p in values):
                errors.append(f"{location}：{field} 应为非空仓库相对 glob 数组（/分隔；支持 */**/?）")
                invalid = True
            elif len(values) != len(set(values)):
                errors.append(f"{location}：{field} 包含重复 glob")
                invalid = True
        if not invalid:
            modules.append(module)
    return modules, errors


def load_catalog(root):
    try:
        return catalog_modules(json.loads(read_text(root / CATALOG_PATH)))
    except (OSError, ValueError) as error:
        return [], [f"{CATALOG_PATH}：无法读取 catalog：{error}"]


def check_catalog(root):
    modules, errors = load_catalog(root)
    warnings = []
    if errors:
        return modules, errors, warnings
    inventory = repository_files(root)
    for module in modules:
        for field in ("overview", "development"):
            path = root / module[field]
            if not path.is_file():
                errors.append(f"模块 {module['id']}：文档不存在：{module[field]}")
        for field in ("code", "docs"):
            for pattern in module[field]:
                if not any(glob_matches(path, pattern) for path in inventory):
                    errors.append(f"模块 {module['id']}：{field} glob 未匹配文件：{pattern}")
        for field in ("overview", "development"):
            if not any(glob_matches(module[field], pattern) for pattern in module["docs"]):
                errors.append(f"模块 {module['id']}：docs 映射未包含 {module[field]}")
    catalog_ids = {m["id"] for m in modules}
    module_dir = root / "docs/modules"
    if module_dir.exists():
        for directory in sorted(p for p in module_dir.iterdir() if p.is_dir()):
            if directory.name not in catalog_ids:
                errors.append(f"模块目录未登记 catalog：docs/modules/{directory.name}")
    # 不能用 PR 的 unmapped 理由绕过真实后端模块或 Agent 的结构覆盖。
    required = [p for p in inventory if p.startswith(("src/GZCTF/Modules/", "src/GZCTF.Agent/"))]
    for path in required:
        # Modules 根目录的共享装配标记允许 platform；实际模块子目录必须有真实模块归属。
        candidates = modules if path.startswith("src/GZCTF/Modules/") and len(path.split("/")) == 4 else [m for m in modules if m["id"] != "platform"]
        if not owners(path, candidates, "code"):
            errors.append(f"Modules/Agent 文件未映射：{path}")
    return modules, errors, warnings


def length_warnings(root, modules):
    limits = {"docs/current-state.md": 120, "docs/README.md": 120, "README.md": 180,
              "docs/modules/README.md": 120}
    limits.update({m["overview"]: 120 for m in modules})
    limits.update({m["development"]: 200 for m in modules})
    warnings = []
    for name, limit in sorted(limits.items()):
        path = root / name
        if path.is_file():
            count = len(read_text(path).splitlines())
            if count > limit:
                warnings.append(f"{name}：{count} 行，建议不超过 {limit} 行；保留关键条件，按独立链路拆分或引用专题")
    # README 的中文字数只是写作提示，不对短页强行补字。
    for module in modules:
        path = root / module["overview"]
        if path.is_file():
            count = len(re.findall(r"[\u3400-\u9fff]", read_text(path)))
            if count > 1000:
                warnings.append(f"{module['overview']}：约 {count} 个中文字，建议压缩为 500–1000 字入口")
    return warnings


def generated_index(modules):
    lines = ["# 功能模块文档", "", "本页由 [catalog](../catalog.json) 生成。先读用途与现有能力，再按任务阅读开发说明。",
             "", "生成：`python scripts/validation/generate-module-index.py`；检查：在命令末尾添加 `--check`。",
             "", "| 模块 | 用途与能力 | 开发与接手 |", "| --- | --- | --- |"]
    for module in sorted(modules, key=lambda m: (m["id"] == "platform", m["id"])):
        overview = posixpath.relpath(module["overview"], "docs/modules")
        development = posixpath.relpath(module["development"], "docs/modules")
        lines.append(f"| {module['title']}（{module['id']}） | [说明]({overview}) | [开发]({development}) |")
    return "\n".join(lines) + "\n"


def git(root, *args, check=True):
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True, check=False)
    if check and result.returncode:
        raise ValueError(f"Git 操作失败（{' '.join(args[:2])}）：{result.stderr.decode('utf-8', errors='replace').strip()}")
    return result


def diff_changes(root, base, head="HEAD"):
    # --end-of-options prevents refs from being interpreted as Git switches.
    if not base or base.startswith("-") or head.startswith("-"):
        raise ValueError("base/head 必须是 Git 引用，不能是命令选项")
    base_sha = git(root, "rev-parse", "--verify", "--end-of-options", base + "^{commit}").stdout.decode().strip()
    head_sha = git(root, "rev-parse", "--verify", "--end-of-options", head + "^{commit}").stdout.decode().strip()
    common = git(root, "merge-base", base_sha, head_sha).stdout.decode().strip()
    fields = git(root, "diff", "--name-status", "-z", "--find-renames", common, head_sha, "--").stdout.decode("utf-8").split("\0")
    changes, index = [], 0
    while index < len(fields) and fields[index]:
        status, path = fields[index:index + 2]
        index += 2
        if status.startswith(("R", "C")):
            new_path = fields[index]
            index += 1
            changes.append((status, path, new_path))
        elif status == "D":
            changes.append((status, path, None))
        else:
            changes.append((status, None if status == "A" else path, path))
    return common, head_sha, changes


def base_catalog(root, base):
    result = git(root, "show", f"{base}:{CATALOG_PATH}", check=False)
    if result.returncode:
        return []
    try:
        modules, errors = catalog_modules(json.loads(result.stdout.decode("utf-8-sig")))
        return modules if not errors else []
    except ValueError:
        return []


def code_path(path):
    # 文档、图片和生成报告不冒充源码；未知源码/配置仍会进入 unmapped。
    if path.endswith(".md") or path == CATALOG_PATH:
        return False
    return path.startswith(("src/", "scripts/", "tests/", ".github/", "scenarios/")) or Path(path).suffix.lower() in {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".ps1", ".sh", ".yml", ".yaml", ".json", ".toml",
        ".csproj", ".props", ".targets", ".slnx", ".sql", ".conf"} or Path(path).name.startswith("Dockerfile")


def impact(root, base, modules, head="HEAD"):
    common, head_sha, changes = diff_changes(root, base, head)
    previous = base_catalog(root, common)
    affected, unmapped, changed_docs = {}, [], set()
    for status, old_path, new_path in changes:
        for path, catalog in ((old_path, previous or modules), (new_path, modules)):
            if path is None:
                continue
            if path.endswith(".md") or path == CATALOG_PATH:
                changed_docs.add(path)
            if not code_path(path):
                continue
            mapped = owners(path, catalog, "code")
            if mapped:
                for module in mapped:
                    item = affected.setdefault(module["id"], {"module": module, "changes": set()})
                    item["changes"].add((status, old_path, new_path))
            else:
                unmapped.append((status, path))
    for item in affected.values():
        module_id = item["module"]["id"]
        candidates = [m for m in [*modules, *previous] if m["id"] == module_id]
        item["updated_docs"] = sorted(path for path in changed_docs if any(owners(path, [m], "docs") for m in candidates))
    return {"base": common, "head": head_sha, "changes": changes, "affected": affected, "unmapped": sorted(set(unmapped)), "changed_docs": sorted(changed_docs)}


def pr_review(body, valid_ids):
    """正文固定段落按纯数据读取；不给 shell、eval 或动态指令。"""
    body = without_examples(body or "", inline=False)
    sections = re.findall(r"(?ms)^## 文档影响[ \t]*\r?\n(.*?)(?=^## |\Z)", body)
    if len(sections) != 1:
        return {}, ["PR 需要且只能有一个“## 文档影响”段落"]
    section = sections[0]
    # 一行列模块，理由逐模块写一行，拒绝重复/不明确声明。
    fields = re.findall(r"(?m)^无需修改模块：[ \t]*(.*?)\r?$", section)
    if len(fields) != 1:
        return {}, ["文档影响：需要且只能有一行“无需修改模块：无”或模块 ID 列表"]
    value = fields[0].strip()
    ids = [] if value == "无" else [p.strip() for p in re.split(r"[,，]", value)]
    errors = []
    if len(ids) != len(set(ids)) or any(p not in valid_ids | {"unmapped"} for p in ids):
        errors.append("文档影响：无需修改模块含重复/未知 ID；使用 catalog ID，未映射普通源码用 unmapped")
    reason_parts = re.split(r"(?m)^无需修改理由：[ \t]*\r?$", section)
    reasons = {}
    if ids and len(reason_parts) != 2:
        errors.append("文档影响：声明无需修改时，需一行“无需修改理由：”，随后逐行填写“模块ID: 具体原因”")
    elif len(reason_parts) == 2:
        for line in reason_parts[1].splitlines():
            match = re.fullmatch(r"([a-z][a-z0-9-]*)[：:][ \t]*(.+)", line.strip())
            if match:
                if match[1] in reasons:
                    errors.append(f"文档影响：{match[1]} 的理由重复")
                reasons[match[1]] = match[2].strip()
    for module_id in ids:
        reason = reasons.get(module_id, "")
        normalized = re.sub(r"[\s\W_]+", "", reason).casefold()
        placeholders = {"无", "无需修改", "不需要", "无需更新", "n/a", "na", "none", "skip", "todo", "待填写", "纯代码", "仅重构", "文档无变化", "不涉及文档"}
        concrete = re.search(r"`[^`\n]{3,}`|(?:src|scripts|tests)/\S+|(?<![A-Za-z0-9_])[A-Z][a-z]+[A-Z][A-Za-z0-9_]*(?![A-Za-z0-9_])|\d+", reason)
        if len(normalized) < 16 or normalized in placeholders or not concrete or re.search(r"待填写|请填写|此处填写|todo|不涉及文档|无需修改文档|纯代码改动|文档无变化|无意义", reason, re.I):
            errors.append(f"文档影响：{module_id} 的理由缺少具体事实；请说明变更、为何现行说明仍准确及验证依据")
        else:
            reasons[module_id] = reason
    if any(module_id not in ids for module_id in reasons):
        errors.append("文档影响：理由中的模块必须列入“无需修改模块”")
    return {module_id: reasons[module_id] for module_id in ids if module_id in reasons}, errors


def review_impact(result, body, modules):
    reasons, errors = pr_review(body, {m["id"] for m in modules} | set(result["affected"]))
    for module_id, item in sorted(result["affected"].items()):
        if not item["updated_docs"] and module_id not in reasons:
            errors.append(f"模块 {module_id} 有源码变更，但没有相关文档差异或合法无需修改理由；应复核 {item['module']['overview']}、{item['module']['development']}")
    if result["unmapped"] and "unmapped" not in reasons:
        errors.append("存在未映射源码：补充 catalog 归属，或对普通非 Modules/Agent 源码提供 unmapped 的具体复核理由")
    return reasons, errors


def impact_report(result, reasons=None, errors=None, is_pr=False):
    lines = ["# 文档影响报告", "", f"比较范围（merge-base → head）：`{result['base']}` → `{result['head']}`。共 {len(result['changes'])} 个文件变化。",
             "", "影响报告只汇总上述区间的文件变化。手动/每周 CI 默认比较最新一次提交（HEAD^ → HEAD）；静态文档检查覆盖全部现行说明。",
             "", "PR 模式：要求相关说明变更或明确复核理由。" if is_pr else "只读报告模式：未收到 PR 正文，不要求 PR 复核声明。", ""]
    if not result["affected"]:
        lines.extend(["未识别到已登记模块的源码变化。", ""])
    for module_id, item in sorted(result["affected"].items()):
        module = item["module"]
        lines.extend([f"## {module['title']}（{module_id}）", "", f"复核入口：{module['overview']}；{module['development']}。", ""])
        for status, old, new in sorted(item["changes"], key=lambda c: (c[1] or "", c[2] or "")):
            description = f"{old} → {new}" if old and new and old != new else new or old
            lines.append(f"- {status}：`{description}`")
        lines.extend(["", "相关文档差异：" + ("、".join(item["updated_docs"]) or "无") + "。"])
        if reasons and module_id in reasons:
            lines.append("无需修改复核理由：" + html.escape(reasons[module_id]) + "。")
        lines.append("")
    if result["unmapped"]:
        lines.extend(["## 未映射源码（必须人工核对）", ""])
        lines.extend(f"- {status}：`{path}`" for status, path in result["unmapped"])
        if reasons and "unmapped" in reasons:
            lines.extend(["", "复核理由：" + html.escape(reasons["unmapped"])])
        lines.append("")
    if errors:
        lines.extend(["## 未通过", ""])
        lines.extend(f"- {error}" for error in errors)
        lines.append("")
    return "\n".join(lines)
