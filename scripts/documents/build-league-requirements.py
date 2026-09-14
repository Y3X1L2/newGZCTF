"""Build the league requirements document with the verified Chinese styles."""

import importlib.util
from pathlib import Path

from docx import Document
from docx.shared import Cm


ROOT = Path(__file__).resolve().parents[2]
helper_path = Path(__file__).with_name("build-platform-guides.py")
spec = importlib.util.spec_from_file_location("guide_builder", helper_path)
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)

original_table = helper.table


def league_table(document, lines):
    original_table(document, lines)
    table = document.tables[-1]
    headers = [cell.text for cell in table.rows[0].cells]
    if len(headers) == 2:
        widths = [4.0, 12.4]
    else:
        widths = {
            "现有基础": [3.3, 5.6, 7.5],
            "阶段": [3.4, 3.5, 9.5],
            "商品": [3.3, 6.5, 6.6],
            "调用能力": [3.5, 6.0, 6.9],
            "工作包": [4.0, 4.2, 8.2],
            "验收项": [3.3, 6.0, 7.1],
            "编号与负责人": [4.1, 7.0, 5.3],
        }.get(headers[0], [3.3, 6.0, 7.1])
    for col, width in zip(table.columns, widths):
        col.width = Cm(width)
    for row in table.rows:
        for cell, width in zip(row.cells, widths):
            cell.width = Cm(width)


if __name__ == "__main__":
    helper.table = league_table
    source = ROOT / "docs/development/league-development-requirements-v0.1.md"
    output = ROOT / "docs/development/联赛开发需求与分工稿V0.1.docx"
    helper.build(source, output)
    document = Document(output)
    document.core_properties.subject = "双队联赛功能需求、模块分工和验收安排"
    document.core_properties.keywords = "联赛,需求,分工,金币,裁判,组网"
    document.save(output)
