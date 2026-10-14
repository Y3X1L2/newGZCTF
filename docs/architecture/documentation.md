# 文档维护

文档按功能找，先读简短说明，再进入代码和专题。维护工作由修改功能的人一起完成，代码审查者同时核对说明，沿用现有PR审查流程。

## 放在哪里

| 内容 | 位置 |
| --- | --- |
| 项目用途与功能关系 | `docs/overview.md` |
| 功能目的、使用者、主流程、大体实现与边界 | `docs/modules/<功能>/README.md` |
| 关键代码、契约、不变量、测试和修改入口 | 同目录的 `development.md` |
| 完成一件实际操作 | 模块的 `guides/`；全站部署/恢复在 `docs/operations/` |
| 状态机、网络、字段等深度参考 | 模块的 `reference/` |
| 跨模块共用规则 | `docs/architecture/` |
| 最新基线和重点未决项 | [当前状态](../current-state.md)、[后续事项](../backlog.md) |
| 复杂任务交接与发布证据 | [records](../records/README.md) |

一个事实只维护一个主要位置，其他页面链接过去。生产SHA与测试记录集中保存在状态页和任务证据中，模块页引用它们；专业术语第一次出现时解释用途，保留真实权限、条件、状态、接口与未验收边界。

## 拆分长文

模块README目标500–1000中文字；development是导航与代码地图，目标控制在200行以内，简单模块可以更短。超过200行自动提醒复核，检查继续执行。

出现多个独立业务链路时按链路拆专题，例如TeamLab的发布、运行生命周期、网络、会话、流量。完整字段/状态表进入reference，操作步骤进入guides，共用约束引用其所属模块或architecture。拆后入口保留摘要与链接，旧内容有明确承接位置，每条流程保留完整的条件、步骤和结果。

## 一次任务的使用流程

1. 先读AGENTS、当前状态中的相关事项和目标模块development；独立分支/worktree核对代码。
2. 跨会话、多模块或部署任务建立一份 [记录](../records/task-template.md)；首个有效提交后创建Draft PR。小改动可完成验证后直接创建普通PR。
3. 功能行为改变更新README/专题；代码入口改变更新development；操作变化更新运维手册；只更新受影响内容。
4. 提交前运行链接与生成一致性检查；提交后运行影响报告，PR中说明无需修改的受影响模块及具体原因。完成后转为Ready for review，审查和授权满足后合入。
5. 项目基线改变才替换current-state；过程放records，遗留放backlog。记录完成后从活动入口移出，保留重要证据。

## 自动检测与生成

[catalog.json](../catalog.json) 是“模块—代码—文档”对应表。每项只有ID、名称、两个入口、代码glob与文档glob；`platform`表示共用架构/工具。功能进度与部署状态继续引用current-state和发布证据。

新功能模块或代码搬家时更新对应表；共享源码可以归属多个模块。用仓库相对路径和`/`分隔，glob要能匹配真实文件。生成API与安装脚本的既有路径暂保留，避免文档迁移破坏实际消费者。

在仓库根目录执行：

```powershell
python scripts/validation/check-documentation.py
python scripts/validation/generate-module-index.py --check
python scripts/validation/documentation-impact.py --base origin/main
git diff --check
```

影响报告默认以已提交的HEAD为终点；工作区的未提交修改在下次提交后纳入比较。

catalog变更后执行 `python scripts/validation/generate-module-index.py` 更新模块导航，生成页由该命令统一维护。检查器核现行链接/锚点、源码文件链接、catalog路径与真实模块覆盖；README/development过长只提醒。归档仅检查被引用的文件仍在，历史正文保留原时点内容。

影响工具比较Git分支差异，列出相关模块、修改的代码和应读文档，新增/删除/重命名及未映射代码也会显示。PR有对应文档变更时进入正常审查；确实无需改正文时，在PR中提供复核理由，例如：

```text
## 文档影响
无需修改模块：training
无需修改理由：
training: 本次仅为TrainingContainerQuotaTests中现有额度拒绝路径增加回归用例，接口、配置与运行行为未改变，已核对培训开发页。
```

理由写清具体改动、现行说明仍适用的依据和验证结果，多模块共享变更逐项列出。模板里的“无需修改”字段由检查器解析，正文含义由审查者结合代码确认。

GitHub Actions对PR/main提交运行检查，每周和手动巡检生成报告，权限为只读。静态目录检查覆盖现行文档；定期/手动影响报告默认比较最近一次提交，具体起止SHA写入报告。模块导航由脚本生成；AI依据报告提出正文修改，随PR审查。本流程使用仓库脚本与GitHub Actions，正文合入、历史清理和生产操作沿用各自授权。
