"""Export EF snapshot relationships and render the two database briefing diagrams.

Requires Python 3 and Pillow. This is a source snapshot reader, not a C# parser or
live database inspector. Unknown snapshot formatting fails instead of omitting FKs.
"""

import argparse
import csv
import hashlib
import json
import math
import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SNAPSHOT = ROOT / "src/GZCTF/Migrations/AppDbContextModelSnapshot.cs"


def snapshot_model():
    source = SNAPSHOT.read_text(encoding="utf-8-sig")
    blocks = re.findall(
        r'modelBuilder.Entity\("([^"]+)", b =>\s*\{(.*?)\n                \}\);',
        source, re.S,
    )
    if len(blocks) != len(re.findall(r'modelBuilder.Entity\(', source)):
        raise ValueError("Unrecognized entity block format in snapshot")
    entities = {}
    for entity, body in blocks:
        table = re.search(r'\.ToTable\("([^"]+)"', body)
        if table:
            key = re.search(r'\.HasKey\((.*?)\)', body, re.S)
            entities[entity] = {
                "table": table.group(1), "entity": entity,
                "primary_key": re.findall(r'"([^"]+)"', key.group(1)) if key else [],
                "columns": set(re.findall(r'b\.Property(?:<.*?>)?\("([^"]+)"\)', body)),
            }
            unique_keys = [entities[entity]["primary_key"]] if key else []
            for index in re.finditer(r'\.HasIndex\((.*?)\)(.*?);', body, re.S):
                if ".IsUnique()" in index.group(2) and ".HasFilter(" not in index.group(2):
                    unique_keys.append(re.findall(r'"([^"]+)"', index.group(1)))
            entities[entity]["unique_keys"] = unique_keys
    relationships = []
    for entity, body in blocks:
        for match in re.finditer(r'b.HasOne\("([^"]+)".*?;', body, re.S):
            expression = match.group()
            fk = re.search(r'\.HasForeignKey\((.*?)\)', expression, re.S)
            if not fk:
                raise ValueError(f"Reference without recognized FK: {entity}")
            columns = re.findall(r'"([^"]+)"', fk.group(1))
            dependent, principal = entity, match.group(1)
            if columns and columns[0] in entities:
                dependent = columns.pop(0)
            if dependent not in entities or principal not in entities:
                raise ValueError(f"Unmapped FK endpoint: {dependent} -> {principal}")
            if not columns or not set(columns) <= entities[dependent]["columns"]:
                raise ValueError(f"Unknown FK columns: {dependent} {columns}")
            principal_key = re.search(r'\.HasPrincipalKey\((.*?)\)', expression, re.S)
            reference_columns = (
                re.findall(r'"([^"]+)"', principal_key.group(1))
                if principal_key else entities[principal]["primary_key"]
            )
            if reference_columns and reference_columns[0] in entities:
                reference_columns.pop(0)
            if not set(reference_columns) <= entities[principal]["columns"]:
                raise ValueError(f"Unknown principal key: {principal} {reference_columns}")
            unique_child = ".WithOne(" in expression or any(
                set(key) <= set(columns) for key in entities[dependent]["unique_keys"] if key
            )
            delete = re.search(r'\.OnDelete\(DeleteBehavior\.(\w+)\)', expression)
            relationships.append({
                "child_table": entities[dependent]["table"],
                "foreign_key": ", ".join(columns),
                "parent_table": entities[principal]["table"],
                "parent_key": ", ".join(reference_columns),
                "parent_per_child": "1" if ".IsRequired()" in expression else "0..1",
                "children_per_parent": "0..1" if unique_child else "0..N",
                "delete_behavior": delete.group(1) if delete else "ClientSetNull (EF default)",
            })
    if len(relationships) != source.count(".HasForeignKey("):
        raise ValueError("Not all snapshot FKs were exported")
    relationships.sort(key=lambda row: (row["child_table"], row["parent_table"], row["foreign_key"]))
    if len({tuple(row.values()) for row in relationships}) != len(relationships):
        raise ValueError("Duplicate FK export")
    return entities, relationships


