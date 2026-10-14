"""从 docs/catalog.json 确定性生成模块导航；--check 只检查，不写入。"""

import argparse
from pathlib import Path

from documentation import configure_cli_output, generated_index, load_catalog, read_text


def main():
    configure_cli_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    modules, errors = load_catalog(args.root)
    if errors:
        print("\n".join(errors))
        return 1
    output = args.root / "docs/modules/README.md"
    expected = generated_index(modules)
    if args.check:
        if not output.is_file() or read_text(output) != expected:
            print("模块导航与 catalog 不一致；运行 python scripts/validation/generate-module-index.py")
            return 1
        print("通过：模块导航与 catalog 一致。")
    else:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(expected, encoding="utf-8", newline="\n")
        print("已生成 docs/modules/README.md。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
