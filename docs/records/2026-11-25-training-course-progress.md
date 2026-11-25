# 培训章节完成与课程详情修复

2026-11-25 完成发布与线上验收。[PR #48](https://github.com/Y3X1L2/newGZCTF/pull/48) 修复首次确认阅读的写入，[PR #50](https://github.com/Y3X1L2/newGZCTF/pull/50) 修复 [Issue #49](https://github.com/Y3X1L2/newGZCTF/issues/49) 的课程详情显示与汇总计数。

## 实现

- 首次确认阅读复用 EF 已跟踪的章节进度，课程汇总同时计入本次事务中刚完成的章节。
- 课程详情回传当前用户的章节进度和已有题目绑定，按已发布章节及其完成记录计算完成数与总数。创建学习汇总时初始化已发布章节总数。

## 生产发布

主站 `.27` 发布后端提交 `935aa63f`，随后切换到包含两次修复的 `e629b795`。最新发布目录为 `/srv/yinyu-data/releases/training-course-detail-e629b7951e506fd4ef087ff001e1b4c4b09ab5d0/publish`。上一个后端版本 `935aa63f` 已保留，可按[生产回滚手册](../operations/vnext-maintenance-window-rollout.md#7-回滚条件和步骤)切回。

最新切换从停止主站到配置接口恢复用时 12.653 秒。前端静态资源、Agent 和共享附件目录沿用原部署。首页 HTTP 200，迁移头为 `20261110070457_IntegrateNetlabSceneBindings`。

备份目录为 `/srv/yinyu-data/backups/training-course-detail-20261125`，包含 PostgreSQL 两库、附件、加密配置和上一发布，摘要与归档可读性已核验。备份后按现有章节记录对齐了已发布课程的 54 条汇总计数，复核不一致记录为 0 条。题目绑定和课后练习保留原配置。

## 验收

- [TrainingChapterCompletionTests](../../src/GZCTF.Integration.Test/Tests/Api/TrainingChapterCompletionTests.cs) 两项定向集成测试及 Linux Release 构建通过。
- 首次确认阅读在生产用独立学员验收，单次点击成功，章节状态已完成，课程汇总刷新为 1/1。验收账号和学习记录已清理。
- 《Misc 基础》在生产浏览器核对「课程章节」「学习状态」「课程题目」。二维码、流量分析、音频三章显示已完成，汇总为 3/7（43%），8 道题均显示已有的章节绑定。

业务验证覆盖培训修复范围。合并沿用上述生产验收和定向集成测试结果。
