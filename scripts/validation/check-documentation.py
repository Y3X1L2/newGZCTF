"""Check local Markdown links and the size of the current-state entry point.

Historical archives are intentionally excluded. This is a file-link check, not
an API, anchor, source-symbol, or external-URL validator. Uses only the stdlib.
"""

import argparse
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit


def markdown_targets(content):
    # Fenced examples and inline code are not navigable links.
    fence = None
    lines = []
    for line in content.splitlines():
        marker = re.match(r"^\s*(`{3,}|~{3,})", line)
        if marker:
            token = marker.group(1)
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None
            continue
        if fence is None:
            lines.append(re.sub(r"(`+).*?\1", "", line))
    text = "\n".join(lines)
    definitions = dict(re.findall(r"(?m)^\s*\[([^\]]+)\]:\s*<?([^\s>]+)>?", text))
    yield from re.findall(r"\[[^\]\n]*\]\(\s*<?([^\s)>]+)>?(?:\s+[^)]*)?\)", text)
    for label, reference in re.findall(r"\[([^\]\n]+)\]\[([^\]\n]*)\]", text):
        key = reference or label
        if key in definitions:
            yield definitions[key]
    # Check all definitions, including ones whose usage is a shortcut reference.
    yield from definitions.values()


def check(root):
    root = root.resolve()
    docs = root / "docs"
    files = [root / "AGENTS.md", root / "README.md"]
    files.extend(sorted(p for p in docs.rglob("*.md") if "archive" not in p.relative_to(docs).parts))
    errors = []
    for source in files:
        if not source.is_file():
            errors.append(f"Missing entry point: {source.relative_to(root)}")
            continue
        content = source.read_text(encoding="utf-8-sig")
        for target in set(markdown_targets(content)):
            url = urlsplit(target)
            if url.scheme or url.netloc or not url.path:
                continue
            path = unquote(url.path)
            resolved = (root / path.lstrip("/") if path.startswith("/") else source.parent / path).resolve()
            if not resolved.exists():
                errors.append(f"{source.relative_to(root).as_posix()}: missing link {target}")
    state = docs / "development/current-state.md"
    if not state.is_file():
        errors.append("Missing docs/development/current-state.md")
    elif len(state.read_text(encoding="utf-8-sig").splitlines()) > 120:
        errors.append("current-state.md exceeds 120 lines; replace current facts and link to task evidence")
    return files, sorted(errors)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    files, errors = check(args.root)
    if errors:
        print("\n".join(errors))
        return 1
    print(f"PASS: {len(files)} Markdown files, local file links and current-state size (archives excluded)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
