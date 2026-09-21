# 双港比赛背景与总流程交接

## 目标与状态

用户需要一份比赛内容说明，补充新开发会话的故事背景、双方目标、比赛流程与结局，不重复人员分工。文档 complete；业务实现、场景攻击与运行验收 NOT_RUN。

## 基线

从最新 origin/main `9999e6b13ffdc5d04abbe9721c9be993998a9a6c` 创建 `codex/league-story-20260921`，独立 worktree 为 `D:/Work/newGZCTF-league-story-20260921`。按此前已读规范及最新差异核对 current-state，未通读 archive，未连接服务器。

参考用户已确认的首期规则、旧规划分支中的 Demo 验证设计，以及 `codex/league-handbook-20260921` 的分工手册。后续开发以当前源码和最新规则为准。

## 改动与结论

- 新增[双港比赛背景与总流程](../league/dual-port-competition-flow.md)和同版四页 Word，更新文档导航，并提供生成脚本。
- 故事采用虚构企业参与应急运输竞标演练。台风、公司名称、路线和结局文案为本稿内容建议；已确认规则与后续玩法分开说明。
- 首版按进入、报名审核、配置、双队准备、手动开赛、攻防、核心 Flag KO、关闭访问与清理展开。
- 胜负来自真实终局，剧情跟随结果。管理员中止记为无胜者，金币与赛果分开。
- 商店、分阶段剧情和可信业务 Checker 保留为后续方向。场次与队伍使用通用模型，港口名称与故事属于场景内容。
- 当前 main 的运行创建和传统本队 Flag 判定入口已抽查；本次未改动业务源码、API 或数据库。

## 验证

- Markdown、DOCX 正文与 Word 渲染文本规范化后一致。
- 四页逐页视觉检查通过，字体实际为 SimSun 与 SimHei；正文 12 磅、表格 11 磅。
- 导航与正文合计 48 个相对文件链接检查通过；交接链接另行验证。
- 生成器执行及语法检查通过，git diff --check 通过。
- 标准 render_docx.py 缺少 LibreOffice，使用本机 Word COM 导出 PDF，再由 bundled pypdfium2 渲染。最终 QA 在仓库外 `D:/Work/league-story-qa-20260921/word-v3`。WPS 未自动打开实测。
- 本次为文档任务，未运行后端、前端全量测试或真实比赛验收。

## 提交与后续

提交意图为 `docs: describe dual-port competition story and flow`，推送 `codex/league-story-20260921`，交付时报告实际 SHA。未合并 main，未部署，分支与 worktree 保留供审查。

新开发会话先读比赛总流程，再按分工手册认领实现范围。具体场景素材和可操作权限由出题准备进一步确定；文档中的后续玩法不自动进入首期开发范围。
