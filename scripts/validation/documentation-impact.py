"""基于 merge-base 的 Git 变更影响报告；只有提供 PR 数据时要求文档复核。"""

import argparse
import json
from pathlib import Path

from documentation import check_catalog, impact, impact_report, load_catalog, read_text, review_impact


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--base", required=True, help="Git 引用；以它与 head 的 merge-base 比较")
    parser.add_argument("--head", default="HEAD", help="比较终点；默认 HEAD，不含未提交修改")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--pr-body-file", type=Path, help="PR 正文 UTF-8 文件；启用 PR 复核")
    group.add_argument("--event-file", type=Path, help="GitHub 事件 JSON，含 pull_request 时才启用 PR 复核")
    parser.add_argument("--report", type=Path, help="保存 UTF-8 Markdown 报告")
    args = parser.parse_args()
    errors, reasons, body, is_pr = [], {}, "", False
    try:
        modules, errors = load_catalog(args.root)
        if args.pr_body_file:
            body, is_pr = read_text(args.pr_body_file), True
        elif args.event_file:
            event = json.loads(read_text(args.event_file))
            if not isinstance(event, dict):
                raise ValueError("事件 JSON 必须为对象")
            if "pull_request" in event:
                pr = event["pull_request"]
                if not isinstance(pr, dict) or not isinstance(pr.get("body") or "", str):
                    raise ValueError("pull_request.body 必须为字符串或 null")
                body, is_pr = pr.get("body") or "", True
        if errors:
            raise ValueError("\n".join(errors))
        # 单独运行影响检查也不能用 unmapped 理由绕过缺失的实际模块/Agent 归属。
        _, errors, _ = check_catalog(args.root)
        result = impact(args.root, args.base, modules, args.head)
        if is_pr:
            reasons, review_errors = review_impact(result, body, modules)
            errors.extend(review_errors)
        report = impact_report(result, reasons, errors, is_pr)
    except (OSError, ValueError, UnicodeError) as error:
        errors = [str(error)]
        report = "# 文档影响报告\n\n未通过：" + str(error) + "\n"
    print(report)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(report, encoding="utf-8")
    return int(bool(errors))


if __name__ == "__main__":
    raise SystemExit(main())
