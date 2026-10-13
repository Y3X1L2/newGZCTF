# 理论考试开发定位

读者流程见[概览](README.md)。排查先区分比赛理论考试与课程课后测试，后者见[培训开发页](../training/development.md)。

## 题库与标签

[TheoryAdminController](../../../src/GZCTF/Controllers/TheoryAdminController.cs)
暴露 `/api/admin/theory`，题库检索与编辑调用
[ITheoryQuestionCatalog](../../../src/GZCTF/Modules/Theory/Application/ITheoryQuestionCatalog.cs)。
EF 实现是 [EfTheoryQuestionCatalog](../../../src/GZCTF/Modules/Theory/Infrastructure/EfTheoryQuestionCatalog.cs)。

| 问题 | 定位点 | 判断依据 |
| --- | --- | --- |
| 题目或选项无法保存 | [TheoryExamService](../../../src/GZCTF/Services/TheoryExamService.cs) 的 `NormalizeAndValidate` | 标题、选项数量、答案索引、单选/判断答案数和正分值 |
| 标签筛选不匹配 | [TheoryTagNormalizer](../../../src/GZCTF/Modules/Theory/Application/TheoryTagNormalizer.cs)、catalog 的 `SearchAsync` / `SetTagsAsync` | 标签规范化、关联表与匹配语义，不能按 BankName 代替标签 |
| 数据模型或查询关系错误 | [TheoryExam.cs](../../../src/GZCTF/Models/Data/TheoryExam.cs)、[TheoryEntityConfigurations](../../../src/GZCTF/Modules/Theory/Infrastructure/Persistence/TheoryEntityConfigurations.cs) | 题库、试卷、答卷、答案和唯一约束 |

`DefaultBankName` 在未填写题库名时提供默认值。
答案索引由 `NormalizeIndexes` 去重并排序，题目校验与答题判分必须使用同一规范。

## 组卷、发布与替换

`TheoryAdminController.SavePaper` 将请求转换为试卷题目快照，保存后回到未发布状态。
保存前检查 Theory/Mixed 比赛、题目内容、来源题 ID 和是否已有最终答卷。
`PublishPaper` 拒绝空卷，`GetResults` 返回成绩，`RecalculateResults` 调用重判分。

外部替换走 [TheoryOpenApiController](../../../src/GZCTF/Modules/Provisioning/Api/TheoryOpenApiController.cs)
与 `AcademicImportOperationHandler.ImportTheoryPaperAsync`，
拥有已提交答卷的试卷会被拒绝替换；外部请求的 `Publish` 是独立发布意图。
具体批次、幂等和 operation 规则见[批量导入开发页](../provisioning/development.md)。

## 考生无法答题或重复交卷

[TheoryPlayerController](../../../src/GZCTF/Controllers/TheoryPlayerController.cs)
暴露 `/api/theory/games/{gameId}/paper|draft|submit|scoreboard`。
依次查看：

1. `GetPlayerContext`：Theory/Mixed、开始/结束时间、PracticeMode、当前用户和 Accepted 参赛关系。
2. `GetPublishedPaper`：试卷已发布且属于当前比赛。
3. `GetOrCreateSheet`：答卷按比赛、试卷、用户、参赛关系查找；不能复用他人答卷。
4. `SaveDraft` / `Submit`：答题数、题目归属与是否已提交。

草稿禁止覆盖已提交答卷；最终提交在事务中判分并保存，重复提交返回409。
并发冲突不能被前端包装为成功，失败时应保留当前草稿并提示真实错误。

## 判分、成绩与答案回顾

`TheoryExamService.ApplyAnswers` 验证题目与选择，再保存完整答案集合。
`Grade` 比较规范化后的答案索引集合，完全相等得本题全分，否则零分；
同时设置总分、满分、Submitted 和提交时间。
`BuildResults` 对各队已提交答卷选最佳成绩，并按成绩、提交时间、队伍 ID 稳定排序。

[TheoryStatisticsProjectionService](../../../src/GZCTF/Modules/Theory/Application/TheoryStatisticsProjectionService.cs)
维护可读成绩投影。题库/试卷/答卷改变后的统计失效也要检查。
[TheoryModels](../../../src/GZCTF/Models/Request/Game/TheoryModels.cs)
中的 `TheoryPlayerPaperModel` 负责响应转换，
确认正确答案与回顾字段只在允许时返回，不能由前端自行隐藏泄露的数据。

## 前端定位

- [theoryPlayerApi](../../../src/GZCTF/ClientApp/src/vnext/features/games/theory/theoryPlayerApi.ts) 适配比赛答题接口。
- [theoryAdminApi](../../../src/GZCTF/ClientApp/src/vnext/features/admin/api/theoryAdminApi.ts) 适配题库和组卷管理。
- [useTheoryExamSession](../../../src/GZCTF/ClientApp/src/vnext/features/theory/workbench/useTheoryExamSession.ts) 管理加载、草稿与最终提交。
- `TheoryExamWorkbench.tsx`、`questionModel.ts`、`paperModel.ts` 和 `theoryReview.ts` 负责展示与转换。

共用工作台不得内置比赛或课程专有权限；请求差异留在 feature adapter/controller。
遵守[前端组件边界](../../architecture/frontend/component-boundary.md)。

## 定向验证与现有范围

[TheoryTagNormalizationTests](../../../src/GZCTF.Test/UnitTests/Theory/TheoryTagNormalizationTests.cs)
覆盖标签规范化，可定向运行：

`dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~TheoryTagNormalizationTests`

前端 `questionModel.test.ts`、`paperModel.test.ts`、`useTheoryExamSession.test.tsx`、
`TheoryExamWorkbench.test.tsx` 和 `theoryReview.test.ts` 定位转换、会话与回顾行为。
现有这些测试不构成整场考试 HTTP 生命周期验收；改动判分或提交约束时，
需要补足受影响的数据库/HTTP 场景，至少检查非参赛者、未发布卷、重复/并发交卷与答案可见性。
本页不宣称本轮执行了这些测试，部署边界见[当前状态](../../current-state.md)。
