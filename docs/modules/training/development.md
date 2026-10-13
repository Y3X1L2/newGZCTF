# 培训开发定位

学习流程见[概览](README.md)，实例额度与 Flag 细节见[生命周期参考](reference/container-lifecycle.md)。
课程题复用 Exercise 数据与执行能力，权限、提交、试卷和进度仍归 Training。

## 管理与学习入口

| 问题 | 首先查看 | 关键位置 |
| --- | --- | --- |
| 学员看不到课程、无法报名或读取章节 | [TrainingCourseController](../../../src/GZCTF/Controllers/TrainingCourseController.cs) | `Courses`、`Enroll`、`CanLearnCourse`、`Chapter`；报名、课程状态、章节发布与归属 |
| 教师不能编辑或审核课程 | [TrainingCourseAdminController](../../../src/GZCTF/Controllers/TrainingCourseAdminController.cs) | `CanEditCourse`、教师角色、课程所有者、报名审核 |
| 学习列表显示或删除权限不一致 | [TrainingCourseAccessPolicy](../../../src/GZCTF/Services/Training/TrainingCourseAccessPolicy.cs) | 归档可见性与管理员/创建者/Owner 删除资格 |
| 课程数据、资源或进度关联错误 | [Training.cs](../../../src/GZCTF/Models/Data/Training.cs) | Course、Chapter、Enrollment、Challenge、Progress、TheoryPaper/Sheet |

课程管理包含发布、归档、回到草稿、教师与学员管理、章节、资源、实验和课后卷。
不要把教师全局角色与某课程的 Owner/Teacher 关系混为一项权限。
`StudentLearningDetail` 和 `LearningSummaries` 是教师读取学习事实的入口，不能由页面编造统计。

## 实验绑定和题目来源

课程通过 `TrainingCourseChallenge` 与 `TrainingCourseChapterChallenge`
把章节绑定到 `ExerciseChallenge`。
`ExerciseChallenge.TrainingCourseId != null` 表示课程拥有的隔离题目。
从公共来源导入课程时创建快照，后续课程与公共题修改互不覆盖。

管理端 `AddChallenge` / `RemoveChallenge` 与学员端 `GetOrCreateCourseInstance`
共同检查课程、章节和题目归属；只拿到题目 ID 不能操作他人课程。
镜像上传/注册和引用释放须保持[内容模块](../content/development.md)的主资产与引用边界。

## 创建、续期与停止

学员 `/api/training/courses/{courseId}/challenges/{challengeId}/container`
先验证学习权限及实验启用，再调用
[ExerciseInstanceRepository](../../../src/GZCTF/Repositories/ExerciseInstanceRepository.cs)。
同一实例的重复请求复用有效容器或票据，课程票据为 `TrainingContainer`。
续期与停止也提交统一队列，核对 node、generation 和续期窗口。

创建被拒绝时先查看课程额度及活跃票据，再看节点容量和准入；
“0不限个人数量”不能解释为绕过调度容量。
缺失动态 Flag 的旧实例修复、额度锁及公共练习隔离均以[生命周期参考](reference/container-lifecycle.md)为准。
节点与入口失败继续查[Runtime](../runtime/development.md)。

## 动态额度设置

- [AdminController](../../../src/GZCTF/Controllers/AdminController.cs) 的 Config GET/PUT 读写 `TrainingContainerPolicy`。
- [ConfigService](../../../src/GZCTF/Services/Config/ConfigService.cs) 持久化并重载配置，新请求使用 OptionsSnapshot。
- [useTrainingContainerQuota](../../../src/GZCTF/ClientApp/src/vnext/features/admin/system/useTrainingContainerQuota.ts) 与 `TrainingContainerQuotaPanel.tsx` 管理独立草稿、校验和保存。

修改额度后检查回读、新建准入和已有实例复用，不能只看管理表单。
降低上限不得销毁实例或取消既有票据，公共练习策略不随课程设置改变。

## 提交、课后测试和学习进度

`TrainingCourseController.SubmitFlag` 使用实例答案校验，写 `TrainingCourseSubmission`，
记录答案摘要而非明文；成功后重新计算课程进度。
`CompleteChapter` 和 `MarkChapterCompletedIfReady` 检查必做实验与课后测试，
`RecalculateProgress` 由已保存事实汇总课程状态。

课程理论使用独立 Paper/Sheet；`ChapterTheory`、`SaveChapterTheoryDraft`、
`SubmitChapterTheory`、`RetryChapterTheory` 是四个不同用例。
题目规范与判分复用 [TheoryExamService](../../../src/GZCTF/Services/TheoryExamService.cs)，
但不能写入比赛答卷或拿比赛参赛关系授权课程。

## 删除课程

[TrainingCourseDeletionService](../../../src/GZCTF/Modules/Training/Application/TrainingCourseDeletionService.cs)
核管理员、创建者或 Owner 资格；
[EfTrainingCourseDeletionStore](../../../src/GZCTF/Modules/Training/Infrastructure/EfTrainingCourseDeletionStore.cs)
删除课程拥有的题目、实例、Flag、附件、提交和绑定，并释放镜像引用。
端到端删除仍从管理 Controller 追踪运行资源清理，不能只调用存储删除就宣称节点已清干净。
共享 `ImageTemplate` 主副本不能随课程级联删除。

## 前端与定向验证

前端在 [features/training](../../../src/GZCTF/ClientApp/src/vnext/features/training)：
`trainingLearnerApi.ts`、`trainingAdminApi.ts`、`trainingChapterApi.ts` 适配请求；
`useTrainingChapterController`、`useTrainingInstance` 和 `useTrainingFlagSubmission`
负责章节、实例和提交交互。共用视觉规则见[前端组件边界](../../architecture/frontend/component-boundary.md)。

| 改动 | 现有定向测试 |
| --- | --- |
| 权限与题目响应 | [TrainingCourseAccessPolicyTests](../../../src/GZCTF.Test/UnitTests/Training/TrainingCourseAccessPolicyTests.cs)、[TrainingCourseChallengeDetailModelTests](../../../src/GZCTF.Test/UnitTests/Training/TrainingCourseChallengeDetailModelTests.cs) |
| 额度与旧 Flag 修复 | [TrainingContainerLifecycleTests](../../../src/GZCTF.Test/UnitTests/Training/TrainingContainerLifecycleTests.cs)、[TrainingContainerQuotaTests](../../../src/GZCTF.Integration.Test/Tests/Api/TrainingContainerQuotaTests.cs) |
| 真实 Docker 隔离 | [TrainingContainerDockerTests](../../../src/GZCTF.Integration.Test/Tests/Repository/TrainingContainerDockerTests.cs) |
| 章节实例前端状态 | `chapter/useTrainingInstance.test.tsx`、`chapter/trainingChapterDomain.test.ts` |

示例：`dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~TrainingContainerQuotaTests`。
10月11日发布与限定验收见[记录](../../records/2026-10-11-training-quota-settings.md)；本页没有重新执行生产验收。
