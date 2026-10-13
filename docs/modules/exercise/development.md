# 自主练习开发定位

用户流程见[概览](README.md)，页码、筛选和计数契约见[分页参考](reference/catalog-pagination.md)。

## 入口与可见范围

[ExerciseController](../../../src/GZCTF/Controllers/ExerciseController.cs) 暴露 `/api/exercise`。
读取、提交和容器用例调用 [ExerciseService](../../../src/GZCTF/Modules/Exercise/Application/ExerciseService.cs)，
公共题库写入调用 [ExerciseManagementService](../../../src/GZCTF/Modules/Exercise/Application/ExerciseManagementService.cs)。

| 问题 | 查看位置 | 先核事实 |
| --- | --- | --- |
| 列表少题、停用题泄露或课程题混入 | `GetCatalogPageAsync` / `GetManagementCatalogPageAsync` | IsEnabled、MinimumVisibleRole、TrainingCourseId、教师管理权限 |
| 页码、筛选、标签或总数错误 | [ExerciseCatalogQuery](../../../src/GZCTF/Modules/Exercise/Application/ExerciseCatalogQuery.cs) 的 `PageAsync` | 过滤后的全库计数、页码校正、当前用户完成状态、标签 JSON 查询 |
| 题目详情404或权限异常 | `GetExerciseDetailAsync`、Controller 的用户身份 | 公共题、启用和最小可见角色，而非只有 ID 存在 |
| 外部列表与浏览器列表行为不同 | [ExerciseOpenApiController](../../../src/GZCTF/Modules/Exercise/Api/ExerciseOpenApiController.cs) | 外部使用 cursor 分页；内部使用 page/pageSize，不能统一改成一种协议 |

公共目录明确排除 `TrainingCourseId != null`；管理端包括停用公共题，
学员目录只含当前角色可见的启用题。
搜索和标签的数据库翻译细节以参考页为准，不用客户端加载全库兜底。

## 题库写入、来源导入与附件

`ExerciseManagementService` 负责创建、更新、导入、题库收集与删除。
`ImportFromGameAsync` / `ImportFromTrainingAsync` 处理显式导入；
`CollectGameChallengeAsync` / `CollectTrainingChallengeAsync` / `CollectAwdpServiceAsync`
与 `BackfillPoolAsync` 处理来源收集与回填。
来源字段说明题目从哪里来，不授予操作源比赛或课程的权限。

[ExerciseWriteValidation](../../../src/GZCTF/Modules/Exercise/Application/ExerciseWriteValidation.cs)
维护题目写入校验；附件与多 Flag 关系由管理服务编排。
[ExerciseChallengeRepository](../../../src/GZCTF/Repositories/ExerciseChallengeRepository.cs)
是持久化入口，不能在前端生成类型中自行补字段。
共享镜像主资产和附件管理见[内容开发页](../content/development.md)。

## 提交判定与完成统计

`ExerciseService.SubmitFlagAsync` 检查公共题可见性与提交次数，
调用 [ExerciseInstanceRepository](../../../src/GZCTF/Repositories/ExerciseInstanceRepository.cs) 的 `VerifyAnswer`，
保存 `ExerciseSubmission` 与所命中的 Flag ID。
多 Flag 题需要完成要求的 Flag 集合，不能把一次正确提交等同整题完成。

数据实体是 [ExerciseChallenge](../../../src/GZCTF/Models/Data/ExerciseChallenge.cs)、
[ExerciseInstance](../../../src/GZCTF/Models/Data/ExerciseInstance.cs) 与
[ExerciseSubmission](../../../src/GZCTF/Models/Data/ExerciseSubmission.cs)。
课程使用同一实例基础，但写 `TrainingCourseSubmission`；普通 CTF 写 `Submission`。
三个提交集合不可混作统计事实。

## 实例与额度

`ExerciseService.CreateContainerAsync` 验证用户与题目，实例仓库生成/读取个人实例并创建运行请求。
`ExtendContainerAsync` / `DestroyContainerAsync` 进入统一队列并携带当前节点和代次。
先分清业务拒绝、排队、执行失败和入口未就绪，再转到[Runtime](../runtime/development.md)定位。

公共练习使用 `ContainerPolicy:MaxExerciseContainerCountPerUser`；
自动清理仅在 `AutoDestroyOnLimitReached=true` 时启用，不能选到课程实例。
公共与课程额度隔离、独立 Flag 和实例锁详见[课程生命周期参考](../training/reference/container-lifecycle.md)。

## 外部异步写入

[ExerciseExternalApplicationService](../../../src/GZCTF/Modules/Exercise/Application/ExerciseExternalApplicationService.cs)
提交持久化 `ExerciseMutationJob`，由 `ExerciseMutationOperationHandler` 执行。
核对 `exercises:*` scope、`exercise:*` 或具体题授权、幂等键和 operation 状态。
外部 token 不得访问课程题；通用错误和查询契约见[外部 API 标准](../../architecture/external-api-standard.md)。

## 前端与定向验证

学员页在 [features/practice](../../../src/GZCTF/ClientApp/src/vnext/features/practice)，
管理页在 [admin/exercises](../../../src/GZCTF/ClientApp/src/vnext/features/admin/exercises)。
`practiceApi.ts` / `exerciseAdminApi.ts` 是请求适配入口，
`usePracticeInstance.ts` 管理实例状态。`/practice/stats` 仍兼容旧数组接口。
筛选与页大小变化须复位页码，并规范化无效 URL 参数。

| 改动 | 现有定向测试 |
| --- | --- |
| 分页、过滤与隔离 | [ExerciseCatalogPaginationTests](../../../src/GZCTF.Integration.Test/Tests/Api/ExerciseCatalogPaginationTests.cs) |
| 提交和业务流程 | [ExerciseWorkflowTests](../../../src/GZCTF.Integration.Test/Tests/Api/ExerciseWorkflowTests.cs)、[ExerciseWorkflowRegressionTests](../../../src/GZCTF.Test/UnitTests/Exercise/ExerciseWorkflowRegressionTests.cs) |
| 来源、附件和 Flag 关系 | [ExerciseImportAndAttachmentTests](../../../src/GZCTF.Test/UnitTests/Exercise/ExerciseImportAndAttachmentTests.cs) |
| 页面分页 | `PracticeBrowsePage.test.tsx`、`AdminExercisesPage.test.tsx` |

示例：`dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~ExerciseCatalogPaginationTests`。
10月11日真实分页结果见[发布记录](../../records/2026-10-11-training-quota-settings.md)。
实现与数据库测试不能代替每种镜像、入口与内容的真实验收。
