# 数据库关系图和汇报简报交接

## 任务目标和基线

- 面向新平台复用讨论，交付现有关系图、简报和可直接转发的确认问题。
- 从 `origin/main` 的 `4bef3770` 创建 `codex/database-relationship-brief`。
- 收尾时远端 main 前进，已正常 merge 到 `f389d26c`，重新导出模型和清单；两张重点图的业务关系未变。
- Worktree：`D:/Work/newGZCTF-database-brief`。
- 关系依据为当前 EF 模型快照与计分源码；不将本地模型图冒充生产 catalog 导出。

## 执行计划

1. 核对当前用户、战队、参赛、提交、首次解题和榜单查询链路。
2. 从模型快照导出完整表与外键清单，绘制适合汇报的重点关系图。
3. 按用户后续要求交付 Markdown 简报，附复用边界与需求确认问题，不制作 PDF。
4. 校验关系、链接和文档排版，提交并推送任务分支。

## 当前状态

完成。已核对 152 张逻辑表和 310 条模型外键关系，完成 Markdown 简报、两张 PNG 重点图、完整 CSV 清单和 Mermaid 源文件。关系依据为源码，尚未与生产 catalog 作逐项比对。

## 验证与交付

- 主文档：`docs/commercialization/database-relationship-brief.md`，含一分钟汇报、表分组、两张关系图、审计状态、五项确认问题和可转发说明。
- 配套材料：`docs/commercialization/database-diagrams/` 下两张 PNG、两份 CSV、完整 Mermaid 图及快照摘要 manifest。
- 生成器：`scripts/documentation/build_database_relationship_brief.py`，读取 EF 快照并校验实体块、外键覆盖、端点和字段；结合无条件唯一索引修正外键的实际基数。依赖 Python 与 Pillow，Windows 默认使用微软雅黑，其他环境可通过 `--font` 指定中文字体。
- 可转发 ZIP：仓库外 `D:/Work/database-relationship-brief-delivery/YINYU数据库关系简报.zip`，包含主文档及所有相对引用附件，共 7 个文件。
- 验证通过：152 表、310 外键导出数量；核心图关联与快照匹配；FirstSolves.SubmissionId 唯一性；源文件 SHA-256；文档相对链接；Mermaid 节点/边计数；ZIP CRC 和附件完整性；`git diff --check`。
- 两张 PNG 均已打开目视检查，文字、箭头、图例无裁切或重叠。
- 未运行后端构建/单测、全量集成和前端门禁：本次只修改文档、图件及源码读取生成器，不修改应用或数据库模型。

## 提交

- 任务分支：`codex/database-relationship-brief`，本交接随该任务提交交付；使用 `git log -1 --format=%H -- docs/development/handoffs/2026-09-15-database-relationship-brief.md` 定位最终提交。
- 先前数据库审计位于独立分支 `codex/database-maintainability-review`，修复 `293b6d2d` 和说明补充 `44dd2f0` 未包含在本次起点 main 中；简报明确其未合并、未部署状态。
- 最新 main 增加 TeamLabServiceAccesses 并移除 TeamLabRuntimeDependencyStates，逻辑表总数仍为 152，外键由起始 308 变为 310。分区治理新增单周期数量限制和锁超时；审计分支修复合并前需要同步及回归，不将旧的 4/4 结果视为最新 main 已通过修复验证。
- 本次不向 main 合并，保留分支和 worktree 供审查。

## 部署与遗留

不涉及生产部署、数据库写入或 schema 变更。新平台赛制、数据共享方式和本周验收范围仍待需求方确认。