def export_model(output, entities, relationships):
    with (output / "tables.csv").open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["table", "entity", "primary_key"])
        for item in sorted(entities.values(), key=lambda item: item["table"]):
            writer.writerow([item["table"], item["entity"], ", ".join(item["primary_key"])])
    with (output / "foreign-keys.csv").open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(relationships[0]))
        writer.writeheader()
        writer.writerows(relationships)
    tables = sorted(item["table"] for item in entities.values())
    identifiers = {table: f"t{index}" for index, table in enumerate(tables)}
    mermaid = ["flowchart LR", "  %% Parent to child. Labels are FK columns. No business rows included."]
    for table in tables:
        mermaid.append(f'  {identifiers[table]}["{table}"]')
    for row in relationships:
        mermaid.append(f'  {identifiers[row["parent_table"]]} -->|"{row["foreign_key"]}"| {identifiers[row["child_table"]]}')
    (output / "full-schema.mmd").write_text("\n".join(mermaid) + "\n", encoding="utf-8")
    manifest = {
        "source": str(SNAPSHOT.relative_to(ROOT)).replace("\\", "/"),
        "snapshot_sha256": hashlib.sha256(SNAPSHOT.read_bytes()).hexdigest(),
        "logical_tables": len(tables), "model_foreign_keys": len(relationships),
        "scope": "EF snapshot only; excludes physical partitions, non-FK references and live database drift",
        "cardinality": "EF relationships plus primary keys and unfiltered unique indexes; delete behavior is EF metadata, not a live catalog query",
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


class Diagram:
    def __init__(self, height, font):
        self.image = Image.new("RGB", (1600, height), "white")
        self.draw = ImageDraw.Draw(self.image)
        self.fonts = {size: ImageFont.truetype(str(font), size) for size in (22, 23, 25, 28, 32, 40)}

    def text(self, x, y, value, size=25, color="#253044", max_width=None):
        font = self.fonts[size]
        bounds = self.draw.textbbox((x, y), value, font=font)
        if max_width is not None and bounds[2] - bounds[0] > max_width:
            raise ValueError(f"Diagram text overflow: {value}")
        if bounds[2] > 1580 or bounds[3] > self.image.height - 5:
            raise ValueError(f"Diagram canvas overflow: {value}")
        self.draw.text((x, y), value, fill=color, font=font)

    def box(self, x, y, width, height, title, table, lines, fill="#EFF5FB"):
        self.draw.rounded_rectangle((x, y, x + width, y + height), 14, fill=fill, outline="#6F8CAB", width=2)
        self.text(x + 22, y + 14, title, 32, max_width=width - 44)
        self.text(x + 22, y + 59, table, 25, "#285C8B", width - 44)
        for index, line in enumerate(lines):
            self.text(x + 22, y + 101 + index * 32, line, 23, max_width=width - 44)

    def arrow(self, points, label=None, label_at=None, dashed=False):
        color = "#8B639A" if dashed else "#587590"
        for start, end in zip(points, points[1:]):
            if not dashed:
                self.draw.line([start, end], fill=color, width=4)
                continue
            length = math.dist(start, end)
            for offset in range(0, math.ceil(length), 22):
                stop = min(offset + 12, length)
                a = tuple(start[i] + (end[i] - start[i]) * offset / length for i in (0, 1))
                b = tuple(start[i] + (end[i] - start[i]) * stop / length for i in (0, 1))
                self.draw.line([a, b], fill=color, width=4)
        start, end = points[-2:]
        angle = math.atan2(end[1] - start[1], end[0] - start[0])
        head = [(end[0] - 17 * math.cos(angle + delta), end[1] - 17 * math.sin(angle + delta))
                for delta in (-0.45, 0.45)]
        self.draw.polygon([end, *head], fill=color)
        if label:
            x, y = label_at
            bounds = self.draw.textbbox((x, y), label, font=self.fonts[23])
            self.draw.rectangle((bounds[0] - 5, bounds[1] - 3, bounds[2] + 5, bounds[3] + 3), fill="white")
            self.text(x, y, label, 23, color)

    def save(self, path):
        self.image.save(path, dpi=(180, 180))


def draw_teams(output, font, relationships):
    expected = {
        ("TeamUserInfo", "AspNetUsers", "MembersId"),
        ("TeamUserInfo", "Teams", "TeamsId"),
        ("Participations", "Teams", "TeamId"),
        ("Participations", "Games", "GameId"),
        ("UserParticipations", "AspNetUsers", "UserId"),
        ("UserParticipations", "Participations", "ParticipationId"),
    }
    actual = {(row["child_table"], row["parent_table"], row["foreign_key"]) for row in relationships}
    if not expected <= actual:
        raise ValueError(f"Core diagram FK mismatch: {expected - actual}")
    d = Diagram(1030, font)
    d.text(65, 32, "用户 战队和参赛关系", 40, "#111827")
    d.text(65, 91, "现有模型主关联｜实线为外键，箭头从主表指向关联表（1 → 多）", 25)
    d.arrow([(265, 290), (265, 410)], "MembersId", (280, 336))
    d.arrow([(700, 290), (700, 330), (580, 330), (580, 410)], "TeamsId", (590, 350))
    d.arrow([(900, 290), (900, 330), (1080, 330), (1080, 410)], "TeamId", (930, 344))
    d.arrow([(1335, 290), (1335, 410)], "GameId", (1350, 336))
    d.arrow([(70, 216), (35, 216), (35, 830), (470, 830)], "UserId", (195, 790))
    d.arrow([(1160, 600), (1160, 680), (960, 680), (960, 760)], "ParticipationId", (1010, 640))
    d.box(70, 145, 390, 145, "用户", "AspNetUsers", ["主键 Id"])
    d.box(605, 145, 390, 145, "战队", "Teams", ["主键 Id；队长 CaptainId"])
    d.box(1140, 145, 390, 145, "比赛", "Games", ["主键 Id"])
    d.box(170, 410, 520, 190, "当前队伍成员", "TeamUserInfo", ["MembersId → 用户", "TeamsId → 战队"])
    d.box(910, 410, 520, 190, "战队参加某场比赛", "Participations", ["主键 Id；GameId + TeamId 唯一", "Status；DivisionId（可空）"])
    d.box(470, 760, 660, 190, "个人在本场比赛的参赛关系", "UserParticipations",
          ["UserId / GameId / TeamId / ParticipationId", "UserId + GameId 唯一"], "#F1F5EB")
    d.text(65, 981, "省略队长及个人参赛对比赛、战队的直接外键；完整关系见 foreign-keys.csv。", 23)
    d.save(output / "team-participation.png")


def draw_scoring(output, font, relationships):
    actual = {(row["child_table"], row["parent_table"], row["foreign_key"]) for row in relationships}
    expected = {("FirstSolves", parent, key) for parent, key in (
        ("Participations", "ParticipationId"), ("GameChallenges", "ChallengeId"),
        ("Submissions", "SubmissionId"), ("FlagContexts", "FlagId"))}
    if not expected <= actual:
        raise ValueError(f"Scoring diagram FK mismatch: {expected - actual}")
    d = Diagram(1210, font)
    d.text(60, 28, "解题事实与积分榜生成", 40, "#111827")
    d.text(60, 84, "现有数据关系与代码流程｜计算结果不等同于一张独立的积分主表", 25)
    d.arrow([(260, 285), (260, 350), (550, 350), (550, 430)], "ParticipationId", (285, 312))
    d.arrow([(800, 285), (800, 430)], "ChallengeId", (815, 350))
    d.arrow([(1330, 285), (1330, 355), (1010, 355), (1010, 430)], "SubmissionId", (1090, 312))
    d.arrow([(1170, 525), (1090, 525)], "FlagId", (1095, 485))
    d.arrow([(750, 635), (750, 765)], "解题事实与规则参与计算", (790, 681), dashed=True)
    d.arrow([(750, 930), (750, 1000)], dashed=True)
    d.box(50, 140, 430, 145, "参赛关系", "Participations", ["某支队伍参加某场比赛"])
    d.box(570, 140, 450, 145, "比赛题目", "GameChallenges", ["题目与动态分值配置"])
    d.box(1110, 140, 440, 145, "提交记录", "Submissions", ["提交状态、时间与关联"])
    d.box(410, 430, 680, 205, "该参赛队伍的首次成功解题", "FirstSolves",
          ["组合主键 ParticipationId / ChallengeId / FlagId", "SubmissionId 唯一，引用对应提交"])
    d.box(1170, 455, 380, 150, "Flag 元数据", "FlagContexts", ["只展示关系，不展示值"])
    d.box(410, 765, 680, 165, "榜单计算  代码", "GameRepository.GenScoreboard",
          ["有效解题、动态分值、血奖、分组权限"], "#F6EFF8")
    d.box(410, 1000, 680, 145, "榜单结果与查询", "ScoreboardModel", ["按赛制加入 AWDP 或渗透得分"], "#F1F5EB")
    d.text(60, 1170, "实线表示外键；虚线表示计算或查询。理论考试使用独立答卷与成绩查询路径。", 23)
    d.save(output / "scoring-facts.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "docs/commercialization/database-diagrams")
    parser.add_argument("--font", type=Path, default=Path("C:/Windows/Fonts/msyh.ttc"))
    args = parser.parse_args()
    entities, relationships = snapshot_model()
    args.output.mkdir(parents=True, exist_ok=True)
    manifest = export_model(args.output, entities, relationships)
    draw_teams(args.output, args.font, relationships)
    draw_scoring(args.output, args.font, relationships)
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
