# 文档与协作审计交接

## 目标与基线

梳理协作入口、进度记录和过期方案，减少重复事实源，形成按任务阅读的结构；清理前核对源码、引用和未完成事项。

- 负责人：本任务执行者；后续合入由仓库审查/集成负责人接手。
- 分支：`codex/docs-collaboration-audit`。
- Worktree：`C:/Users/Cloud/.codex/worktrees/docs-collaboration-audit/newGZCTF`。
- 起点与提交前最新远端：`origin/main 03440c51`。
- 范围：文档、协作规则和轻量文档门禁；没有业务源码、API、模型或迁移变更。
- 授权：用户要求独立分支/worktree，整理清理文档、提交推送；后续明确允许只读查看 `.27` 页面和服务器。本次未连接其他 Worker，没有生产写操作或部署。

## 计划与结果

- [x] 盘点现行入口、计划、交接与测试资料；archive 仅列文件，不通读内容。
- [x] 核最新主线、实际模块、路由、课程实例配置、删除 API/预留检查、迁移元数据、中央依赖和 CI。
- [x] 只读核对 `.27` 发布、回退、Main 摘要、服务状态与页面。
- [x] 合并协作规则、替换状态流水、提取后续事项；删除九份重复/已替代文档、归档五份旧材料、重定位两份有效说明并修复引用。
- [x] 加入本地文件链接/120 行状态限制校验及 CI；验证失败边界，核远端后提交分支交付。

工作状态：`complete`（本任务整理与验证）；证据状态：文档与本地门禁 `VERIFIED`，现场只读范围 `VERIFIED`，部署 `NOT_RUN`。合入审查属于下一步，不能把本分支文档写成已进入 main。

详细判断、清理依据与简明协作方案见 [审计结论](../documentation-audit.md)。非归档 docs Markdown 从 131 份变为 120 份，旧 archive 160 份保留，新增五份历史材料；状态页 365→51 行。现行规则集中于 AGENTS，状态集中于 current-state，未决事项集中于 backlog；复杂任务一份 handoff，短任务 PR 说明。

## 现场证据与范围

2026-10-11 08:32–08:33 UTC（北京时间 16:32–16:33），使用 whoami 进行只读 SSH/HTTP 核验：

- `/opt/gzctf/publish` → `/srv/yinyu-data/releases/training-runtime-quota-60170a5266f684581f21e95013687b0e7367515f-20261011/publish`。
- Main DLL SHA-256：`e2265545d6b26097d9dc888cbf1ee2a58dfd60d367ca5ed6d0ccca57f2b73710`，与既有交接相符。
- `publish.previous` → `/srv/yinyu-data/releases/teamlab-delete-reservation-49948764bd0715142911d5b22c68852ab3d38bb8-20261010T0645Z/publish`。
- Main/Agent 均 active/running、NRestarts=0；当次 PID 分别 1077899 / 641728。旧 PID 不等于当前身份，也不足以推断重启根因。
- 首页与 `/api/Config` HTTP 200；指标 `3001/healthz` HTTP 200、正文 Degraded。
- 首次普通账户读 release 目录被权限拒绝，随后通过交互式 sudo 仅核链接与摘要。认证资料没有写入命令行、仓库或报告。

本轮没有数据库查询、备份读取/恢复、健康详情、管理员页面业务操作、节点库存、实例新建/销毁或其他服务器连接。三节点 Agent/迁移/备份恢复等仍按已提交的 10-10/10-11 交接注明时效，不能声称本轮重复验收。

## 验证

| 检查 | 结果与边界 |
| --- | --- |
| `python scripts/validation/check-documentation.py` | PASS，122 份 Markdown（含根 AGENTS/README），现行本地文件链接与状态长度；不读 archive 正文 |
| `python -m unittest discover -s scripts/validation -p test_check_documentation.py` | 6/6；覆盖删除目标、证据记录引用、Unicode/reference 链接、示例排除、历史目标、120 行边界 |
| `git diff --check` / 暂存差异检查 | PASS |
| 删除与迁移路径全仓搜索 | 现行入口、脚本、源码和测试无残留依赖；审计清单的旧文件名与历史归档引用保留 |
| 新工作流 YAML 解析 | PASS；GitHub 托管执行等待 PR/主线触发，未伪写为 CI 已通过 |
| 前后端 build/全量单元/集成、实机业务验收、漏洞扫描 | NOT_RUN；没有改业务实现/依赖/生成制品，采用文档与校验器最小充分验证 |

## 提交、推送与后续

- 封存提交：本文件与治理变更同提交，精确 SHA 用 `git log -1 --format=%H -- docs/development/handoffs/2026-10-11-docs-collaboration-audit.md` 读取，避免伪造自引用提交号。
- 交付分支：`origin/codex/docs-collaboration-audit`；推送结果以 Git 上游和本任务最终报告为准。
- main：本任务没有合入；部署：未执行；worktree/分支：保留用于审查，未合入不得清理。
- 回退：如治理改动不合适，通过正常新提交恢复相关文档；原文可用 `git show 03440c51:docs/development/<原文件名>` 查看，不回滚他人工作。
- 遗留：旧长手册和专业规格逐模块核对后再合并；旧能力地图未决要求须重核而非关单；见 [后续事项](../backlog.md) DOC-01/DOC-02。原生产技术缺口仍保留，本任务没有修复。
- 下一位接手者：fetch 最新 main，审查清理清单与 backlog 承接、规则/门禁差异；如主线前进，正常同步并重新核当前事实与受影响文档检查，然后按授权合入。
