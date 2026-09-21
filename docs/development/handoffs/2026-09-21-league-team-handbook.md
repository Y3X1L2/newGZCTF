# 联赛第一阶段分工手册交接

## 任务目标与状态

把已确认的最小联赛范围写成简单易懂的分工手册，按实际比赛流程说明任务、负责人、依赖、交接和验收。按任务安排，不做逐日排期；个人进展可以多次提交，在可验证的工作完成时集中评审和合并。

文档交付 complete，文档内容与排版 VERIFIED；联赛业务实现和运行验收 NOT_RUN。

## 基线

- 本次从最新 origin/main `26c09375` 创建分支 `codex/league-handbook-20260921`。
- 独立 worktree：`D:/Work/newGZCTF-league-handbook-20260921`。
- 旧规划保留在 `codex/league-roadmap-20260919`，本手册沿用用户已确认的首期范围和角色。
- 主工作区及旧规划工作区保持原状。未连接测试或生产服务器。

## 改动与技术结论

- 新增[开发分工手册](../league/phase1-team-handbook.md)、同版六页 Word、可重复生成的脚本，并更新 docs/README 入口。
- T0 共同约定接口与必要基础；T1–T3 并行完成场次、场景与 Flag；T4–T6 连接准备、开赛、KO 和清理；T7 金币独立安排。U1–U4 是 lcx 的四项页面任务。
- 各任务标明开始条件、交付对象与完成标准；区分测试替身开发、真实联调和对外发布。
- lxy 拥有场次终局，lmr 拥有 Flag 校验与账本；有效提交和 KO 结果在同一本地数据库事务中确认。yhr 负责运行绑定、访问与清理，场景业务素材由出题人提供并签收。
- 当前 main 的 TeamLab 已有通用 Runtime Grant 和资产变更能力。沿用最新[对接文档](../../commercialization/teamlab-integration-guide.md)，没有将此前版本的能力缺口作为当前事实。
- 源码核对：GameController 的报名入口、PenetrationObjectiveService 的本队 Flag 校验、PenetrationTeamLabAdapter 的运行接入、ITeamLabRuntimeApplicationService 和 TeamLabRuntimeGrantService。独立联赛实现仍未在所查模块和 vNext 注册中发现。
- 本次未修改业务代码、数据库结构或实际 API。

## 验证

- Markdown、DOCX 正文和 Word 渲染文本按空白规范化后一致，UTF-8 无替换字符。
- 正文宋体 12 磅、标题黑体、表格 11 磅；PDF 中全部汉字实际使用 SimSun 或 SimHei。
- Word 导出后逐页查看六张 PNG，正文和表格无截断、重叠或孤立尾页。
- 导航及手册的 50 个相对文件链接存在；生成器成功执行。
- 执行 git diff --check 和生成器语法检查。未执行业务构建、前后端全量测试或真实比赛验收，本次为文档任务。

标准 render_docx.py 因运行包缺少 LibreOffice 失败，采用本机 Microsoft Word COM 导出 PDF，bundled pypdfium2 渲染并检查实际字体。WPS 未自动打开实测。QA 中间文件位于仓库外 `D:/Work/league-handbook-qa-20260921/word-v1`。

## 提交与部署

- 提交意图：`docs: add practical phase-one league team handbook`，交付时报告实际 SHA。
- 推送分支：`codex/league-handbook-20260921`；未合并 main，保留分支和 worktree 供审查。
- 没有部署或迁移，生产状态未改变。

## 后续

团队从 T0 开始确认实际接口、最小数据结构和场景素材。各任务的真实实现和验收尚待开展。后续开发拉取最新 main，重新检查代码与测试基线，避免复用旧规划中的测试数量或部署状态。
