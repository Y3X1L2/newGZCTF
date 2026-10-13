"""检查现行 Markdown 文件/锚点/源码链接、catalog 与模块覆盖；篇幅只提示。"""

import argparse
from pathlib import Path

from documentation import check_catalog, check_links, configure_cli_output, generated_index, length_warnings, read_text


def check(root):
    root = root.resolve()
    files, errors, warnings = check_links(root)
    modules, catalog_errors, catalog_warnings = check_catalog(root)
    errors.extend(catalog_errors)
    warnings.extend(catalog_warnings)
    if not (root / "docs/current-state.md").is_file():
        errors.append("缺少当前状态入口：docs/current-state.md")
    warnings.extend(length_warnings(root, modules))
    if not catalog_errors:
        index = root / "docs/modules/README.md"
        if not index.is_file() or read_text(index) != generated_index(modules):
            errors.append("docs/modules/README.md 与 catalog 不一致；运行 python scripts/validation/generate-module-index.py")
    return files, sorted(set(errors)), sorted(set(warnings))


def main():
    configure_cli_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--report", type=Path, help="同时保存 UTF-8 Markdown 报告")
    args = parser.parse_args()
    files, errors, warnings = check(args.root)
    lines = ["# 文档检查报告", "", f"检查 {len(files)} 个 Markdown 文件；归档正文未读取，外链未联网。", ""]
    lines.extend(f"错误：{error}" for error in errors)
    lines.extend(f"提示：{warning}" for warning in warnings)
    lines.extend(["", "未通过。" if errors else "通过；篇幅提示不阻断。"])
    report = "\n".join(lines) + "\n"
    print(report)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(report, encoding="utf-8")
    return int(bool(errors))


if __name__ == "__main__":
    raise SystemExit(main())
