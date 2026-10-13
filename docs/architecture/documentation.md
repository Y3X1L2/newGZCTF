# 文档怎么写、怎么维护

文档按功能找，先读简短说明，再进入代码和专题。维护工作由修改功能的人一起完成，代码审查者同时核对说明；小团队不增加专门审批层。

## 放在哪里

| 内容 | 位置 |
| --- | --- |
| 项目是什么、功能如何关联 | `docs/overview.md` |
| 功能目的、使用者、主流程、大体实现与边界 | `docs/modules/<功能>/README.md` |
| 关键代码、契约、不变量、测试和修改入口 | 同目录的 `development.md` |
| 完成一件实际操作 | 模块的 `guides/`；全站部署/恢复在 `docs/operations/` |
| 状态机、网络、字段等深度参考 | 模块的 `reference/` |
| 跨模块共用规则 | `docs/architecture/` |
| 最新基线和重点未决项 | [当前状态](../current-state.md)、[后续事项](../backlog.md) |
| 复杂任务交接与发布证据 | [records](../records/README.md) |

一个事实只维护一个主要位置，其他页面链接过去。模块说明不复制生产SHA与历史测试数量；专业术语第一次出现时解释用途，保留真实权限、条件、状态、接口与未验收边界。

## 拆分长文

模块README目标500–1000中文字；development是导航与代码地图，目标100–200行以内，不用凑到最低行数。超过200行自动提醒复核；篇幅不是失败条件。

出现多个独立业务链路时按链路拆专题，例如TeamLab的发布、运行生命周期、网络、会话、流量。完整字段/状态表进入reference，操作步骤进入guides，共用约束引用其所属模块或architecture。拆后入口保留摘要与链接，旧内容要有承接位置；不要按行数机械拆碎一条流程。

## 一次任务的使用流程

1. 先读AGENTS、当前状态中的相关事项和目标模块development；独立分支/worktree核对代码。
2. 跨会话、多模块或部署任务建立一份 [记录](../records/task-template.md)；首个有效提交后创建Draft PR。小改动可完成验证后直接创建普通PR。
3. 功能行为改变更新README/专题；代码入口改变更新development；操作变化更新运维手册；只更新受影响内容。
4. 提交前运行以下文档门禁，PR中说明无需修改的受影响模块及具体原因。完成后转为Ready for review，审查和授权满足后合入。
5. 项目基线改变才替换current-state；过程放records，遗留放backlog。记录完成后从活动入口移出，保留重要证据。

## 自动检测与生成

[catalog.json](../catalog.json) 是“模块—代码—文档”对应表。每项只有ID、名称、两个入口、代码glob与文档glob；`platform`表示共用架构/工具。它不保存第二份功能完成状态，也不自动证明部署成功。

新功能模块或代码搬家时更新对应表；共享源码可以归属多个模块。用仓库相对路径和`/`分隔，glob要能匹配真实文件。生成API与安装脚本的既有路径暂保留，避免文档迁移破坏实际消费者。

在仓库根目录执行：

```powershell
python scripts/validation/check-documentation.py
python scripts/validation/generate-module-index.py --check
python scripts/validation/documentation-impact.py --base origin/main
git diff --check
```

catalog变更后执行 `python scripts/validation/generate-module-index.py` 更新模块导航；生成页不手改。检查器核现行链接/锚点、源码文件链接、catalog路径与真实模块覆盖；README/development过长只提醒。归档正文不参与现行规则判断。

影响工具比较Git分支差异，列出相关模块、修改的代码和应读文档，新增/删除/重命名及未映射代码也会显示。PR有对应文档变更时进入正常审查；确实无需改正文时，在PR中提供复核理由，例如：

```text
## 文档影响
无需修改模块：training
无需修改理由：
training: 本次仅为TrainingContainerQuotaTests中现有额度拒绝路径增加回归用例，接口、配置与运行行为未改变，已核对培训开发页。
```

理由应说明具体变化及为何不影响说明；“无需修改”“纯代码”“N/A”不能代替复核。多模块共享变更要分别列出。实际语义是否准确仍由审查者判断，改了几个字或填了理由不构成自动证明。

GitHub Actions对PR/main提交运行检查；巡检仅生成报告，不自动修改业务说明或生产。生成导航属于确定性自动维护；AI可依据报告提出正文改动，仍随PR审查。本实现不连接外部付费AI服务、不自动合并或删除历史证据。
